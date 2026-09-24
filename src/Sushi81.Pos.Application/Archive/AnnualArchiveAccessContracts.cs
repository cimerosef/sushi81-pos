using Sushi81.Pos.Domain;
using Sushi81.Pos.Application.Catalogue;

namespace Sushi81.Pos.Application.Archive;

/// <summary>One validated, application-managed annual archive available for read-only access.</summary>
public sealed record AnnualArchiveDescriptor(
    int ArchiveYear,
    string CanonicalPath,
    int OrderCount,
    DateTimeOffset BuiltAtUtc)
{
    public string DisplayName => $"{ArchiveYear:D4} ({OrderCount})";
}

public sealed record AnnualArchiveSearchCriteria(
    string? Query = null,
    OrderStatus? Status = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null);

public sealed record AnnualArchiveCopyResult(
    int ArchiveYear,
    string DestinationPath,
    long Length,
    string Sha256);

/// <summary>
/// Read-only historical access boundary. Implementations must use only the
/// selected canonical archive and its persisted snapshots; they must not query
/// live.db, Catalogue, settings, or authority state for historical values.
/// </summary>
public interface IAnnualArchiveAccess
{
    Task<IReadOnlyList<AnnualArchiveDescriptor>> DiscoverAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderBrowserRow>> SearchAsync(
        int archiveYear,
        AnnualArchiveSearchCriteria criteria,
        CancellationToken cancellationToken = default);

    Task<OrderSnapshot?> GetOrderAsync(
        int archiveYear,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<AnnualArchiveCopyResult> CopyAsync(
        int archiveYear,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
