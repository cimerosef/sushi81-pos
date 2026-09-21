using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Archive;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.Archive;

public sealed record AnnualArchiveEligibilityRecord(
    Guid OrderId,
    OrderStatus Status,
    OrderSourceType SourceType,
    DateTimeOffset EndedAtUtc,
    int ArchiveYear);

public sealed record AnnualArchiveStagingResult(
    int ArchiveYear,
    string StagedDatabasePath,
    DateTimeOffset BuiltAtUtc,
    IReadOnlyList<Guid> OrderIds);

/// <summary>Reads the eligible order boundary from the real live SQLite database without mutating it.</summary>
public sealed class SqliteAnnualArchiveEligibilityReader(SqliteConnectionFactory connectionFactory, IBusinessClock clock)
{
    private readonly SqliteConnectionFactory connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    private readonly IBusinessClock clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<IReadOnlyList<AnnualArchiveEligibilityRecord>> ReadEligibleAsync(int targetYear, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT order_id,status,source_type,closed_at_utc,cancelled_at_utc FROM orders ORDER BY order_id;";

        var result = new List<AnnualArchiveEligibilityRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var orderId = ParseGuid(reader.GetString(0), "order_id");
            var status = ParseStatus(reader.GetString(1));
            var sourceType = ParseSource(reader.GetString(2));
            var closedAt = ReadNullableDateTime(reader, 3);
            var cancelledAt = ReadNullableDateTime(reader, 4);
            var endedAt = status switch
            {
                OrderStatus.Closed => closedAt,
                OrderStatus.Cancelled => cancelledAt,
                _ => null
            };

            if (endedAt is null)
                continue;

            var archiveYear = AnnualArchivePolicy.GetEligibleArchiveYear(status, closedAt, cancelledAt, clock.BusinessTimeZone);
            if (archiveYear == targetYear)
                result.Add(new(orderId, status, sourceType, endedAt.Value, archiveYear.Value));
        }

        return result;
    }

    private static DateTimeOffset? ReadNullableDateTime(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index)
            ? null
            : DateTimeOffset.Parse(reader.GetString(index), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static Guid ParseGuid(string value, string name) =>
        Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw new InvalidDataException($"The live database contains an invalid {name}.");

    private static OrderStatus ParseStatus(string value) => value switch
    {
        "OPEN" => OrderStatus.Open,
        "CLOSED" => OrderStatus.Closed,
        "CANCELLED" => OrderStatus.Cancelled,
        _ => throw new InvalidDataException("The live database contains an invalid order status.")
    };

    private static OrderSourceType ParseSource(string value) => value switch
    {
        "POS" => OrderSourceType.Pos,
        "HIBOUTIK_PASTE" => OrderSourceType.HiboutikPaste,
        _ => throw new InvalidDataException("The live database contains an invalid order source.")
    };
}

