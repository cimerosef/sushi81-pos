using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Transactions;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.Order;

/// <summary>Persists complete M04 sale snapshots without current-catalogue foreign keys.</summary>
public sealed class SqliteOrderStore(
    SqliteConnectionFactory connectionFactory,
    ITransactionRunner transactionRunner,
    Func<string, Exception?>? writeFailureInjector = null,
    IIdGenerator? idGenerator = null) : IOrderStore
{
    private readonly SqliteConnectionFactory connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    private readonly ITransactionRunner transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    private readonly Func<string, Exception?>? writeFailureInjector = writeFailureInjector;
    private readonly IIdGenerator? idGenerator = idGenerator;

    public async Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Id == Guid.Empty) throw new ArgumentException("An order identity is required.", nameof(snapshot));
        if (snapshot.Items.Count == 0) throw new ArgumentException("An order must contain at least one item.", nameof(snapshot));

        await transactionRunner.ExecuteAsync(async (transaction, token) =>
        {
            var sqlite = RequireSqlite(transaction);
            Inject("order");
            await ExecuteAsync(sqlite, """
                INSERT INTO orders(order_id,source_type,status,created_at_utc,updated_at_utc,closed_at_utc,cancelled_at_utc,fulfilment_mode,planned_fulfilment_date,planned_fulfilment_time,advance_order_marker,telephone,delivery_address,comment,total_ttc_cents,manual_total_override_active,pickup_discount_applied,pickup_discount_rate,delivery_fee_ttc_cents)
                VALUES ($id,$source,$status,$created,$updated,$closed,$cancelled,$fulfilment,$date,$time,$advance,$telephone,$address,$comment,$total,$manual,$discount,$rate,$fee);
                """, token,
                ("$id", snapshot.Id.ToString()), ("$source", SourceName(snapshot.SourceType)), ("$status", StatusName(snapshot.Status)),
                ("$created", Format(snapshot.CreatedAt)), ("$updated", Format(snapshot.UpdatedAt)), ("$closed", FormatNullable(snapshot.ClosedAt)),
                ("$cancelled", FormatNullable(snapshot.CancelledAt)), ("$fulfilment", FulfilmentName(snapshot.Fulfilment)),
                ("$date", snapshot.PlannedFulfilmentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$time", snapshot.PlannedFulfilmentTime?.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)),
                ("$advance", snapshot.AdvanceOrderMarker ? 1 : 0), ("$telephone", snapshot.Telephone), ("$address", snapshot.DeliveryAddress),
                ("$comment", snapshot.Comment), ("$total", snapshot.TotalTtc.Cents), ("$manual", snapshot.ManualTotalOverrideActive ? 1 : 0),
                ("$discount", snapshot.PickupDiscountApplied ? 1 : 0), ("$rate", FormatDecimalNullable(snapshot.PickupDiscountRate)), ("$fee", snapshot.DeliveryFeeTtc.Cents));

            foreach (var item in snapshot.Items.OrderBy(item => item.Position))
            {
                Inject("item");
                await ExecuteAsync(sqlite, """
                    INSERT INTO order_items(order_item_id,order_id,line_position,source_product_id,product_code_snapshot,product_name_snapshot,category_name_snapshot,product_base_price_ttc_cents,product_vat_rate,product_discount_eligible_snapshot,quantity,extended_base_ttc_cents,calculated_line_total_ttc_cents)
                    VALUES ($id,$order,$position,$product,$code,$name,$category,$base,$vat,$eligible,$quantity,$extended,$total);
                    """, token,
                    ("$id", item.Id.ToString()), ("$order", snapshot.Id.ToString()), ("$position", item.Position),
                    ("$product", item.SourceProductId?.ToString()), ("$code", item.ProductCode), ("$name", item.ProductName),
                    ("$category", item.CategoryName), ("$base", item.ProductBasePriceTtc.Cents), ("$vat", FormatDecimal(item.ProductVatRate)),
                    ("$eligible", item.ProductDiscountEligible ? 1 : 0), ("$quantity", item.Quantity), ("$extended", item.ExtendedBaseTtc.Cents),
                    ("$total", item.CalculatedLineTotalTtc.Cents));

                foreach (var adjustment in item.Adjustments.OrderBy(adjustment => adjustment.DisplayOrder))
                {
                    Inject("adjustment");
                    await ExecuteAsync(sqlite, """
                        INSERT INTO order_item_adjustments(order_item_adjustment_id,order_item_id,display_order,adjustment_kind,source_option_id,group_name_snapshot,label_snapshot,adjustment_ttc_per_unit_cents,vat_rate)
                        VALUES ($id,$item,$order,$kind,$option,$groupName,$label,$amount,$vat);
                        """, token,
                        ("$id", adjustment.Id.ToString()), ("$item", item.Id.ToString()), ("$order", adjustment.DisplayOrder),
                        ("$kind", AdjustmentName(adjustment.Kind)), ("$option", adjustment.SourceOptionId?.ToString()),
                        ("$groupName", adjustment.GroupName), ("$label", adjustment.Label),
                        ("$amount", adjustment.AdjustmentTtcPerUnit.Cents), ("$vat", FormatDecimalNullable(adjustment.VatRate)));
                }
            }

            foreach (var tax in snapshot.TaxBreakdown.OrderBy(tax => tax.VatRate))
            {
                Inject("tax");
                var id = tax.Id == Guid.Empty ? idGenerator?.NewId() ?? throw new InvalidOperationException("Tax snapshot identities must be allocated before persistence.") : tax.Id;
                await ExecuteAsync(sqlite, "INSERT INTO order_tax_breakdown(order_tax_breakdown_id,order_id,vat_rate,taxable_ttc_cents,included_vat_ttc_cents) VALUES ($id,$order,$vat,$taxable,$included);", token,
                    ("$id", id.ToString()), ("$order", snapshot.Id.ToString()), ("$vat", FormatDecimal(tax.VatRate)),
                    ("$taxable", tax.TaxableTtc.Cents), ("$included", tax.IncludedVatTtc.Cents));
            }
        }, cancellationToken);
    }

    public async Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty) return null;
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT order_id,source_type,status,created_at_utc,updated_at_utc,closed_at_utc,cancelled_at_utc,fulfilment_mode,planned_fulfilment_date,planned_fulfilment_time,advance_order_marker,telephone,delivery_address,comment,total_ttc_cents,manual_total_override_active,pickup_discount_applied,pickup_discount_rate,delivery_fee_ttc_cents FROM orders WHERE order_id=$id;";
        command.Parameters.AddWithValue("$id", orderId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var snapshot = new OrderSnapshot(
            ParseGuid(reader.GetString(0)), ParseSource(reader.GetString(1)), ParseStatus(reader.GetString(2)), ParseDateTime(reader.GetString(3)),
            ParseDateTime(reader.GetString(4)), ParseNullableDateTime(reader, 5), ParseNullableDateTime(reader, 6), ParseFulfilment(reader.GetString(7)),
            DateOnly.ParseExact(reader.GetString(8), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.IsDBNull(9) ? null : TimeOnly.ParseExact(reader.GetString(9), "HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
            reader.GetInt64(10) == 1, ReadNullableString(reader, 11), ReadNullableString(reader, 12), ReadNullableString(reader, 13),
            Money.FromCents(reader.GetInt64(14)), reader.GetInt64(15) == 1, reader.GetInt64(16) == 1,
            reader.IsDBNull(17) ? null : ParseDecimal(reader.GetString(17)), Money.FromCents(reader.GetInt64(18)), [], []);
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
                var adjustments = await ReadAdjustmentsAsync(connection, itemId, cancellationToken);
                items.Add(new(itemId, itemReader.GetInt32(1), itemReader.IsDBNull(2) ? null : ParseGuid(itemReader.GetString(2)), itemReader.GetString(3), itemReader.GetString(4), itemReader.GetString(5), Money.FromCents(itemReader.GetInt64(6)), ParseDecimal(itemReader.GetString(7)), itemReader.GetInt64(8) == 1, itemReader.GetInt32(9), Money.FromCents(itemReader.GetInt64(10)), Money.FromCents(itemReader.GetInt64(11)), adjustments));
            }
        }

        var taxes = new List<OrderTaxBreakdown>();
        await using (var taxCommand = connection.CreateCommand())
        {
            taxCommand.CommandText = "SELECT order_tax_breakdown_id,vat_rate,taxable_ttc_cents,included_vat_ttc_cents FROM order_tax_breakdown WHERE order_id=$order ORDER BY CAST(vat_rate AS NUMERIC),order_tax_breakdown_id;";
            taxCommand.Parameters.AddWithValue("$order", orderId.ToString());
            await using var taxReader = await taxCommand.ExecuteReaderAsync(cancellationToken);
            while (await taxReader.ReadAsync(cancellationToken)) taxes.Add(new(ParseDecimal(taxReader.GetString(1)), Money.FromCents(taxReader.GetInt64(2)), Money.FromCents(taxReader.GetInt64(3)), ParseGuid(taxReader.GetString(0))));
        }

        return snapshot with { Items = items, TaxBreakdown = taxes };
    }

    private static async Task<IReadOnlyList<OrderLineAdjustmentSnapshot>> ReadAdjustmentsAsync(SqliteConnection connection, Guid itemId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT order_item_adjustment_id,display_order,adjustment_kind,source_option_id,group_name_snapshot,label_snapshot,adjustment_ttc_per_unit_cents,vat_rate FROM order_item_adjustments WHERE order_item_id=$item ORDER BY display_order,order_item_adjustment_id;";
        command.Parameters.AddWithValue("$item", itemId.ToString());
        var result = new List<OrderLineAdjustmentSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(ParseGuid(reader.GetString(0)), reader.GetInt32(1), ParseAdjustmentKind(reader.GetString(2)), reader.IsDBNull(3) ? null : ParseGuid(reader.GetString(3)), ReadNullableString(reader, 4), reader.GetString(5), Money.FromCents(reader.GetInt64(6)), reader.IsDBNull(7) ? null : ParseDecimal(reader.GetString(7))));
        return result;
    }

    private void Inject(string stage)
    {
        if (writeFailureInjector?.Invoke(stage) is { } exception) throw exception;
    }

    private static async Task<int> ExecuteAsync(SqliteApplicationTransaction sqlite, string sql, CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using var command = sqlite.Connection.CreateCommand();
        command.Transaction = sqlite.Transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(token);
    }

    private static SqliteApplicationTransaction RequireSqlite(IApplicationTransaction transaction) => transaction as SqliteApplicationTransaction ?? throw new InvalidOperationException("The configured transaction is not SQLite-backed.");
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string? FormatNullable(DateTimeOffset? value) => value is null ? null : Format(value.Value);
    private static string FormatDecimal(decimal value) => value.ToString("0.#############################", CultureInfo.InvariantCulture);
    private static string? FormatDecimalNullable(decimal? value) => value is null ? null : FormatDecimal(value.Value);
    private static decimal ParseDecimal(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDateTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static DateTimeOffset? ParseNullableDateTime(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : ParseDateTime(reader.GetString(index));
    private static string? ReadNullableString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static Guid ParseGuid(string value) => Guid.TryParse(value, out var id) ? id : throw new InvalidDataException("The database contains an invalid opaque identifier.");
    private static string SourceName(OrderSourceType value) => value switch { OrderSourceType.Pos => "POS", OrderSourceType.HiboutikPaste => "HIBOUTIK_PASTE", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string StatusName(OrderStatus value) => value switch { OrderStatus.Open => "OPEN", OrderStatus.Closed => "CLOSED", OrderStatus.Cancelled => "CANCELLED", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string FulfilmentName(FulfilmentMode value) => value switch { FulfilmentMode.Retrait => "RETRAIT", FulfilmentMode.Livraison => "LIVRAISON", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string AdjustmentName(OrderAdjustmentKind value) => value switch { OrderAdjustmentKind.PredefinedOption => "PREDEFINED_OPTION", OrderAdjustmentKind.CustomAdjustment => "CUSTOM_ADJUSTMENT", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static OrderSourceType ParseSource(string value) => value switch { "POS" => OrderSourceType.Pos, "HIBOUTIK_PASTE" => OrderSourceType.HiboutikPaste, _ => throw new InvalidDataException("The database contains an invalid order source.") };
    private static OrderStatus ParseStatus(string value) => value switch { "OPEN" => OrderStatus.Open, "CLOSED" => OrderStatus.Closed, "CANCELLED" => OrderStatus.Cancelled, _ => throw new InvalidDataException("The database contains an invalid order status.") };
    private static FulfilmentMode ParseFulfilment(string value) => value switch { "RETRAIT" => FulfilmentMode.Retrait, "LIVRAISON" => FulfilmentMode.Livraison, _ => throw new InvalidDataException("The database contains an invalid fulfilment mode.") };
    private static OrderAdjustmentKind ParseAdjustmentKind(string value) => value switch { "PREDEFINED_OPTION" => OrderAdjustmentKind.PredefinedOption, "CUSTOM_ADJUSTMENT" => OrderAdjustmentKind.CustomAdjustment, _ => throw new InvalidDataException("The database contains an invalid adjustment kind.") };
}

/// <summary>Production M04 boundary; actual Windows printing is intentionally deferred.</summary>
public sealed class NoOpOrderPrintDispatcher : IOrderPrintDispatcher
{
    public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(committedOrder);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
