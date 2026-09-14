using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Foundation.Transactions;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Application.Printing;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.OrderEntry;

public interface IOrderEntryCatalogueQueries
{
    Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? categoryId = null, CancellationToken cancellationToken = default);
    Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default);

    async Task<OrderEntryProduct?> GetActiveProductByCodeAsync(string? productCode, CancellationToken cancellationToken = default)
    {
        var key = CatalogueNormalization.Key(productCode);
        if (key.Length == 0) return null;
        var match = (await ListActiveProductsAsync(cancellationToken: cancellationToken))
            .SingleOrDefault(product => string.Equals(CatalogueNormalization.Key(product.Code), key, StringComparison.Ordinal));
        return match is null ? null : await GetActiveProductAsync(match.Id, cancellationToken);
    }
}

public sealed record OrderEntryProduct(ProductAggregate Aggregate, string CategoryName);

public sealed class OrderEntryCatalogueService(ICatalogueQueries catalogue) : IOrderEntryCatalogueQueries
{
    private readonly ICatalogueQueries catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));

    public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => catalogue.ListCategoriesAsync(cancellationToken);

    public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? categoryId = null, CancellationToken cancellationToken = default) =>
        catalogue.ListProductsAsync(search, categoryId, true, cancellationToken);

    public async Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var draft = await catalogue.GetProductForEditAsync(productId, cancellationToken);
        return draft is null || !draft.IsActive ? null : await BuildProductAsync(draft, cancellationToken);
    }

    public async Task<OrderEntryProduct?> GetActiveProductByCodeAsync(string? productCode, CancellationToken cancellationToken = default)
    {
        var key = CatalogueNormalization.Key(productCode);
        if (key.Length == 0) return null;
        var draft = catalogue is IActiveProductCodeQueries exactQueries
            ? await exactQueries.GetActiveProductByCodeAsync(productCode, cancellationToken)
            : await ResolveFromActiveSummariesAsync(key, cancellationToken);
        return draft is null || !draft.IsActive ? null : await BuildProductAsync(draft, cancellationToken);
    }

    private async Task<ProductDraft?> ResolveFromActiveSummariesAsync(string key, CancellationToken cancellationToken)
    {
        var match = (await catalogue.ListProductsAsync(active: true, cancellationToken: cancellationToken))
            .SingleOrDefault(product => string.Equals(CatalogueNormalization.Key(product.Code), key, StringComparison.Ordinal));
        return match is null ? null : await catalogue.GetProductForEditAsync(match.Id, cancellationToken);
    }

    private async Task<OrderEntryProduct> BuildProductAsync(ProductDraft draft, CancellationToken cancellationToken)
    {
        var categories = await catalogue.ListCategoriesAsync(cancellationToken);
        var categoryName = categories.FirstOrDefault(category => category.Id == draft.CategoryId)?.Name ?? string.Empty;
        var product = new Product(
            draft.Id, draft.Code, draft.Name, draft.CategoryId, draft.PriceTtc, draft.VatRate, draft.IsActive,
            draft.DiscountEligible, draft.OptionsEnabled, default, default);
        var groups = (draft.Groups ?? []).Select(group => new OptionGroup(
            group.Id, draft.Id, group.Name, group.SelectionMode, group.IsRequired, group.MinSelections, group.MaxSelections,
            group.DisplayOrder, default, default)).ToArray();
        var options = new Dictionary<Guid, IReadOnlyList<ProductOption>>();
        foreach (var group in draft.Groups ?? [])
        {
            options[group.Id] = (group.Options ?? []).Select(option => new ProductOption(
                option.Id, group.Id, option.Name, option.PriceAdjustmentTtc, option.IsActive, option.DisplayOrder, default, default)).ToArray();
        }

        return new OrderEntryProduct(new ProductAggregate(product, groups, options), categoryName);
    }
}

public interface IOrderStore
{
    Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default);
}

public interface IOrderPrintDispatcher
{
    Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default);
}

