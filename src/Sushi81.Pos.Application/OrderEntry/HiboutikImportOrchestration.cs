using System.Collections.Immutable;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.OrderEntry;

/// <summary>Transient resolution state for one parsed Hiboutik source row.</summary>
public enum HiboutikImportLineResolution
{
    Unresolved,
    Resolved,
    KnownIgnored,
    ExplicitlyIgnored
}

/// <summary>
/// Application-owned transient state for one imported row. It retains only the
/// operator-facing source evidence and the current catalogue resolution; it is
/// never a persistence or domain entity.
/// </summary>
public sealed record HiboutikImportLineState(
    int SourceLineNumber,
    string SourceText,
    HiboutikPasteLineKind ParsedKind,
    HiboutikPasteIgnoredReason? IgnoredReason,
    int? ParsedQuantity,
    string? CandidateCode,
    string? SourceDescription,
    Money? SourceAmountTtc,
    HiboutikImportLineResolution Resolution,
    OrderEntryProduct? Product,
    int? Quantity,
    IReadOnlyList<Guid> SelectedOptionIds,
    IReadOnlyList<OrderLineAdjustmentDraft> CustomAdjustments,
    bool OptionReviewCompleted)
{
    public bool IsResolved => Resolution == HiboutikImportLineResolution.Resolved;
    public bool IsUnresolved => Resolution == HiboutikImportLineResolution.Unresolved;
    public bool IsIgnored => Resolution is HiboutikImportLineResolution.KnownIgnored or HiboutikImportLineResolution.ExplicitlyIgnored;
    public bool RequiresOptionReview => IsResolved && Product?.Aggregate.Product.OptionsEnabled == true;
    public bool IsOptionReviewPending => RequiresOptionReview && !OptionReviewCompleted;

    public OrderLineDraft? ToOrderLineDraft()
    {
        if (!IsResolved || Product is null || Quantity is not > 0) return null;
        return OrderLineDraft.Create(Product.Aggregate, Quantity.Value) with
        {
            CategoryName = Product.CategoryName,
            SelectedOptionIds = SelectedOptionIds ?? [],
            CustomAdjustments = CustomAdjustments ?? []
        };
    }
}

/// <summary>One transient reason that prevents an imported session from being confirmed.</summary>
public sealed record HiboutikImportBlocker(int SourceLineNumber, string Code, string Message);

