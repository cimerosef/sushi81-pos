using Sushi81.Pos.Domain;
using Sushi81.Pos.Application.Foundation;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Recovery;

namespace Sushi81.Pos.Application.Catalogue;

public static class ValidationCodes
{
    public const string Generic = "generic";
    public const string Required = "required";
    public const string CategoryMissing = "category-missing";
    public const string CategoryDuplicate = "category-duplicate";
    public const string CategoryShortCodeDuplicate = "category-short-code-duplicate";
    public const string CategoryShortCodeTooLong = "category-short-code-too-long";
    public const string ProductMissing = "product-missing";
    public const string ProductDuplicateCode = "product-duplicate-code";
    public const string PriceNegative = "price-negative";
    public const string VatRange = "vat-range";
    public const string GroupStructure = "group-structure";
    public const string OptionStructure = "option-structure";
    public const string RequiredChoices = "required-choices";
    public const string SettingsRange = "settings-range";
    public const string InvalidNumber = "invalid-number";
    public const string Busy = "busy";
    public const string Conflict = "conflict";
    public const string BulkRequestInvalid = "bulk-request-invalid";
    public const string PastPlannedDate = "past-planned-date";
    public const string PlannedTimeRequired = "planned-time-required";
    public const string PlannedTimeInvalid = "planned-time-invalid";
    public const string NotFound = "not-found";
    public const string PaymentNegative = "payment-negative";
    public const string PaymentMismatch = "payment-mismatch";
    public const string AuthorityBlocked = "authority-blocked";

    public static string Infer(string message) => message switch
    {
        "Category name is required." or "Product code is required." or "Product name is required." or "Option group name is required." or "Option name is required." => Required,
        "A category with that name already exists." => CategoryDuplicate,
        "A category with that short code already exists." => CategoryShortCodeDuplicate,
        _ when message.StartsWith("Category short code cannot exceed ", StringComparison.Ordinal) => CategoryShortCodeTooLong,
        "The category no longer exists." => CategoryMissing,
        "The product no longer exists." => ProductMissing,
        "A product with that code already exists." => ProductDuplicateCode,
        "Product price cannot be negative." => PriceNegative,
        "VAT rate must be between 0 and 100 percent." => VatRange,
        "Settings money values cannot be negative." or "Pickup discount rate must be between 0 and 100 percent." => SettingsRange,
        "A settings save is already in progress." => Busy,
        _ when message.Contains("Option group", StringComparison.OrdinalIgnoreCase) && message.Contains("active", StringComparison.OrdinalIgnoreCase) => RequiredChoices,
        _ when message.Contains("Option group", StringComparison.OrdinalIgnoreCase) || message.Contains("Selection", StringComparison.OrdinalIgnoreCase) => GroupStructure,
        _ when message.Contains("Option", StringComparison.OrdinalIgnoreCase) => OptionStructure,
        _ => Generic,
    };
}

public sealed record ValidationIssue(string Field, string Message, string? Code = null)
{
    public string StableCode => string.IsNullOrWhiteSpace(Code) ? ValidationCodes.Infer(Message) : Code;
}

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

public sealed record CategorySummary(Guid Id, string Name, string? ShortCode = null)
{
    public string NavigationLabel => string.IsNullOrWhiteSpace(ShortCode) ? Name : ShortCode;
    public string MaintenanceLabel => string.IsNullOrWhiteSpace(ShortCode) ? Name : $"{ShortCode} — {Name}";
}

public sealed record OrderBrowserRow(
    Guid Id,
    DateOnly PlannedFulfilmentDate,
    TimeOnly? PlannedFulfilmentTime,
    FulfilmentMode Fulfilment,
    OrderStatus Status,
    Money TotalTtc,
    string? Telephone)
{
    public string Reference { get; init; } = string.Empty;
    public string? DeliveryAddress { get; init; }
    public string? Comment { get; init; }
    public Money CardPaymentTtc { get; init; } = Money.Zero;
    public Money CashPaymentTtc { get; init; } = Money.Zero;
    public bool AdvanceOrderMarker { get; init; }

    public Money CbPaymentTtc { get => CardPaymentTtc; init => CardPaymentTtc = value; }
    public Money EspecePaymentTtc { get => CashPaymentTtc; init => CashPaymentTtc = value; }

    public Money PaidTtc => CardPaymentTtc + CashPaymentTtc;
    public Money DifferenceTtc => TotalTtc - PaidTtc;
}

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

