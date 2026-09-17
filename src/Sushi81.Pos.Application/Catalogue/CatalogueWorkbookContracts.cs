using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Catalogue;

/// <summary>
/// Application-owned, library-neutral representation of a complete current Catalogue
/// export. Technical IDs are carried for safe later import binding but are never business
/// identifiers or exposed through a workbook business column.
/// </summary>
public sealed record CatalogueWorkbookExport(
    IReadOnlyList<CatalogueWorkbookProduct> Products,
    Guid ExportInstanceId);

public sealed record CatalogueWorkbookProduct(
    Guid ProductId,
    string Code,
    string Name,
    string CategoryName,
    string? CategoryShortCode,
    Money PriceTtc,
    decimal VatRate,
    bool IsActive,
    bool DiscountEligible,
    bool OptionsEnabled,
    IReadOnlyList<CatalogueWorkbookOptionGroup> OptionGroups);

public sealed record CatalogueWorkbookOptionGroup(
    Guid OptionGroupId,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string Name,
    SelectionMode SelectionMode,
    bool IsRequired,
    int? MinSelections,
    int? MaxSelections,
    int DisplayOrder,
    IReadOnlyList<CatalogueWorkbookOption> Options);

public sealed record CatalogueWorkbookOption(
    Guid OptionId,
    Guid OptionGroupId,
    string ProductCode,
    string ProductName,
    string OptionGroupName,
    string Name,
    Money PriceAdjustmentTtc,
    bool IsActive,
    int DisplayOrder);

/// <summary>Infrastructure owns the .xlsx library; this interface deliberately does not.</summary>
public interface ICatalogueWorkbookGateway
{
    Task WriteAsync(CatalogueWorkbookExport model, Stream destination, CancellationToken cancellationToken = default);
}

/// <summary>
/// Mandatory production read seam that materializes the complete Catalogue from one
/// consistent database snapshot before workbook serialization starts.
/// </summary>
public interface ICatalogueWorkbookSnapshotQueries
{
    Task<IReadOnlyList<CatalogueWorkbookProduct>> ReadCatalogueWorkbookSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds a complete read-only export from the mandatory Catalogue snapshot seam.
/// Export does not acquire write authority and does not notify recovery.
/// </summary>
public sealed class CatalogueWorkbookService(ICatalogueWorkbookSnapshotQueries snapshotQueries, ICatalogueWorkbookGateway gateway)
{
    private readonly ICatalogueWorkbookSnapshotQueries snapshotQueries = snapshotQueries ?? throw new ArgumentNullException(nameof(snapshotQueries));
    private readonly ICatalogueWorkbookGateway gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));

    public async Task ExportAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var snapshotProducts = await snapshotQueries.ReadCatalogueWorkbookSnapshotAsync(cancellationToken);
        var orderedProducts = snapshotProducts
            .OrderBy(product => product.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(product => product.ProductId)
            .ToArray();
        await gateway.WriteAsync(new CatalogueWorkbookExport(orderedProducts, Guid.NewGuid()), destination, cancellationToken);
    }
}
