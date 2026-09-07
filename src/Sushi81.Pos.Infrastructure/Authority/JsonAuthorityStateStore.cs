using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Paths;

namespace Sushi81.Pos.Infrastructure.Authority;

/// <summary>Crash-safe local authority document and bootstrap marker storage.</summary>
public sealed class JsonAuthorityStateStore(IAppPaths paths, Action<string>? durabilityProbe = null) : IAuthorityStateStore
{
    private const int LegacySchemaVersion = 1;
    private const int CanonicalSchemaVersion = 2;
    private const string StateFileName = "authority-state.json";
    private const string BootstrapMarkerFileName = "authority-bootstrap.marker";
    private const string BootstrapAnchorFileName = "authority-bootstrap.anchor";
    private static readonly byte[] BootstrapMarkerContents = "Sushi81 POS local authority bootstrap completed\n"u8.ToArray();
    private static readonly byte[] BootstrapAnchorContents = "Sushi81 POS M06 bootstrap completed\n"u8.ToArray();
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
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var schema = json.RootElement.GetProperty("schemaVersion").GetInt32();
            AuthorityStateDocument? document;
            if (schema == 2)
            {
                if (json.RootElement.TryGetProperty("state", out _))
                    throw new InvalidDataException("Canonical authority may not persist a competing coarse state.");
                var protocol = json.RootElement.GetProperty("protocol").Deserialize<AuthorityProtocolState>(SerializerOptions)
                    ?? throw new InvalidDataException("Canonical authority is missing.");
                protocol.Validate();
                document = new(2, protocol.WriteState, json.RootElement.GetProperty("updatedAtUtc").GetDateTimeOffset()) { Protocol = protocol };
            }
            else document = json.RootElement.Deserialize<AuthorityStateDocument>(SerializerOptions);
            if (document is null) throw new InvalidDataException("The authority state file contains no document.");
            if (document.SchemaVersion is not (LegacySchemaVersion or CanonicalSchemaVersion)) throw new InvalidDataException($"The authority state schema version {document.SchemaVersion} is not supported.");
            if (!Enum.IsDefined(document.State) || document.State == WriteAuthorityState.Uninitialized)
                throw new InvalidDataException("The authority state file contains an invalid state.");
            if (document.SchemaVersion == CanonicalSchemaVersion && document.Protocol is null)
                throw new InvalidDataException("The canonical authority state is missing its protocol.");
            if (document.UpdatedAtUtc == default) throw new InvalidDataException("The authority state file has no update timestamp.");
            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The authority state file '{path}' is malformed.", exception);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"The authority state file '{path}' is malformed.", exception);
        }
    }

    public async Task SaveAsync(AuthorityStateDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion is not (LegacySchemaVersion or CanonicalSchemaVersion) || !Enum.IsDefined(document.State) || document.State == WriteAuthorityState.Uninitialized || document.UpdatedAtUtc == default)
            throw new InvalidDataException("The authority state document is invalid.");
        if (document.SchemaVersion == CanonicalSchemaVersion)
        {
            if (document.Protocol is null || document.State != document.Protocol.WriteState)
                throw new InvalidDataException("Canonical authority contradicts its derived state.");
            document.Protocol.Validate();
        }
        else if (document.Protocol is not null) throw new InvalidDataException("Legacy state cannot carry canonical protocol metadata.");
        paths.EnsureInitialized();
        object payload = document.SchemaVersion == CanonicalSchemaVersion
            ? new { document.SchemaVersion, document.UpdatedAtUtc, document.Protocol }
            : new { document.SchemaVersion, document.State, document.UpdatedAtUtc };
        await WriteAtomicallyAsync(Path.Combine(paths.ConfigDirectory, StateFileName), payload, cancellationToken);
        durabilityProbe?.Invoke("before-reopen");
        var persisted = await LoadAsync(cancellationToken);
        if (persisted != document) throw new InvalidDataException("Authority replacement failed read-back validation.");
        durabilityProbe?.Invoke("after-reopen");
    }

    public async Task<bool> HasBootstrapMarkerAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        return await HasExpectedEvidenceAsync(Path.Combine(paths.ConfigDirectory, BootstrapMarkerFileName), BootstrapMarkerContents, cancellationToken);
    }

    public async Task WriteBootstrapMarkerAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        var path = Path.Combine(paths.ConfigDirectory, BootstrapMarkerFileName);
        if (File.Exists(path))
        {
            if (!await HasExpectedEvidenceAsync(path, BootstrapMarkerContents, cancellationToken))
                throw new InvalidDataException("The existing bootstrap marker is corrupt.");
            return;
        }
        var temporaryPath = Path.Combine(paths.ConfigDirectory, $".{BootstrapMarkerFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(BootstrapMarkerContents, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
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

    public async Task<bool> HasBootstrapAnchorAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        return await HasExpectedEvidenceAsync(Path.Combine(paths.DataDirectory, BootstrapAnchorFileName), BootstrapAnchorContents, cancellationToken);
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
        Directory.CreateDirectory(paths.DataDirectory);
        var path = Path.Combine(paths.DataDirectory, BootstrapAnchorFileName);
        if (File.Exists(path))
        {
            if (!await HasExpectedEvidenceAsync(path, BootstrapAnchorContents, cancellationToken))
                throw new InvalidDataException("The existing bootstrap anchor is corrupt.");
            return;
        }
        var temporaryPath = Path.Combine(paths.DataDirectory, $".{BootstrapAnchorFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(BootstrapAnchorContents, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task<bool> HasExpectedEvidenceAsync(
        string path,
        ReadOnlyMemory<byte> expected,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return false;
        var actual = await File.ReadAllBytesAsync(path, cancellationToken);
        return actual.AsSpan().SequenceEqual(expected.Span);
    }

    private async Task WriteAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            durabilityProbe?.Invoke("before-write");
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            durabilityProbe?.Invoke("before-replace");
            File.Move(temporaryPath, path, overwrite: true);
            durabilityProbe?.Invoke("after-replace");
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
