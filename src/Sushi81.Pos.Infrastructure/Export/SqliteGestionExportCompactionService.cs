using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Export;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Foundation.Transactions;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.Export;

public static class GestionExportCompactionPolicy
{
    public static bool IsRetentionElapsed(DateTimeOffset? completedAtUtc, DateTimeOffset nowUtc)
    {
        if (completedAtUtc is null)
            return false;

        var deadline = nowUtc.ToUniversalTime().AddDays(-30);
        return completedAtUtc.Value.ToUniversalTime() <= deadline;
    }
}

public sealed record GestionExportCompactionResult(int SuccessfulBatchesExamined, int BatchesPruned, int BatchesRetained);

/// <summary>Prunes only expired successful Gestion history whose every order has positive M12 archive proof.</summary>
public sealed class SqliteGestionExportCompactionService(
    SqliteConnectionFactory connectionFactory,
    ITransactionRunner transactionRunner,
    IWriteAuthorityGuard authorityGuard,
    IBusinessClock clock,
    Func<string, Exception?>? failureInjector = null)
{
    private const string ArchiveFormatVersion = "M12-WP1-1";
    private const string ArchiveSchemaVersion = "1";

    private readonly SqliteConnectionFactory connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    private readonly ITransactionRunner transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    private readonly IWriteAuthorityGuard authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
    private readonly IBusinessClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly Func<string, Exception?>? failureInjector = failureInjector;

    public async Task<GestionExportCompactionResult> CompactAsync(CancellationToken cancellationToken = default)
    {
        await using var authorityScope = await authorityGuard.EnterWriteScopeAsync(cancellationToken);
        var nowUtc = clock.UtcNow.ToUniversalTime();

        return await transactionRunner.ExecuteAsync(async (transaction, token) =>
        {
            var sqlite = transaction as SqliteApplicationTransaction
                ?? throw new InvalidOperationException("Gestion export compaction requires a SQLite-backed transaction.");
            var preparedOrderIds = await ReadPreparedOrderDependenciesAsync(sqlite, token);
            var successfulBatches = await ReadSuccessfulBatchesAsync(sqlite, token);
            var pruned = 0;
            var retained = 0;

            foreach (var batch in successfulBatches)
            {
                if (preparedOrderIds is null
                    || !TryParseUtc(batch.CompletedAtUtc, out var completedAtUtc)
                    || !GestionExportCompactionPolicy.IsRetentionElapsed(completedAtUtc, nowUtc))
                {
                    retained++;
                    continue;
                }

                var batchOrderIds = await ValidateBatchStructureAsync(sqlite, batch, requireEmissions: true, token);
                if (batchOrderIds is null || batchOrderIds.Count == 0)
                {
                    retained++;
                    continue;
                }

                var eligible = true;
                foreach (var orderId in batchOrderIds)
                {
                    if (preparedOrderIds.Contains(orderId)
                        || await IsLiveOrderAsync(sqlite, orderId, token)
                        || !await HasValidArchiveProofAsync(sqlite, orderId, token))
                    {
                        eligible = false;
                        break;
                    }
                }

                if (!eligible)
                {
                    retained++;
                    continue;
                }

                var removedEmissions = await ExecuteAsync(sqlite,
                    "DELETE FROM export_emissions WHERE batch_id=$batch;",
                    token,
                    ("$batch", batch.BatchId));
                if (removedEmissions != batch.OrderCount)
                    throw new InvalidDataException("The successful Gestion batch emission set changed during compaction.");

                Inject("after-emissions-delete-before-batch-delete");
                var removedBatch = await ExecuteAsync(sqlite,
                    "DELETE FROM export_batches WHERE batch_id=$batch AND status='SUCCESS';",
                    token,
                    ("$batch", batch.BatchId));
                if (removedBatch != 1)
                    throw new InvalidDataException("The successful Gestion batch changed during compaction.");

                pruned++;
            }

            return new GestionExportCompactionResult(successfulBatches.Count, pruned, retained);
        }, cancellationToken);
    }

    private static async Task<IReadOnlySet<Guid>?> ReadPreparedOrderDependenciesAsync(
        SqliteApplicationTransaction sqlite,
        CancellationToken cancellationToken)
    {
        var batches = new List<BatchRow>();
        await using (var command = CreateCommand(sqlite, "SELECT batch_id,order_count,payload_json,payload_hash,completed_at_utc FROM export_batches WHERE status='PREPARED' ORDER BY generated_at_utc DESC,batch_id DESC;"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                batches.Add(new BatchRow(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        var dependencies = new HashSet<Guid>();
        foreach (var batch in batches)
        {
            if (batch.CompletedAtUtc is not null)
                return null;

            var orderIds = await ValidateBatchStructureAsync(sqlite, batch, requireEmissions: false, cancellationToken);
            if (orderIds is null || orderIds.Count == 0)
                return null;

            foreach (var orderId in orderIds)
                dependencies.Add(orderId);
        }

        return dependencies;
    }

    private static async Task<List<BatchRow>> ReadSuccessfulBatchesAsync(
        SqliteApplicationTransaction sqlite,
        CancellationToken cancellationToken)
    {
        var batches = new List<BatchRow>();
        await using var command = CreateCommand(sqlite, "SELECT batch_id,order_count,payload_json,payload_hash,completed_at_utc FROM export_batches WHERE status='SUCCESS' ORDER BY generated_at_utc DESC,batch_id DESC;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            batches.Add(new BatchRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return batches;
    }

    private static async Task<HashSet<Guid>?> ValidateBatchStructureAsync(
        SqliteApplicationTransaction sqlite,
        BatchRow batch,
        bool requireEmissions,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(batch.BatchId, "D", out var batchId)
            || batchId == Guid.Empty
            || batch.OrderCount <= 0
            || !string.Equals(ExportPayloadSerializer.ComputeSha256(batch.PayloadJson), batch.PayloadHash, StringComparison.Ordinal))
            return null;

        ExportBatchPayload payload;
        try
        {
            payload = ExportPayloadSerializer.DeserializeBatch(batch.PayloadJson);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException or InvalidDataException)
        {
            return null;
        }

        if (payload.Meta is null
            || payload.Orders is null
            || payload.Meta.BatchId != batchId
            || payload.Meta.OrderCount != batch.OrderCount
            || payload.Orders.Count != batch.OrderCount
            || payload.Meta.OrderLineCount != payload.Orders.Sum(order => order?.Lines?.Count ?? 0)
            || payload.Meta.TaxBreakdownCount != payload.Orders.Sum(order => order?.TaxBreakdown?.Count ?? 0))
            return null;

        var payloadOrders = new Dictionary<Guid, ExportOrderPayload>();
        foreach (var order in payload.Orders)
        {
            if (order is null || order.OrderId == Guid.Empty || order.Lines is null || order.TaxBreakdown is null
                || !payloadOrders.TryAdd(order.OrderId, order))
                return null;
        }

        var ledgerOrders = new Dictionary<Guid, BatchOrderRow>();
        await using (var command = CreateCommand(sqlite, "SELECT order_id,action,action_payload_hash FROM export_batch_orders WHERE batch_id=$batch;", ("$batch", batch.BatchId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!Guid.TryParseExact(reader.GetString(0), "D", out var orderId)
                    || orderId == Guid.Empty
                    || !ledgerOrders.TryAdd(orderId, new BatchOrderRow(reader.GetString(1), reader.GetString(2))))
                    return null;
            }
        }

        if (ledgerOrders.Count != batch.OrderCount || payloadOrders.Count != batch.OrderCount)
            return null;

        foreach (var (orderId, order) in payloadOrders)
        {
            if (!ledgerOrders.TryGetValue(orderId, out var ledgerOrder)
                || !string.Equals(ledgerOrder.Action, ActionName(order.Action), StringComparison.Ordinal)
                || !string.Equals(
                    ledgerOrder.ActionPayloadHash,
                    ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializeAction(order)),
                    StringComparison.Ordinal))
                return null;
        }

        var emissionOrders = new Dictionary<Guid, string>();
        await using (var command = CreateCommand(sqlite, "SELECT order_id,action FROM export_emissions WHERE batch_id=$batch;", ("$batch", batch.BatchId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!Guid.TryParseExact(reader.GetString(0), "D", out var orderId)
                    || orderId == Guid.Empty
                    || !emissionOrders.TryAdd(orderId, reader.GetString(1)))
                    return null;
            }
        }

        if (requireEmissions)
        {
            if (emissionOrders.Count != batch.OrderCount)
                return null;
            foreach (var (orderId, ledgerOrder) in ledgerOrders)
            {
                if (!emissionOrders.TryGetValue(orderId, out var action)
                    || !string.Equals(action, ledgerOrder.Action, StringComparison.Ordinal))
                    return null;
            }
        }
        else if (emissionOrders.Count != 0)
        {
            return null;
        }

        return payloadOrders.Keys.ToHashSet();
    }

    private static async Task<bool> IsLiveOrderAsync(
        SqliteApplicationTransaction sqlite,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(sqlite, "SELECT 1 FROM orders WHERE order_id=$order LIMIT 1;", ("$order", orderId.ToString("D")));
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<bool> HasValidArchiveProofAsync(
        SqliteApplicationTransaction sqlite,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(sqlite, """
            SELECT p.archive_year,p.archive_sha256,p.archive_completed_at_utc,
                   c.archive_format_version,c.archive_schema_version,c.archive_file_name,
                   c.archive_order_count,c.archive_sha256,c.completed_at_utc
            FROM annual_archive_order_proofs p
            JOIN annual_archive_completions c ON c.archive_year=p.archive_year
            WHERE p.order_id=$order;
            """, ("$order", orderId.ToString("D")));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return false;

        var year = reader.GetInt32(0);
        var proofHash = reader.GetString(1);
        var proofCompletedAt = reader.GetString(2);
        var completionHash = reader.GetString(7);
        var completionCompletedAt = reader.GetString(8);
        return year is >= 1 and <= 9999
            && IsSha256(proofHash)
            && string.Equals(proofHash, completionHash, StringComparison.Ordinal)
            && string.Equals(proofCompletedAt, completionCompletedAt, StringComparison.Ordinal)
            && TryParseUtc(proofCompletedAt, out _)
            && string.Equals(reader.GetString(3), ArchiveFormatVersion, StringComparison.Ordinal)
            && string.Equals(reader.GetString(4), ArchiveSchemaVersion, StringComparison.Ordinal)
            && string.Equals(reader.GetString(5), $"sushi81-archive-{year:D4}.db", StringComparison.Ordinal)
            && reader.GetInt32(6) > 0;
    }

    private static bool TryParseUtc(string? value, out DateTimeOffset result)
    {
        result = default;
        return value is not null
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result)
            && result.Offset == TimeSpan.Zero;
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private void Inject(string stage)
    {
        if (failureInjector?.Invoke(stage) is { } exception)
            throw exception;
    }

    private static string ActionName(ExportAction action) => action switch
    {
        ExportAction.Create => "CREATE",
        ExportAction.Update => "UPDATE",
        ExportAction.Cancel => "CANCEL",
        _ => "<invalid>"
    };

    private static SqliteCommand CreateCommand(
        SqliteApplicationTransaction sqlite,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = sqlite.Connection.CreateCommand();
        command.Transaction = sqlite.Transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return command;
    }

    private static async Task<int> ExecuteAsync(
        SqliteApplicationTransaction sqlite,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(sqlite, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record BatchRow(string BatchId, int OrderCount, string PayloadJson, string PayloadHash, string? CompletedAtUtc);
    private sealed record BatchOrderRow(string Action, string ActionPayloadHash);
}
