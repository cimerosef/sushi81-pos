using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Export;

public enum ExportAction
{
    Create,
    Update,
    Cancel
}

public enum ExportBatchStatus
{
    Prepared,
    Success
}

public sealed record ExportDateRange
{
    public ExportDateRange(DateOnly? startDate = null, DateOnly? endDate = null)
    {
        if (startDate is not null && endDate is not null && startDate > endDate)
            throw new ArgumentException("The export start date cannot be after the end date.");

        StartDate = startDate;
        EndDate = endDate;
    }

    public DateOnly? StartDate { get; }
    public DateOnly? EndDate { get; }

    public bool Includes(DateOnly date) =>
        (StartDate is null || date >= StartDate.Value) &&
        (EndDate is null || date <= EndDate.Value);
}

public sealed record ExportSelectionOptions(DateOnly? StartDate = null, DateOnly? EndDate = null)
{
    public ExportDateRange DateRange => new(StartDate, EndDate);
}

/// <summary>A persisted order plus its signed effective-dated payment facts.</summary>
public sealed record ExportOrderSourceRecord(
    OrderSnapshot Snapshot,
    IReadOnlyList<PaymentAdjustment> PaymentAdjustments);

/// <summary>The fixed V1 positive order payload. Values remain cent-precise until workbook generation.</summary>
public sealed record ExportOrderPayload(
    ExportAction Action,
    Guid OrderId,
    string OrderStatus,
    DateTimeOffset CreatedAt,
    DateOnly FulfilmentDate,
    TimeOnly? FulfilmentTime,
    DateOnly SettlementDate,
    string FulfilmentMode,
    long TotalTtcCents,
    long CardTotalCents,
    long CashTotalCents,
    string? Telephone,
    string? Address,
    string? Comment,
    IReadOnlyList<ExportLinePayload> Lines,
    IReadOnlyList<ExportTaxPayload> TaxBreakdown)
{
    public ExportOrderPayload WithAction(ExportAction action, string? orderStatus = null) =>
        this with { Action = action, OrderStatus = orderStatus ?? OrderStatus };
}

public sealed record ExportLinePayload(
    Guid OrderId,
    int LineNo,
    string ProductCode,
    string ProductName,
    int Quantity,
    long UnitBaseTtcCents,
    long OptionAdjustmentTtcCents,
    long LineTtcCents,
    decimal VatRate,
    string OptionsSummary);

public sealed record ExportTaxPayload(
    Guid OrderId,
    decimal VatRate,
    long TaxableHtCents,
    long VatAmountCents,
    long TtcCents);

public sealed record ExportBatchMeta(
    string SchemaVersion,
    Guid BatchId,
    DateTimeOffset GeneratedAt,
    string AppVersion,
    DateOnly? FilterStartDate,
    DateOnly? FilterEndDate,
    int OrderCount,
    int OrderLineCount,
    int TaxBreakdownCount);

public sealed record ExportBatchPayload(
    ExportBatchMeta Meta,
    IReadOnlyList<ExportOrderPayload> Orders);

public sealed record ExportBatchRecord(
    ExportBatchPayload Payload,
    ExportBatchStatus Status,
    string PayloadHash,
    DateTimeOffset? CompletedAtUtc = null);

public sealed record ExportEmissionRecord(
    Guid BatchId,
    Guid OrderId,
    ExportAction Action,
    string PositiveSnapshotHash,
    ExportOrderPayload PositivePayload,
    DateOnly FulfilmentDate,
    DateOnly SettlementDate,
    DateTimeOffset EmittedAtUtc);

public enum ExportDiagnosticSeverity
{
    Blocking,
    Informational
}

public sealed record ExportSelectionDiagnostic(
    Guid OrderId,
    ExportDiagnosticSeverity Severity,
    string Code,
    string Message);

public sealed record ExportSelectionResult(
    IReadOnlyList<ExportOrderPayload> Actions,
    IReadOnlyList<ExportSelectionDiagnostic> Diagnostics)
{
    public bool IsBlocked => Diagnostics.Any(diagnostic => diagnostic.Severity == ExportDiagnosticSeverity.Blocking);
}