/// <summary>
/// Builds a complete annual archive in the application-managed Temp area. This
/// class deliberately has no canonical promotion or live-row deletion operation.
/// </summary>
public sealed class SqliteAnnualArchiveStagingService(
    IAppPaths paths,
    IBusinessClock clock,
    IWriteAuthorityGuard authorityGuard,
    SqliteConnectionFactory connectionFactory,
    Func<string, Exception?>? failureInjector = null)
{
    private const string ArchiveFormatVersion = "M12-WP1-1";

    private readonly IAppPaths paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IBusinessClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IWriteAuthorityGuard authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
    private readonly SqliteConnectionFactory connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    private readonly Func<string, Exception?>? failureInjector = failureInjector;

    public async Task<AnnualArchiveStagingResult?> StageNextArchiveAsync(CancellationToken cancellationToken = default)
    {
        var targetYear = AnnualArchivePolicy.GetTargetArchiveYear(clock.BusinessDate);
        if (targetYear is null)
            return null;

        await using var writeScope = await authorityGuard.EnterWriteScopeAsync(cancellationToken);
        paths.EnsureInitialized();

        var eligibilityReader = new SqliteAnnualArchiveEligibilityReader(connectionFactory, clock);
        var eligible = await eligibilityReader.ReadEligibleAsync(targetYear.Value, cancellationToken);
        var orderIds = eligible.Select(item => item.OrderId).OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
        var stageDirectory = Path.Combine(paths.TempDirectory, "annual-archive", $"{targetYear.Value:D4}-{Guid.NewGuid():N}");
        var stagedDatabasePath = Path.Combine(stageDirectory, $"sushi81-archive-{targetYear.Value:D4}.db");
        Directory.CreateDirectory(stageDirectory);

        try
        {
            Inject("builder");
            await BuildAsync(stagedDatabasePath, targetYear.Value, orderIds, cancellationToken);
            Inject("validation");
            await new SqliteAnnualArchiveValidator(clock).ValidateAsync(
                stagedDatabasePath,
                connectionFactory.LiveDatabasePath,
                targetYear.Value,
                orderIds,
                cancellationToken);

            return new(targetYear.Value, stagedDatabasePath, clock.UtcNow, orderIds);
        }
        catch
        {
            TryDeleteDirectory(stageDirectory);
            throw;
        }
    }

    public Task<AnnualArchiveStagingResult?> StageAsync(CancellationToken cancellationToken = default) =>
        StageNextArchiveAsync(cancellationToken);

    private async Task BuildAsync(string stagedDatabasePath, int targetYear, Guid[] orderIds, CancellationToken cancellationToken)
    {
        await using var source = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = stagedDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        await destination.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(destination, "PRAGMA foreign_keys=ON; PRAGMA journal_mode=DELETE;", cancellationToken);

        using var transaction = destination.BeginTransaction();
        await ExecuteNonQueryAsync(destination, ArchiveSchema, transaction, cancellationToken);

        foreach (var orderId in orderIds)
        {
            await CopyRowsAsync(source, destination, transaction,
                "orders",
                "SELECT order_id,source_type,status,created_at_utc,updated_at_utc,closed_at_utc,cancelled_at_utc,fulfilment_mode,planned_fulfilment_date,planned_fulfilment_time,advance_order_marker,telephone,delivery_address,comment,total_ttc_cents,manual_total_override_active,pickup_discount_applied,pickup_discount_rate,delivery_fee_ttc_cents,order_reference,card_payment_ttc_cents,cash_payment_ttc_cents,source_total_ttc_cents FROM orders WHERE order_id=$id;",
                cancellationToken,
                ("$id", orderId.ToString()));
            await CopyRowsAsync(source, destination, transaction,
                "order_items",
                "SELECT order_item_id,order_id,line_position,source_product_id,product_code_snapshot,product_name_snapshot,category_name_snapshot,product_base_price_ttc_cents,product_vat_rate,product_discount_eligible_snapshot,quantity,extended_base_ttc_cents,calculated_line_total_ttc_cents FROM order_items WHERE order_id=$id ORDER BY line_position,order_item_id;",
                cancellationToken,
                ("$id", orderId.ToString()));
            await CopyRowsAsync(source, destination, transaction,
                "order_item_adjustments",
                "SELECT a.order_item_adjustment_id,a.order_item_id,a.display_order,a.adjustment_kind,a.source_option_id,a.group_name_snapshot,a.label_snapshot,a.adjustment_ttc_per_unit_cents,a.vat_rate FROM order_item_adjustments a JOIN order_items i ON i.order_item_id=a.order_item_id WHERE i.order_id=$id ORDER BY a.order_item_id,a.display_order,a.order_item_adjustment_id;",
                cancellationToken,
                ("$id", orderId.ToString()));
            await CopyRowsAsync(source, destination, transaction,
                "order_tax_breakdown",
                "SELECT order_tax_breakdown_id,order_id,vat_rate,taxable_ttc_cents,included_vat_ttc_cents FROM order_tax_breakdown WHERE order_id=$id ORDER BY vat_rate,order_tax_breakdown_id;",
                cancellationToken,
                ("$id", orderId.ToString()));
            await CopyRowsAsync(source, destination, transaction,
                "payment_adjustments",
                "SELECT payment_adjustment_id,order_id,bucket,delta_cents,effective_business_date,effective_at,recorded_at FROM payment_adjustments WHERE order_id=$id ORDER BY recorded_at,payment_adjustment_id;",
                cancellationToken,
                ("$id", orderId.ToString()));
            Inject("write");
        }

        await InsertMetadataAsync(destination, transaction, targetYear, orderIds.Length, cancellationToken);
        transaction.Commit();
    }

    private async Task InsertMetadataAsync(SqliteConnection connection, SqliteTransaction transaction, int targetYear, int orderCount, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO archive_metadata(key,value) VALUES ($key,$value);";
        foreach (var pair in new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["archive_format_version"] = ArchiveFormatVersion,
            ["archive_schema_version"] = "1",
            ["archive_year"] = targetYear.ToString(CultureInfo.InvariantCulture),
            ["built_at_utc"] = clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["expected_order_count"] = orderCount.ToString(CultureInfo.InvariantCulture)
        })
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$key", pair.Key);
            command.Parameters.AddWithValue("$value", pair.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private void Inject(string stage)
    {
        if (failureInjector?.Invoke(stage) is { } exception)
            throw exception;
    }

    private static async Task CopyRowsAsync(
        SqliteConnection source,
        SqliteConnection destination,
        SqliteTransaction transaction,
        string table,
        string selectSql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var sourceCommand = source.CreateCommand();
        sourceCommand.CommandText = selectSql;
        foreach (var parameter in parameters)
            sourceCommand.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);

        await using var reader = await sourceCommand.ExecuteReaderAsync(cancellationToken);
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var names = string.Join(',', columns);
        var placeholders = string.Join(',', columns.Select((_, index) => "$p" + index.ToString(CultureInfo.InvariantCulture)));
        while (await reader.ReadAsync(cancellationToken))
        {
            await using var insert = destination.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO {table}({names}) VALUES ({placeholders});";
            for (var index = 0; index < columns.Length; index++)
                insert.Parameters.AddWithValue("$p" + index.ToString(CultureInfo.InvariantCulture), reader.IsDBNull(index) ? DBNull.Value : reader.GetValue(index));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // A failed cleanup must never obscure the original staging failure.
        }
    }

    private const string ArchiveSchema = """
        CREATE TABLE orders (
            order_id TEXT NOT NULL PRIMARY KEY,
            source_type TEXT NOT NULL CHECK(source_type IN ('POS','HIBOUTIK_PASTE')),
            status TEXT NOT NULL CHECK(status IN ('OPEN','CLOSED','CANCELLED')),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            closed_at_utc TEXT NULL,
            cancelled_at_utc TEXT NULL,
            fulfilment_mode TEXT NOT NULL CHECK(fulfilment_mode IN ('RETRAIT','LIVRAISON')),
            planned_fulfilment_date TEXT NOT NULL,
            planned_fulfilment_time TEXT NULL,
            advance_order_marker INTEGER NOT NULL CHECK(advance_order_marker IN (0,1)),
            telephone TEXT NULL,
            delivery_address TEXT NULL,
            comment TEXT NULL,
            total_ttc_cents INTEGER NOT NULL,
            manual_total_override_active INTEGER NOT NULL CHECK(manual_total_override_active IN (0,1)),
            pickup_discount_applied INTEGER NOT NULL CHECK(pickup_discount_applied IN (0,1)),
            pickup_discount_rate TEXT NULL,
            delivery_fee_ttc_cents INTEGER NOT NULL CHECK(delivery_fee_ttc_cents >= 0),
            order_reference TEXT NULL,
            card_payment_ttc_cents INTEGER NOT NULL DEFAULT 0 CHECK(card_payment_ttc_cents >= 0),
            cash_payment_ttc_cents INTEGER NOT NULL DEFAULT 0 CHECK(cash_payment_ttc_cents >= 0),
            source_total_ttc_cents INTEGER NULL
        );
        CREATE TABLE order_items (
            order_item_id TEXT NOT NULL PRIMARY KEY,
            order_id TEXT NOT NULL REFERENCES orders(order_id) ON DELETE CASCADE,
            line_position INTEGER NOT NULL CHECK(line_position >= 0),
            source_product_id TEXT NULL,
            product_code_snapshot TEXT NOT NULL,
            product_name_snapshot TEXT NOT NULL,
            category_name_snapshot TEXT NOT NULL,
            product_base_price_ttc_cents INTEGER NOT NULL,
            product_vat_rate TEXT NOT NULL,
            product_discount_eligible_snapshot INTEGER NOT NULL CHECK(product_discount_eligible_snapshot IN (0,1)),
            quantity INTEGER NOT NULL CHECK(quantity > 0),
            extended_base_ttc_cents INTEGER NOT NULL,
            calculated_line_total_ttc_cents INTEGER NOT NULL,
            UNIQUE(order_id, line_position)
        );
        CREATE TABLE order_item_adjustments (
            order_item_adjustment_id TEXT NOT NULL PRIMARY KEY,
            order_item_id TEXT NOT NULL REFERENCES order_items(order_item_id) ON DELETE CASCADE,
            display_order INTEGER NOT NULL CHECK(display_order >= 0),
            adjustment_kind TEXT NOT NULL CHECK(adjustment_kind IN ('PREDEFINED_OPTION','CUSTOM_ADJUSTMENT')),
            source_option_id TEXT NULL,
            group_name_snapshot TEXT NULL,
            label_snapshot TEXT NOT NULL,
            adjustment_ttc_per_unit_cents INTEGER NOT NULL,
            vat_rate TEXT NULL,
            UNIQUE(order_item_id, display_order)
        );
        CREATE TABLE order_tax_breakdown (
            order_tax_breakdown_id TEXT NOT NULL PRIMARY KEY,
            order_id TEXT NOT NULL REFERENCES orders(order_id) ON DELETE CASCADE,
            vat_rate TEXT NOT NULL,
            taxable_ttc_cents INTEGER NOT NULL,
            included_vat_ttc_cents INTEGER NOT NULL,
            UNIQUE(order_id, vat_rate)
        );
        CREATE TABLE payment_adjustments (
            payment_adjustment_id TEXT NOT NULL PRIMARY KEY,
            order_id TEXT NOT NULL REFERENCES orders(order_id) ON DELETE CASCADE,
            bucket TEXT NOT NULL CHECK(bucket IN ('CB','ESPECE')),
            delta_cents INTEGER NOT NULL CHECK(delta_cents <> 0),
            effective_business_date TEXT NOT NULL,
            effective_at TEXT NOT NULL,
            recorded_at TEXT NOT NULL
        );
        CREATE TABLE archive_metadata (
            key TEXT NOT NULL PRIMARY KEY,
            value TEXT NOT NULL
        );
        CREATE INDEX ix_order_items_order ON order_items(order_id, line_position);
        CREATE INDEX ix_order_tax_order ON order_tax_breakdown(order_id);
        CREATE INDEX ix_payment_adjustments_order ON payment_adjustments(order_id, recorded_at, payment_adjustment_id);
        """;
}

