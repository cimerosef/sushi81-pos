using System.Security.Cryptography;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Foundation.Paths;
using Microsoft.Data.Sqlite;

namespace Sushi81.Pos.Infrastructure.Authority;

/// <summary>Stages and installs a handoff snapshot only after exact bytes and SQLite checks pass.</summary>
public sealed class SqliteTransferSnapshotInstaller(IAppPaths paths) : ITransferSnapshotInstaller
{
    public async Task<TargetSnapshotStaging> StageAndValidateAsync(
        Guid transferId,
        Stream content,
        string expectedName,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (transferId == Guid.Empty || !GitHubHandoffAssetNames.IsSnapshotName(expectedName)
            || expectedSize <= 0 || !GitHubSha256.TryNormalizeExpected(expectedSha256, out var normalized))
            throw new InvalidDataException("The target snapshot metadata is invalid.");

        paths.EnsureInitialized();
        var stagingPath = Path.Combine(paths.TempDirectory, $"m07-acquisition-{transferId:N}.db");
        if (File.Exists(stagingPath)) File.Delete(stagingPath);
        try
        {
            await using (var output = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await content.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            var size = new FileInfo(stagingPath).Length;
            var hash = await ComputeSha256Async(stagingPath, cancellationToken);
            if (size != expectedSize || !string.Equals(hash, normalized[7..], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded handoff snapshot failed size or SHA-256 validation.");
            await ValidateSqliteAsync(stagingPath, cancellationToken);
            var staging = new TargetSnapshotStaging(stagingPath, size, hash);
            staging.Validate();
            return staging;
        }
        catch
        {
            if (File.Exists(stagingPath)) File.Delete(stagingPath);
            throw;
        }
    }

    public async Task InstallAndVerifyAsync(TargetSnapshotStaging staging, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staging);
        staging.Validate();
        paths.EnsureInitialized();
        var livePath = paths.LiveDatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(livePath)!);
        try
        {
            if (File.Exists(livePath))
                File.Replace(staging.Path, livePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(staging.Path, livePath, overwrite: false);
            await ValidateSqliteAsync(livePath, cancellationToken);
        }
        finally
        {
            if (File.Exists(staging.Path)) File.Delete(staging.Path);
        }
    }

    private static async Task ValidateSqliteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            var result = Convert.ToString(await integrity.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The SQLite handoff snapshot failed integrity_check.");
            await using var schema = connection.CreateCommand();
            schema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
            if (Convert.ToInt32(await schema.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("The SQLite handoff snapshot lacks the expected application schema marker.");
        }
        catch (SqliteException exception)
        {
            throw new InvalidDataException("The handoff snapshot is not a valid SQLite application database.", exception);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