public sealed record BulkProductActiveStateItem(Guid ProductId, bool ExpectedIsActive);

public sealed record BulkProductActiveStateRequest(
    bool TargetIsActive,
    IReadOnlyList<BulkProductActiveStateItem> Items);

public sealed record BulkProductActiveStateResult(int MatchedCount, int ChangedCount);

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
    Task<OperationResult<CategorySummary>> CreateCategoryWithCodeAsync(string name, string? shortCode, CancellationToken cancellationToken = default);
    Task<OperationResult<CategorySummary>> RenameCategoryWithCodeAsync(Guid categoryId, string name, string? shortCode, CancellationToken cancellationToken = default);
    Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default);
    Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default);
    Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default);
    Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default);
}

public sealed class CatalogueService
{
    private readonly ICatalogueStore store;
    private readonly IWriteAuthorityGuard authorityGuard;
    private readonly IDurableChangeNotifier notifier;

    public CatalogueService(
        ICatalogueStore store,
        IWriteAuthorityGuard authorityGuard,
        IDurableChangeNotifier notifier)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
        this.notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    }

    // Test assemblies use the explicit test-only wiring supplied by the application project.
    // Production composition has no constructor that can omit the M06 write/recovery seam.
    internal CatalogueService(ICatalogueStore store)
        : this(store, TestOnlyAuthoritativeGuard.Instance, TestOnlyDurableChangeNotifier.Instance) { }

    public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => store.ListCategoriesAsync(cancellationToken);
    public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default) => store.ListProductsAsync(search, categoryId, active, cancellationToken);
    public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => store.GetProductForEditAsync(productId, cancellationToken);
    public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => MutateAsync(() => store.CreateCategoryAsync(name, cancellationToken));
    public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid id, string name, CancellationToken cancellationToken = default) => MutateAsync(() => store.RenameCategoryAsync(id, name, cancellationToken));
    public Task<OperationResult<CategorySummary>> CreateCategoryWithCodeAsync(string name, string? shortCode, CancellationToken cancellationToken = default) => ValidateAndCreateCategoryAsync(name, shortCode, cancellationToken);
    public Task<OperationResult<CategorySummary>> RenameCategoryWithCodeAsync(Guid id, string name, string? shortCode, CancellationToken cancellationToken = default) => ValidateAndRenameCategoryAsync(id, name, shortCode, cancellationToken);
    public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => ValidateAndCreateAsync(draft, cancellationToken);
    public Task<OperationResult> UpdateProductAsync(Guid id, ProductDraft draft, CancellationToken cancellationToken = default) => ValidateAndUpdateAsync(id, draft, cancellationToken);
    public async Task<OperationResult> SetProductActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var current = await store.GetProductForEditAsync(id, cancellationToken);
        if (current is not null && current.IsActive == active) return OperationResult.Success();
        return await MutateAsync(() => store.SetProductActiveAsync(id, active, cancellationToken));
    }
    public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) return Task.FromResult(OperationResult<BulkProductActiveStateResult>.Failure(new ValidationIssue("products", "The bulk catalogue request is invalid.", ValidationCodes.BulkRequestInvalid)));
        var items = request.Items ?? [];
        if (items.Count == 0) return Task.FromResult(OperationResult<BulkProductActiveStateResult>.Failure(new ValidationIssue("products", "The bulk catalogue request is empty.", ValidationCodes.BulkRequestInvalid)));
        if (items.Any(item => item.ProductId == Guid.Empty)) return Task.FromResult(OperationResult<BulkProductActiveStateResult>.Failure(new ValidationIssue("products", "The bulk catalogue request contains an invalid product.", ValidationCodes.BulkRequestInvalid)));
        if (items.Select(item => item.ProductId).Distinct().Count() != items.Count) return Task.FromResult(OperationResult<BulkProductActiveStateResult>.Failure(new ValidationIssue("products", "The bulk catalogue request contains duplicate products.", ValidationCodes.BulkRequestInvalid)));
        return BulkMutateAsync(request with { Items = items }, cancellationToken);
    }
    public Task<OperationResult> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(() => store.DeleteProductAsync(id, cancellationToken));

    private async Task<OperationResult<Guid>> ValidateAndCreateAsync(ProductDraft draft, CancellationToken cancellationToken)
    {
        var validation = ValidateDraft(draft, requireId: false);
        if (validation is not null) return OperationResult<Guid>.Failure(validation);
        return await MutateAsync(() => store.CreateProductAsync(draft, cancellationToken));
    }

    private async Task<OperationResult<CategorySummary>> ValidateAndCreateCategoryAsync(string name, string? shortCode, CancellationToken cancellationToken)
    {
        var error = CatalogueValidation.ValidateCategoryShortCode(shortCode);
        if (error is not null) return OperationResult<CategorySummary>.Failure(new ValidationIssue("shortCode", error, ValidationCodes.Infer(error)));
        return await MutateAsync(() => store.CreateCategoryWithCodeAsync(name, shortCode, cancellationToken));
    }

    private async Task<OperationResult<CategorySummary>> ValidateAndRenameCategoryAsync(Guid id, string name, string? shortCode, CancellationToken cancellationToken)
    {
        var error = CatalogueValidation.ValidateCategoryShortCode(shortCode);
        if (error is not null) return OperationResult<CategorySummary>.Failure(new ValidationIssue("shortCode", error, ValidationCodes.Infer(error)));
        return await MutateAsync(() => store.RenameCategoryWithCodeAsync(id, name, shortCode, cancellationToken));
    }

    private async Task<OperationResult> ValidateAndUpdateAsync(Guid id, ProductDraft draft, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || draft.Id != Guid.Empty && draft.Id != id) return OperationResult.Failure(new ValidationIssue("product", "The product no longer exists.", ValidationCodes.ProductMissing));
        var validation = ValidateDraft(draft with { Id = id }, requireId: true);
        if (validation is not null) return OperationResult.Failure(validation);
        var current = await store.GetProductForEditAsync(id, cancellationToken);
        if (current is not null && ProductDraftsEqual(current, draft with { Id = id })) return OperationResult.Success();
        return await MutateAsync(() => store.UpdateProductAsync(id, draft with { Id = id }, cancellationToken));
    }

    private async Task<OperationResult<T>> MutateAsync<T>(Func<Task<OperationResult<T>>> operation)
    {
        try
        {
            await using var authorityScope = await authorityGuard.EnterWriteScopeAsync();
            var result = await operation();
            if (result.Succeeded) await NotifySafelyAsync();
            return result;
        }
        catch (WriteAuthorityException exception)
        {
            return OperationResult<T>.Failure(AuthorityIssue(exception));
        }
    }

    private async Task<OperationResult> MutateAsync(Func<Task<OperationResult>> operation)
    {
        try
        {
            await using var authorityScope = await authorityGuard.EnterWriteScopeAsync();
            var result = await operation();
            if (result.Succeeded) await NotifySafelyAsync();
            return result;
        }
        catch (WriteAuthorityException exception)
        {
            return OperationResult.Failure(AuthorityIssue(exception));
        }
    }

    private async Task<OperationResult<BulkProductActiveStateResult>> BulkMutateAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var authorityScope = await authorityGuard.EnterWriteScopeAsync(cancellationToken);
            var result = await store.BulkSetProductsActiveAsync(request, cancellationToken);
            if (result.Succeeded && result.Value?.ChangedCount > 0) await NotifySafelyAsync();
            return result;
        }
        catch (WriteAuthorityException exception)
        {
            return OperationResult<BulkProductActiveStateResult>.Failure(AuthorityIssue(exception));
        }
    }

    private async Task NotifySafelyAsync()
    {
        try { await notifier.NotifyCommittedAsync(CancellationToken.None); }
        catch { /* The business transaction is already durable; recovery failure is logged by its infrastructure seam. */ }
    }

    private static ValidationIssue AuthorityIssue(WriteAuthorityException exception) =>
        new("authority", $"Local write authority is unavailable ({exception.State}).", ValidationCodes.AuthorityBlocked);

    private static bool ProductDraftsEqual(ProductDraft left, ProductDraft right) =>
        left.Id == right.Id && string.Equals(left.Code, right.Code, StringComparison.Ordinal)
        && string.Equals(left.Name, right.Name, StringComparison.Ordinal) && left.CategoryId == right.CategoryId
        && left.PriceTtc == right.PriceTtc && left.VatRate == right.VatRate && left.IsActive == right.IsActive
        && left.DiscountEligible == right.DiscountEligible && left.OptionsEnabled == right.OptionsEnabled
        && (left.Groups ?? []).OrderBy(group => group.DisplayOrder).SequenceEqual((right.Groups ?? []).OrderBy(group => group.DisplayOrder), GroupComparer.Instance);

    private sealed class GroupComparer : IEqualityComparer<OptionGroupDraft>
    {
        public static GroupComparer Instance { get; } = new();
        public bool Equals(OptionGroupDraft? left, OptionGroupDraft? right) => left is not null && right is not null
            && left.Id == right.Id && left.Name == right.Name && left.SelectionMode == right.SelectionMode && left.IsRequired == right.IsRequired
            && left.MinSelections == right.MinSelections && left.MaxSelections == right.MaxSelections && left.DisplayOrder == right.DisplayOrder
            && (left.Options ?? []).OrderBy(option => option.DisplayOrder).SequenceEqual(right.Options ?? [], OptionComparer.Instance);
        public int GetHashCode(OptionGroupDraft value) => value.Id.GetHashCode();
    }

    private sealed class OptionComparer : IEqualityComparer<OptionDraft>
    {
        public static OptionComparer Instance { get; } = new();
        public bool Equals(OptionDraft? left, OptionDraft? right) => left is not null && right is not null && left == right;
        public int GetHashCode(OptionDraft value) => value.Id.GetHashCode();
    }

    private static ValidationIssue? ValidateDraft(ProductDraft draft, bool requireId)
    {
        if (requireId && draft.Id == Guid.Empty) return new("product", "The product no longer exists.", ValidationCodes.ProductMissing);
        var error = CatalogueValidation.ValidateProduct(draft.Code, draft.Name, draft.CategoryId, draft.PriceTtc, draft.VatRate);
        if (error is not null) return new("product", error, ValidationCodes.Infer(error));
        var groups = draft.Groups ?? [];
        var seenGroupOrders = new HashSet<int>();
        var seenGroupIds = new HashSet<Guid>();
        var seenOptionIds = new HashSet<Guid>();
        foreach (var group in groups)
        {
            if (group.Id != Guid.Empty && !seenGroupIds.Add(group.Id)) return new("groups", "Option group identities must be unique.", ValidationCodes.GroupStructure);
            if (!seenGroupOrders.Add(group.DisplayOrder)) return new("groups", "Option group order must be unique.", ValidationCodes.GroupStructure);
            var groupEntity = new OptionGroup(group.Id, draft.Id, group.Name, group.SelectionMode, group.IsRequired, group.MinSelections, group.MaxSelections, group.DisplayOrder, default, default);
            error = CatalogueValidation.ValidateGroup(groupEntity);
            if (error is not null) return new("groups", error, ValidationCodes.Infer(error));
            var seenOptionOrders = new HashSet<int>();
            foreach (var option in group.Options ?? [])
            {
                if (option.Id != Guid.Empty && !seenOptionIds.Add(option.Id)) return new("options", "Option identities must be unique.", ValidationCodes.OptionStructure);
                if (!seenOptionOrders.Add(option.DisplayOrder)) return new("options", "Option order must be unique.", ValidationCodes.OptionStructure);
                var optionEntity = new ProductOption(option.Id, group.Id, option.Name, option.PriceAdjustmentTtc, option.IsActive, option.DisplayOrder, default, default);
                error = CatalogueValidation.ValidateOption(optionEntity);
                if (error is not null) return new("options", error, ValidationCodes.Infer(error));
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
            if (active < minimum) return new("options", $"Option group '{CatalogueNormalization.Display(group.Name)}' does not have enough active choices.", ValidationCodes.RequiredChoices);
        }

        return null;
    }
}