public sealed record ExportBatchPreparationResult(
    ExportSelectionResult Selection,
    ExportBatchRecord? Batch);

public interface IExportOrderSourceReader
{
    Task<IReadOnlyList<ExportOrderSourceRecord>> ListExportOrderSourcesAsync(CancellationToken cancellationToken = default);
}

public interface IExportLedgerStore
{
    Task<IReadOnlyList<ExportEmissionRecord>> ListLatestSuccessfulEmissionsAsync(CancellationToken cancellationToken = default);
    Task PrepareBatchAsync(ExportBatchRecord batch, CancellationToken cancellationToken = default);
    Task MarkBatchSucceededAsync(Guid batchId, IReadOnlyList<ExportEmissionRecord> emissions, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default);
}

public static class ExportPayloadSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    static ExportPayloadSerializer()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public static string SerializeBatch(ExportBatchPayload payload) =>
        JsonSerializer.Serialize(payload ?? throw new ArgumentNullException(nameof(payload)), JsonOptions);

    public static string SerializePositive(ExportOrderPayload payload) =>
        JsonSerializer.Serialize(payload with { Action = ExportAction.Create }, JsonOptions);

    public static string ComputeSha256(string canonicalJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
    }

    public static ExportOrderPayload DeserializePositive(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var payload = JsonSerializer.Deserialize<ExportOrderPayload>(json, JsonOptions)
            ?? throw new InvalidDataException("The durable export positive snapshot payload is empty.");
        return payload with { Action = ExportAction.Create };
    }
}

public static class ExportSelectionRules
{
    public static ExportSelectionResult Select(
        IReadOnlyList<ExportOrderSourceRecord> sources,
        IReadOnlyList<ExportEmissionRecord> latestSuccessfulEmissions,
        ExportSelectionOptions options,
        TimeZoneInfo businessTimeZone)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(latestSuccessfulEmissions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(businessTimeZone);

        var range = options.DateRange;
        var history = latestSuccessfulEmissions.ToDictionary(emission => emission.OrderId);
        var actions = new List<ExportOrderPayload>();
        var diagnostics = new List<ExportSelectionDiagnostic>();

        foreach (var source in sources.OrderBy(item => item.Snapshot.Id.ToString("D"), StringComparer.Ordinal))
        {
            var snapshot = source.Snapshot;
            if (snapshot.SourceType != OrderSourceType.Pos)
                continue;

            history.TryGetValue(snapshot.Id, out var lastEmission);
            if (lastEmission is null)
            {
                if (snapshot.Status != OrderStatus.Closed)
                    continue;

                if (!TryBuildPositivePayload(source, businessTimeZone, out var positive, out var diagnostic))
                {
                    diagnostics.Add(diagnostic!);
                    continue;
                }

                if (range.Includes(positive.FulfilmentDate))
                    actions.Add(positive.WithAction(ExportAction.Create));
                continue;
            }

            if (lastEmission.Action == ExportAction.Cancel)
                continue;

            if (snapshot.Status == OrderStatus.Cancelled)
            {
                if (range.Includes(lastEmission.FulfilmentDate))
                    actions.Add(lastEmission.PositivePayload.WithAction(ExportAction.Cancel, "CANCELLED"));
                continue;
            }

            if (snapshot.Status != OrderStatus.Closed)
                continue;

            if (!TryBuildPositivePayload(source, businessTimeZone, out var currentPositive, out var currentDiagnostic))
            {
                diagnostics.Add(currentDiagnostic!);
                continue;
            }

            var currentHash = ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializePositive(currentPositive));
            if (!string.Equals(currentHash, lastEmission.PositiveSnapshotHash, StringComparison.Ordinal)
                && range.Includes(currentPositive.FulfilmentDate))
                actions.Add(currentPositive.WithAction(ExportAction.Update));
        }

