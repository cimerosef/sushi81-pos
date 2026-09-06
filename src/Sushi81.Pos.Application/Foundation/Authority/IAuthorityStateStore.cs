namespace Sushi81.Pos.Application.Foundation.Authority;

/// <summary>Durable local authority state. Missing state is not itself writable authority.</summary>
public sealed record AuthorityStateDocument(
    int SchemaVersion,
    WriteAuthorityState State,
    DateTimeOffset UpdatedAtUtc);

public interface IAuthorityStateStore
{
    Task<AuthorityStateDocument?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AuthorityStateDocument document, CancellationToken cancellationToken = default);

    Task<bool> HasBootstrapMarkerAsync(CancellationToken cancellationToken = default);

    Task WriteBootstrapMarkerAsync(CancellationToken cancellationToken = default);

    /// <summary>Independent evidence that M01-M05 local data exists for the one-time M06 bootstrap.</summary>
    Task<bool> HasLegacyBootstrapEvidenceAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>Independent durable evidence that M06 bootstrap has already completed.</summary>
    Task<bool> HasBootstrapAnchorAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    Task WriteBootstrapAnchorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
