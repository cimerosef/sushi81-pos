using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Archive;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.Archive;

/// <summary>
/// Provides the M12 WP4 read-only boundary over validated canonical archives.
/// The selected archive is the only historical data source used by this type.
/// </summary>
public sealed class SqliteAnnualArchiveAccess(
    IAppPaths paths,
    IBusinessClock clock,
    Func<string, Exception?>? failureInjector = null) : IAnnualArchiveAccess
{
    private static readonly System.Text.RegularExpressions.Regex CanonicalName = new(
        "^sushi81-archive-(?<year>[0-9]{4})\\.db$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly IAppPaths paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IBusinessClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly Func<string, Exception?>? failureInjector = failureInjector;

    public async Task<IReadOnlyList<AnnualArchiveDescriptor>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        if (!Directory.Exists(paths.ArchiveDirectory))
            return [];

        var result = new List<AnnualArchiveDescriptor>();
        foreach (var path in Directory.EnumerateFiles(paths.ArchiveDirectory, "*.db", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = CanonicalName.Match(Path.GetFileName(path));
            if (!match.Success || !int.TryParse(match.Groups["year"].Value, CultureInfo.InvariantCulture, out var year))
                continue;

            try
            {
                await ValidateAsync(path, year, cancellationToken);
                result.Add(await ReadDescriptorAsync(path, year, cancellationToken));
            }
            catch (InvalidDataException)
            {
                // A damaged or incomplete candidate is not historical data. It
                // remains on disk for diagnostics but is not offered to users.
            }
            catch (SqliteException)
            {
                // Treat unreadable candidates the same way as other invalid
                // candidates; discovery must never make the live UI unusable.
            }
        }

        return result
            .OrderByDescending(item => item.ArchiveYear)
            .ThenBy(item => item.CanonicalPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<OrderBrowserRow>> SearchAsync(
        int archiveYear,
        AnnualArchiveSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var path = await RequireValidatedArchiveAsync(archiveYear, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(path, cancellationToken);

        var query = criteria.Query?.Trim() ?? string.Empty;
        await using var command = connection.CreateCommand();
        var predicates = new List<string>();
        if (query.Length > 0)
        {
            predicates.Add("(COALESCE(o.order_reference,'') LIKE $query ESCAPE '\\' OR COALESCE(o.comment,'') LIKE $query ESCAPE '\\' OR COALESCE(o.telephone,'') LIKE $query ESCAPE '\\' OR EXISTS (SELECT 1 FROM order_items si WHERE si.order_id=o.order_id AND (si.product_code_snapshot LIKE $query ESCAPE '\\' OR si.product_name_snapshot LIKE $query ESCAPE '\\' OR si.category_name_snapshot LIKE $query ESCAPE '\\')))" );
            command.Parameters.AddWithValue("$query", $"%{EscapeLikePattern(query)}%");
        }

        if (criteria.Status is { } status)
        {
            predicates.Add("o.status=$status");
            command.Parameters.AddWithValue("$status", StatusName(status));
        }

        if (criteria.FromDate is { } from)
        {
            predicates.Add("o.planned_fulfilment_date >= $from");
            command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (criteria.ToDate is { } to)
        {
            predicates.Add("o.planned_fulfilment_date <= $to");
            command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        command.CommandText = $"""
            SELECT o.order_id,o.order_reference,o.planned_fulfilment_date,o.planned_fulfilment_time,
                   o.fulfilment_mode,o.status,o.total_ttc_cents,o.advance_order_marker,o.telephone,
                   o.delivery_address,o.comment,o.card_payment_ttc_cents,o.cash_payment_ttc_cents,
                   o.source_type,o.source_total_ttc_cents
            FROM orders o
            {(predicates.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", predicates))}
            ORDER BY o.planned_fulfilment_date,o.planned_fulfilment_time IS NULL,o.planned_fulfilment_time,o.order_id;
            """;

        var result = new List<OrderBrowserRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(
                ParseGuid(reader.GetString(0)),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                ReadNullableTime(reader, 3),
                ParseFulfilment(reader.GetString(4)),
                ParseStatus(reader.GetString(5)),
                Money.FromCents(reader.GetInt64(6)),
                ReadNullableString(reader, 8))
            {
                Reference = ReadNullableString(reader, 1) ?? string.Empty,
                DeliveryAddress = ReadNullableString(reader, 9),
                Comment = ReadNullableString(reader, 10),
                AdvanceOrderMarker = reader.GetInt64(7) == 1,
                CardPaymentTtc = Money.FromCents(reader.GetInt64(11)),
                CashPaymentTtc = Money.FromCents(reader.GetInt64(12)),
                SourceType = ParseSource(reader.GetString(13)),
                SourceTotalTtc = reader.IsDBNull(14) ? null : Money.FromCents(reader.GetInt64(14))
            });
        }

        return result;
    }

    public async Task<OrderSnapshot?> GetOrderAsync(int archiveYear, Guid orderId, CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
            return null;

        var path = await RequireValidatedArchiveAsync(archiveYear, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(path, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT order_id,source_type,status,created_at_utc,updated_at_utc,closed_at_utc,cancelled_at_utc,fulfilment_mode,planned_fulfilment_date,planned_fulfilment_time,advance_order_marker,telephone,delivery_address,comment,total_ttc_cents,manual_total_override_active,pickup_discount_applied,pickup_discount_rate,delivery_fee_ttc_cents,order_reference,card_payment_ttc_cents,cash_payment_ttc_cents,source_total_ttc_cents FROM orders WHERE order_id=$id;";
        command.Parameters.AddWithValue("$id", orderId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var snapshot = new OrderSnapshot(
            ParseGuid(reader.GetString(0)),
            ParseSource(reader.GetString(1)),
            ParseStatus(reader.GetString(2)),
            ParseDateTime(reader.GetString(3)),
            ParseDateTime(reader.GetString(4)),
            ReadNullableDateTime(reader, 5),
            ReadNullableDateTime(reader, 6),
            ParseFulfilment(reader.GetString(7)),
            DateOnly.ParseExact(reader.GetString(8), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            ReadNullableTime(reader, 9),
            reader.GetInt64(10) == 1,
            ReadNullableString(reader, 11),
            ReadNullableString(reader, 12),
            ReadNullableString(reader, 13),
            Money.FromCents(reader.GetInt64(14)),
            reader.GetInt64(15) == 1,
            reader.GetInt64(16) == 1,
            ReadNullableDecimal(reader, 17),
            Money.FromCents(reader.GetInt64(18)),
            [], [])
        {
            Reference = ReadNullableString(reader, 19) ?? string.Empty,
            CardPaymentTtc = Money.FromCents(reader.GetInt64(20)),
            CashPaymentTtc = Money.FromCents(reader.GetInt64(21)),
            SourceTotalTtc = reader.IsDBNull(22) ? null : Money.FromCents(reader.GetInt64(22))
        };
        await reader.CloseAsync();

        var items = new List<OrderItemSnapshot>();
        await using (var itemCommand = connection.CreateCommand())
        {
            itemCommand.CommandText = "SELECT order_item_id,line_position,source_product_id,product_code_snapshot,product_name_snapshot,category_name_snapshot,product_base_price_ttc_cents,product_vat_rate,product_discount_eligible_snapshot,quantity,extended_base_ttc_cents,calculated_line_total_ttc_cents FROM order_items WHERE order_id=$order ORDER BY line_position,order_item_id;";
            itemCommand.Parameters.AddWithValue("$order", orderId.ToString());
            await using var itemReader = await itemCommand.ExecuteReaderAsync(cancellationToken);
            while (await itemReader.ReadAsync(cancellationToken))
            {
                var itemId = ParseGuid(itemReader.GetString(0));
                items.Add(new(
                    itemId,
                    itemReader.GetInt32(1),
                    itemReader.IsDBNull(2) ? null : ParseGuid(itemReader.GetString(2)),
                    itemReader.GetString(3),
                    itemReader.GetString(4),
                    itemReader.GetString(5),
                    Money.FromCents(itemReader.GetInt64(6)),
                    ParseDecimal(itemReader.GetString(7)),
                    itemReader.GetInt64(8) == 1,
                    itemReader.GetInt32(9),
                    Money.FromCents(itemReader.GetInt64(10)),
                    Money.FromCents(itemReader.GetInt64(11)),
                    await ReadAdjustmentsAsync(connection, itemId, cancellationToken)));
            }
        }

        var taxes = new List<OrderTaxBreakdown>();
        await using (var taxCommand = connection.CreateCommand())
        {
            taxCommand.CommandText = "SELECT order_tax_breakdown_id,vat_rate,taxable_ttc_cents,included_vat_ttc_cents FROM order_tax_breakdown WHERE order_id=$order ORDER BY CAST(vat_rate AS NUMERIC),order_tax_breakdown_id;";
            taxCommand.Parameters.AddWithValue("$order", orderId.ToString());
            await using var taxReader = await taxCommand.ExecuteReaderAsync(cancellationToken);
            while (await taxReader.ReadAsync(cancellationToken))
                taxes.Add(new(ParseDecimal(taxReader.GetString(1)), Money.FromCents(taxReader.GetInt64(2)), Money.FromCents(taxReader.GetInt64(3)), ParseGuid(taxReader.GetString(0))));
        }

        return snapshot with { Items = items, TaxBreakdown = taxes };
    }

    public async Task<AnnualArchiveCopyResult> CopyAsync(int archiveYear, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var sourcePath = await RequireValidatedArchiveAsync(archiveYear, cancellationToken);
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var destinationFullPath = Path.GetFullPath(destinationPath);
        if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected archive cannot be copied over its canonical source.");

        var parent = Path.GetDirectoryName(destinationFullPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            throw new DirectoryNotFoundException("The selected export directory does not exist.");

        var sourceFacts = await ReadFileFactsAsync(sourceFullPath, cancellationToken);
        var tempPath = destinationFullPath + $".partial-{Guid.NewGuid():N}";
        var backupPath = destinationFullPath + $".previous-{Guid.NewGuid():N}";
        var backupContainsPrevious = false;
        try
        {
            Inject("copy");
            await using (var source = new FileStream(sourceFullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(target, 128 * 1024, cancellationToken);
                Inject("flush");
                target.Flush(flushToDisk: true);
            }

            Inject("verify");
            var temporaryFacts = await ReadFileFactsAsync(tempPath, cancellationToken);
            if (temporaryFacts.Length != sourceFacts.Length || !string.Equals(temporaryFacts.Sha256, sourceFacts.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The selected archive copy failed length or SHA-256 verification.");

            Inject("replace");
            var hadDestination = File.Exists(destinationFullPath);
            if (hadDestination)
            {
                File.Move(destinationFullPath, backupPath);
                backupContainsPrevious = true;
            }

            try
            {
                File.Move(tempPath, destinationFullPath);
            }
            catch
            {
                if (backupContainsPrevious && File.Exists(backupPath) && !File.Exists(destinationFullPath))
                {
                    File.Move(backupPath, destinationFullPath);
                    backupContainsPrevious = false;
                }
                throw;
            }

            if (hadDestination)
            {
                TryDelete(backupPath);
                backupContainsPrevious = false;
            }

            return new(archiveYear, destinationFullPath, sourceFacts.Length, sourceFacts.Sha256);
        }
        finally
        {
            TryDelete(tempPath);
            if (!backupContainsPrevious)
                TryDelete(backupPath);
        }
    }

    private async Task<string> RequireValidatedArchiveAsync(int year, CancellationToken cancellationToken)
    {
        if (year is < 1 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(year));
        paths.EnsureInitialized();
        var path = Path.Combine(paths.ArchiveDirectory, $"sushi81-archive-{year:D4}.db");
        if (!File.Exists(path))
            throw new FileNotFoundException("The selected annual archive is not available.", path);
        await ValidateAsync(path, year, cancellationToken);
        return path;
    }

    private async Task ValidateAsync(string path, int year, CancellationToken cancellationToken) =>
        await new SqliteAnnualArchiveValidator(clock).ValidateStandaloneAsync(path, year, cancellationToken);

    private static async Task<AnnualArchiveDescriptor> ReadDescriptorAsync(string path, int year, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(path, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key,value FROM archive_metadata WHERE key IN ('built_at_utc','expected_order_count');";
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) values[reader.GetString(0)] = reader.GetString(1);
        if (!values.TryGetValue("built_at_utc", out var builtAt) || !DateTimeOffset.TryParse(builtAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            throw new InvalidDataException("The canonical annual archive has invalid build metadata.");
        if (!values.TryGetValue("expected_order_count", out var count) || !int.TryParse(count, CultureInfo.InvariantCulture, out var orderCount))
            throw new InvalidDataException("The canonical annual archive has invalid order-count metadata.");
        return new(year, path, orderCount, timestamp);
    }

    private static async Task<IReadOnlyList<OrderLineAdjustmentSnapshot>> ReadAdjustmentsAsync(SqliteConnection connection, Guid itemId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT order_item_adjustment_id,display_order,adjustment_kind,source_option_id,group_name_snapshot,label_snapshot,adjustment_ttc_per_unit_cents,vat_rate FROM order_item_adjustments WHERE order_item_id=$item ORDER BY display_order,order_item_adjustment_id;";
        command.Parameters.AddWithValue("$item", itemId.ToString());
        var result = new List<OrderLineAdjustmentSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(ParseGuid(reader.GetString(0)), reader.GetInt32(1), ParseAdjustmentKind(reader.GetString(2)), reader.IsDBNull(3) ? null : ParseGuid(reader.GetString(3)), ReadNullableString(reader, 4), reader.GetString(5), Money.FromCents(reader.GetInt64(6)), reader.IsDBNull(7) ? null : ParseDecimal(reader.GetString(7))));
        return result;
    }

    private void Inject(string stage)
    {
        if (failureInjector?.Invoke(stage) is { } exception)
            throw exception;
    }

    private static async Task<FileFacts> ReadFileFactsAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return new(stream.Length, Convert.ToHexString(hash));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record FileFacts(long Length, string Sha256);

    private static string EscapeLikePattern(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
    private static string StatusName(OrderStatus value) => value switch { OrderStatus.Open => "OPEN", OrderStatus.Closed => "CLOSED", OrderStatus.Cancelled => "CANCELLED", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static Guid ParseGuid(string value) => Guid.TryParse(value, out var id) && id != Guid.Empty ? id : throw new InvalidDataException("The archive contains an invalid identifier.");
    private static OrderSourceType ParseSource(string value) => value switch { "POS" => OrderSourceType.Pos, "HIBOUTIK_PASTE" => OrderSourceType.HiboutikPaste, _ => throw new InvalidDataException("The archive contains an invalid order source.") };
    private static OrderStatus ParseStatus(string value) => value switch { "OPEN" => OrderStatus.Open, "CLOSED" => OrderStatus.Closed, "CANCELLED" => OrderStatus.Cancelled, _ => throw new InvalidDataException("The archive contains an invalid order status.") };
    private static FulfilmentMode ParseFulfilment(string value) => value switch { "RETRAIT" => FulfilmentMode.Retrait, "LIVRAISON" => FulfilmentMode.Livraison, _ => throw new InvalidDataException("The archive contains an invalid fulfilment mode.") };
    private static OrderAdjustmentKind ParseAdjustmentKind(string value) => value switch { "PREDEFINED_OPTION" => OrderAdjustmentKind.PredefinedOption, "CUSTOM_ADJUSTMENT" => OrderAdjustmentKind.CustomAdjustment, _ => throw new InvalidDataException("The archive contains an invalid adjustment kind.") };
    private static DateTimeOffset ParseDateTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static DateTimeOffset? ReadNullableDateTime(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : ParseDateTime(reader.GetString(index));
    private static TimeOnly? ReadNullableTime(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : TimeOnly.ParseExact(reader.GetString(index), "HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
    private static string? ReadNullableString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static decimal ParseDecimal(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    private static decimal? ReadNullableDecimal(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : ParseDecimal(reader.GetString(index));
}
