using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Catalogue;

public sealed record ValidationIssue(string Field, string Message);

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "Factory methods provide concise immutable result construction for generic results.")]
public class OperationResult(bool succeeded, IReadOnlyList<ValidationIssue> issues)
{
    public bool Succeeded { get; } = succeeded;
    public bool IsSuccess => Succeeded;
    public IReadOnlyList<ValidationIssue> Issues { get; } = issues;
    public string? ErrorMessage => Issues.Count == 0 ? null : string.Join(" ", Issues.Select(issue => issue.Message));

    public static OperationResult Success() => new(true, []);
    public static OperationResult Failure(params ValidationIssue[] issues) => new(false, issues);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "Factory methods provide concise immutable result construction for generic results.")]
public sealed class OperationResult<T>(bool succeeded, T? value, IReadOnlyList<ValidationIssue> issues) : OperationResult(succeeded, issues)
{
    public T? Value { get; } = value;
    public static OperationResult<T> Success(T value) => new(true, value, []);
    public static new OperationResult<T> Failure(params ValidationIssue[] issues) => new(false, default, issues);
}

public sealed record CategorySummary(Guid Id, string Name);

public sealed record ProductSummary(
    Guid Id,
    string Code,
    string Name,
    Guid CategoryId,
    string CategoryName,
    Money PriceTtc,
    decimal VatRate,
    bool IsActive,
    bool DiscountEligible,
    bool OptionsEnabled);

public sealed record OptionDraft(
    Guid Id,
    string Name,
    Money PriceAdjustmentTtc,
    bool IsActive,
    int DisplayOrder);

public sealed record OptionGroupDraft(
    Guid Id,
    string Name,
    SelectionMode SelectionMode,
    bool IsRequired,
    int? MinSelections,
    int? MaxSelections,
    int DisplayOrder,
    IReadOnlyList<OptionDraft> Options);

public sealed record ProductDraft(
    Guid Id,
    string Code,
    string Name,
    Guid CategoryId,
    Money PriceTtc,
    decimal VatRate,
    bool IsActive,
    bool DiscountEligible,
    bool OptionsEnabled,
    IReadOnlyList<OptionGroupDraft> Groups);

public interface ICatalogueQueries
{
    Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default);
    Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default);
}

public interface ICatalogueStore : ICatalogueQueries
{
    Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default);
    Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid categoryId, string name, CancellationToken cancellationToken = default);
    Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default);
    Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default);
    Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default);
}

public sealed class CatalogueService(ICatalogueStore store)
{
    private readonly ICatalogueStore store = store ?? throw new ArgumentNullException(nameof(store));

    public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => store.ListCategoriesAsync(cancellationToken);
    public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default) => store.ListProductsAsync(search, categoryId, active, cancellationToken);
    public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => store.GetProductForEditAsync(productId, cancellationToken);
    public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => store.CreateCategoryAsync(name, cancellationToken);
    public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid id, string name, CancellationToken cancellationToken = default) => store.RenameCategoryAsync(id, name, cancellationToken);
    public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => ValidateAndCreateAsync(draft, cancellationToken);
    public Task<OperationResult> UpdateProductAsync(Guid id, ProductDraft draft, CancellationToken cancellationToken = default) => ValidateAndUpdateAsync(id, draft, cancellationToken);
    public Task<OperationResult> SetProductActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default) => store.SetProductActiveAsync(id, active, cancellationToken);
    public Task<OperationResult> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => store.DeleteProductAsync(id, cancellationToken);

    private async Task<OperationResult<Guid>> ValidateAndCreateAsync(ProductDraft draft, CancellationToken cancellationToken)
    {
        var validation = ValidateDraft(draft, requireId: false);
        return validation is not null ? OperationResult<Guid>.Failure(validation) : await store.CreateProductAsync(draft, cancellationToken);
    }

    private async Task<OperationResult> ValidateAndUpdateAsync(Guid id, ProductDraft draft, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || draft.Id != Guid.Empty && draft.Id != id) return OperationResult.Failure(new ValidationIssue("product", "The product no longer exists."));
        var validation = ValidateDraft(draft with { Id = id }, requireId: true);
        return validation is not null ? OperationResult.Failure(validation) : await store.UpdateProductAsync(id, draft with { Id = id }, cancellationToken);
    }

    private static ValidationIssue? ValidateDraft(ProductDraft draft, bool requireId)
    {
        if (requireId && draft.Id == Guid.Empty) return new("product", "The product no longer exists.");
        var error = CatalogueValidation.ValidateProduct(draft.Code, draft.Name, draft.CategoryId, draft.PriceTtc, draft.VatRate);
        if (error is not null) return new("product", error);
        var groups = draft.Groups ?? [];
        var seenGroupOrders = new HashSet<int>();
        var seenGroupIds = new HashSet<Guid>();
        var seenOptionIds = new HashSet<Guid>();
        foreach (var group in groups)
        {
            if (group.Id != Guid.Empty && !seenGroupIds.Add(group.Id)) return new("groups", "Option group identities must be unique.");
            if (!seenGroupOrders.Add(group.DisplayOrder)) return new("groups", "Option group order must be unique.");
            var groupEntity = new OptionGroup(group.Id, draft.Id, group.Name, group.SelectionMode, group.IsRequired, group.MinSelections, group.MaxSelections, group.DisplayOrder, default, default);
            error = CatalogueValidation.ValidateGroup(groupEntity);
            if (error is not null) return new("groups", error);
            var seenOptionOrders = new HashSet<int>();
            foreach (var option in group.Options ?? [])
            {
                if (option.Id != Guid.Empty && !seenOptionIds.Add(option.Id)) return new("options", "Option identities must be unique.");
                if (!seenOptionOrders.Add(option.DisplayOrder)) return new("options", "Option order must be unique.");
                var optionEntity = new ProductOption(option.Id, group.Id, option.Name, option.PriceAdjustmentTtc, option.IsActive, option.DisplayOrder, default, default);
                error = CatalogueValidation.ValidateOption(optionEntity);
                if (error is not null) return new("options", error);
            }
        }

        return ValidateRequiredChoices(draft);
    }

    private static ValidationIssue? ValidateRequiredChoices(ProductDraft draft)
    {
        if (!draft.OptionsEnabled) return null;
        foreach (var group in draft.Groups ?? [])
        {
            var active = (group.Options ?? []).Count(option => option.IsActive);
            var minimum = group.SelectionMode == SelectionMode.Single ? (group.IsRequired ? 1 : 0) : group.MinSelections ?? 0;
            if (active < minimum) return new("options", $"Option group '{CatalogueNormalization.Display(group.Name)}' does not have enough active choices.");
        }

        return null;
    }
}
