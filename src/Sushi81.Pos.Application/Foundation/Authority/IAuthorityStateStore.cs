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
}