public sealed class SqliteAnnualArchiveValidator(IBusinessClock clock)
{
    private static readonly string[] RequiredTables = ["orders", "order_items", "order_item_adjustments", "order_tax_breakdown", "payment_adjustments", "archive_metadata"];
    private readonly IBusinessClock clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task ValidateAsync(
        string archivePath,
        string sourcePath,
        int targetYear,
        IReadOnlyCollection<Guid> expectedOrderIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(expectedOrderIds);

        await using var source = await SqliteAnnualArchiveValidationConnection.OpenAsync(sourcePath, cancellationToken);
        await using var archive = await SqliteAnnualArchiveValidationConnection.OpenAsync(archivePath, cancellationToken);

        if (!string.Equals(await ScalarStringAsync(archive, "PRAGMA integrity_check;", cancellationToken), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The staged annual archive failed PRAGMA integrity_check.");
        await EnsureForeignKeysAreValidAsync(archive, cancellationToken);
        await EnsureRequiredTablesAsync(archive, cancellationToken);

        var metadata = await ReadMetadataAsync(archive, cancellationToken);
        RequireMetadata(metadata, "archive_format_version", "M12-WP1-1");
        RequireMetadata(metadata, "archive_schema_version", "1");
        RequireMetadata(metadata, "archive_year", targetYear.ToString(CultureInfo.InvariantCulture));
        if (!metadata.TryGetValue("built_at_utc", out var builtAt) || !DateTimeOffset.TryParse(builtAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            throw new InvalidDataException("The staged annual archive has invalid build metadata.");
        if (!metadata.TryGetValue("expected_order_count", out var countValue) || !int.TryParse(countValue, CultureInfo.InvariantCulture, out var expectedCount) || expectedCount != expectedOrderIds.Count)
            throw new InvalidDataException("The staged annual archive has an invalid expected order count.");

        var expectedIds = expectedOrderIds.OrderBy(id => id.ToString("D"), StringComparer.Ordinal).Select(id => id.ToString()).ToArray();
        var actualIds = new List<string>();
        await using (var command = archive.CreateCommand())
        {
            command.CommandText = "SELECT order_id,status,closed_at_utc,cancelled_at_utc FROM orders ORDER BY order_id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                actualIds.Add(id);
                var status = ParseStatus(reader.GetString(1));
                if (status == OrderStatus.Open)
                    throw new InvalidDataException("An OPEN order was written to the staged annual archive.");
                var closedAt = ReadNullableDateTime(reader, 2);
                var cancelledAt = ReadNullableDateTime(reader, 3);
                if (AnnualArchivePolicy.GetEligibleArchiveYear(status, closedAt, cancelledAt, clock.BusinessTimeZone) != targetYear)
                    throw new InvalidDataException("A staged order has an archive year different from the requested target.");
            }
        }

        if (!actualIds.SequenceEqual(expectedIds, StringComparer.Ordinal))
            throw new InvalidDataException("The staged annual archive order identity set does not match the live eligibility set.");

        var childSpecifications = new[]
        {
            ("order_items", "SELECT COUNT(*) FROM order_items WHERE order_id=$id;"),
            ("order_item_adjustments", "SELECT COUNT(*) FROM order_item_adjustments a JOIN order_items i ON i.order_item_id=a.order_item_id WHERE i.order_id=$id;"),
            ("order_tax_breakdown", "SELECT COUNT(*) FROM order_tax_breakdown WHERE order_id=$id;"),
            ("payment_adjustments", "SELECT COUNT(*) FROM payment_adjustments WHERE order_id=$id;")
        };
        foreach (var id in expectedIds)
        {
            foreach (var specification in childSpecifications)
            {
                var sourceCount = await ScalarLongAsync(source, specification.Item2, id, cancellationToken);
                var archiveCount = await ScalarLongAsync(archive, specification.Item2, id, cancellationToken);
                if (sourceCount != archiveCount)
                    throw new InvalidDataException($"The staged annual archive has a {specification.Item1} child-count mismatch.");
            }
        }
    }

    private static async Task EnsureRequiredTablesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var actual = new HashSet<string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) actual.Add(reader.GetString(0));
        if (RequiredTables.Any(table => !actual.Contains(table)))
            throw new InvalidDataException("The staged annual archive is missing a required table.");
    }

    private static async Task EnsureForeignKeysAreValidAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidDataException("The staged annual archive failed PRAGMA foreign_key_check.");
    }

    private static async Task<Dictionary<string, string>> ReadMetadataAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key,value FROM archive_metadata;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    private static void RequireMetadata(Dictionary<string, string> metadata, string key, string expected)
    {
        if (!metadata.TryGetValue(key, out var actual) || !string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"The staged annual archive has invalid metadata for '{key}'.");
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static DateTimeOffset? ReadNullableDateTime(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : DateTimeOffset.Parse(reader.GetString(index), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static OrderStatus ParseStatus(string value) => value switch
    {
        "OPEN" => OrderStatus.Open,
        "CLOSED" => OrderStatus.Closed,
        "CANCELLED" => OrderStatus.Cancelled,
        _ => throw new InvalidDataException("The staged annual archive contains an invalid order status.")
    };
}

internal static class SqliteAnnualArchiveValidationConnection
{
    public static async Task<SqliteConnection> OpenAsync(string path, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
