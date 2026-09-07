using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.OrderEntry;

/// <summary>Compact values used by the Caisse operational strip and Commandes entry points.</summary>
public sealed record OrderOperationalSummary(
    Money TurnoverTtc,
    Money ReceivedTtc,
    Money ReceivedCardTtc,
    Money ReceivedCashTtc,
    int FutureOrderCount,
    int DueTodayAdvanceOrderCount,
    int OverdueUnsettledOrderCount);

public enum OperationalOrderView
{
    Future,
    DueToday,
    OverdueUnsettled
}

/// <summary>Infrastructure seam for the M05 multi-row lifecycle/payment queries.</summary>
public interface IOrderLifecycleStore
{
    Task SaveLifecycleAsync(OrderSnapshot snapshot, IReadOnlyList<PaymentAdjustment> adjustments, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderBrowserRow>> SearchAsync(string? query, CancellationToken cancellationToken = default);
    Task<OrderOperationalSummary> GetOperationalSummaryAsync(DateOnly businessDate, CancellationToken cancellationToken = default);
}

public sealed record OrderLifecycleResult(
    bool Succeeded,
    OrderSnapshot? Snapshot,
    IReadOnlyList<ValidationIssue> Issues)
{
    public static OrderLifecycleResult Failure(params ValidationIssue[] issues) => new(false, null, issues);
}

/// <summary>Application-owned existing-order lifecycle, payment and live-search boundary.</summary>
public sealed class OrderLifecycleService(
    IOrderStore orders,
    IIdGenerator idGenerator,
    IBusinessClock clock,
    IWriteAuthorityGuard authorityGuard,
    IDurableChangeNotifier notifier,
    IOrderEntryCatalogueQueries? catalogue = null,
    IBusinessSettingsStore? settings = null) : IDisposable
{
    private readonly IOrderStore orders = orders ?? throw new ArgumentNullException(nameof(orders));
    private readonly IIdGenerator idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
    private readonly IBusinessClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IOrderEntryCatalogueQueries? catalogue = catalogue;
    private readonly IBusinessSettingsStore? settings = settings;
    private readonly IWriteAuthorityGuard authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
    private readonly IDurableChangeNotifier notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private int activeCalls;
    private bool disposed;

    // Test assemblies use explicit internal test-only wiring. Production composition must provide
    // both the authority guard and the durable-change notifier.
    internal OrderLifecycleService(
        IOrderStore orders,
        IIdGenerator idGenerator,
        IBusinessClock clock,
        IOrderEntryCatalogueQueries? catalogue = null,
        IBusinessSettingsStore? settings = null)
        : this(orders, idGenerator, clock, TestOnlyAuthoritativeGuard.Instance,
            TestOnlyDurableChangeNotifier.Instance, catalogue, settings) { }

    internal OrderLifecycleService(
        IOrderStore orders,
        IIdGenerator idGenerator,
        IBusinessClock clock,
        IWriteAuthorityGuard authorityGuard,
        IOrderEntryCatalogueQueries? catalogue = null,
        IBusinessSettingsStore? settings = null)
        : this(orders, idGenerator, clock, authorityGuard,
            TestOnlyDurableChangeNotifier.Instance, catalogue, settings) { }

    public DateOnly BusinessDate => clock.BusinessDate;

    public async Task<OrderSnapshot?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        await orders.GetByIdAsync(orderId, cancellationToken);

    public async Task<IReadOnlyList<OrderBrowserRow>> BrowseByPlannedDateAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        await orders.ListByPlannedDateAsync(date, cancellationToken);

    public async Task<IReadOnlyList<OrderBrowserRow>> SearchLiveAsync(string? query, CancellationToken cancellationToken = default)
    {
        if (orders is IOrderLifecycleStore lifecycleStore)
            return await lifecycleStore.SearchAsync(query, cancellationToken);

        // This fallback keeps the application seam usable by small in-memory M04 test stores.
        var rows = await orders.ListByPlannedDateAsync(clock.BusinessDate, cancellationToken);
        if (string.IsNullOrWhiteSpace(query)) return rows;
        var text = query.Trim();
        return rows.Where(row => row.Reference.Contains(text, StringComparison.OrdinalIgnoreCase)
            || (row.Comment?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
            || TelephoneSearchNormalization.QueryTerms(text).Any(term => TelephoneSearchNormalization.Digits(row.Telephone).Contains(term, StringComparison.Ordinal))).ToArray();
    }

    public async Task<IReadOnlyList<OrderBrowserRow>> ListOperationalAsync(OperationalOrderView view, DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        var rows = await SearchLiveAsync(null, cancellationToken);
        return view switch
        {
            OperationalOrderView.Future => rows.Where(row => row.Status != OrderStatus.Cancelled && row.PlannedFulfilmentDate > businessDate).ToArray(),
            OperationalOrderView.DueToday => rows.Where(row => row.Status != OrderStatus.Cancelled && row.PlannedFulfilmentDate == businessDate && row.AdvanceOrderMarker).ToArray(),
            OperationalOrderView.OverdueUnsettled => rows.Where(row => row.Status != OrderStatus.Cancelled && row.PlannedFulfilmentDate < businessDate && !(row.Status == OrderStatus.Closed && row.DifferenceTtc == Money.Zero)).ToArray(),
            _ => []
        };
    }

    public async Task<OrderOperationalSummary> GetOperationalSummaryAsync(DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        if (orders is IOrderLifecycleStore lifecycleStore)
            return await lifecycleStore.GetOperationalSummaryAsync(businessDate, cancellationToken);

        return new OrderOperationalSummary(Money.Zero, Money.Zero, Money.Zero, Money.Zero, 0, 0, 0);
    }

    /// <summary>Builds one new immutable line from the current active Catalogue, never from historical values.</summary>
    public async Task<OrderItemSnapshot?> CreateCurrentCatalogueLineAsync(
        OrderLineDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (catalogue is null || settings is null || draft.Product.Product.Id == Guid.Empty) return null;
        var current = await catalogue.GetActiveProductAsync(draft.Product.Product.Id, cancellationToken);
        if (current is null) return null;
        var currentDraft = draft with { Product = current.Aggregate, CategoryName = current.CategoryName };
        var pricing = OrderPricingService.Calculate(
            new NewOrderDraft([currentDraft], FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0), null, null, null, false),
            await settings.GetAsync(cancellationToken));
        if (!pricing.IsValid || pricing.Lines.Count != 1) return null;
        var line = pricing.Lines[0];
        return new OrderItemSnapshot(
            idGenerator.NewId(), 0,
            current.Aggregate.Product.Id,
            CatalogueNormalization.Display(current.Aggregate.Product.Code),
            CatalogueNormalization.Display(current.Aggregate.Product.Name),
            CatalogueNormalization.Display(current.CategoryName),
            current.Aggregate.Product.PriceTtc,
            current.Aggregate.Product.VatRate,
            current.Aggregate.Product.DiscountEligible,
            line.Draft.Quantity,
            line.ExtendedBaseTtc,
            line.CalculatedLineTotalTtc,
            line.Adjustments.Select(adjustment => new OrderLineAdjustmentSnapshot(
                idGenerator.NewId(), adjustment.DisplayOrder, adjustment.Kind, adjustment.OptionId,
                CatalogueNormalization.Display(adjustment.GroupName), CatalogueNormalization.Display(adjustment.Label),
                adjustment.AmountTtcPerUnit, adjustment.VatRate)).ToArray());
    }

    /// <summary>
    /// Saves a proposed snapshot in place. The caller passes only the latest business state; payment deltas are derived
    /// from the previously committed cumulative values and are written in the same transaction as the snapshot.
    /// </summary>
    public async Task<OrderLifecycleResult> SaveModificationAsync(
        OrderSnapshot proposed,
        DateOnly? paymentEffectiveDate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        if (!await TryEnterAsync(cancellationToken))
            return OrderLifecycleResult.Failure(new ValidationIssue("order", "An order operation is already in progress.", ValidationCodes.Busy));

        try
        {
            var current = await orders.GetByIdAsync(proposed.Id, cancellationToken);
            if (current is null) return OrderLifecycleResult.Failure(new ValidationIssue("order", "The order was not found.", ValidationCodes.NotFound));
            if (current.Status == OrderStatus.Cancelled)
                return OrderLifecycleResult.Failure(new ValidationIssue("order", "A cancelled order cannot be modified.", ValidationCodes.Conflict));
            if (proposed.Id != current.Id || !string.Equals(proposed.Reference, current.Reference, StringComparison.Ordinal))
                return OrderLifecycleResult.Failure(new ValidationIssue("order", "The order identity and reference cannot change.", ValidationCodes.Conflict));

            var validation = ValidatePayment(proposed);
            if (validation is not null) return OrderLifecycleResult.Failure(validation);
            authorityGuard.RequireWriteAuthority();
            var scheduleValidation = ValidateSchedule(current, proposed);
            if (scheduleValidation is not null) return OrderLifecycleResult.Failure(scheduleValidation);

            var now = clock.UtcNow;
            var status = current.Status;
            var closedAt = current.ClosedAt;
            var stickyAdvance = current.AdvanceOrderMarker || proposed.PlannedFulfilmentDate > clock.BusinessDate;
            var normalized = proposed with
            {
                Reference = current.Reference,
                SourceType = current.SourceType,
                CreatedAt = current.CreatedAt,
                UpdatedAt = now,
                Status = status,
                ClosedAt = closedAt,
                CancelledAt = current.CancelledAt,
                AdvanceOrderMarker = stickyAdvance,
                Telephone = TelephoneNormalization.Normalize(proposed.Telephone),
                DeliveryAddress = NormalizeOptional(proposed.DeliveryAddress),
                Comment = NormalizeOptional(proposed.Comment)
            };
            var priceAffecting = current.Fulfilment != normalized.Fulfilment
                || !ItemsMatch(current.Items, normalized.Items)
                || current.PickupDiscountApplied != normalized.PickupDiscountApplied;
            if (priceAffecting)
            {
                if (settings is null)
                    return OrderLifecycleResult.Failure(new ValidationIssue("settings", "Current business settings are unavailable for price-affecting modification.", ValidationCodes.Generic));

                var repriced = OrderPricingService.CalculateSnapshots(
                    normalized.Items,
                    normalized.Fulfilment,
                    normalized.PickupDiscountApplied,
                    await settings.GetAsync(cancellationToken));
                if (!repriced.IsValid)
                    return OrderLifecycleResult.Failure(repriced.ValidationErrors.Select(message => new ValidationIssue("order", message, ValidationCodes.Generic)).ToArray());

                normalized = normalized with
                {
                    Items = repriced.Items,
                    TotalTtc = repriced.TotalTtc,
                    PickupDiscountApplied = repriced.PickupDiscountApplied,
                    PickupDiscountRate = repriced.PickupDiscountRate,
                    DeliveryFeeTtc = repriced.DeliveryFeeTtc,
                    TaxBreakdown = repriced.TaxBreakdown.Select(tax => tax with { Id = idGenerator.NewId() }).ToArray()
                };
                normalized = normalized with { ManualTotalOverrideActive = false };
            }
            if (current.Status == OrderStatus.Closed && !IsReconciled(normalized))
            {
                status = OrderStatus.Open;
                closedAt = null;
                normalized = normalized with { Status = status, ClosedAt = closedAt };
            }
            var adjustments = BuildPaymentAdjustments(current, normalized, paymentEffectiveDate ?? clock.BusinessDate, now);
            if (BusinessStateEqual(current, normalized) && adjustments.Count == 0)
                return new(true, current, []);
            await SaveAsync(normalized, adjustments, cancellationToken);
            return new(true, await orders.GetByIdAsync(current.Id, cancellationToken), []);
        }
        catch (OperationCanceledException) { throw; }
        catch (WriteAuthorityException exception)
        {
            return OrderLifecycleResult.Failure(AuthorityIssue(exception));
        }
        catch (Exception exception)
        {
            return OrderLifecycleResult.Failure(new ValidationIssue("order", exception.Message, ValidationCodes.Generic));
        }
        finally { Exit(); }
    }

    public async Task<OrderLifecycleResult> CloseAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        if (!await TryEnterAsync(cancellationToken))
            return OrderLifecycleResult.Failure(new ValidationIssue("order", "An order operation is already in progress.", ValidationCodes.Busy));
        try
        {
            var current = await orders.GetByIdAsync(orderId, cancellationToken);
            if (current is null) return OrderLifecycleResult.Failure(new ValidationIssue("order", "The order was not found.", ValidationCodes.NotFound));
            if (current.Status == OrderStatus.Cancelled) return OrderLifecycleResult.Failure(new ValidationIssue("order", "A cancelled order cannot be closed.", ValidationCodes.Conflict));
            if (current.Status == OrderStatus.Closed) return new(true, current, []);
            if (!IsReconciled(current)) return OrderLifecycleResult.Failure(new ValidationIssue("payment", "CB + Espèce must equal the order total exactly before Close.", ValidationCodes.PaymentMismatch));
            var closed = current with { Status = OrderStatus.Closed, ClosedAt = clock.UtcNow, UpdatedAt = clock.UtcNow };
            await SaveAsync(closed, [], cancellationToken);
            return new(true, await orders.GetByIdAsync(orderId, cancellationToken), []);
        }
        catch (OperationCanceledException) { throw; }
        catch (WriteAuthorityException exception)
        {
            return OrderLifecycleResult.Failure(AuthorityIssue(exception));
        }
        catch (Exception exception) { return OrderLifecycleResult.Failure(new ValidationIssue("order", exception.Message, ValidationCodes.Generic)); }
        finally { Exit(); }
    }

