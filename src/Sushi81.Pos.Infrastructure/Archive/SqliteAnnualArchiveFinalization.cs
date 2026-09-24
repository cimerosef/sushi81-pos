using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Archive;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Foundation.Transactions;
using Sushi81.Pos.Application.Export;
using Sushi81.Pos.Infrastructure.Export;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.Archive;

public enum AnnualArchiveFinalizationOutcome
{
    CompletedNow,
    AlreadyCompleted,
    NoTargetYet
}

public sealed record AnnualArchiveFinalizationResult(
    AnnualArchiveFinalizationOutcome Outcome,
    int ArchiveYear,
    int ArchivedOrderCount,
    string CanonicalArchivePath,
    string? ArchiveSha256);

/// <summary>
/// Publishes the validated WP1 staging database to the local canonical archive,
/// preserves pending M11 actions, and removes only the exact validated order IDs.
/// It deliberately has no scheduler, UI, OneDrive or user-export behavior.
/// </summary>
public sealed class SqliteAnnualArchiveFinalizationService(
    IAppPaths paths,
    IBusinessClock clock,
    IWriteAuthorityGuard authorityGuard,
    SqliteConnectionFactory connectionFactory,
    ITransactionRunner transactionRunner,
    SqliteGestionExportStore exportStore,
    IIdGenerator idGenerator,
    IDurableChangeNotifier notifier,
    Func<string, Exception?>? failureInjector = null)
{
    private const string ArchiveFormatVersion = "M12-WP1-1";
    private const string ArchiveSchemaVersion = "1";
    private const string PreservationAppVersion = "M12-WP2-ARCHIVE-PRESERVATION-1";

    private readonly IAppPaths paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IBusinessClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IWriteAuthorityGuard authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
    private readonly SqliteConnectionFactory connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    private readonly ITransactionRunner transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    private readonly SqliteGestionExportStore exportStore = exportStore ?? throw new ArgumentNullException(nameof(exportStore));
    private readonly IIdGenerator idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
    private readonly IDurableChangeNotifier notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    private readonly Func<string, Exception?>? failureInjector = failureInjector;

    public async Task<AnnualArchiveFinalizationResult> FinalizeNextArchiveAsync(CancellationToken cancellationToken = default)
    {
        var targetYear = AnnualArchivePolicy.GetTargetArchiveYear(clock.BusinessDate);
        if (targetYear is null)
            return new(AnnualArchiveFinalizationOutcome.NoTargetYet, clock.BusinessDate.Year - 1, 0, string.Empty, null);

        await using var authorityScope = await authorityGuard.EnterWriteScopeAsync(cancellationToken);
        paths.EnsureInitialized();
        Directory.CreateDirectory(paths.ArchiveDirectory);
        var canonicalPath = CanonicalPath(targetYear.Value);
        var completion = await ReadCompletionAsync(targetYear.Value, cancellationToken);
        if (completion is not null)
        {
            await ValidateCompletedArchiveAsync(completion, canonicalPath, cancellationToken);
            return new(AnnualArchiveFinalizationOutcome.AlreadyCompleted, targetYear.Value, completion.ArchiveOrderCount, canonicalPath, completion.ArchiveSha256);
        }

        var staging = await new SqliteAnnualArchiveStagingService(paths, clock, authorityGuard, connectionFactory)
            .StageNextArchiveWithoutAuthorityAsync(cancellationToken)
            ?? throw new InvalidOperationException("The annual archive target disappeared while finalization was starting.");

        await RecheckEligibilityAndArchiveAsync(staging, targetYear.Value, cancellationToken);
        var canonical = await PublishCanonicalAsync(staging, targetYear.Value, canonicalPath, cancellationToken);
        await PreservePendingExportActionsAsync(staging.OrderIds, cancellationToken);
        Inject("before-live-transaction");
        await RemoveExactOrdersAndRecordCompletionAsync(staging, canonical, cancellationToken);

        try
        {
            await notifier.NotifyCommittedAsync(CancellationToken.None);
        }
        catch
        {
            // The SQLite transaction is already durable. Recovery notification is best effort,
            // matching the existing post-commit mutation contract.
        }

        return new(AnnualArchiveFinalizationOutcome.CompletedNow, targetYear.Value, staging.OrderIds.Count, canonical.Path, canonical.Sha256);
    }

    private async Task RecheckEligibilityAndArchiveAsync(
        AnnualArchiveStagingResult staging,
        int targetYear,
        CancellationToken cancellationToken)
    {
        var reader = new SqliteAnnualArchiveEligibilityReader(connectionFactory, clock);
        var current = (await reader.ReadEligibleAsync(targetYear, cancellationToken))
            .Select(item => item.OrderId)
            .OrderBy(id => id.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        if (!current.SequenceEqual(staging.OrderIds.OrderBy(id => id.ToString("D"), StringComparer.Ordinal)))
            throw new InvalidDataException("The eligible live order set changed after archive staging; finalization must be retried.");

        await new SqliteAnnualArchiveValidator(clock).ValidateAsync(
            staging.StagedDatabasePath,
            connectionFactory.LiveDatabasePath,
            targetYear,
            staging.OrderIds,
            cancellationToken);
    }

    private async Task<CanonicalArchive> PublishCanonicalAsync(
        AnnualArchiveStagingResult staging,
        int targetYear,
        string canonicalPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(canonicalPath))
        {
            Inject("existing-canonical-validation");
            await new SqliteAnnualArchiveValidator(clock).ValidateAsync(
                canonicalPath,
                connectionFactory.LiveDatabasePath,
                targetYear,
                staging.OrderIds,
                cancellationToken);
            var existingHash = await ComputeSha256Async(canonicalPath, cancellationToken);
            return new(canonicalPath, existingHash);
        }

        var incomingPath = Path.Combine(paths.ArchiveDirectory, $".{Path.GetFileName(canonicalPath)}.{Guid.NewGuid():N}.incoming");
        try
        {
            Inject("copy");
            await using (var source = new FileStream(staging.StagedDatabasePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true))
            await using (var destination = new FileStream(incomingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken);
                Inject("flush");
                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            Inject("incoming-validation");
            await new SqliteAnnualArchiveValidator(clock).ValidateAsync(
                incomingPath,
                connectionFactory.LiveDatabasePath,
                targetYear,
                staging.OrderIds,
                cancellationToken);
            Inject("rename");
            File.Move(incomingPath, canonicalPath);
            Inject("post-rename-validation");
            await new SqliteAnnualArchiveValidator(clock).ValidateAsync(
                canonicalPath,
                connectionFactory.LiveDatabasePath,
                targetYear,
                staging.OrderIds,
                cancellationToken);
            return new(canonicalPath, await ComputeSha256Async(canonicalPath, cancellationToken));
        }
        catch
        {
            TryDeleteFile(incomingPath);
            throw;
        }
    }

    private async Task PreservePendingExportActionsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0) return;

        var sources = await exportStore.ListExportOrderSourcesAsync(cancellationToken);
        var history = await exportStore.ListLatestSuccessfulEmissionsAsync(cancellationToken);
        var selected = ExportSelectionRules.Select(
            sources,
            history,
            new ExportSelectionOptions(),
            clock.BusinessTimeZone);
        var targetIds = orderIds.ToHashSet();
        var blocking = selected.Diagnostics.Where(item => targetIds.Contains(item.OrderId)).ToArray();
        if (blocking.Length > 0)
            throw new InvalidDataException($"Pending Gestion preservation is blocked for archived order '{blocking[0].OrderId:D}': {blocking[0].Code}.");

        var actions = selected.Actions.Where(action => targetIds.Contains(action.OrderId)).ToArray();
        Inject("before-export-preservation");
        await exportStore.PrepareMissingActionsAsync(actions, PreservationAppVersion, clock.UtcNow, cancellationToken);
    }

    private async Task RemoveExactOrdersAndRecordCompletionAsync(
        AnnualArchiveStagingResult staging,
        CanonicalArchive canonical,
        CancellationToken cancellationToken)
    {
        await transactionRunner.ExecuteAsync(async (transaction, token) =>
        {
            var sqlite = transaction as SqliteApplicationTransaction
                ?? throw new InvalidOperationException("The annual archive finalization transaction must be SQLite-backed.");
            var ids = staging.OrderIds
                .OrderBy(id => id.ToString("D"), StringComparer.Ordinal)
                .ToArray();
            var currentCount = await ExecuteScalarLongAsync(sqlite, BuildInQuery("SELECT COUNT(*) FROM orders WHERE order_id IN ({0});", ids), ids, token);
            if (currentCount != ids.Length)
                throw new InvalidDataException("The exact archived order set is no longer present before live removal.");

            await ExecuteAsync(sqlite,
                "INSERT INTO annual_archive_completions(archive_year,archive_format_version,archive_schema_version,archive_file_name,archive_order_count,archive_sha256,completed_at_utc) VALUES($year,$format,$schema,$file,$count,$sha,$completed);",
                token,
                ("$year", staging.ArchiveYear),
                ("$format", ArchiveFormatVersion),
                ("$schema", ArchiveSchemaVersion),
                ("$file", Path.GetFileName(canonical.Path)),
                ("$count", ids.Length),
                ("$sha", canonical.Sha256),
                ("$completed", clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));

            var deleted = await ExecuteWithIdsAsync(sqlite, BuildInQuery("DELETE FROM orders WHERE order_id IN ({0});", ids), ids, token);
            if (deleted != ids.Length)
                throw new InvalidDataException("The annual archive live-order delete count did not match the validated order set.");
            Inject("after-delete-before-commit");
        }, cancellationToken);
    }

    private string CanonicalPath(int year) => Path.Combine(paths.ArchiveDirectory, $"sushi81-archive-{year:D4}.db");

    private async Task<ArchiveCompletion?> ReadCompletionAsync(int year, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT archive_format_version,archive_schema_version,archive_file_name,archive_order_count,archive_sha256,completed_at_utc FROM annual_archive_completions WHERE archive_year=$year;";
        command.Parameters.AddWithValue("$year", year);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(
            year,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetString(5));
    }

    private async Task ValidateCompletedArchiveAsync(ArchiveCompletion completion, string canonicalPath, CancellationToken cancellationToken)
    {
        if (!string.Equals(completion.ArchiveFormatVersion, ArchiveFormatVersion, StringComparison.Ordinal)
            || !string.Equals(completion.ArchiveSchemaVersion, ArchiveSchemaVersion, StringComparison.Ordinal)
            || !string.Equals(completion.ArchiveFileName, Path.GetFileName(canonicalPath), StringComparison.Ordinal))
            throw new InvalidDataException("The annual archive completion marker has incompatible metadata.");

        if (!File.Exists(canonicalPath)) return;
        await new SqliteAnnualArchiveValidator(clock).ValidateStandaloneAsync(canonicalPath, completion.ArchiveYear, cancellationToken);
        var actualHash = await ComputeSha256Async(canonicalPath, cancellationToken);
        if (!string.Equals(actualHash, completion.ArchiveSha256, StringComparison.Ordinal))
            throw new InvalidDataException("The canonical annual archive hash does not match its completion marker.");
    }

    private void Inject(string stage)
    {
        if (failureInjector?.Invoke(stage) is { } exception)
            throw exception;
    }

    private static string BuildInQuery(string format, Guid[] ids)
    {
        var placeholders = ids.Length == 0
            ? "NULL"
            : string.Join(',', ids.Select((_, index) => "$id" + index.ToString(CultureInfo.InvariantCulture)));
        return string.Format(CultureInfo.InvariantCulture, format, placeholders);
    }

    private static async Task<long> ExecuteScalarLongAsync(SqliteApplicationTransaction sqlite, string sql, Guid[] ids, CancellationToken cancellationToken)
    {
        await using var command = sqlite.Connection.CreateCommand();
        command.Transaction = sqlite.Transaction;
        command.CommandText = sql;
        AddIds(command, ids);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<int> ExecuteAsync(SqliteApplicationTransaction sqlite, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = sqlite.Connection.CreateCommand();
        command.Transaction = sqlite.Transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ExecuteWithIdsAsync(SqliteApplicationTransaction sqlite, string sql, Guid[] ids, CancellationToken cancellationToken)
    {
        await using var command = sqlite.Connection.CreateCommand();
        command.Transaction = sqlite.Transaction;
        command.CommandText = sql;
        AddIds(command, ids);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddIds(SqliteCommand command, Guid[] ids)
    {
        for (var index = 0; index < ids.Length; index++)
            command.Parameters.AddWithValue("$id" + index.ToString(CultureInfo.InvariantCulture), ids[index].ToString());
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private sealed record CanonicalArchive(string Path, string Sha256);

    private sealed record ArchiveCompletion(
        int ArchiveYear,
        string ArchiveFormatVersion,
        string ArchiveSchemaVersion,
        string ArchiveFileName,
        int ArchiveOrderCount,
        string ArchiveSha256,
        string CompletedAtUtc);
}
