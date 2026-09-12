namespace Sushi81.Pos.Application.Foundation.Authority;

/// <summary>Durable local authority state. Missing state is not itself writable authority.</summary>
public sealed record AuthorityStateDocument(
    int SchemaVersion,
    WriteAuthorityState State,
    DateTimeOffset UpdatedAtUtc)
{
    public AuthorityProtocolState? Protocol { get; init; }

    public WriteAuthorityState EffectiveState => Protocol?.WriteState ?? State;
}

public interface IAuthorityStateStore
{
    Task<AuthorityStateDocument?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AuthorityStateDocument document, CancellationToken cancellationToken = default);

    Task<bool> HasBootstrapMarkerAsync(CancellationToken cancellationToken = default);

    Task WriteBootstrapMarkerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Independent evidence captured before startup migrations that M01-M05 local data already
    /// existed for the one-time M06 bootstrap. The result must not be recomputed after migration.
    /// </summary>
    Task<bool> HasLegacyBootstrapEvidenceAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>Independent durable evidence that M06 bootstrap has already completed.</summary>
    Task<bool> HasBootstrapAnchorAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    Task WriteBootstrapAnchorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Reports whether any persisted authority artifact exists before startup migrations.
    /// This is used to prevent a missing established database from being recreated and then
    /// accepted as the authoritative local database.
    /// </summary>
    Task<bool> HasEstablishedAuthorityArtifactsAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>
    /// Persists non-authority provenance for an installation that was genuinely empty at the
    /// beginning of its first M07-capable startup. The provenance can only make legacy
    /// bootstrap ineligible; it can never grant authority or lineage.
    /// </summary>
    Task EnsureFreshInstallProvenanceAsync(
        bool hasPreExistingLiveDatabase,
        bool hasEstablishedAuthorityArtifacts,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Reports valid fresh-install provenance; malformed or partial provenance must fail closed.</summary>
    Task<bool> HasFreshInstallProvenanceAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
}
