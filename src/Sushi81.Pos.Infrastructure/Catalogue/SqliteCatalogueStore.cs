using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Foundation.Transactions;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.Catalogue;

/// <summary>Explicit SQLite persistence for the current catalogue. No generic repository abstraction is used.</summary>
public sealed class SqliteCatalogueStore(
    SqliteConnectionFactory connectionFactory,
    ITransactionRunner transactionRunner,
    IIdGenerator idGenerator,
    IBusinessClock clock,
    Func<int, Exception?>? bulkWriteFailureInjector = null) : ICatalogueStore, IActiveProductCodeQueries, ICatalogueWorkbookSnapshotQueries, ICatalogueImportStore
{
    private readonly SqliteConnectionFactory connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    private readonly ITransactionRunner transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    private readonly IIdGenerator idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
    private readonly IBusinessClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly Func<int, Exception?>? bulkWriteFailureInjector = bulkWriteFailureInjector;

    /// <summary>
    /// Test-only synchronization seam. It is intentionally internal and null in
    /// production; integration tests use it to pause after the read transaction has
    /// established its first logical snapshot before a concurrent writer commits.
    /// </summary>
    internal Func<CancellationToken, Task>? SnapshotAfterProductsReadAsync { get; set; }

    /// <summary>Test-only pause used to prove the import baseline is one coherent snapshot.</summary>
    internal Func<CancellationToken, Task>? ImportBaselineAfterProductsReadAsync { get; set; }

    /// <summary>Test-only import failure seam; production composition leaves it null.</summary>
    internal Func<int, Exception?>? ImportWriteFailureInjector { get; set; }
    // Test-only fault hook. It can issue an actual SQLite statement inside the
    // import transaction; production behavior is unchanged when unset.
    internal Func<SqliteApplicationTransaction, int, Task>? ImportWriteConstraintInjector { get; set; }

    /// <summary>Test-only pause after the commit transaction has captured its live baseline.</summary>
    internal Func<CancellationToken, Task>? ImportCommitAfterBaselineReadAsync { get; set; }

    public async Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        var hasShortCodes = await HasCategoryShortCodeColumnsAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = hasShortCodes
            ? "SELECT category_id, name, short_code FROM categories ORDER BY CASE WHEN normalized_short_code IS NULL THEN 1 ELSE 0 END, normalized_short_code COLLATE NOCASE, category_id;"
            : "SELECT category_id, name FROM categories ORDER BY name COLLATE NOCASE, category_id;";
        var result = new List<CategorySummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(ParseGuid(reader.GetString(0)), reader.GetString(1), hasShortCodes && !reader.IsDBNull(2) ? reader.GetString(2) : null));
        }
        return result;
    }

    public async Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        var hasShortCodes = await HasCategoryShortCodeColumnsAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = hasShortCodes ? """
            SELECT p.product_id, p.code, p.name, p.category_id, c.name, p.price_ttc_cents, p.vat_rate,
                   p.is_active, p.discount_eligible, p.options_enabled, c.short_code
            FROM products p JOIN categories c ON c.category_id = p.category_id
            ORDER BY p.code COLLATE NOCASE, p.product_id;
            """ : """
            SELECT p.product_id, p.code, p.name, p.category_id, c.name, p.price_ttc_cents, p.vat_rate,
                   p.is_active, p.discount_eligible, p.options_enabled
            FROM products p JOIN categories c ON c.category_id = p.category_id
            ORDER BY p.code COLLATE NOCASE, p.product_id;
            """;
        var result = new List<ProductSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = ParseGuid(reader.GetString(0));
            var code = reader.GetString(1);
            var name = reader.GetString(2);
            var category = ParseGuid(reader.GetString(3));
            var categoryName = reader.GetString(4);
            var shortCode = hasShortCodes && !reader.IsDBNull(10) ? reader.GetString(10) : null;
            var row = new ProductSummary(id, code, name, category, categoryName, Money.FromCents(reader.GetInt64(5)), ParseDecimal(reader.GetString(6)), reader.GetInt64(7) == 1, reader.GetInt64(8) == 1, reader.GetInt64(9) == 1, shortCode);
            if (categoryId is not null && row.CategoryId != categoryId.Value) continue;
            if (active is not null && row.IsActive != active.Value) continue;
            if (!string.IsNullOrWhiteSpace(search) && !row.Code.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) && !row.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(row);
        }
        return result;
    }

    public async Task<IReadOnlyList<CatalogueWorkbookProduct>> ReadCatalogueWorkbookSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var products = new List<SnapshotProduct>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT p.product_id, p.code, p.name, c.name, c.short_code, p.price_ttc_cents, p.vat_rate,
                       p.is_active, p.discount_eligible, p.options_enabled
                FROM products p JOIN categories c ON c.category_id = p.category_id
                ORDER BY p.code COLLATE NOCASE, p.product_id;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                products.Add(new(
                    ParseGuid(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    Money.FromCents(reader.GetInt64(5)),
                    ParseDecimal(reader.GetString(6)),
                    reader.GetInt64(7) == 1,
                    reader.GetInt64(8) == 1,
                    reader.GetInt64(9) == 1));
            }
        }

        var snapshotHook = SnapshotAfterProductsReadAsync;
        if (snapshotHook is not null)
            await snapshotHook(cancellationToken);

        var groups = new Dictionary<Guid, List<SnapshotGroup>>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT option_group_id, product_id, name, selection_mode, is_required,
                       min_selections, max_selections, display_order
                FROM option_groups
                ORDER BY product_id, display_order, option_group_id;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var productId = ParseGuid(reader.GetString(1));
                if (!groups.TryGetValue(productId, out var productGroups))
                {
                    productGroups = [];
                    groups.Add(productId, productGroups);
                }

                productGroups.Add(new(
                    ParseGuid(reader.GetString(0)),
                    productId,
                    reader.GetString(2),
                    ParseSelectionMode(reader.GetString(3)),
                    reader.GetInt64(4) == 1,
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.GetInt32(7),
                    []));
            }
        }

        var options = new Dictionary<Guid, List<SnapshotOption>>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT option_id, option_group_id, name, price_adjustment_ttc_cents, is_active, display_order
                FROM options
                ORDER BY option_group_id, display_order, option_id;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var groupId = ParseGuid(reader.GetString(1));
                if (!options.TryGetValue(groupId, out var groupOptions))
                {
                    groupOptions = [];
                    options.Add(groupId, groupOptions);
                }

                groupOptions.Add(new(
                    ParseGuid(reader.GetString(0)),
                    groupId,
                    reader.GetString(2),
                    Money.FromCents(reader.GetInt64(3)),
                    reader.GetInt64(4) == 1,
                    reader.GetInt32(5)));
            }
        }

        foreach (var group in groups.Values.SelectMany(value => value))
            group.Options.AddRange(options.TryGetValue(group.Id, out var values) ? values : []);

        await transaction.CommitAsync(cancellationToken);

        return products.Select(product => new CatalogueWorkbookProduct(
            product.Id,
            product.Code,
            product.Name,
            product.CategoryName,
            product.CategoryShortCode,
            product.PriceTtc,
            product.VatRate,
            product.IsActive,
            product.DiscountEligible,
            product.OptionsEnabled,
            groups.TryGetValue(product.Id, out var productGroups)
                ? productGroups
                    .OrderBy(group => group.DisplayOrder)
                    .ThenBy(group => group.Id)
                    .Select(group => new CatalogueWorkbookOptionGroup(
                        group.Id,
                        product.Id,
                        product.Code,
                        product.Name,
                        group.Name,
                        group.SelectionMode,
                        group.IsRequired,
                        group.MinSelections,
                        group.MaxSelections,
                        group.DisplayOrder,
                        group.Options
                            .OrderBy(option => option.DisplayOrder)
                            .ThenBy(option => option.Id)
                            .Select(option => new CatalogueWorkbookOption(
                                option.Id,
                                group.Id,
                                product.Code,
                                product.Name,
                                group.Name,
                                option.Name,
                                option.PriceAdjustmentTtc,
                                option.IsActive,
                                option.DisplayOrder))
                            .ToArray()))
                    .ToArray()
                : [])).ToArray();
    }

    public async Task<CatalogueImportBaseline> ReadCatalogueImportBaselineAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var categories = new List<CatalogueImportCategory>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT category_id, name, short_code FROM categories ORDER BY normalized_name, category_id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                categories.Add(new(ParseGuid(reader.GetString(0)), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        var products = new List<BaselineProductRow>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT p.product_id, p.code, p.name, p.category_id, c.name, c.short_code, p.price_ttc_cents, p.vat_rate,
                       p.is_active, p.discount_eligible, p.options_enabled
                FROM products p JOIN categories c ON c.category_id = p.category_id
                ORDER BY p.normalized_code, p.product_id;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                products.Add(new(ParseGuid(reader.GetString(0)), reader.GetString(1), reader.GetString(2), ParseGuid(reader.GetString(3)), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), Money.FromCents(reader.GetInt64(6)), ParseDecimal(reader.GetString(7)), reader.GetInt64(8) == 1, reader.GetInt64(9) == 1, reader.GetInt64(10) == 1));
        }

        var afterProducts = ImportBaselineAfterProductsReadAsync;
        if (afterProducts is not null) await afterProducts(cancellationToken);

        var groups = new Dictionary<Guid, List<BaselineGroupRow>>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT option_group_id, product_id, name, selection_mode, is_required, min_selections, max_selections, display_order FROM option_groups ORDER BY product_id, display_order, option_group_id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var productId = ParseGuid(reader.GetString(1));
                if (!groups.TryGetValue(productId, out var values)) { values = []; groups.Add(productId, values); }
                values.Add(new(ParseGuid(reader.GetString(0)), productId, reader.GetString(2), ParseSelectionMode(reader.GetString(3)), reader.GetInt64(4) == 1, reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.GetInt32(7), []));
            }
        }

        var options = new Dictionary<Guid, List<CatalogueImportBaselineOption>>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT option_id, option_group_id, name, price_adjustment_ttc_cents, is_active, display_order FROM options ORDER BY option_group_id, display_order, option_id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var groupId = ParseGuid(reader.GetString(1));
                if (!options.TryGetValue(groupId, out var values)) { values = []; options.Add(groupId, values); }
                values.Add(new(ParseGuid(reader.GetString(0)), groupId, reader.GetString(2), Money.FromCents(reader.GetInt64(3)), reader.GetInt64(4) == 1, reader.GetInt32(5)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        var result = products.Select(product => new CatalogueImportBaselineProduct(
            product.Id, product.Code, product.Name, product.CategoryId, product.CategoryName, product.CategoryShortCode, product.PriceTtc, product.VatRate, product.IsActive, product.DiscountEligible, product.OptionsEnabled,
            groups.TryGetValue(product.Id, out var productGroups)
                ? productGroups.OrderBy(group => group.DisplayOrder).ThenBy(group => group.Id).Select(group => new CatalogueImportBaselineOptionGroup(group.Id, product.Id, group.Name, group.SelectionMode, group.IsRequired, group.MinSelections, group.MaxSelections, group.DisplayOrder,
                    options.TryGetValue(group.Id, out var groupOptions) ? groupOptions.OrderBy(option => option.DisplayOrder).ThenBy(option => option.Id).ToArray() : [])).ToArray()
                : [])).ToArray();
        return new CatalogueImportBaseline(categories, result);
    }

    /// <summary>
    /// Commits one complete import plan through exactly one transaction. The
    /// live baseline is read on that transaction before any allocation or write,
    /// then the plan is revalidated and applied with explicit INSERT/UPDATE SQL.
    /// Omitted rows are intentionally never part of the write set.
    /// </summary>
    public async Task<CatalogueImportCommitResult> CommitAsync(CatalogueImportCommitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Plan is null || request.PreviewBaseline is null)
            return CatalogueImportCommitResult.Failure(new CatalogueImportIssue(CatalogueImportIssueSeverity.Error, "invalid-request", "The import commit request is incomplete."));
        var previewFingerprint = CatalogueImportBaselineFingerprint.Compute(request.PreviewBaseline);
        if (request.ExpectedBaselineFingerprint is not null && !string.Equals(previewFingerprint, request.ExpectedBaselineFingerprint, StringComparison.Ordinal))
            return CatalogueImportCommitResult.Failure(new CatalogueImportIssue(CatalogueImportIssueSeverity.Error, "baseline-token-mismatch", "The supplied baseline token does not describe the preview baseline."));
        try
        {
            var committed = await transactionRunner.ExecuteAsync(async (transaction, token) =>
            {
                var sqlite = RequireSqlite(transaction);
                var current = await ReadCatalogueImportBaselineAsync(sqlite, token);
                var commitBaselineHook = ImportCommitAfterBaselineReadAsync;
                if (commitBaselineHook is not null) await commitBaselineHook(token);
                var currentFingerprint = CatalogueImportBaselineFingerprint.Compute(current);
                if (!string.Equals(currentFingerprint, request.BaselineFingerprint, StringComparison.Ordinal))
                    throw new ImportCommitException(new CatalogueImportIssue(CatalogueImportIssueSeverity.Error, "stale-baseline", "The current Catalogue changed after preview; refresh the preview before committing."));

                var validation = ValidateImportPlanAgainstBaseline(request.Plan, current);
                if (validation.Count > 0) throw new ImportCommitException(validation);

                var allocated = AllocateImportIds(request.Plan, current);
                var writeCount = await ApplyImportPlanAsync(sqlite, request.Plan, current, allocated, token);
                return new ImportCommitTransactionResult(writeCount > 0, allocated);
            }, cancellationToken);

            return new(true, committed.Changed, [], committed.AllocatedIds);
        }
        catch (ImportCommitException exception)
        {
            return new(false, false, exception.Issues);
        }
        catch (SqliteException exception) when (IsConcurrencyConflict(exception))
        {
            return new(false, false, [new(CatalogueImportIssueSeverity.Error, "concurrent-write-conflict", "The Catalogue changed during import; refresh the preview and retry.")]);
        }
        catch (SqliteException exception) when (IsConstraint(exception))
        {
            return new(false, false, [new(CatalogueImportIssueSeverity.Error, "persistence-conflict", "The Catalogue import conflicted with the current SQLite data and was rolled back.")]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(false, false, [new(CatalogueImportIssueSeverity.Error, "transaction-failed", $"The Catalogue import was rolled back: {exception.Message}")]);
        }
    }

    public async Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty) return null;
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        await using var productCommand = connection.CreateCommand();
        productCommand.CommandText = "SELECT product_id, code, name, category_id, price_ttc_cents, vat_rate, is_active, discount_eligible, options_enabled FROM products WHERE product_id = $id;";
        productCommand.Parameters.AddWithValue("$id", productId.ToString());
        await using var productReader = await productCommand.ExecuteReaderAsync(cancellationToken);
        if (!await productReader.ReadAsync(cancellationToken)) return null;
        var draft = new ProductDraft(ParseGuid(productReader.GetString(0)), productReader.GetString(1), productReader.GetString(2), ParseGuid(productReader.GetString(3)), Money.FromCents(productReader.GetInt64(4)), ParseDecimal(productReader.GetString(5)), productReader.GetInt64(6) == 1, productReader.GetInt64(7) == 1, productReader.GetInt64(8) == 1, []);
        await productReader.CloseAsync();

        await using var groupCommand = connection.CreateCommand();
        groupCommand.CommandText = "SELECT option_group_id, name, selection_mode, is_required, min_selections, max_selections, display_order FROM option_groups WHERE product_id = $product ORDER BY display_order, option_group_id;";
        groupCommand.Parameters.AddWithValue("$product", productId.ToString());
        var groupRows = new List<(Guid Id, string Name, SelectionMode Mode, bool Required, int? Min, int? Max, int Order)>();
        await using var groupReader = await groupCommand.ExecuteReaderAsync(cancellationToken);
        while (await groupReader.ReadAsync(cancellationToken))
        {
            var groupId = ParseGuid(groupReader.GetString(0));
            groupRows.Add((groupId, groupReader.GetString(1), ParseSelectionMode(groupReader.GetString(2)), groupReader.GetInt64(3) == 1, groupReader.IsDBNull(4) ? null : groupReader.GetInt32(4), groupReader.IsDBNull(5) ? null : groupReader.GetInt32(5), groupReader.GetInt32(6)));
        }
        var groups = new List<OptionGroupDraft>();
        foreach (var row in groupRows) groups.Add(new(row.Id, row.Name, row.Mode, row.Required, row.Min, row.Max, row.Order, await ReadOptionsAsync(connection, row.Id, cancellationToken)));
        return draft with { Groups = groups };
    }

    public async Task<ProductDraft?> GetActiveProductByCodeAsync(string? productCode, CancellationToken cancellationToken = default)
    {
        var normalizedCode = CatalogueNormalization.Key(productCode);
        if (normalizedCode.Length == 0) return null;
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT product_id FROM products WHERE normalized_code=$code AND is_active=1;";
        command.Parameters.AddWithValue("$code", normalizedCode);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull || !Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var productId)) return null;
        return await GetProductForEditAsync(productId, cancellationToken);
    }

    public async Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default)
    {
        var display = CatalogueNormalization.Display(name);
        if (display.Length == 0) return OperationResult<CategorySummary>.Failure(new ValidationIssue("name", "Category name is required.", ValidationCodes.Required));
        var id = idGenerator.NewId();
        var now = clock.UtcNow;
        try
        {
            await transactionRunner.ExecuteAsync(async (transaction, token) =>
            {
                await ExecuteAsync(RequireSqlite(transaction), "INSERT INTO categories(category_id,name,normalized_name,created_at_utc,updated_at_utc) VALUES ($id,$name,$normalized,$created,$updated);", token,
                    ("$id", id.ToString()), ("$name", display), ("$normalized", CatalogueNormalization.Key(display)), ("$created", Format(now)), ("$updated", Format(now)));
            }, cancellationToken);
            return OperationResult<CategorySummary>.Success(new(id, display));
        }
        catch (SqliteException exception) when (IsConstraint(exception))
        {
            return OperationResult<CategorySummary>.Failure(MapCategoryConstraint(exception));
        }
    }

    public async Task<OperationResult<CategorySummary>> CreateCategoryWithCodeAsync(string name, string? shortCode, CancellationToken cancellationToken = default)
    {
        var display = CatalogueNormalization.Display(name);
        if (display.Length == 0) return OperationResult<CategorySummary>.Failure(new ValidationIssue("name", "Category name is required.", ValidationCodes.Required));
        var code = CatalogueNormalization.Display(shortCode);
        if (CatalogueValidation.ValidateCategoryShortCode(code) is { } shortCodeError)
            return OperationResult<CategorySummary>.Failure(new ValidationIssue("shortCode", shortCodeError, ValidationCodes.Infer(shortCodeError)));
        var id = idGenerator.NewId();
        var now = clock.UtcNow;
        try
        {
            await transactionRunner.ExecuteAsync(async (transaction, token) =>
            {
                var sqlite = RequireSqlite(transaction);
                await ExecuteAsync(sqlite, "INSERT INTO categories(category_id,name,normalized_name,short_code,normalized_short_code,created_at_utc,updated_at_utc) VALUES ($id,$name,$normalized,$shortCode,$normalizedShortCode,$created,$updated);", token,
                    ("$id", id.ToString()), ("$name", display), ("$normalized", CatalogueNormalization.Key(display)),
                    ("$shortCode", code.Length == 0 ? null : code), ("$normalizedShortCode", code.Length == 0 ? null : CatalogueNormalization.Key(code)),
                    ("$created", Format(now)), ("$updated", Format(now)));
            }, cancellationToken);
            return OperationResult<CategorySummary>.Success(new(id, display, code.Length == 0 ? null : code));
        }
        catch (SqliteException exception) when (IsConstraint(exception))
        {
            return OperationResult<CategorySummary>.Failure(MapCategoryConstraint(exception));
        }
    }

    public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid categoryId, string name, CancellationToken cancellationToken = default) =>
        RenameCategoryCoreAsync(categoryId, name, null, preserveShortCode: true, cancellationToken);

    public Task<OperationResult<CategorySummary>> RenameCategoryWithCodeAsync(Guid categoryId, string name, string? shortCode, CancellationToken cancellationToken = default) =>
        RenameCategoryCoreAsync(categoryId, name, shortCode, preserveShortCode: false, cancellationToken);

    private async Task<OperationResult<CategorySummary>> RenameCategoryCoreAsync(Guid categoryId, string name, string? shortCode, bool preserveShortCode, CancellationToken cancellationToken)
    {
        if (categoryId == Guid.Empty) return OperationResult<CategorySummary>.Failure(new ValidationIssue("category", "The category no longer exists.", ValidationCodes.CategoryMissing));
        var display = CatalogueNormalization.Display(name);
        if (display.Length == 0) return OperationResult<CategorySummary>.Failure(new ValidationIssue("name", "Category name is required.", ValidationCodes.Required));
        var code = CatalogueNormalization.Display(shortCode);
        if (!preserveShortCode && CatalogueValidation.ValidateCategoryShortCode(code) is { } shortCodeError)
            return OperationResult<CategorySummary>.Failure(new ValidationIssue("shortCode", shortCodeError, ValidationCodes.Infer(shortCodeError)));
        var now = clock.UtcNow;
        try
        {
            var updated = await transactionRunner.ExecuteAsync(async (transaction, token) =>
            {
                var sqlite = RequireSqlite(transaction);
                var sql = preserveShortCode
                    ? "UPDATE categories SET name=$name, normalized_name=$normalized, updated_at_utc=$updated WHERE category_id=$id;"
                    : "UPDATE categories SET name=$name, normalized_name=$normalized, short_code=$shortCode, normalized_short_code=$normalizedShortCode, updated_at_utc=$updated WHERE category_id=$id;";
                return await ExecuteAsync(sqlite, sql, token,
                    ("$name", display), ("$normalized", CatalogueNormalization.Key(display)),
                    ("$shortCode", code.Length == 0 ? null : code), ("$normalizedShortCode", code.Length == 0 ? null : CatalogueNormalization.Key(code)),
                    ("$updated", Format(now)), ("$id", categoryId.ToString()));
            }, cancellationToken);
            if (updated == 0) return OperationResult<CategorySummary>.Failure(new ValidationIssue("category", "The category no longer exists.", ValidationCodes.CategoryMissing));
            var effectiveCode = preserveShortCode ? await ReadCategoryCodeAsync(categoryId, cancellationToken) : (code.Length == 0 ? null : code);
            return OperationResult<CategorySummary>.Success(new CategorySummary(categoryId, display, effectiveCode));
        }
        catch (SqliteException exception) when (IsConstraint(exception))
        {
            return OperationResult<CategorySummary>.Failure(MapCategoryConstraint(exception));
        }
    }

    public async Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default)
    {
        // A create operation always allocates a fresh opaque identity at the persistence boundary.
        var productId = idGenerator.NewId();
        draft = draft with
        {
            Id = productId,
            Groups = (draft.Groups ?? []).Select(group => group with
            {
                Id = Guid.Empty,
                Options = (group.Options ?? []).Select(option => option with { Id = Guid.Empty }).ToArray()
            }).ToArray()
        };
        var now = clock.UtcNow;
        try
        {
            await transactionRunner.ExecuteAsync(async (transaction, token) =>
            {
                var sqlite = RequireSqlite(transaction);
                if (!await ExistsAsync(sqlite, "SELECT 1 FROM categories WHERE category_id=$id;", token, ("$id", draft.CategoryId.ToString()))) throw new CatalogueConflictException("The selected category no longer exists.");
                await InsertProductAsync(sqlite, productId, draft, now, token);
            }, cancellationToken);
            return OperationResult<Guid>.Success(productId);
        }
        catch (CatalogueConflictException exception) { return OperationResult<Guid>.Failure(new ValidationIssue("product", exception.Message, ValidationCodes.CategoryMissing)); }
        catch (SqliteException exception) when (IsConstraint(exception)) { return OperationResult<Guid>.Failure(MapConstraint(exception)); }
    }

    public async Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty) return OperationResult.Failure(new ValidationIssue("product", "The product no longer exists."));
        var now = clock.UtcNow;
        try
        {
            await transactionRunner.ExecuteAsync(async (transaction, token) =>
            {
                var sqlite = RequireSqlite(transaction);
                if (!await ExistsAsync(sqlite, "SELECT 1 FROM products WHERE product_id=$id;", token, ("$id", productId.ToString()))) throw new CatalogueConflictException("The product no longer exists.");
                if (!await ExistsAsync(sqlite, "SELECT 1 FROM categories WHERE category_id=$id;", token, ("$id", draft.CategoryId.ToString()))) throw new CatalogueConflictException("The selected category no longer exists.");
                await UpdateProductRowAsync(sqlite, productId, draft, now, token);
                await SaveGroupsAsync(sqlite, productId, draft.Groups ?? [], now, token);
            }, cancellationToken);
            return OperationResult.Success();
        }
        catch (CatalogueConflictException exception) { return OperationResult.Failure(new ValidationIssue("product", exception.Message, ValidationCodes.Conflict)); }
        catch (SqliteException exception) when (IsConstraint(exception)) { return OperationResult.Failure(MapConstraint(exception)); }
    }

    public async Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty) return OperationResult.Failure(new ValidationIssue("product", "The product no longer exists.", ValidationCodes.ProductMissing));
        try
        {
            var count = await transactionRunner.ExecuteAsync(async (transaction, token) => await ExecuteAsync(RequireSqlite(transaction), "UPDATE products SET is_active=$active, updated_at_utc=$updated WHERE product_id=$id;", token,
                ("$active", isActive ? 1 : 0), ("$updated", Format(clock.UtcNow)), ("$id", productId.ToString())) , cancellationToken);
            return count == 0 ? OperationResult.Failure(new ValidationIssue("product", "The product no longer exists.", ValidationCodes.ProductMissing)) : OperationResult.Success();
        }
        catch (SqliteException exception) when (IsConstraint(exception)) { return OperationResult.Failure(new ValidationIssue("product", "The product could not be updated.", ValidationCodes.Conflict)); }
    }

    public async Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.Items is null || request.Items.Count == 0)
            return OperationResult<BulkProductActiveStateResult>.Failure(new ValidationIssue("products", "The bulk catalogue request is empty.", ValidationCodes.BulkRequestInvalid));
        if (request.Items.Any(item => item.ProductId == Guid.Empty) || request.Items.Select(item => item.ProductId).Distinct().Count() != request.Items.Count)
            return OperationResult<BulkProductActiveStateResult>.Failure(new ValidationIssue("products", "The bulk catalogue request contains invalid or duplicate products.", ValidationCodes.BulkRequestInvalid));

        try
        {
            var result = await transactionRunner.ExecuteAsync(async (transaction, token) =>
            {
                var sqlite = RequireSqlite(transaction);
                // Revalidate the entire immutable capture before attempting the first write.
                foreach (var item in request.Items)
                {
                    var state = await ReadActiveStateAsync(sqlite, item.ProductId, token);
                    if (state is null || state.Value != item.ExpectedIsActive)
                        throw new CatalogueConflictException("The selected catalogue no longer matches the captured state.");
                }

                var effective = request.Items.Where(item => item.ExpectedIsActive != request.TargetIsActive).ToArray();
                if (effective.Length == 0) return new BulkProductActiveStateResult(request.Items.Count, 0);

                var operationTime = Format(clock.UtcNow);
                var attempt = 0;
                foreach (var item in effective)
                {
                    var injectedFailure = bulkWriteFailureInjector?.Invoke(++attempt);
                    if (injectedFailure is not null) throw injectedFailure;
                    var changed = await ExecuteAsync(sqlite,
                        "UPDATE products SET is_active=$active, updated_at_utc=$updated WHERE product_id=$id AND is_active=$expected;",
                        token,
                        ("$active", request.TargetIsActive ? 1 : 0),
                        ("$updated", operationTime),
                        ("$id", item.ProductId.ToString()),
                        ("$expected", item.ExpectedIsActive ? 1 : 0));
                    if (changed != 1) throw new CatalogueConflictException("The selected catalogue no longer matches the captured state.");
                }

                return new BulkProductActiveStateResult(request.Items.Count, effective.Length);
            }, cancellationToken);
            return OperationResult<BulkProductActiveStateResult>.Success(result);
        }
        catch (CatalogueConflictException exception)
        {
            return OperationResult<BulkProductActiveStateResult>.Failure(new ValidationIssue("products", exception.Message, ValidationCodes.Conflict));
        }
        catch (SqliteException exception) when (IsConstraint(exception))
        {
            return OperationResult<BulkProductActiveStateResult>.Failure(new ValidationIssue("products", "The catalogue change conflicts with existing data.", ValidationCodes.Conflict));
        }
    }

    public async Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty) return OperationResult.Failure(new ValidationIssue("product", "The product no longer exists.", ValidationCodes.ProductMissing));
        try
        {
            var count = await transactionRunner.ExecuteAsync(async (transaction, token) => await ExecuteAsync(RequireSqlite(transaction), "DELETE FROM products WHERE product_id=$id;", token, ("$id", productId.ToString())), cancellationToken);
            return count == 0 ? OperationResult.Failure(new ValidationIssue("product", "The product no longer exists.", ValidationCodes.ProductMissing)) : OperationResult.Success();
        }
        catch (SqliteException exception) when (IsConstraint(exception)) { return OperationResult.Failure(new ValidationIssue("product", "The product could not be deleted.", ValidationCodes.Conflict)); }
    }

    private async Task InsertProductAsync(SqliteApplicationTransaction sqlite, Guid id, ProductDraft draft, DateTimeOffset now, CancellationToken token)
    {
        await ExecuteAsync(sqlite, "INSERT INTO products(product_id,code,normalized_code,name,category_id,price_ttc_cents,vat_rate,is_active,discount_eligible,options_enabled,created_at_utc,updated_at_utc) VALUES ($id,$code,$normalized,$name,$category,$price,$vat,$active,$discount,$options,$created,$updated);", token,
            ("$id", id.ToString()), ("$code", CatalogueNormalization.Display(draft.Code)), ("$normalized", CatalogueNormalization.Key(draft.Code)), ("$name", CatalogueNormalization.Display(draft.Name)), ("$category", draft.CategoryId.ToString()), ("$price", draft.PriceTtc.Cents), ("$vat", FormatDecimal(draft.VatRate)), ("$active", draft.IsActive ? 1 : 0), ("$discount", draft.DiscountEligible ? 1 : 0), ("$options", draft.OptionsEnabled ? 1 : 0), ("$created", Format(now)), ("$updated", Format(now)));
        await SaveGroupsAsync(sqlite, id, draft.Groups ?? [], now, token);
    }

    private static async Task UpdateProductRowAsync(SqliteApplicationTransaction sqlite, Guid id, ProductDraft draft, DateTimeOffset now, CancellationToken token) =>
        await ExecuteAsync(sqlite, "UPDATE products SET code=$code,normalized_code=$normalized,name=$name,category_id=$category,price_ttc_cents=$price,vat_rate=$vat,is_active=$active,discount_eligible=$discount,options_enabled=$options,updated_at_utc=$updated WHERE product_id=$id;", token,
            ("$id", id.ToString()), ("$code", CatalogueNormalization.Display(draft.Code)), ("$normalized", CatalogueNormalization.Key(draft.Code)), ("$name", CatalogueNormalization.Display(draft.Name)), ("$category", draft.CategoryId.ToString()), ("$price", draft.PriceTtc.Cents), ("$vat", FormatDecimal(draft.VatRate)), ("$active", draft.IsActive ? 1 : 0), ("$discount", draft.DiscountEligible ? 1 : 0), ("$options", draft.OptionsEnabled ? 1 : 0), ("$updated", Format(now)));

    private async Task SaveGroupsAsync(SqliteApplicationTransaction sqlite, Guid productId, IReadOnlyList<OptionGroupDraft> drafts, DateTimeOffset now, CancellationToken token)
    {
        var existingGroups = await ReadIdsAsync(sqlite, "SELECT option_group_id FROM option_groups WHERE product_id=$id;", token, ("$id", productId.ToString()));
        var incoming = new HashSet<Guid>();
        await ExecuteAsync(sqlite, "UPDATE option_groups SET display_order=display_order+1000000 WHERE product_id=$id;", token, ("$id", productId.ToString()));
        foreach (var (draft, index) in (drafts ?? []).OrderBy(x => x.DisplayOrder).Select((value, index) => (value, index)))
        {
            var id = draft.Id == Guid.Empty ? idGenerator.NewId() : draft.Id;
            if (draft.Id != Guid.Empty && !await ExistsAsync(sqlite, "SELECT 1 FROM option_groups WHERE option_group_id=$id AND product_id=$product;", token, ("$id", id.ToString()), ("$product", productId.ToString())))
                throw new CatalogueConflictException("An option group no longer belongs to this product.");
            incoming.Add(id);
            var created = await ReadCreatedAtAsync(sqlite, "SELECT created_at_utc FROM option_groups WHERE option_group_id=$id AND product_id=$product;", token, ("$id", id.ToString()), ("$product", productId.ToString())) ?? Format(now);
            var mode = draft.SelectionMode == SelectionMode.Single ? "SINGLE" : "MULTI";
            await ExecuteAsync(sqlite, "INSERT INTO option_groups(option_group_id,product_id,name,selection_mode,is_required,min_selections,max_selections,display_order,created_at_utc,updated_at_utc) VALUES ($id,$product,$name,$mode,$required,$min,$max,$order,$created,$updated) ON CONFLICT(option_group_id) DO UPDATE SET name=excluded.name,selection_mode=excluded.selection_mode,is_required=excluded.is_required,min_selections=excluded.min_selections,max_selections=excluded.max_selections,display_order=excluded.display_order,updated_at_utc=excluded.updated_at_utc;", token,
                ("$id", id.ToString()), ("$product", productId.ToString()), ("$name", CatalogueNormalization.Display(draft.Name)), ("$mode", mode), ("$required", draft.IsRequired ? 1 : 0), ("$min", draft.SelectionMode == SelectionMode.Multi ? draft.MinSelections : null), ("$max", draft.SelectionMode == SelectionMode.Multi ? draft.MaxSelections : null), ("$order", index), ("$created", created), ("$updated", Format(now)));
            await SaveOptionsAsync(sqlite, id, draft.Options ?? [], now, token);
        }

        foreach (var old in existingGroups.Where(x => !incoming.Contains(x)))
            await ExecuteAsync(sqlite, "DELETE FROM option_groups WHERE option_group_id=$id;", token, ("$id", old.ToString()));
    }

    private async Task SaveOptionsAsync(SqliteApplicationTransaction sqlite, Guid groupId, IReadOnlyList<OptionDraft> drafts, DateTimeOffset now, CancellationToken token)
    {
        var existing = await ReadIdsAsync(sqlite, "SELECT option_id FROM options WHERE option_group_id=$id;", token, ("$id", groupId.ToString()));
        var incoming = new HashSet<Guid>();
        await ExecuteAsync(sqlite, "UPDATE options SET display_order=display_order+1000000 WHERE option_group_id=$id;", token, ("$id", groupId.ToString()));
        foreach (var (draft, index) in (drafts ?? []).OrderBy(x => x.DisplayOrder).Select((value, index) => (value, index)))
        {
            var id = draft.Id == Guid.Empty ? idGenerator.NewId() : draft.Id;
            if (draft.Id != Guid.Empty && !await ExistsAsync(sqlite, "SELECT 1 FROM options WHERE option_id=$id AND option_group_id=$group;", token, ("$id", id.ToString()), ("$group", groupId.ToString())))
                throw new CatalogueConflictException("An option no longer belongs to this option group.");
            incoming.Add(id);
            var created = await ReadCreatedAtAsync(sqlite, "SELECT created_at_utc FROM options WHERE option_id=$id AND option_group_id=$group;", token, ("$id", id.ToString()), ("$group", groupId.ToString())) ?? Format(now);
            await ExecuteAsync(sqlite, "INSERT INTO options(option_id,option_group_id,name,price_adjustment_ttc_cents,is_active,display_order,created_at_utc,updated_at_utc) VALUES ($id,$group,$name,$price,$active,$order,$created,$updated) ON CONFLICT(option_id) DO UPDATE SET name=excluded.name,price_adjustment_ttc_cents=excluded.price_adjustment_ttc_cents,is_active=excluded.is_active,display_order=excluded.display_order,updated_at_utc=excluded.updated_at_utc;", token,
                ("$id", id.ToString()), ("$group", groupId.ToString()), ("$name", CatalogueNormalization.Display(draft.Name)), ("$price", draft.PriceAdjustmentTtc.Cents), ("$active", draft.IsActive ? 1 : 0), ("$order", index), ("$created", created), ("$updated", Format(now)));
        }
        foreach (var old in existing.Where(x => !incoming.Contains(x))) await ExecuteAsync(sqlite, "DELETE FROM options WHERE option_id=$id;", token, ("$id", old.ToString()));
    }

    private static List<CatalogueImportIssue> ValidateImportPlanAgainstBaseline(CatalogueImportPlan plan, CatalogueImportBaseline current)
        => CatalogueImportCommitValidator.Validate(plan, current).ToList();

    private Dictionary<string, Guid> AllocateImportIds(CatalogueImportPlan plan, CatalogueImportBaseline current)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var occupied = new HashSet<Guid>((current.Categories ?? []).Select(value => value.Id).Concat((current.Products ?? []).Select(value => value.Id)).Concat((current.Products ?? []).SelectMany(value => value.OptionGroups).Select(value => value.Id)).Concat((current.Products ?? []).SelectMany(value => value.OptionGroups).SelectMany(value => value.Options).Select(value => value.Id)));
        foreach (var key in (plan.NewCategories ?? []).Select(value => "category:" + value.LocalKey).Concat(plan.Operations.Where(value => value.Kind == CatalogueImportOperationKind.Create).Select(value => value.EntityType + ":" + value.LocalKey)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
        {
            var id = idGenerator.NewId();
            if (id == Guid.Empty || !occupied.Add(id)) throw new ImportCommitException(new CatalogueImportIssue(CatalogueImportIssueSeverity.Error, "invalid-allocated-id", "The durable id generator returned an empty or colliding id."));
            result[key] = id;
        }
        return result;
    }

    private async Task<int> ApplyImportPlanAsync(SqliteApplicationTransaction sqlite, CatalogueImportPlan plan, CatalogueImportBaseline current, Dictionary<string, Guid> allocated, CancellationToken token)
    {
        var now = Format(clock.UtcNow);
        var changed = 0;
        foreach (var category in plan.NewCategories ?? [])
        {
            var id = allocated["category:" + category.LocalKey];
            changed += await ExecuteAsync(sqlite, "INSERT INTO categories(category_id,name,normalized_name,short_code,normalized_short_code,created_at_utc,updated_at_utc) VALUES ($id,$name,$normalized,$shortCode,$normalizedShortCode,$created,$updated);", token,
                ("$id", id.ToString()), ("$name", CatalogueNormalization.Display(category.Name)), ("$normalized", category.NormalizedName),
                ("$shortCode", string.IsNullOrWhiteSpace(category.ShortCode) ? null : CatalogueNormalization.Display(category.ShortCode)), ("$normalizedShortCode", category.NormalizedShortCode), ("$created", now), ("$updated", now));
        }

        var productOps = plan.Operations.Where(value => value.EntityType == CatalogueImportEntityType.Product).GroupBy(value => value.LocalKey, StringComparer.Ordinal).Select(value => value.ToArray()).ToArray();
        var currentProducts = (current.Products ?? []).ToDictionary(value => value.Id);
        var occupiedProductCodes = (current.Products ?? []).Select(value => value.NormalizedCode).ToHashSet(StringComparer.Ordinal);
        var finalProductCodes = productOps.Select(value => CatalogueNormalization.Key(Required(value[0], "code"))).ToHashSet(StringComparer.Ordinal);
        var tempIndex = 0;
        foreach (var operations in productOps.Where(value => value[0].EntityId is not null))
        {
            var id = operations[0].EntityId!.Value;
            if (!currentProducts.TryGetValue(id, out var existing) || string.Equals(existing.NormalizedCode, CatalogueNormalization.Key(Required(operations[0], "code")), StringComparison.Ordinal)) continue;
            var temp = "__sushi81_import_tmp_" + id.ToString("N") + "_" + tempIndex.ToString(CultureInfo.InvariantCulture);
            while (occupiedProductCodes.Contains(CatalogueNormalization.Key(temp)) || finalProductCodes.Contains(CatalogueNormalization.Key(temp)))
                temp = "__sushi81_import_tmp_" + id.ToString("N") + "_" + (++tempIndex).ToString(CultureInfo.InvariantCulture);
            tempIndex++;
            changed += await ExecuteAsync(sqlite, "UPDATE products SET code=$code,normalized_code=$normalized WHERE product_id=$id;", token,
                ("$id", id.ToString()), ("$code", temp), ("$normalized", CatalogueNormalization.Key(temp)));
        }
        foreach (var operations in productOps)
        {
            var first = operations[0];
            var id = first.EntityId ?? allocated["Product:" + first.LocalKey];
            var categoryId = ResolveReference(first.CategoryReference!, allocated);
            var values = (first.Values ?? new Dictionary<string, string?>());
            var sql = first.EntityId is null
                ? "INSERT INTO products(product_id,code,normalized_code,name,category_id,price_ttc_cents,vat_rate,is_active,discount_eligible,options_enabled,created_at_utc,updated_at_utc) VALUES ($id,$code,$normalized,$name,$category,$price,$vat,$active,$discount,$options,$created,$updated);"
                : "UPDATE products SET code=$code,normalized_code=$normalized,name=$name,category_id=$category,price_ttc_cents=$price,vat_rate=$vat,is_active=$active,discount_eligible=$discount,options_enabled=$options,updated_at_utc=$updated WHERE product_id=$id;";
            changed += await ExecuteAsync(sqlite, sql, token, ("$id", id.ToString()), ("$code", CatalogueNormalization.Display(Required(first, "code"))), ("$normalized", CatalogueNormalization.Key(Required(first, "code"))), ("$name", CatalogueNormalization.Display(Required(first, "name"))), ("$category", categoryId.ToString()), ("$price", ParseLong(first, "priceCents")), ("$vat", FormatDecimal(ParseDecimal(first, "vatRate"))), ("$active", ParseBool(first, "isActive") ? 1 : 0), ("$discount", ParseBool(first, "discountEligible") ? 1 : 0), ("$options", ParseBool(first, "optionsEnabled") ? 1 : 0), ("$created", now), ("$updated", now));
            if (ImportWriteFailureInjector?.Invoke(changed) is { } injected) throw injected;
            if (ImportWriteConstraintInjector is { } constraintInjector) await constraintInjector(sqlite, changed);
        }

        var groupOps = plan.Operations.Where(value => value.EntityType == CatalogueImportEntityType.OptionGroup).GroupBy(value => value.LocalKey, StringComparer.Ordinal).Select(value => value.ToArray()).ToArray();
        var currentGroups = (current.Products ?? []).SelectMany(value => value.OptionGroups).ToDictionary(value => value.Id);
        long groupStage = int.MaxValue + 1L;
        foreach (var operations in groupOps.Where(value => value[0].EntityId is not null))
        {
            var id = operations[0].EntityId!.Value;
            if (!currentGroups.TryGetValue(id, out var existing) || existing.DisplayOrder == ParseInt(operations[0], "displayOrder")) continue;
            changed += await ExecuteAsync(sqlite, "UPDATE option_groups SET display_order=$order WHERE option_group_id=$id;", token, ("$id", id.ToString()), ("$order", groupStage++));
        }
        foreach (var operations in groupOps)
        {
            var first = operations[0]; var id = first.EntityId ?? allocated["OptionGroup:" + first.LocalKey]; var parentId = ResolveReference(first.ParentReference!, allocated);
            var sql = first.EntityId is null
                ? "INSERT INTO option_groups(option_group_id,product_id,name,selection_mode,is_required,min_selections,max_selections,display_order,created_at_utc,updated_at_utc) VALUES ($id,$product,$name,$mode,$required,$min,$max,$order,$created,$updated);"
                : "UPDATE option_groups SET name=$name,selection_mode=$mode,is_required=$required,min_selections=$min,max_selections=$max,display_order=$order,updated_at_utc=$updated WHERE option_group_id=$id;";
            changed += await ExecuteAsync(sqlite, sql, token, ("$id", id.ToString()), ("$product", parentId.ToString()), ("$name", CatalogueNormalization.Display(Required(first, "name"))), ("$mode", Required(first, "selectionMode").ToUpperInvariant()), ("$required", ParseBool(first, "isRequired") ? 1 : 0), ("$min", ParseOptionalInt(first, "minSelections")), ("$max", ParseOptionalInt(first, "maxSelections")), ("$order", ParseInt(first, "displayOrder")), ("$created", now), ("$updated", now));
            if (ImportWriteFailureInjector?.Invoke(changed) is { } injected) throw injected;
            if (ImportWriteConstraintInjector is { } constraintInjector) await constraintInjector(sqlite, changed);
        }

        var optionOps = plan.Operations.Where(value => value.EntityType == CatalogueImportEntityType.Option).GroupBy(value => value.LocalKey, StringComparer.Ordinal).Select(value => value.ToArray()).ToArray();
        var currentOptions = (current.Products ?? []).SelectMany(value => value.OptionGroups).SelectMany(value => value.Options).ToDictionary(value => value.Id);
        long optionStage = int.MaxValue + 1L;
        foreach (var operations in optionOps.Where(value => value[0].EntityId is not null))
        {
            var id = operations[0].EntityId!.Value;
            if (!currentOptions.TryGetValue(id, out var existing) || existing.DisplayOrder == ParseInt(operations[0], "displayOrder")) continue;
            changed += await ExecuteAsync(sqlite, "UPDATE options SET display_order=$order WHERE option_id=$id;", token, ("$id", id.ToString()), ("$order", optionStage++));
        }
        foreach (var operations in optionOps)
        {
            var first = operations[0]; var id = first.EntityId ?? allocated["Option:" + first.LocalKey]; var parentId = ResolveReference(first.ParentReference!, allocated);
            var sql = first.EntityId is null
                ? "INSERT INTO options(option_id,option_group_id,name,price_adjustment_ttc_cents,is_active,display_order,created_at_utc,updated_at_utc) VALUES ($id,$group,$name,$price,$active,$order,$created,$updated);"
                : "UPDATE options SET name=$name,price_adjustment_ttc_cents=$price,is_active=$active,display_order=$order,updated_at_utc=$updated WHERE option_id=$id;";
            changed += await ExecuteAsync(sqlite, sql, token, ("$id", id.ToString()), ("$group", parentId.ToString()), ("$name", CatalogueNormalization.Display(Required(first, "name"))), ("$price", ParseLong(first, "priceAdjustmentCents")), ("$active", ParseBool(first, "isActive") ? 1 : 0), ("$order", ParseInt(first, "displayOrder")), ("$created", now), ("$updated", now));
            if (ImportWriteFailureInjector?.Invoke(changed) is { } injected) throw injected;
            if (ImportWriteConstraintInjector is { } constraintInjector) await constraintInjector(sqlite, changed);
        }
        return changed;
    }

    private static Guid ResolveReference(CatalogueImportEntityReference reference, Dictionary<string, Guid> allocated)
    {
        if (reference.IsExisting) return reference.ExistingId!.Value;
        var separator = reference.LocalKey.IndexOf(':');
        var typePrefix = separator > 0 ? reference.LocalKey[..separator] : string.Empty;
        var allocationPrefix = typePrefix switch
        {
            "category" => "category:",
            "product" => "Product:",
            "group" => "OptionGroup:",
            "option" => "Option:",
            _ => null
        };
        if (allocationPrefix is not null && allocated.TryGetValue(allocationPrefix + reference.LocalKey, out var id)) return id;
        throw new ImportCommitException(new CatalogueImportIssue(CatalogueImportIssueSeverity.Error, "unknown-reference", "An import reference could not be resolved."));
    }

    private static string Required(CatalogueImportOperation operation, string key) => operation.Values is not null && operation.Values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value! : throw new FormatException(key);
    private static bool ParseBool(CatalogueImportOperation operation, string key) => bool.TryParse(Required(operation, key), out var value) ? value : throw new FormatException(key);
    private static long ParseLong(CatalogueImportOperation operation, string key) => long.TryParse(Required(operation, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : throw new FormatException(key);
    private static int ParseInt(CatalogueImportOperation operation, string key) => int.TryParse(Required(operation, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : throw new FormatException(key);
    private static int? ParseOptionalInt(CatalogueImportOperation operation, string key) => operation.Values is not null && operation.Values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : throw new FormatException(key)) : null;
    private static decimal ParseDecimal(CatalogueImportOperation operation, string key) => decimal.TryParse(Required(operation, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : throw new FormatException(key);

    private static async Task<CatalogueImportBaseline> ReadCatalogueImportBaselineAsync(SqliteApplicationTransaction sqlite, CancellationToken cancellationToken)
    {
        var categories = new List<CatalogueImportCategory>();
        await using (var command = sqlite.Connection.CreateCommand())
        {
            command.Transaction = sqlite.Transaction; command.CommandText = "SELECT category_id,name,short_code FROM categories ORDER BY normalized_name,category_id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) categories.Add(new(ParseGuid(reader.GetString(0)), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        var products = new List<BaselineProductRow>();
        await using (var command = sqlite.Connection.CreateCommand())
        {
            command.Transaction = sqlite.Transaction; command.CommandText = "SELECT p.product_id,p.code,p.name,p.category_id,c.name,c.short_code,p.price_ttc_cents,p.vat_rate,p.is_active,p.discount_eligible,p.options_enabled FROM products p JOIN categories c ON c.category_id=p.category_id ORDER BY p.normalized_code,p.product_id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) products.Add(new(ParseGuid(reader.GetString(0)), reader.GetString(1), reader.GetString(2), ParseGuid(reader.GetString(3)), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), Money.FromCents(reader.GetInt64(6)), ParseDecimal(reader.GetString(7)), reader.GetInt64(8) == 1, reader.GetInt64(9) == 1, reader.GetInt64(10) == 1));
        }
        var groups = new Dictionary<Guid, List<BaselineGroupRow>>();
        await using (var command = sqlite.Connection.CreateCommand())
        {
            command.Transaction = sqlite.Transaction; command.CommandText = "SELECT option_group_id,product_id,name,selection_mode,is_required,min_selections,max_selections,display_order FROM option_groups ORDER BY product_id,display_order,option_group_id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var productId = ParseGuid(reader.GetString(1));
                if (!groups.TryGetValue(productId, out var values)) { values = []; groups.Add(productId, values); }
                values.Add(new(ParseGuid(reader.GetString(0)), productId, reader.GetString(2), ParseSelectionMode(reader.GetString(3)), reader.GetInt64(4) == 1, reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.GetInt32(7), []));
            }
        }
        var options = new Dictionary<Guid, List<CatalogueImportBaselineOption>>();
        await using (var command = sqlite.Connection.CreateCommand())
        {
            command.Transaction = sqlite.Transaction; command.CommandText = "SELECT option_id,option_group_id,name,price_adjustment_ttc_cents,is_active,display_order FROM options ORDER BY option_group_id,display_order,option_id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var groupId = ParseGuid(reader.GetString(1));
                if (!options.TryGetValue(groupId, out var values)) { values = []; options.Add(groupId, values); }
                values.Add(new(ParseGuid(reader.GetString(0)), groupId, reader.GetString(2), Money.FromCents(reader.GetInt64(3)), reader.GetInt64(4) == 1, reader.GetInt32(5)));
            }
        }
        var result = products.Select(product => new CatalogueImportBaselineProduct(product.Id, product.Code, product.Name, product.CategoryId, product.CategoryName, product.CategoryShortCode, product.PriceTtc, product.VatRate, product.IsActive, product.DiscountEligible, product.OptionsEnabled,
            groups.TryGetValue(product.Id, out var productGroups) ? productGroups.OrderBy(value => value.DisplayOrder).ThenBy(value => value.Id).Select(group => new CatalogueImportBaselineOptionGroup(group.Id, product.Id, group.Name, group.SelectionMode, group.IsRequired, group.MinSelections, group.MaxSelections, group.DisplayOrder, options.TryGetValue(group.Id, out var groupOptions) ? groupOptions.OrderBy(value => value.DisplayOrder).ThenBy(value => value.Id).ToArray() : [])).ToArray() : [])).ToArray();
        return new CatalogueImportBaseline(categories, result);
    }

    private static async Task<IReadOnlyList<OptionDraft>> ReadOptionsAsync(SqliteConnection connection, Guid groupId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT option_id,name,price_adjustment_ttc_cents,is_active,display_order FROM options WHERE option_group_id=$id ORDER BY display_order,option_id;";
        command.Parameters.AddWithValue("$id", groupId.ToString());
        var result = new List<OptionDraft>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(ParseGuid(reader.GetString(0)), reader.GetString(1), Money.FromCents(reader.GetInt64(2)), reader.GetInt64(3) == 1, reader.GetInt32(4)));
        return result;
    }

    private static async Task<HashSet<Guid>> ReadIdsAsync(SqliteApplicationTransaction sqlite, string sql, CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using var command = sqlite.Connection.CreateCommand(); command.Transaction = sqlite.Transaction; command.CommandText = sql; AddParameters(command, parameters);
        var result = new HashSet<Guid>(); await using var reader = await command.ExecuteReaderAsync(token); while (await reader.ReadAsync(token)) result.Add(ParseGuid(reader.GetString(0))); return result;
    }

    private static async Task<string?> ReadCreatedAtAsync(SqliteApplicationTransaction sqlite, string sql, CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using var command = sqlite.Connection.CreateCommand(); command.Transaction = sqlite.Transaction; command.CommandText = sql; AddParameters(command, parameters); var value = await command.ExecuteScalarAsync(token); return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> ExistsAsync(SqliteApplicationTransaction sqlite, string sql, CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using var command = sqlite.Connection.CreateCommand(); command.Transaction = sqlite.Transaction; command.CommandText = sql; AddParameters(command, parameters); return await command.ExecuteScalarAsync(token) is not null;
    }

    private static async Task<bool?> ReadActiveStateAsync(SqliteApplicationTransaction sqlite, Guid productId, CancellationToken token)
    {
        await using var command = sqlite.Connection.CreateCommand();
        command.Transaction = sqlite.Transaction;
        command.CommandText = "SELECT is_active FROM products WHERE product_id=$id;";
        command.Parameters.AddWithValue("$id", productId.ToString());
        var value = await command.ExecuteScalarAsync(token);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture) == 1;
    }

    private async Task<string?> ReadCategoryCodeAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        if (!await HasCategoryShortCodeColumnsAsync(connection, cancellationToken)) return null;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT short_code FROM categories WHERE category_id=$id;";
        command.Parameters.AddWithValue("$id", categoryId.ToString());
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> HasCategoryShortCodeColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('categories') WHERE name IN ('short_code','normalized_short_code');";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 2;
    }

    private static async Task<int> ExecuteAsync(SqliteApplicationTransaction sqlite, string sql, CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using var command = sqlite.Connection.CreateCommand(); command.Transaction = sqlite.Transaction; command.CommandText = sql; AddParameters(command, parameters); return await command.ExecuteNonQueryAsync(token);
    }

    private static void AddParameters(SqliteCommand command, IEnumerable<(string Name, object? Value)> parameters)
    {
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static SqliteApplicationTransaction RequireSqlite(IApplicationTransaction transaction) => transaction as SqliteApplicationTransaction ?? throw new InvalidOperationException("The configured transaction is not SQLite-backed.");
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string FormatDecimal(decimal value) => value.ToString("0.#############################", CultureInfo.InvariantCulture);
    private static decimal ParseDecimal(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    private static Guid ParseGuid(string value) => Guid.TryParse(value, out var id) ? id : throw new InvalidDataException("The database contains an invalid opaque identifier.");
    private static SelectionMode ParseSelectionMode(string value) => value == "MULTI" ? SelectionMode.Multi : SelectionMode.Single;
    private static bool IsConstraint(SqliteException exception) => exception.SqliteErrorCode is 19 or 1555 or 2067;
    private static bool IsConcurrencyConflict(SqliteException exception) => exception.SqliteErrorCode is 5 or 6 or 517;
    private static ValidationIssue MapCategoryConstraint(SqliteException exception) =>
        exception.Message.Contains("normalized_short_code", StringComparison.OrdinalIgnoreCase) || exception.Message.Contains("short_code", StringComparison.OrdinalIgnoreCase)
            ? new("shortCode", "A category with that short code already exists.", ValidationCodes.CategoryShortCodeDuplicate)
            : new("name", "A category with that name already exists.", ValidationCodes.CategoryDuplicate);
    private static ValidationIssue MapConstraint(SqliteException exception) => exception.Message.Contains("normalized_code", StringComparison.OrdinalIgnoreCase) || exception.Message.Contains("code", StringComparison.OrdinalIgnoreCase) ? new("code", "A product with that code already exists.") : new("product", "The catalogue change conflicts with existing data.");

    private sealed record SnapshotProduct(
        Guid Id,
        string Code,
        string Name,
        string CategoryName,
        string? CategoryShortCode,
        Money PriceTtc,
        decimal VatRate,
        bool IsActive,
        bool DiscountEligible,
        bool OptionsEnabled);

    private sealed record SnapshotOption(
        Guid Id,
        Guid GroupId,
        string Name,
        Money PriceAdjustmentTtc,
        bool IsActive,
        int DisplayOrder);

    private sealed record SnapshotGroup(
        Guid Id,
        Guid ProductId,
        string Name,
        SelectionMode SelectionMode,
        bool IsRequired,
        int? MinSelections,
        int? MaxSelections,
        int DisplayOrder,
        List<SnapshotOption> Options);

    private sealed record BaselineProductRow(
        Guid Id,
        string Code,
        string Name,
        Guid CategoryId,
        string CategoryName,
        string? CategoryShortCode,
        Money PriceTtc,
        decimal VatRate,
        bool IsActive,
        bool DiscountEligible,
        bool OptionsEnabled);

    private sealed record BaselineGroupRow(
        Guid Id,
        Guid ProductId,
        string Name,
        SelectionMode SelectionMode,
        bool IsRequired,
        int? MinSelections,
        int? MaxSelections,
        int DisplayOrder,
        IReadOnlyList<CatalogueImportBaselineOption> Options);

    private sealed record ImportCommitTransactionResult(bool Changed, IReadOnlyDictionary<string, Guid> AllocatedIds);


    private sealed class ImportCommitException : Exception
    {
        public ImportCommitException(params CatalogueImportIssue[] issues) : base(issues.FirstOrDefault()?.Message) => Issues = issues;
        public ImportCommitException(IReadOnlyList<CatalogueImportIssue> issues) : base(issues.Count == 0 ? null : issues[0].Message) => Issues = issues.ToArray();
        public IReadOnlyList<CatalogueImportIssue> Issues { get; }
    }

    private sealed class CatalogueConflictException(string message) : Exception(message);
}