/// <summary>
/// Immutable, in-memory import session. The source total is reference evidence
/// only and is not used as pricing authority.
/// </summary>
public sealed record HiboutikImportSession(
    ImmutableArray<HiboutikImportLineState> Lines,
    Money? SourceTotalTtc)
{
    public bool HasUsefulLines => Lines.Length > 0;
    public IReadOnlyList<HiboutikImportLineState> ResolvedLines => Lines.Where(line => line.IsResolved).ToArray();
    public IReadOnlyList<HiboutikImportLineState> UnresolvedLines => Lines.Where(line => line.IsUnresolved).ToArray();
    public IReadOnlyList<HiboutikImportLineState> IgnoredLines => Lines.Where(line => line.IsIgnored).ToArray();
    public IReadOnlyList<HiboutikImportLineState> OptionReviewPendingLines => Lines.Where(line => line.IsOptionReviewPending).ToArray();

    public IReadOnlyList<HiboutikImportBlocker> Blockers
    {
        get
        {
            var blockers = new List<HiboutikImportBlocker>();
            if (!HasUsefulLines)
                blockers.Add(new(0, "import-empty", "The pasted Hiboutik block contains no useful source lines."));
            if (ResolvedLines.Count == 0 && HasUsefulLines)
                blockers.Add(new(0, "import-no-product-lines", "At least one product line must be resolved before confirmation."));

            foreach (var line in Lines)
            {
                if (line.IsUnresolved)
                    blockers.Add(new(line.SourceLineNumber, "import-unresolved", $"Source line {line.SourceLineNumber} still requires an explicit product selection or ignore decision."));
                else if (line.IsOptionReviewPending)
                    blockers.Add(new(line.SourceLineNumber, "import-options-pending", $"Options for source line {line.SourceLineNumber} still require explicit review."));
                else if (line.IsResolved && line.Quantity is not > 0)
                    blockers.Add(new(line.SourceLineNumber, "import-quantity-required", $"Source line {line.SourceLineNumber} requires a positive quantity."));
            }
            return blockers;
        }
    }

    public bool CanConfirm => Blockers.Count == 0;

    public IReadOnlyList<OrderLineDraft> MaterializeOrderLines() =>
        Lines.Select(line => line.ToOrderLineDraft()).Where(line => line is not null).Select(line => line!).ToArray();

    public OperationResult<IReadOnlyList<OrderLineDraft>> TryMaterializeOrderLines()
    {
        var blockers = Blockers;
        return blockers.Count != 0
            ? OperationResult<IReadOnlyList<OrderLineDraft>>.Failure(blockers.Select(ToValidationIssue).ToArray())
            : OperationResult<IReadOnlyList<OrderLineDraft>>.Success(MaterializeOrderLines());
    }

    public OperationResult<NewOrderDraft> TryCreateDraft(
        FulfilmentMode fulfilment,
        DateOnly plannedDate,
        TimeOnly? plannedTime,
        string? telephone = null,
        string? deliveryAddress = null,
        string? comment = null,
        bool pickupDiscountRequested = false,
        Money? manualTotalOverride = null)
    {
        var materialized = TryMaterializeOrderLines();
        return !materialized.Succeeded
            ? OperationResult<NewOrderDraft>.Failure(materialized.Issues.ToArray())
            : OperationResult<NewOrderDraft>.Success(new NewOrderDraft(
                materialized.Value!, fulfilment, plannedDate, plannedTime, telephone,
                deliveryAddress, comment, pickupDiscountRequested, manualTotalOverride));
    }

    private static ValidationIssue ToValidationIssue(HiboutikImportBlocker blocker) =>
        new("import", blocker.Message, blocker.Code);
}

