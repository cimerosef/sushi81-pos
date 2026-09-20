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
    IIdGenerator idGenerator) : IExportOrderSourceReader, IExportLedgerStore, IExportBatchHistoryReader
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
            var positiveJson = reader.GetString(4);
            var positive = ExportPayloadSerializer.DeserializePositive(positiveJson);
            var computedHash = ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializePositive(positive));
            if (!string.Equals(computedHash, reader.GetString(3), StringComparison.Ordinal)
                || positive.OrderId != orderId)
                throw new InvalidDataException("The durable export positive snapshot is malformed or tampered.");
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

    public async Task<ExportBatchRecord?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        if (batchId == Guid.Empty) throw new ArgumentException("A batch identity is required.", nameof(batchId));

        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT schema_version,generated_at_utc,app_version,filter_start_date,filter_end_date,
                   order_count,order_line_count,tax_breakdown_count,status,payload_json,payload_hash,completed_at_utc
            FROM export_batches
            WHERE batch_id=$batch;
            """;
        command.Parameters.AddWithValue("$batch", batchId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var payloadJson = reader.GetString(9);
        var payloadHash = reader.GetString(10);
        if (!string.Equals(ExportPayloadSerializer.ComputeSha256(payloadJson), payloadHash, StringComparison.Ordinal))
            throw new InvalidDataException("The persisted export batch payload hash does not match its payload.");

        var payload = ExportPayloadSerializer.DeserializeBatch(payloadJson);
        ValidateBatchPayload(payload, batchId);
        if (!string.Equals(reader.GetString(0), payload.Meta.SchemaVersion, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(2), payload.Meta.AppVersion, StringComparison.Ordinal)
            || payload.Meta.OrderCount != reader.GetInt32(5)
            || payload.Meta.OrderLineCount != reader.GetInt32(6)
            || payload.Meta.TaxBreakdownCount != reader.GetInt32(7))
            throw new InvalidDataException("The persisted export batch metadata does not match its immutable payload.");

        return new ExportBatchRecord(
            payload,
            reader.GetString(8) switch
            {
                "PREPARED" => ExportBatchStatus.Prepared,
                "SUCCESS" => ExportBatchStatus.Success,
                _ => throw new InvalidDataException("The database contains an invalid export batch status.")
            },
            payloadHash,
            reader.IsDBNull(11) ? null : ParseDateTime(reader.GetString(11)));
    }

    public async Task<IReadOnlyList<ExportBatchRecord>> ListSuccessfulBatchesAsync(CancellationToken cancellationToken = default)
    {
        var batchIds = new List<Guid>();
        await using (var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT batch_id FROM export_batches WHERE status='SUCCESS' ORDER BY generated_at_utc DESC, batch_id DESC;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                batchIds.Add(ParseGuid(reader.GetString(0)));
        }

        var result = new List<ExportBatchRecord>(batchIds.Count);
        foreach (var batchId in batchIds)
        {
            var batch = await GetBatchAsync(batchId, cancellationToken)
                ?? throw new InvalidDataException("The export history contains a missing successful batch.");
            if (batch.Status != ExportBatchStatus.Success)
                throw new InvalidDataException("The export history contains a batch with an invalid status.");
            result.Add(batch);
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
        ValidateBatchPayload(batch.Payload, batch.Payload.Meta.BatchId);

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

            foreach (var order in batch.Payload.Orders)
            {
                var latest = await ReadLatestSuccessfulEmissionAsync(sqlite, order.OrderId, token);
                var expectedPreviousHash = latest?.PositiveSnapshotHash;
                if (order.Action == ExportAction.Create && latest is not null)
                    throw new InvalidOperationException("A CREATE batch cannot be prepared after a successful export emission already exists for the order.");
                if (order.Action is ExportAction.Update or ExportAction.Cancel
                    && (latest is null || latest.Action == ExportAction.Cancel))
                    throw new InvalidOperationException("The export batch action has no valid successful positive predecessor.");

                await ExecuteAsync(sqlite, """
                    INSERT INTO export_batch_orders(batch_id,order_id,action,action_payload_hash,expected_previous_positive_hash)
                    VALUES ($batch,$order,$action,$payloadHash,$expectedPreviousHash);
                    """, token,
                    ("$batch", batch.Payload.Meta.BatchId.ToString()),
                    ("$order", order.OrderId.ToString()),
                    ("$action", ActionName(order.Action)),
                    ("$payloadHash", ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializeAction(order))),
                    ("$expectedPreviousHash", expectedPreviousHash));
            }
        }, cancellationToken);
    }

    public async Task MarkBatchSucceededAsync(
        Guid batchId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (batchId == Guid.Empty) throw new ArgumentException("A batch identity is required.", nameof(batchId));

        await transactionRunner.ExecuteAsync(async (transaction, token) =>
        {
            var sqlite = RequireSqlite(transaction);
            var persistedBatch = await ReadPersistedBatchAsync(sqlite, batchId, token)
                ?? throw new InvalidOperationException("The export batch is missing.");
            if (!string.Equals(persistedBatch.Status, "PREPARED", StringComparison.Ordinal))
                throw new InvalidOperationException("The export batch is missing or is no longer in PREPARED state.");

            if (!string.Equals(
                    ExportPayloadSerializer.ComputeSha256(persistedBatch.PayloadJson),
                    persistedBatch.PayloadHash,
                    StringComparison.Ordinal))
                throw new InvalidDataException("The persisted export batch payload hash does not match its payload.");

            var payload = ExportPayloadSerializer.DeserializeBatch(persistedBatch.PayloadJson);
            ValidateBatchPayload(payload, batchId);
            var batchOrders = await ReadBatchOrdersAsync(sqlite, batchId, token);
            if (batchOrders.Count != payload.Orders.Count)
                throw new InvalidDataException("The persisted export batch order ledger does not match the immutable batch payload.");

            var emissions = new List<ExportEmissionRecord>(payload.Orders.Count);
            foreach (var order in payload.Orders)
            {
                if (!batchOrders.TryGetValue(order.OrderId, out var batchOrder)
                    || batchOrder.Action != order.Action
                    || !string.Equals(
                        batchOrder.ActionPayloadHash,
                        ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializeAction(order)),
                        StringComparison.Ordinal))
                    throw new InvalidDataException("The persisted export batch order ledger does not match the immutable batch payload.");

                var latest = await ReadLatestSuccessfulEmissionAsync(sqlite, order.OrderId, token);
                ExportOrderPayload positive;
                switch (order.Action)
                {
                    case ExportAction.Create:
                        if (latest is not null || batchOrder.ExpectedPreviousPositiveHash is not null)
                            throw new InvalidOperationException("A stale PREPARED CREATE batch cannot be finalized after another successful emission.");
                        positive = order with { Action = ExportAction.Create };
                        break;

                    case ExportAction.Update:
                        if (latest is null
                            || latest.Action == ExportAction.Cancel
                            || string.IsNullOrWhiteSpace(batchOrder.ExpectedPreviousPositiveHash)
                            || !string.Equals(latest.PositiveSnapshotHash, batchOrder.ExpectedPreviousPositiveHash, StringComparison.Ordinal))
                            throw new InvalidOperationException("A stale PREPARED UPDATE batch cannot be finalized after the export chain advanced.");
                        positive = order with { Action = ExportAction.Create };
                        if (string.Equals(
                                ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializePositive(positive)),
                                latest.PositiveSnapshotHash,
                                StringComparison.Ordinal))
                            throw new InvalidOperationException("An UPDATE batch cannot emit an unchanged positive snapshot.");
                        break;

                    case ExportAction.Cancel:
                        if (latest is null
                            || latest.Action == ExportAction.Cancel
                            || string.IsNullOrWhiteSpace(batchOrder.ExpectedPreviousPositiveHash)
                            || !string.Equals(latest.PositiveSnapshotHash, batchOrder.ExpectedPreviousPositiveHash, StringComparison.Ordinal))
                            throw new InvalidOperationException("A stale PREPARED CANCEL batch cannot be finalized after the export chain advanced.");
                        positive = ExportPayloadSerializer.DeserializePositive(latest.PositivePayloadJson);
                        if (positive.OrderId != order.OrderId
                            || positive.FulfilmentDate != order.FulfilmentDate
                            || positive.SettlementDate != order.SettlementDate
                            || order.Lines.Count != 0
                            || order.TaxBreakdown.Count != 0
                            || !string.Equals(order.OrderStatus, "CANCELLED", StringComparison.Ordinal))
                            throw new InvalidDataException("The persisted CANCEL payload is not anchored to the last positive snapshot.");
                        break;

                    default:
                        throw new InvalidDataException("The persisted export batch contains an unsupported action.");
                }

                var positiveJson = ExportPayloadSerializer.SerializePositive(positive);
                var positiveHash = ExportPayloadSerializer.ComputeSha256(positiveJson);
                emissions.Add(new ExportEmissionRecord(
                    batchId,
                    order.OrderId,
                    order.Action,
                    positiveHash,
                    positive,
                    positive.FulfilmentDate,
                    positive.SettlementDate,
                    completedAtUtc));
            }

            var updated = await ExecuteAsync(sqlite,
                "UPDATE export_batches SET status='SUCCESS',completed_at_utc=$completed WHERE batch_id=$batch AND status='PREPARED';",
                token, ("$completed", Format(completedAtUtc)), ("$batch", batchId.ToString()));
            if (updated != 1)
                throw new InvalidOperationException("The export batch is missing or is no longer in PREPARED state.");

            foreach (var emission in emissions)
            {
                var positiveJson = ExportPayloadSerializer.SerializePositive(emission.PositivePayload);
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

    private static void ValidateBatchPayload(ExportBatchPayload payload, Guid expectedBatchId)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Meta is null
            || payload.Orders is null
            || payload.Meta.BatchId != expectedBatchId
            || payload.Meta.OrderCount != payload.Orders.Count
            || payload.Meta.OrderLineCount != payload.Orders.Sum(order => order.Lines.Count)
            || payload.Meta.TaxBreakdownCount != payload.Orders.Sum(order => order.TaxBreakdown.Count))
            throw new InvalidDataException("The export batch metadata does not match its immutable payload.");

        var orderIds = new HashSet<Guid>();
        foreach (var order in payload.Orders)
        {
            if (order.OrderId == Guid.Empty || !orderIds.Add(order.OrderId))
                throw new InvalidDataException("An export batch cannot contain duplicate or empty order identities.");

            if (order.Action == ExportAction.Cancel
                && (order.Lines.Count != 0
                    || order.TaxBreakdown.Count != 0
                    || !string.Equals(order.OrderStatus, "CANCELLED", StringComparison.Ordinal)))
                throw new InvalidDataException("A CANCEL export payload must contain no lines or tax breakdown and must be CANCELLED.");

            if (order.Action is ExportAction.Create or ExportAction.Update
                && !string.Equals(order.OrderStatus, "CLOSED", StringComparison.Ordinal))
                throw new InvalidDataException("A positive export payload must be CLOSED.");

            if (order.Lines.Any(line => line.OrderId != order.OrderId)
                || order.TaxBreakdown.Any(tax => tax.OrderId != order.OrderId))
                throw new InvalidDataException("The export batch contains a child payload for another order.");
        }
    }

    private static async Task<PersistedBatch?> ReadPersistedBatchAsync(
        SqliteApplicationTransaction sqlite,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        await using var command = sqlite.Connection.CreateCommand();
        command.Transaction = sqlite.Transaction;
        command.CommandText = "SELECT status,payload_json,payload_hash FROM export_batches WHERE batch_id=$batch;";
        command.Parameters.AddWithValue("$batch", batchId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return new PersistedBatch(reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    private static async Task<Dictionary<Guid, PersistedBatchOrder>> ReadBatchOrdersAsync(
        SqliteApplicationTransaction sqlite,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        await using var command = sqlite.Connection.CreateCommand();
        command.Transaction = sqlite.Transaction;
        command.CommandText = """
            SELECT order_id,action,action_payload_hash,expected_previous_positive_hash
            FROM export_batch_orders
            WHERE batch_id=$batch;
            """;
        command.Parameters.AddWithValue("$batch", batchId.ToString());
        var result = new Dictionary<Guid, PersistedBatchOrder>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var orderId = ParseGuid(reader.GetString(0));
            if (!result.TryAdd(
                    orderId,
                    new PersistedBatchOrder(
                        ParseAction(reader.GetString(1)),
                        reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3))))
                throw new InvalidDataException("The durable export batch order ledger contains a duplicate order.");
        }
        return result;
    }

    private static async Task<PersistedEmission?> ReadLatestSuccessfulEmissionAsync(
        SqliteApplicationTransaction sqlite,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        await using var command = sqlite.Connection.CreateCommand();
        command.Transaction = sqlite.Transaction;
        command.CommandText = """
            SELECT e.action,e.positive_snapshot_hash,e.positive_payload_json,e.fulfilment_date,e.settlement_date
            FROM export_emissions e
            JOIN export_batches b ON b.batch_id=e.batch_id
            WHERE b.status='SUCCESS' AND e.order_id=$order
            ORDER BY e.emitted_at_utc DESC,e.emission_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$order", orderId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var action = ParseAction(reader.GetString(0));
        var positiveJson = reader.GetString(2);
        var positive = ExportPayloadSerializer.DeserializePositive(positiveJson);
        var positiveHash = reader.GetString(1);
        if (positive.OrderId != orderId
            || !string.Equals(
                ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializePositive(positive)),
                positiveHash,
                StringComparison.Ordinal))
            throw new InvalidDataException("The durable export positive snapshot is malformed or tampered.");

        return new PersistedEmission(
            action,
            positiveHash,
            positiveJson,
            DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(reader.GetString(4), "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private sealed record PersistedBatch(string Status, string PayloadJson, string PayloadHash);

    private sealed record PersistedBatchOrder(
        ExportAction Action,
        string ActionPayloadHash,
        string? ExpectedPreviousPositiveHash);

    private sealed record PersistedEmission(
        ExportAction Action,
        string PositiveSnapshotHash,
        string PositivePayloadJson,
        DateOnly FulfilmentDate,
        DateOnly SettlementDate);

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
