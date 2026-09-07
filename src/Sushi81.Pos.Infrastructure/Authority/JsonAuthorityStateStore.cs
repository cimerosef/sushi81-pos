using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Paths;

namespace Sushi81.Pos.Infrastructure.Authority;

/// <summary>Crash-safe local authority document and bootstrap marker storage.</summary>
public sealed class JsonAuthorityStateStore(IAppPaths paths) : IAuthorityStateStore
{
    private const int CurrentSchemaVersion = 1;
    private const string StateFileName = "authority-state.json";
    private const string BootstrapMarkerFileName = "authority-bootstrap.marker";
    private const string BootstrapAnchorFileName = "authority-bootstrap.anchor";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<AuthorityStateDocument?> LoadAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        var path = Path.Combine(paths.ConfigDirectory, StateFileName);
        if (!File.Exists(path)) return null;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var document = await JsonSerializer.DeserializeAsync<AuthorityStateDocument>(stream, SerializerOptions, cancellationToken);
            if (document is null) throw new InvalidDataException("The authority state file contains no document.");
            if (document.SchemaVersion != CurrentSchemaVersion) throw new InvalidDataException($"The authority state schema version {document.SchemaVersion} is not supported.");
            if (!Enum.IsDefined(document.State) || document.State == WriteAuthorityState.Uninitialized)
                throw new InvalidDataException("The authority state file contains an invalid state.");
            if (document.UpdatedAtUtc == default) throw new InvalidDataException("The authority state file has no update timestamp.");
            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The authority state file '{path}' is malformed.", exception);
        }
    }

    public async Task SaveAsync(AuthorityStateDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != CurrentSchemaVersion || !Enum.IsDefined(document.State) || document.State == WriteAuthorityState.Uninitialized)
            throw new InvalidDataException("The authority state document is invalid.");
        paths.EnsureInitialized();
        await WriteAtomicallyAsync(Path.Combine(paths.ConfigDirectory, StateFileName), document, cancellationToken);
    }

    public Task<bool> HasBootstrapMarkerAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(Path.Combine(paths.ConfigDirectory, BootstrapMarkerFileName)));
    }

    public async Task WriteBootstrapMarkerAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        var path = Path.Combine(paths.ConfigDirectory, BootstrapMarkerFileName);
        if (File.Exists(path)) return;
        var temporaryPath = Path.Combine(paths.ConfigDirectory, $".{BootstrapMarkerFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync("Sushi81 POS local authority bootstrap completed\n"u8.ToArray(), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    /// <summary>
    /// Captures evidence before startup migrations run. A caller must retain the result and
    /// pass it to <see cref="AuthorityStateCoordinator.InitializeAsync(bool, CancellationToken)"/>;
    /// checking the migrated schema afterward would make a fresh database look like an M01-M05 installation.
    /// </summary>
    public async Task<bool> HasLegacyBootstrapEvidenceAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        if (!File.Exists(paths.LiveDatabasePath)) return false;

        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = paths.LiveDatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) >= 5;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    public Task<bool> HasBootstrapAnchorAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(Path.Combine(paths.DataDirectory, BootstrapAnchorFileName)));
    }

    public Task<bool> HasEstablishedAuthorityArtifactsAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            File.Exists(Path.Combine(paths.ConfigDirectory, StateFileName))
            || File.Exists(Path.Combine(paths.ConfigDirectory, BootstrapMarkerFileName))
            || File.Exists(Path.Combine(paths.DataDirectory, BootstrapAnchorFileName)));
    }

    public async Task WriteBootstrapAnchorAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        var path = Path.Combine(paths.DataDirectory, BootstrapAnchorFileName);
        if (File.Exists(path)) return;
        var temporaryPath = Path.Combine(paths.DataDirectory, $".{BootstrapAnchorFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync("Sushi81 POS M06 bootstrap completed\n"u8.ToArray(), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task WriteAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
