using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Catalogue;

/// <summary>
/// Application-owned, library-neutral representation of a complete current Catalogue
/// export. Technical IDs are carried for safe later import binding but are never business
/// identifiers or exposed through a workbook business column.
/// </summary>
public sealed record CatalogueWorkbookExport(
    IReadOnlyList<CatalogueWorkbookProduct> Products,
    Guid ExportInstanceId,
    int ContractVersion = 1);

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
/// Builds a complete read-only export from the existing Catalogue query seam.
/// Export does not acquire write authority and does not notify recovery.
/// </summary>
public sealed class CatalogueWorkbookService(ICatalogueQueries catalogue, ICatalogueWorkbookGateway gateway)
{
    private readonly ICatalogueQueries catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
    private readonly ICatalogueWorkbookGateway gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));

    public async Task ExportAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var products = await catalogue.ListProductsAsync(cancellationToken: cancellationToken);
        var exported = new List<CatalogueWorkbookProduct>(products.Count);

        foreach (var summary in products.OrderBy(product => product.Code, StringComparer.OrdinalIgnoreCase).ThenBy(product => product.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var draft = await catalogue.GetProductForEditAsync(summary.Id, cancellationToken)
                ?? throw new InvalidDataException($"Current product '{summary.Id}' disappeared during Catalogue export.");

            var groups = (draft.Groups ?? [])
                .OrderBy(group => group.DisplayOrder)
                .ThenBy(group => group.Id)
                .Select(group => new CatalogueWorkbookOptionGroup(
                    group.Id,
                    draft.Id,
                    summary.Code,
                    summary.Name,
                    group.Name,
                    group.SelectionMode,
                    group.IsRequired,
                    group.MinSelections,
                    group.MaxSelections,
                    group.DisplayOrder,
                    (group.Options ?? [])
                        .OrderBy(option => option.DisplayOrder)
                        .ThenBy(option => option.Id)
                        .Select(option => new CatalogueWorkbookOption(
                            option.Id,
                            group.Id,
                            summary.Code,
                            summary.Name,
                            group.Name,
                            option.Name,
                            option.PriceAdjustmentTtc,
                            option.IsActive,
                            option.DisplayOrder))
                        .ToArray()))
                .ToArray();

            exported.Add(new CatalogueWorkbookProduct(
                draft.Id,
                summary.Code,
                summary.Name,
                summary.CategoryName,
                summary.CategoryShortCode,
                summary.PriceTtc,
                summary.VatRate,
                summary.IsActive,
                summary.DiscountEligible,
                summary.OptionsEnabled,
                groups));
        }

        await gateway.WriteAsync(new CatalogueWorkbookExport(exported, Guid.NewGuid()), destination, cancellationToken);
    }
}