/// <summary>
/// Application-layer Hiboutik import orchestration. Every operation before final
/// confirmation is transient and read-only: it parses, resolves or validates but
/// never saves an order or changes the catalogue.
/// </summary>
public sealed class HiboutikImportOrchestrator(
    IOrderEntryCatalogueQueries catalogue,
    IBusinessSettingsStore settings)
{
    private readonly IOrderEntryCatalogueQueries catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
    private readonly IBusinessSettingsStore settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public async Task<HiboutikImportSession> StartImportAsync(string? sourceText, CancellationToken cancellationToken = default)
    {
        var parsed = HiboutikProductBlockParser.Parse(sourceText);
        var states = new List<HiboutikImportLineState>(parsed.Lines.Length);
        foreach (var line in parsed.Lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!line.IsProductCandidate)
            {
                states.Add(CreateNonProductState(line));
                continue;
            }

            var current = await catalogue.GetActiveProductByCodeAsync(line.CandidateCode, cancellationToken);
            if (current is null || !current.Aggregate.Product.IsActive)
            {
                states.Add(CreateUnresolvedState(line));
                continue;
            }

            var quantity = PositiveQuantityOrNull(line.Quantity);
            states.Add(new HiboutikImportLineState(
                line.SourceLineNumber, line.SourceText, line.Kind, line.IgnoredReason,
                line.Quantity, line.CandidateCode, line.SourceDescription, line.SourceAmountTtc,
                HiboutikImportLineResolution.Resolved, current, quantity, [], [],
                !current.Aggregate.Product.OptionsEnabled));
        }

        return new HiboutikImportSession(states.ToImmutableArray(), parsed.SourceTotalTtc);
    }

    public Task<HiboutikImportSession> StartAsync(string? sourceText, CancellationToken cancellationToken = default) =>
        StartImportAsync(sourceText, cancellationToken);

    public async Task<OperationResult<HiboutikImportSession>> ResolveUnresolvedLineAsync(
        HiboutikImportSession session,
        int sourceLineNumber,
        Guid productId,
        int? quantity = null,
        CancellationToken cancellationToken = default)
    {
        var lineResult = FindLine(session, sourceLineNumber, requireUnresolved: true);
        if (!lineResult.Succeeded) return OperationResult<HiboutikImportSession>.Failure(lineResult.Issues.ToArray());
        if (productId == Guid.Empty)
            return OperationResult<HiboutikImportSession>.Failure(new ValidationIssue("product", "A current Catalogue product is required for manual resolution.", ValidationCodes.ProductMissing));

        var current = await catalogue.GetActiveProductAsync(productId, cancellationToken);
        if (current is null || current.Aggregate.Product.Id != productId || !current.Aggregate.Product.IsActive)
            return OperationResult<HiboutikImportSession>.Failure(new ValidationIssue("product", "The selected product is no longer active or no longer exists.", ValidationCodes.ProductMissing));

        var line = lineResult.Value!;
        var resolvedQuantity = quantity ?? line.Quantity ?? PositiveQuantityOrNull(line.ParsedQuantity);
        if (resolvedQuantity is not > 0)
            return OperationResult<HiboutikImportSession>.Failure(new ValidationIssue("quantity", $"Source line {sourceLineNumber} requires an explicit positive quantity.", ValidationCodes.Required));

        var replacement = line with
        {
            Resolution = HiboutikImportLineResolution.Resolved,
            Product = current,
            Quantity = resolvedQuantity,
            SelectedOptionIds = [],
            CustomAdjustments = [],
            OptionReviewCompleted = !current.Aggregate.Product.OptionsEnabled
        };
        return OperationResult<HiboutikImportSession>.Success(Replace(session!, replacement));
    }

    public Task<OperationResult<HiboutikImportSession>> ResolveLineAsync(
        HiboutikImportSession session,
        int sourceLineNumber,
        Guid productId,
        int? quantity = null,
        CancellationToken cancellationToken = default) =>
        ResolveUnresolvedLineAsync(session, sourceLineNumber, productId, quantity, cancellationToken);

    public static OperationResult<HiboutikImportSession> IgnoreUnresolvedLine(HiboutikImportSession session, int sourceLineNumber)
    {
        var lineResult = FindLine(session, sourceLineNumber, requireUnresolved: true);
        if (!lineResult.Succeeded) return OperationResult<HiboutikImportSession>.Failure(lineResult.Issues.ToArray());
        return OperationResult<HiboutikImportSession>.Success(Replace(session!, lineResult.Value! with
        {
            Resolution = HiboutikImportLineResolution.ExplicitlyIgnored,
            Product = null,
            Quantity = null,
            SelectedOptionIds = [],
            CustomAdjustments = [],
            OptionReviewCompleted = true
        }));
    }

    public static OperationResult<HiboutikImportSession> IgnoreLine(HiboutikImportSession session, int sourceLineNumber) =>
        IgnoreUnresolvedLine(session, sourceLineNumber);

    public async Task<OperationResult<HiboutikImportSession>> CompleteOptionReviewAsync(
        HiboutikImportSession session,
        int sourceLineNumber,
        IReadOnlyList<Guid>? selectedOptionIds = null,
        IReadOnlyList<OrderLineAdjustmentDraft>? customAdjustments = null,
        int? quantity = null,
        CancellationToken cancellationToken = default)
    {
        var lineResult = FindLine(session, sourceLineNumber, requireUnresolved: false);
        if (!lineResult.Succeeded) return OperationResult<HiboutikImportSession>.Failure(lineResult.Issues.ToArray());
        var line = lineResult.Value!;
        if (!line.IsResolved || line.Product is null)
            return OperationResult<HiboutikImportSession>.Failure(new ValidationIssue("import", $"Source line {sourceLineNumber} is not a resolved product line.", "import-not-resolved"));

        var selected = (selectedOptionIds ?? []).ToArray();
        var adjustments = (customAdjustments ?? []).ToArray();
        var resolvedQuantity = quantity ?? line.Quantity ?? 0;
        if (resolvedQuantity <= 0)
            return OperationResult<HiboutikImportSession>.Failure(new ValidationIssue("quantity", $"Source line {sourceLineNumber} requires an explicit positive quantity.", ValidationCodes.Required));
        var validationDraft = new NewOrderDraft(
            [OrderLineDraft.Create(line.Product.Aggregate, resolvedQuantity) with
            {
                CategoryName = line.Product.CategoryName,
                SelectedOptionIds = selected,
                CustomAdjustments = adjustments
            }],
            FulfilmentMode.Retrait, new DateOnly(2000, 1, 1), null, null, null, null, false);

        var pricing = OrderPricingService.Calculate(validationDraft, await settings.GetAsync(cancellationToken));
        if (!pricing.IsValid)
            return OperationResult<HiboutikImportSession>.Failure(pricing.ValidationErrors
                .Select(message => new ValidationIssue("options", message, ValidationCodes.GroupStructure)).ToArray());

        return OperationResult<HiboutikImportSession>.Success(Replace(session!, line with
        {
            Quantity = resolvedQuantity,
            SelectedOptionIds = selected,
            CustomAdjustments = adjustments,
            OptionReviewCompleted = true
        }));
    }

    public Task<OperationResult<HiboutikImportSession>> ReviewOptionsAsync(
        HiboutikImportSession session,
        int sourceLineNumber,
        IReadOnlyList<Guid>? selectedOptionIds = null,
        IReadOnlyList<OrderLineAdjustmentDraft>? customAdjustments = null,
        CancellationToken cancellationToken = default) =>
        CompleteOptionReviewAsync(session, sourceLineNumber, selectedOptionIds, customAdjustments, cancellationToken: cancellationToken);

    private static OperationResult<HiboutikImportLineState> FindLine(
        HiboutikImportSession? session,
        int sourceLineNumber,
        bool requireUnresolved)
    {
        if (session is null)
            return OperationResult<HiboutikImportLineState>.Failure(new ValidationIssue("import", "The import session is required.", ValidationCodes.Required));
        var line = session.Lines.SingleOrDefault(item => item.SourceLineNumber == sourceLineNumber);
        if (line is null)
            return OperationResult<HiboutikImportLineState>.Failure(new ValidationIssue("import", $"Source line {sourceLineNumber} was not found in the import session.", ValidationCodes.NotFound));
        if (requireUnresolved && !line.IsUnresolved)
            return OperationResult<HiboutikImportLineState>.Failure(new ValidationIssue("import", $"Source line {sourceLineNumber} is not awaiting manual resolution.", "import-resolution-state"));
        return OperationResult<HiboutikImportLineState>.Success(line);
    }

    private static HiboutikImportSession Replace(HiboutikImportSession session, HiboutikImportLineState replacement) =>
        session with { Lines = session.Lines.Select(line => line.SourceLineNumber == replacement.SourceLineNumber ? replacement : line).ToImmutableArray() };

    private static HiboutikImportLineState CreateNonProductState(HiboutikPasteLine line) => new(
        line.SourceLineNumber, line.SourceText, line.Kind, line.IgnoredReason,
        line.Quantity, line.CandidateCode, line.SourceDescription, line.SourceAmountTtc,
        line.IsKnownIgnoredLine ? HiboutikImportLineResolution.KnownIgnored : HiboutikImportLineResolution.Unresolved,
        null, PositiveQuantityOrNull(line.Quantity), [], [], line.IsKnownIgnoredLine);

    private static HiboutikImportLineState CreateUnresolvedState(HiboutikPasteLine line) => new(
        line.SourceLineNumber, line.SourceText, line.Kind, line.IgnoredReason,
        line.Quantity, line.CandidateCode, line.SourceDescription, line.SourceAmountTtc,
        HiboutikImportLineResolution.Unresolved, null, PositiveQuantityOrNull(line.Quantity), [], [], false);

    private static int? PositiveQuantityOrNull(int? quantity) => quantity is > 0 ? quantity : null;
}