public sealed record ConfirmOrderResult(
    bool PersistenceSucceeded,
    bool DispatchSucceeded,
    OrderSnapshot? CommittedOrder,
    IReadOnlyList<ValidationIssue> Issues)
{
    public Guid? PersistedOrderId { get; init; }
    public PrintDispatchResult? Output { get; init; }
    public bool Succeeded => PersistenceSucceeded;
    public bool OutputSucceeded => DispatchSucceeded;
    public bool HasOutputFailure => PersistenceSucceeded && !DispatchSucceeded;

    public static ConfirmOrderResult Failure(params ValidationIssue[] issues) => new(false, false, null, issues);
}

/// <summary>Application-owned M04 order-entry orchestration.</summary>
public sealed class OrderEntryService(
    IOrderEntryCatalogueQueries catalogue,
    IBusinessSettingsStore settings,
    IOrderStore orders,
    IOrderPrintDispatcher dispatcher,
    IIdGenerator idGenerator,
    IBusinessClock clock,
    IWriteAuthorityGuard authorityGuard,
    IDurableChangeNotifier notifier) : IDisposable
{
    private readonly IOrderEntryCatalogueQueries catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
    private readonly IBusinessSettingsStore settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IOrderStore orders = orders ?? throw new ArgumentNullException(nameof(orders));
    private readonly IOrderPrintDispatcher dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    private readonly IIdGenerator idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
    private readonly IBusinessClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IWriteAuthorityGuard authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
    private readonly IDurableChangeNotifier notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    private readonly SemaphoreSlim confirmationGate = new(1, 1);
    private readonly object lifecycleLock = new();
    private int activeConfirmationCalls;
    private bool disposed;

    // Test assemblies use explicit internal test-only wiring. Production composition must provide
    // both the authority guard and the durable-change notifier.
    internal OrderEntryService(
        IOrderEntryCatalogueQueries catalogue,
        IBusinessSettingsStore settings,
        IOrderStore orders,
        IOrderPrintDispatcher dispatcher,
        IIdGenerator idGenerator,
        IBusinessClock clock)
        : this(catalogue, settings, orders, dispatcher, idGenerator, clock,
            TestOnlyAuthoritativeGuard.Instance, TestOnlyDurableChangeNotifier.Instance) { }

    public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => catalogue.ListCategoriesAsync(cancellationToken);
    public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? categoryId = null, CancellationToken cancellationToken = default) => catalogue.ListActiveProductsAsync(search, categoryId, cancellationToken);
    public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default) => catalogue.GetActiveProductAsync(productId, cancellationToken);
    public Task<OrderEntryProduct?> GetActiveProductByCodeAsync(string? productCode, CancellationToken cancellationToken = default) => catalogue.GetActiveProductByCodeAsync(productCode, cancellationToken);

    public async Task<OrderPricingResult> PriceAsync(NewOrderDraft draft, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return OrderPricingService.Calculate(draft, await settings.GetAsync(cancellationToken));
    }

    /// <summary>Calculates a draft against an explicitly supplied settings snapshot.</summary>
    public static OrderPricingResult PriceWithSettings(NewOrderDraft draft, BusinessSettings businessSettings)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(businessSettings);
        return OrderPricingService.Calculate(draft, businessSettings);
    }

    public async Task<ConfirmOrderResult> ConfirmNewOrderAsync(NewOrderDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        lock (lifecycleLock)
        {
            if (disposed) return ConfirmOrderResult.Failure(new ValidationIssue("order", "The order entry service is unavailable.", ValidationCodes.Generic));
            activeConfirmationCalls++;
        }

        var acquired = false;
        try
        {
            if (!await confirmationGate.WaitAsync(0, cancellationToken))
                return ConfirmOrderResult.Failure(new ValidationIssue("order", "An order confirmation is already in progress.", ValidationCodes.Busy));
            acquired = true;
            await using var authorityScope = await authorityGuard.EnterWriteScopeAsync(cancellationToken);
            var businessSettings = await settings.GetAsync(cancellationToken);
            var currentLines = new List<OrderLineDraft>();
            foreach (var line in draft.Lines ?? [])
            {
                if (line.Product.Product.Id == Guid.Empty)
                    return ConfirmOrderResult.Failure(new ValidationIssue("product", "A current Catalogue product is required for a POS order line.", ValidationCodes.ProductMissing));

                var current = await catalogue.GetActiveProductAsync(line.Product.Product.Id, cancellationToken);
                if (current is null || current.Aggregate.Product.Id != line.Product.Product.Id)
                    return ConfirmOrderResult.Failure(new ValidationIssue("product", $"Product '{line.Product.Product.Name}' is no longer active or no longer exists.", ValidationCodes.ProductMissing));
                currentLines.Add(line with { Product = current.Aggregate, CategoryName = current.CategoryName });
            }
            var normalizedDraft = draft with
            {
                Lines = currentLines,
                Telephone = TelephoneNormalization.Normalize(draft.Telephone),
                DeliveryAddress = NormalizeOptional(draft.DeliveryAddress),
                Comment = NormalizeOptional(draft.Comment)
            };
            var pricing = OrderPricingService.Calculate(normalizedDraft, businessSettings);
            if (!pricing.IsValid)
            {
                var issues = pricing.ValidationErrors.Select(message => new ValidationIssue("order", message, ValidationCodes.Generic)).ToArray();
                return ConfirmOrderResult.Failure(issues);
            }

            if (normalizedDraft.Fulfilment is null || normalizedDraft.PlannedFulfilmentDate is null)
                return ConfirmOrderResult.Failure(new ValidationIssue("order", "Fulfilment mode and planned date are required.", ValidationCodes.Required));

            if (normalizedDraft.PlannedFulfilmentDate.Value < clock.BusinessDate)
                return ConfirmOrderResult.Failure(new ValidationIssue("order", "The planned fulfilment date cannot be in the past.", ValidationCodes.PastPlannedDate));

            if (normalizedDraft.PlannedFulfilmentTime is { } plannedTime && !IsApprovedPlannedTime(plannedTime))
                return ConfirmOrderResult.Failure(new ValidationIssue("order", "The planned fulfilment time is not valid.", ValidationCodes.PlannedTimeInvalid));

            var now = clock.UtcNow;
            var orderId = idGenerator.NewId();
            var itemSnapshots = pricing.Lines.Select((line, index) => new OrderItemSnapshot(
                idGenerator.NewId(), index,
                line.Draft.Product.Product.Id == Guid.Empty ? null : line.Draft.Product.Product.Id,
                CatalogueNormalization.Display(line.Draft.Product.Product.Code),
                CatalogueNormalization.Display(line.Draft.Product.Product.Name),
                CatalogueNormalization.Display(line.Draft.CategoryName),
                line.Draft.Product.Product.PriceTtc,
                line.Draft.Product.Product.VatRate,
                line.Draft.Product.Product.DiscountEligible,
                line.Draft.Quantity,
                line.ExtendedBaseTtc,
                line.CalculatedLineTotalTtc,
                line.Adjustments.Select(adjustment => new OrderLineAdjustmentSnapshot(
                    idGenerator.NewId(), adjustment.DisplayOrder, adjustment.Kind, adjustment.OptionId,
                    CatalogueNormalization.Display(adjustment.GroupName), CatalogueNormalization.Display(adjustment.Label),
                    adjustment.AmountTtcPerUnit, adjustment.VatRate)).ToArray())).ToArray();
            var snapshot = new OrderSnapshot(
                orderId, OrderSourceType.Pos, OrderStatus.Open, now, now, null, null,
                normalizedDraft.Fulfilment.Value, normalizedDraft.PlannedFulfilmentDate.Value, normalizedDraft.PlannedFulfilmentTime,
                normalizedDraft.PlannedFulfilmentDate.Value > clock.BusinessDate,
                normalizedDraft.Telephone, normalizedDraft.DeliveryAddress, normalizedDraft.Comment,
                pricing.TotalTtc, pricing.ManualTotalOverrideActive, pricing.PickupDiscountApplied, pricing.PickupDiscountRate,
                pricing.DeliveryFeeTtc, itemSnapshots,
                pricing.TaxBreakdown.Select(tax => tax with { Id = idGenerator.NewId() }).ToArray());

            await orders.SaveAsync(snapshot, cancellationToken);
            try { await notifier.NotifyCommittedAsync(CancellationToken.None); }
            catch { /* Snapshot scheduling cannot undo a committed order. */ }
            OrderSnapshot? committed;
            try { committed = await orders.GetByIdAsync(orderId, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                return new ConfirmOrderResult(true, false, null, [new ValidationIssue("order", $"The order was committed but could not be reloaded: {exception.Message}", ValidationCodes.Generic)]) { PersistedOrderId = orderId };
            }
            if (committed is null)
                return new ConfirmOrderResult(true, false, null, [new ValidationIssue("order", "The order was committed but could not be reloaded.", ValidationCodes.Generic)]) { PersistedOrderId = orderId };
            try
            {
                if (dispatcher is IOrderPrintOutcomeDispatcher outputDispatcher)
                {
                    var output = await outputDispatcher.DispatchInitialAsync(committed, PrintIntent.InitialAutomatic, cancellationToken);
                    return new(true, output.Succeeded, committed, output.Issues)
                    {
                        PersistedOrderId = orderId,
                        Output = output
                    };
                }

                await dispatcher.DispatchAsync(committed, cancellationToken);
                return new(true, true, committed, []);
            }
            catch (Exception exception)
            {
                return new(true, false, committed, [new ValidationIssue("output", $"The order was saved but output dispatch failed: {exception.Message}", ValidationCodes.Generic)]);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (WriteAuthorityException exception)
        {
            return ConfirmOrderResult.Failure(new ValidationIssue("authority", $"Local write authority is unavailable ({exception.State}).", ValidationCodes.AuthorityBlocked));
        }
        catch (Exception exception)
        {
            return ConfirmOrderResult.Failure(new ValidationIssue("order", exception.Message, ValidationCodes.Generic));
        }
        finally
        {
            if (acquired) confirmationGate.Release();
            lock (lifecycleLock)
            {
                activeConfirmationCalls--;
                if (disposed && activeConfirmationCalls == 0) confirmationGate.Dispose();
            }
        }
    }

    public Task<OrderSnapshot?> GetOrderByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => orders.GetByIdAsync(orderId, cancellationToken);
    public Task<IReadOnlyList<OrderBrowserRow>> ListOrdersByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => orders.ListByPlannedDateAsync(plannedDate, cancellationToken);

    /// <summary>
    /// Retries one known failed initial output from the latest committed order. Ambiguous
    /// submissions must use the explicit reprint path because another physical copy may exist.
    /// </summary>
    public async Task<PrintDocumentResult> RetryInitialPrintAsync(Guid orderId, PrintDocumentKind kind, CancellationToken cancellationToken = default)
    {
        var committed = await orders.GetByIdAsync(orderId, cancellationToken);
        if (committed is null) return new(kind, PrintOutcomeStatus.OrderNotFound, "The committed order could not be found.");
        if (dispatcher is not IOrderPrintOutcomeDispatcher output)
            return new(kind, PrintOutcomeStatus.Unsupported, "Printing is not configured on this device.");
        return await output.PrintDocumentAsync(committed, kind, PrintIntent.InitialRetry, cancellationToken);
    }

    public DateOnly BusinessDate => clock.BusinessDate;

    private static string? NormalizeOptional(string? value)
    {
        var display = (value ?? string.Empty).Trim();
        return display.Length == 0 ? null : display;
    }

    private static bool IsApprovedPlannedTime(TimeOnly time) =>
        time.Hour is 11 or 12 or 13 or 14 or 18 or 19 or 20 or 21 or 22 &&
        time.Minute % 5 == 0 &&
        time.Ticks % TimeSpan.TicksPerMinute == 0;

    public void Dispose()
    {
        lock (lifecycleLock)
        {
            if (disposed) return;
            disposed = true;
            if (activeConfirmationCalls == 0) confirmationGate.Dispose();
        }
    }
}
