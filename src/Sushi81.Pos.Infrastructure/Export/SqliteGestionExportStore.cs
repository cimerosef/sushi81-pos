using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Export;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Transactions;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Order;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.Export;

/// <summary>
/// Reads the authoritative order/payment snapshots and stores M11 export state in the same live SQLite database.
/// It deliberately contains no ClosedXML or Desktop dependency.
/// </summary>
public sealed class SqliteGestionExportStore(
    SqliteConnectionFactory connectionFactory,
    SqliteOrderStore orderStore,
    ITransactionRunner transactionRunner,
    IIdGenerator idGenerator) : IExportOrderSourceReader, IExportLedgerStore
{
    private readonly SqliteConnectionFactory connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    private readonly SqliteOrderStore orderStore = orderStore ?? throw new ArgumentNullException(nameof(orderStore));
    private readonly ITransactionRunner transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    private readonly IIdGenerator idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));

    public async Task<IReadOnlyList<ExportOrderSourceRecord>> ListExportOrderSourcesAsync(CancellationToken cancellationToken = default)
    {
        var orderIds = new List<Guid>();
        await using (var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT order_id FROM orders ORDER BY order_id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!Guid.TryParse(reader.GetString(0), out var id))
                    throw new InvalidDataException("The database contains an invalid order identifier for export selection.");
                orderIds.Add(id);
            }
        }

        var result = new List<ExportOrderSourceRecord>(orderIds.Count);
        foreach (var orderId in orderIds)
        {
            var snapshot = await orderStore.GetByIdAsync(orderId, cancellationToken)
                ?? throw new InvalidDataException($"Order '{orderId:D}' disappeared while export selection was reading the authoritative database.");
            var adjustments = await ReadPaymentAdjustmentsAsync(orderId, cancellationToken);
            result.Add(new ExportOrderSourceRecord(snapshot, adjustments));
        }

        return result;
    }

    public async Task<IReadOnlyList<ExportEmissionRecord>> ListLatestSuccessfulEmissionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.batch_id,e.order_id,e.action,e.positive_snapshot_hash,e.positive_payload_json,
                   e.fulfilment_date,e.settlement_date,e.emitted_at_utc
            FROM export_emissions e
            JOIN export_batches b ON b.batch_id=e.batch_id
            WHERE b.status='SUCCESS'
            ORDER BY e.order_id,e.emitted_at_utc DESC,e.emission_id DESC;
            """;

        var result = new List<ExportEmissionRecord>();
        var seenOrders = new HashSet<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var orderId = ParseGuid(reader.GetString(1));
            if (!seenOrders.Add(orderId))
                continue;

            var batchId = ParseGuid(reader.GetString(0));
            var action = ParseAction(reader.GetString(2));
            var positive = ExportPayloadSerializer.DeserializePositive(reader.GetString(4));
            result.Add(new ExportEmissionRecord(
                batchId,
                orderId,
                action,
                reader.GetString(3),
                positive,
                DateOnly.ParseExact(reader.GetString(5), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly.ParseExact(reader.GetString(6), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                ParseDateTime(reader.GetString(7))));
        }

        return result;
    }

    public async Task PrepareBatchAsync(ExportBatchRecord batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Status != ExportBatchStatus.Prepared)
            throw new ArgumentException("Only prepared export batches may be persisted before workbook finalization.", nameof(batch));

        var payloadJson = ExportPayloadSerializer.SerializeBatch(batch.Payload);
        var payloadHash = ExportPayloadSerializer.ComputeSha256(payloadJson);
        if (!string.Equals(payloadHash, batch.PayloadHash, StringComparison.Ordinal))
            throw new InvalidDataException("The export batch payload hash does not match its canonical payload.");

        await transactionRunner.ExecuteAsync(async (transaction, token) =>
        {
            var sqlite = RequireSqlite(transaction);
            await ExecuteAsync(sqlite, """
                INSERT INTO export_batches(batch_id,schema_version,generated_at_utc,app_version,filter_start_date,filter_end_date,
                    order_count,order_line_count,tax_breakdown_count,status,payload_json,payload_hash,completed_at_utc)
                VALUES ($batch,$schema,$generated,$app,$start,$end,$orders,$lines,$taxes,'PREPARED',$payload,$hash,NULL);
                """, token,
                ("$batch", batch.Payload.Meta.BatchId.ToString()),
                ("$schema", batch.Payload.Meta.SchemaVersion),
                ("$generated", Format(batch.Payload.Meta.GeneratedAt)),
                ("$app", batch.Payload.Meta.AppVersion),
                ("$start", FormatDate(batch.Payload.Meta.FilterStartDate)),
                ("$end", FormatDate(batch.Payload.Meta.FilterEndDate)),
                ("$orders", batch.Payload.Meta.OrderCount),
                ("$lines", batch.Payload.Meta.OrderLineCount),
                ("$taxes", batch.Payload.Meta.TaxBreakdownCount),
                ("$payload", payloadJson),
                ("$hash", batch.PayloadHash));
        }, cancellationToken);
    }

    public async Task MarkBatchSucceededAsync(
        Guid batchId,
        IReadOnlyList<ExportEmissionRecord> emissions,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (batchId == Guid.Empty) throw new ArgumentException("A batch identity is required.", nameof(batchId));
        ArgumentNullException.ThrowIfNull(emissions);
        if (emissions.Any(emission => emission.BatchId != batchId))
            throw new ArgumentException("Every emission must belong to the batch being finalized.", nameof(emissions));

        await transactionRunner.ExecuteAsync(async (transaction, token) =>
        {
            var sqlite = RequireSqlite(transaction);
            var updated = await ExecuteAsync(sqlite,
                "UPDATE export_batches SET status='SUCCESS',completed_at_utc=$completed WHERE batch_id=$batch AND status='PREPARED';",
                token, ("$completed", Format(completedAtUtc)), ("$batch", batchId.ToString()));
            if (updated != 1)
                throw new InvalidOperationException("The export batch is missing or is no longer in PREPARED state.");

            var emissionIds = new HashSet<Guid>();
            foreach (var emission in emissions)
            {
                if (!emissionIds.Add(emission.OrderId))
                    throw new InvalidDataException("An export batch cannot emit more than one action for the same order.");

                var positive = emission.PositivePayload with { Action = ExportAction.Create };
                var positiveJson = ExportPayloadSerializer.SerializePositive(positive);
                var positiveHash = ExportPayloadSerializer.ComputeSha256(positiveJson);
                if (!string.Equals(positiveHash, emission.PositiveSnapshotHash, StringComparison.Ordinal))
                    throw new InvalidDataException("The export emission positive snapshot hash does not match its canonical payload.");

                await ExecuteAsync(sqlite, """
                    INSERT INTO export_emissions(emission_id,batch_id,order_id,action,positive_snapshot_hash,positive_payload_json,
                        fulfilment_date,settlement_date,emitted_at_utc)
                    VALUES ($emission,$batch,$order,$action,$hash,$payload,$fulfilment,$settlement,$emitted);
                    """, token,
                    ("$emission", idGenerator.NewId().ToString()),
                    ("$batch", batchId.ToString()),
                    ("$order", emission.OrderId.ToString()),
                    ("$action", ActionName(emission.Action)),
                    ("$hash", emission.PositiveSnapshotHash),
                    ("$payload", positiveJson),
                    ("$fulfilment", FormatDate(emission.FulfilmentDate)),
                    ("$settlement", FormatDate(emission.SettlementDate)),
                    ("$emitted", Format(emission.EmittedAtUtc)));
            }
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<PaymentAdjustment>> ReadPaymentAdjustmentsAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payment_adjustment_id,bucket,delta_cents,effective_business_date,effective_at,recorded_at
            FROM payment_adjustments
            WHERE order_id=$order
            ORDER BY effective_business_date,recorded_at,payment_adjustment_id;
            """;
        command.Parameters.AddWithValue("$order", orderId.ToString());
        var result = new List<PaymentAdjustment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PaymentAdjustment(
                ParseGuid(reader.GetString(0)),
                orderId,
                ParseBucket(reader.GetString(1)),
                Money.FromCents(reader.GetInt64(2)),
                NormalizeEffectiveAt(
                    DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ParseDateTime(reader.GetString(4))),
                ParseDateTime(reader.GetString(5))));
        }
        return result;
    }

    private static async Task<int> ExecuteAsync(SqliteApplicationTransaction sqlite, string sql, CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using var command = sqlite.Connection.CreateCommand();
        command.Transaction = sqlite.Transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(token);
    }

    private static SqliteApplicationTransaction RequireSqlite(IApplicationTransaction transaction) =>
        transaction as SqliteApplicationTransaction ?? throw new InvalidOperationException("The configured transaction is not SQLite-backed.");

    private static string ActionName(ExportAction value) => value switch
    {
        ExportAction.Create => "CREATE",
        ExportAction.Update => "UPDATE",
        ExportAction.Cancel => "CANCEL",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static ExportAction ParseAction(string value) => value switch
    {
        "CREATE" => ExportAction.Create,
        "UPDATE" => ExportAction.Update,
        "CANCEL" => ExportAction.Cancel,
        _ => throw new InvalidDataException("The database contains an invalid export action.")
    };

    private static PaymentBucket ParseBucket(string value) => value switch
    {
        "CB" => PaymentBucket.Card,
        "ESPECE" => PaymentBucket.Cash,
        _ => throw new InvalidDataException("The database contains an invalid payment bucket.")
    };

    private static Guid ParseGuid(string value) => Guid.TryParse(value, out var id) ? id : throw new InvalidDataException("The database contains an invalid export identifier.");
    private static DateTimeOffset ParseDateTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static DateTimeOffset NormalizeEffectiveAt(DateOnly businessDate, DateTimeOffset storedEffectiveAt) =>
        new(businessDate.ToDateTime(TimeOnly.FromDateTime(storedEffectiveAt.DateTime)), storedEffectiveAt.Offset);
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string? FormatDate(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string FormatDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