    public async Task<OrderLifecycleResult> CancelAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        if (!await TryEnterAsync(cancellationToken))
            return OrderLifecycleResult.Failure(new ValidationIssue("order", "An order operation is already in progress.", ValidationCodes.Busy));
        try
        {
            var current = await orders.GetByIdAsync(orderId, cancellationToken);
            if (current is null) return OrderLifecycleResult.Failure(new ValidationIssue("order", "The order was not found.", ValidationCodes.NotFound));
            if (current.Status == OrderStatus.Cancelled) return new(true, current, []);
            var cancelled = current with { Status = OrderStatus.Cancelled, CancelledAt = clock.UtcNow, UpdatedAt = clock.UtcNow };
            await SaveAsync(cancelled, [], cancellationToken);
            return new(true, await orders.GetByIdAsync(orderId, cancellationToken), []);
        }
        catch (OperationCanceledException) { throw; }
        catch (WriteAuthorityException exception)
        {
            return OrderLifecycleResult.Failure(AuthorityIssue(exception));
        }
        catch (Exception exception) { return OrderLifecycleResult.Failure(new ValidationIssue("order", exception.Message, ValidationCodes.Generic)); }
        finally { Exit(); }
    }

    private async Task SaveAsync(OrderSnapshot snapshot, IReadOnlyList<PaymentAdjustment> adjustments, CancellationToken cancellationToken)
    {
        authorityGuard.RequireWriteAuthority();
        if (orders is IOrderLifecycleStore lifecycleStore)
            await lifecycleStore.SaveLifecycleAsync(snapshot, adjustments, cancellationToken);
        else
            await orders.SaveAsync(snapshot, cancellationToken);
        try { await notifier.NotifyCommittedAsync(CancellationToken.None); }
        catch { /* The durable business commit remains; snapshot failure is non-rollback. */ }
    }

    private static ValidationIssue AuthorityIssue(WriteAuthorityException exception) =>
        new("authority", $"Local write authority is unavailable ({exception.State}).", ValidationCodes.AuthorityBlocked);

    private List<PaymentAdjustment> BuildPaymentAdjustments(OrderSnapshot before, OrderSnapshot after, DateOnly effectiveDate, DateTimeOffset recordedAt)
    {
        if (before.CardPaymentTtc == after.CardPaymentTtc && before.CashPaymentTtc == after.CashPaymentTtc) return [];
        var effective = AtBusinessDate(effectiveDate, recordedAt);
        var result = new List<PaymentAdjustment>(2);
        Add(PaymentBucket.Card, after.CardPaymentTtc - before.CardPaymentTtc);
        Add(PaymentBucket.Cash, after.CashPaymentTtc - before.CashPaymentTtc);
        return result;

        void Add(PaymentBucket bucket, Money delta)
        {
            if (delta != Money.Zero) result.Add(new(idGenerator.NewId(), before.Id, bucket, delta, effective, recordedAt));
        }
    }

    private DateTimeOffset AtBusinessDate(DateOnly date, DateTimeOffset recordedAt)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = clock.BusinessTimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static ValidationIssue? ValidatePayment(OrderSnapshot snapshot)
    {
        if (snapshot.CardPaymentTtc < Money.Zero) return new ValidationIssue("card", "CB cannot be negative.", ValidationCodes.PaymentNegative);
        if (snapshot.CashPaymentTtc < Money.Zero) return new ValidationIssue("cash", "Espèce cannot be negative.", ValidationCodes.PaymentNegative);
        return null;
    }

    private ValidationIssue? ValidateSchedule(OrderSnapshot current, OrderSnapshot snapshot)
    {
        if (snapshot.PlannedFulfilmentDate < clock.BusinessDate && snapshot.PlannedFulfilmentDate != current.PlannedFulfilmentDate)
            return new ValidationIssue("planned-date", "The planned fulfilment date cannot be in the past.", ValidationCodes.PastPlannedDate);
        if (snapshot.PlannedFulfilmentTime is { } time && !IsApprovedPlannedTime(time))
            return new ValidationIssue("planned-time", "The planned fulfilment time is not valid.", ValidationCodes.PlannedTimeInvalid);
        return null;
    }

    private static bool IsApprovedPlannedTime(TimeOnly time) =>
        time.Hour is 11 or 12 or 13 or 14 or 18 or 19 or 20 or 21 or 22 &&
        time.Minute % 5 == 0 && time.Ticks % TimeSpan.TicksPerMinute == 0;

    private static bool IsReconciled(OrderSnapshot snapshot) => OrderPaymentState.From(snapshot).IsExactlyReconciled;
    private static bool BusinessStateEqual(OrderSnapshot left, OrderSnapshot right) =>
        left.Status == right.Status
        && left.ClosedAt == right.ClosedAt
        && left.CancelledAt == right.CancelledAt
        && left.Fulfilment == right.Fulfilment
        && left.PlannedFulfilmentDate == right.PlannedFulfilmentDate
        && left.PlannedFulfilmentTime == right.PlannedFulfilmentTime
        && left.AdvanceOrderMarker == right.AdvanceOrderMarker
        && string.Equals(left.Telephone, right.Telephone, StringComparison.Ordinal)
        && string.Equals(left.DeliveryAddress, right.DeliveryAddress, StringComparison.Ordinal)
        && string.Equals(left.Comment, right.Comment, StringComparison.Ordinal)
        && left.TotalTtc == right.TotalTtc
        && left.ManualTotalOverrideActive == right.ManualTotalOverrideActive
        && left.PickupDiscountApplied == right.PickupDiscountApplied
        && left.PickupDiscountRate == right.PickupDiscountRate
        && left.DeliveryFeeTtc == right.DeliveryFeeTtc
        && left.CardPaymentTtc == right.CardPaymentTtc
        && left.CashPaymentTtc == right.CashPaymentTtc
        && left.Items.SequenceEqual(right.Items, OrderItemComparer.Instance)
        && left.TaxBreakdown.SequenceEqual(right.TaxBreakdown);

    private sealed class OrderItemComparer : IEqualityComparer<OrderItemSnapshot>
    {
        public static OrderItemComparer Instance { get; } = new();
        public bool Equals(OrderItemSnapshot? left, OrderItemSnapshot? right) => left is not null && right is not null
            && left.Id == right.Id && left.Position == right.Position && left.SourceProductId == right.SourceProductId
            && left.ProductCode == right.ProductCode && left.ProductName == right.ProductName && left.CategoryName == right.CategoryName
            && left.ProductBasePriceTtc == right.ProductBasePriceTtc && left.ProductVatRate == right.ProductVatRate
            && left.ProductDiscountEligible == right.ProductDiscountEligible && left.Quantity == right.Quantity
            && left.ExtendedBaseTtc == right.ExtendedBaseTtc && left.CalculatedLineTotalTtc == right.CalculatedLineTotalTtc
            && left.Adjustments.SequenceEqual(right.Adjustments);
        public int GetHashCode(OrderItemSnapshot value) => value.Id.GetHashCode();
    }
    private static bool ItemsMatch(IReadOnlyList<OrderItemSnapshot> left, IReadOnlyList<OrderItemSnapshot> right) =>
        left.Count == right.Count && left.OrderBy(item => item.Position).Zip(right.OrderBy(item => item.Position)).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Position == pair.Second.Position && pair.First.SourceProductId == pair.Second.SourceProductId
            && pair.First.ProductCode == pair.Second.ProductCode && pair.First.ProductName == pair.Second.ProductName && pair.First.CategoryName == pair.Second.CategoryName
            && pair.First.ProductBasePriceTtc == pair.Second.ProductBasePriceTtc && pair.First.ProductVatRate == pair.Second.ProductVatRate
            && pair.First.ProductDiscountEligible == pair.Second.ProductDiscountEligible && pair.First.Quantity == pair.Second.Quantity
            && pair.First.ExtendedBaseTtc == pair.Second.ExtendedBaseTtc && pair.First.CalculatedLineTotalTtc == pair.Second.CalculatedLineTotalTtc
            && pair.First.Adjustments.SequenceEqual(pair.Second.Adjustments));
    private static string? NormalizeOptional(string? value) { var trimmed = (value ?? string.Empty).Trim(); return trimmed.Length == 0 ? null : trimmed; }

    private async Task<bool> TryEnterAsync(CancellationToken cancellationToken)
    {
        lock (this)
        {
            if (disposed) return false;
            activeCalls++;
        }
        bool acquired;
        try { acquired = await mutationGate.WaitAsync(0, cancellationToken); }
        catch { Exit(); throw; }
        if (!acquired) { Exit(); return false; }
        return true;
    }

    private void Exit()
    {
        if (mutationGate.CurrentCount == 0) mutationGate.Release();
        lock (this)
        {
            activeCalls--;
            if (disposed && activeCalls == 0) mutationGate.Dispose();
        }
    }

    public void Dispose()
    {
        lock (this)
        {
            if (disposed) return;
            disposed = true;
            if (activeCalls == 0) mutationGate.Dispose();
        }
    }
}