        return new ExportSelectionResult(actions, diagnostics);
    }

    public static bool TryBuildPositivePayload(
        ExportOrderSourceRecord source,
        TimeZoneInfo businessTimeZone,
        out ExportOrderPayload payload,
        out ExportSelectionDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(businessTimeZone);
        payload = null!;
        diagnostic = null;
        var snapshot = source.Snapshot;

        if (!TryResolveSettlementDate(snapshot, source.PaymentAdjustments, businessTimeZone, out var settlementDate, out var reason))
        {
            diagnostic = new ExportSelectionDiagnostic(snapshot.Id, ExportDiagnosticSeverity.Blocking, "SETTLEMENT_DATE_UNAVAILABLE", reason!);
            return false;
        }

        var lines = snapshot.Items
            .OrderBy(item => item.Position)
            .ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .Select(item => new ExportLinePayload(
                snapshot.Id,
                item.Position,
                item.ProductCode,
                item.ProductName,
                item.Quantity,
                item.ProductBasePriceTtc.Cents,
                checked(item.Adjustments.Sum(adjustment => checked(adjustment.AdjustmentTtcPerUnit.Cents * (long)item.Quantity))),
                item.CalculatedLineTotalTtc.Cents,
                item.ProductVatRate,
                string.Join("; ", item.Adjustments
                    .OrderBy(adjustment => adjustment.DisplayOrder)
                    .ThenBy(adjustment => adjustment.Id.ToString("D"), StringComparer.Ordinal)
                    .Select(adjustment => string.IsNullOrWhiteSpace(adjustment.GroupName)
                        ? adjustment.Label
                        : $"{adjustment.GroupName}: {adjustment.Label}"))))
            .ToArray();
        var taxes = snapshot.TaxBreakdown
            .OrderBy(tax => tax.VatRate)
            .Select(tax => new ExportTaxPayload(snapshot.Id, tax.VatRate, checked(tax.TaxableTtc.Cents - tax.IncludedVatTtc.Cents), tax.IncludedVatTtc.Cents, tax.TaxableTtc.Cents))
            .ToArray();

        payload = new ExportOrderPayload(
            ExportAction.Create,
            snapshot.Id,
            snapshot.Status switch
            {
                OrderStatus.Closed => "CLOSED",
                OrderStatus.Cancelled => "CANCELLED",
                OrderStatus.Open => "OPEN",
                _ => throw new InvalidOperationException("The order status is not supported for export.")
            },
            snapshot.CreatedAt,
            snapshot.PlannedFulfilmentDate,
            snapshot.PlannedFulfilmentTime,
            settlementDate,
            snapshot.Fulfilment switch
            {
                FulfilmentMode.Retrait => "RETRAIT",
                FulfilmentMode.Livraison => "LIVRAISON",
                _ => throw new InvalidOperationException("The fulfilment mode is not supported for export.")
            },
            snapshot.TotalTtc.Cents,
            snapshot.CardPaymentTtc.Cents,
            snapshot.CashPaymentTtc.Cents,
            snapshot.Telephone,
            snapshot.DeliveryAddress,
            snapshot.Comment,
            lines,
            taxes);
        return true;
    }

    private static bool TryResolveSettlementDate(
        OrderSnapshot snapshot,
        IReadOnlyList<PaymentAdjustment> adjustments,
        TimeZoneInfo businessTimeZone,
        out DateOnly settlementDate,
        out string? reason)
    {
        settlementDate = default;
        reason = null;
        if (snapshot.Status != OrderStatus.Closed)
        {
            reason = "SettlementDate can only be derived for a Closed order.";
            return false;
        }

        if (snapshot.TotalTtc.Cents < 0)
        {
            reason = "A negative authoritative order total cannot be exported.";
            return false;
        }

        if (snapshot.TotalTtc.Cents == 0)
        {
            if (snapshot.ClosedAt is not { } closedAt)
            {
                reason = "A zero-total Closed order must have ClosedAt for SettlementDate.";
                return false;
            }

            settlementDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(closedAt, businessTimeZone).Date);
            return true;
        }

        if (snapshot.CardPaymentTtc + snapshot.CashPaymentTtc != snapshot.TotalTtc)
        {
            reason = "The Closed order payment totals do not reconcile to the authoritative order total.";
            return false;
        }

        var relevant = adjustments
            .Where(adjustment => adjustment.OrderId == snapshot.Id && adjustment.Bucket is PaymentBucket.Card or PaymentBucket.Cash)
            .GroupBy(adjustment => adjustment.EffectiveBusinessDate)
            .OrderBy(group => group.Key)
            .ToArray();
        var cumulative = 0L;
        DateOnly? firstReached = null;
        foreach (var group in relevant)
        {
            try
            {
                cumulative = checked(cumulative + group.Sum(adjustment => adjustment.Delta.Cents));
            }
            catch (OverflowException)
            {
                reason = "Payment adjustment totals overflow the supported cent range.";
                return false;
            }

            if (cumulative == snapshot.TotalTtc.Cents && firstReached is null)
                firstReached = group.Key;
        }

        if (cumulative != snapshot.TotalTtc.Cents || firstReached is null)
        {
            reason = "The effective-dated payment ledger does not derive a consistent SettlementDate for this Closed order.";
            return false;
        }

        settlementDate = firstReached.Value;
        return true;
    }
}

