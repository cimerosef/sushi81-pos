using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Paths;

namespace Sushi81.Pos.Infrastructure.Authority;

public sealed record AuthorityStartupEvidence(
    bool HasPreExistingLiveDatabase,
    bool HasEstablishedAuthorityArtifacts);

/// <summary>
/// Captures the startup continuity evidence that must be known before SQLite migrations can
/// create a missing live database. An established authority artifact without a pre-existing
/// database is a fail-closed startup condition, not permission to create a replacement.
/// </summary>
public static class AuthorityStartupPreflight
{
    public static async Task<AuthorityStartupEvidence> CaptureAsync(
        IAppPaths paths,
        IAuthorityStateStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(store);

        paths.EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        var hasPreExistingLiveDatabase = File.Exists(paths.LiveDatabasePath)
            && new FileInfo(paths.LiveDatabasePath).Length > 0;
        var hasEstablishedAuthorityArtifacts = await store.HasEstablishedAuthorityArtifactsAsync(cancellationToken);

        if (hasEstablishedAuthorityArtifacts && !hasPreExistingLiveDatabase)
        {
            throw new AuthorityStartupBlockedException(
                "Established local authority evidence exists, but the pre-existing live database is missing. Startup is blocked to prevent a replacement database from becoming authoritative.");
        }

        var evidence = new AuthorityStartupEvidence(hasPreExistingLiveDatabase, hasEstablishedAuthorityArtifacts);
        await store.EnsureFreshInstallProvenanceAsync(
            evidence.HasPreExistingLiveDatabase,
            evidence.HasEstablishedAuthorityArtifacts,
            cancellationToken);
        return evidence;
    }
}

public sealed class AuthorityStartupBlockedException(string message) : InvalidOperationException(message);
