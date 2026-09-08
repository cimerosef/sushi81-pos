using System.Security.Cryptography;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Infrastructure.Recovery;

namespace Sushi81.Pos.Infrastructure.Authority;

/// <summary>Data-first staging/install boundary for an exact validated DR candidate.</summary>
public sealed class DisasterRecoveryDatabaseInstaller(
    IAppPaths paths,
    IDisasterRecoveryFaultProbe? faultProbe = null)
{
    private readonly IDisasterRecoveryFaultProbe faultProbe = faultProbe ?? new NoOpDisasterRecoveryFaultProbe();

    public async Task<string> StageAndValidateAsync(
        RecoveryCandidate candidate,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(content);
        candidate.Validate();
        paths.EnsureInitialized();
        var stagingPath = Path.Combine(paths.TempDirectory, $"m07-dr-{Guid.NewGuid():N}.db");
        try
        {
            await using (var output = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await content.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            faultProbe.Hit(DisasterRecoveryFaultPoint.DuringCandidateStagingValidation);
            await ValidateExactAsync(stagingPath, candidate, cancellationToken);
            return stagingPath;
        }
        catch
        {
            DeleteIfExists(stagingPath);
            throw;
        }
    }

    public async Task<string> StageLocalAndValidateAsync(
        RecoveryCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        candidate.Validate();
        if (candidate.Type != RecoveryCandidateType.OneDriveCheckpoint)
            throw new InvalidOperationException("Only a local OneDrive candidate may use local staging.");
        var sourcePath = Path.GetFullPath(candidate.StorageReference);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The recovery candidate payload is unavailable.", sourcePath);
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return await StageAndValidateAsync(candidate, input, cancellationToken);
    }

    public async Task InstallAndVerifyAsync(
        string stagingPath,
        RecoveryCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingPath);
        ArgumentNullException.ThrowIfNull(candidate);
        await ValidateExactAsync(stagingPath, candidate, cancellationToken);
        var livePath = paths.LiveDatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(livePath)!);
        try
        {
            if (File.Exists(livePath))
            {
                faultProbe.Hit(DisasterRecoveryFaultPoint.BeforeLiveDatabaseReplacement);
                File.Replace(stagingPath, livePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                faultProbe.Hit(DisasterRecoveryFaultPoint.BeforeLiveDatabaseReplacement);
                File.Move(stagingPath, livePath, overwrite: false);
            }
            faultProbe.Hit(DisasterRecoveryFaultPoint.AfterLiveDatabaseReplacement);
            await ValidateExactAsync(livePath, candidate, cancellationToken);
        }
        finally
        {
            DeleteIfExists(stagingPath);
        }
    }

    public static async Task ValidateExactAsync(
        string databasePath,
        RecoveryCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        candidate.Validate();
        if (!File.Exists(databasePath)) throw new InvalidDataException("The staged recovery database is missing.");
        var size = new FileInfo(databasePath).Length;
        var hash = await ComputeSha256Async(databasePath, cancellationToken);
        if (size != candidate.PayloadSize || !string.Equals(hash, candidate.PayloadSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The recovery database failed exact size or SHA-256 validation.");
        await ValidateSqliteAsync(databasePath, cancellationToken);
        if (await SqliteBusinessRevisionStore.ReadFromDatabaseAsync(databasePath, cancellationToken) != candidate.BusinessRevision)
            throw new InvalidDataException("The recovery database carries a contradictory business-data revision.");
    }

    private static async Task ValidateSqliteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private, Pooling = false
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            if (!string.Equals(Convert.ToString(await integrity.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The recovery database failed SQLite integrity validation.");
            await using var schema = connection.CreateCommand();
            schema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
            if (Convert.ToInt32(await schema.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("The recovery database lacks the application schema marker.");
        }
        catch (SqliteException exception)
        {
            throw new InvalidDataException("The recovery database is not a readable SQLite database.", exception);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public static void DeleteStagingIfExists(string path) => DeleteIfExists(path);
}