public sealed class GestionExportService(
    IExportOrderSourceReader sourceReader,
    IExportLedgerStore ledger,
    IBusinessClock clock,
    IWriteAuthorityGuard authorityGuard,
    IDurableChangeNotifier notifier,
    IIdGenerator idGenerator)
{
    private readonly IExportOrderSourceReader sourceReader = sourceReader ?? throw new ArgumentNullException(nameof(sourceReader));
    private readonly IExportLedgerStore ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    private readonly IBusinessClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IWriteAuthorityGuard authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
    private readonly IDurableChangeNotifier notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    private readonly IIdGenerator idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));

    public async Task<ExportSelectionResult> SelectAsync(ExportSelectionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sources = await sourceReader.ListExportOrderSourcesAsync(cancellationToken);
        var history = await ledger.ListLatestSuccessfulEmissionsAsync(cancellationToken);
        return ExportSelectionRules.Select(sources, history, options, clock.BusinessTimeZone);
    }

    public async Task<ExportBatchPreparationResult> PrepareBatchAsync(
        ExportSelectionOptions options,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        await using var authorityScope = await authorityGuard.EnterWriteScopeAsync(cancellationToken);
        var selection = await SelectAsync(options, cancellationToken);
        if (selection.IsBlocked || selection.Actions.Count == 0)
            return new ExportBatchPreparationResult(selection, null);

        var batchId = idGenerator.NewId();
        var orders = selection.Actions
            .OrderBy(action => action.OrderId.ToString("D"), StringComparer.Ordinal)
            .ThenBy(action => action.Action)
            .ToArray();
        var payload = new ExportBatchPayload(
            new ExportBatchMeta(
                "1.0",
                batchId,
                clock.UtcNow,
                appVersion,
                options.StartDate,
                options.EndDate,
                orders.Length,
                orders.Sum(order => order.Lines.Count),
                orders.Sum(order => order.TaxBreakdown.Count)),
            orders);
        var hash = ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializeBatch(payload));
        var batch = new ExportBatchRecord(payload, ExportBatchStatus.Prepared, hash);
        await ledger.PrepareBatchAsync(batch, cancellationToken);
        await notifier.NotifyCommittedAsync(cancellationToken);
        return new ExportBatchPreparationResult(selection, batch);
    }

    public async Task MarkBatchSucceededAsync(
        ExportBatchRecord batch,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Status != ExportBatchStatus.Prepared)
            throw new ArgumentException("Only a prepared batch can be marked successful.", nameof(batch));

        await using var authorityScope = await authorityGuard.EnterWriteScopeAsync(cancellationToken);
        var emissions = batch.Payload.Orders.Select(order =>
        {
            var positive = order with { Action = ExportAction.Create };
            return new ExportEmissionRecord(
                batch.Payload.Meta.BatchId,
                order.OrderId,
                order.Action,
                ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializePositive(positive)),
                positive,
                positive.FulfilmentDate,
                positive.SettlementDate,
                completedAtUtc);
        }).ToArray();
        await ledger.MarkBatchSucceededAsync(batch.Payload.Meta.BatchId, emissions, completedAtUtc, cancellationToken);
        await notifier.NotifyCommittedAsync(cancellationToken);
    }
}
