using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Foundation.Transactions;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.Order;

/// <summary>Persists complete order snapshots and M05 lifecycle/payment mutations in SQLite transactions.</summary>
public sealed class SqliteOrderStore(
    SqliteConnectionFactory connectionFactory,
    ITransactionRunner transactionRunner,
    Func<string, Exception?>? writeFailureInjector = null,
    IIdGenerator? idGenerator = null,
    IBusinessClock? clock = null) : IOrderStore, IOrderLifecycleStore
{
    private readonly SqliteConnectionFactory connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    private readonly ITransactionRunner transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    private readonly Func<string, Exception?>? writeFailureInjector = writeFailureInjector;
    private readonly IIdGenerator? idGenerator = idGenerator;
    private readonly IBusinessClock? clock = clock;

    public Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) => SaveLifecycleAsync(snapshot, [], cancellationToken);

    public async Task SaveLifecycleAsync(OrderSnapshot snapshot, IReadOnlyList<PaymentAdjustment> adjustments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(adjustments);
        if (snapshot.Id == Guid.Empty) throw new ArgumentException("An order identity is required.", nameof(snapshot));
        if (snapshot.Items.Count == 0) throw new ArgumentException("An order must contain at least one item.", nameof(snapshot));
        foreach (var adjustment in adjustments)
            if (adjustment.Id == Guid.Empty || adjustment.OrderId != snapshot.Id || adjustment.Delta == Money.Zero)
                throw new ArgumentException("Payment adjustments must have a stable order identity and a non-zero delta.", nameof(adjustments));

        await transactionRunner.ExecuteAsync(async (transaction, token) =>
        {
            var sqlite = RequireSqlite(transaction);
            if (!await HasColumnAsync(sqlite.Connection, "orders", "order_reference", token))
            {
                await SaveLegacyAsync(sqlite, snapshot, token);
                return;
            }

            var existingReference = await ReadReferenceAsync(sqlite, snapshot.Id, token);
            var isExisting = existingReference is not null;
            var reference = isExisting && !string.IsNullOrWhiteSpace(existingReference) ? existingReference! : await AllocateReferenceAsync(sqlite, snapshot);
            Inject("order");
            if (isExisting)
            {
                await ExecuteAsync(sqlite, """
                    UPDATE orders SET source_type=$source,status=$status,created_at_utc=$created,updated_at_utc=$updated,
                    closed_at_utc=$closed,cancelled_at_utc=$cancelled,fulfilment_mode=$fulfilment,planned_fulfilment_date=$date,
                    planned_fulfilment_time=$time,advance_order_marker=$advance,telephone=$telephone,delivery_address=$address,
                    comment=$comment,total_ttc_cents=$total,manual_total_override_active=$manual,pickup_discount_applied=$discount,
                    pickup_discount_rate=$rate,delivery_fee_ttc_cents=$fee,card_payment_ttc_cents=$card,cash_payment_ttc_cents=$cash,
                    order_reference=$reference WHERE order_id=$id;
                    """, token, Parameters(snapshot, reference));
                Inject("order-after-parent");
                await ExecuteAsync(sqlite, "DELETE FROM order_items WHERE order_id=$id;", token, ("$id", snapshot.Id.ToString()));
                await ExecuteAsync(sqlite, "DELETE FROM order_tax_breakdown WHERE order_id=$id;", token, ("$id", snapshot.Id.ToString()));
            }
            else
            {
                await ExecuteAsync(sqlite, """
                    INSERT INTO orders(order_id,source_type,status,created_at_utc,updated_at_utc,closed_at_utc,cancelled_at_utc,
                    fulfilment_mode,planned_fulfilment_date,planned_fulfilment_time,advance_order_marker,telephone,delivery_address,
                    comment,total_ttc_cents,manual_total_override_active,pickup_discount_applied,pickup_discount_rate,
                    delivery_fee_ttc_cents,order_reference,card_payment_ttc_cents,cash_payment_ttc_cents)
                    VALUES ($id,$source,$status,$created,$updated,$closed,$cancelled,$fulfilment,$date,$time,$advance,$telephone,
                    $address,$comment,$total,$manual,$discount,$rate,$fee,$reference,$card,$cash);
                    """, token, Parameters(snapshot, reference));
            }

            await WriteChildrenAsync(sqlite, snapshot, token);
            for (var paymentIndex = 0; paymentIndex < adjustments.Count; paymentIndex++)
            {
                var adjustment = adjustments[paymentIndex];
                Inject("payment");
                await ExecuteAsync(sqlite, """
                    INSERT INTO payment_adjustments(payment_adjustment_id,order_id,bucket,delta_cents,effective_business_date,effective_at,recorded_at)
                    VALUES ($id,$order,$bucket,$delta,$effectiveDate,$effectiveAt,$recordedAt);
                    """, token,
                    ("$id", adjustment.Id.ToString()), ("$order", adjustment.OrderId.ToString()), ("$bucket", BucketName(adjustment.Bucket)),
                    ("$delta", adjustment.Delta.Cents), ("$effectiveDate", adjustment.EffectiveBusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    ("$effectiveAt", Format(adjustment.EffectiveAt)), ("$recordedAt", Format(adjustment.RecordedAt)));
                Inject($"payment-after-{paymentIndex + 1}");
            }
        }, cancellationToken);
    }

    public async Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty) return null;
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT * FROM orders WHERE order_id=$id;"; command.Parameters.AddWithValue("$id", orderId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var snapshot = new OrderSnapshot(
            ParseGuid(reader.GetString(reader.GetOrdinal("order_id"))), ParseSource(reader.GetString(reader.GetOrdinal("source_type"))), ParseStatus(reader.GetString(reader.GetOrdinal("status"))),
            ParseDateTime(reader.GetString(reader.GetOrdinal("created_at_utc"))), ParseDateTime(reader.GetString(reader.GetOrdinal("updated_at_utc"))),
            ParseNullableDateTime(reader, reader.GetOrdinal("closed_at_utc")), ParseNullableDateTime(reader, reader.GetOrdinal("cancelled_at_utc")),
            ParseFulfilment(reader.GetString(reader.GetOrdinal("fulfilment_mode"))), DateOnly.ParseExact(reader.GetString(reader.GetOrdinal("planned_fulfilment_date")), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            ReadNullableTime(reader, reader.GetOrdinal("planned_fulfilment_time")), reader.GetInt64(reader.GetOrdinal("advance_order_marker")) == 1,
            ReadNullableString(reader, reader.GetOrdinal("telephone")), ReadNullableString(reader, reader.GetOrdinal("delivery_address")), ReadNullableString(reader, reader.GetOrdinal("comment")),
            Money.FromCents(reader.GetInt64(reader.GetOrdinal("total_ttc_cents"))), reader.GetInt64(reader.GetOrdinal("manual_total_override_active")) == 1,
            reader.GetInt64(reader.GetOrdinal("pickup_discount_applied")) == 1, ReadNullableDecimal(reader, reader.GetOrdinal("pickup_discount_rate")),
            Money.FromCents(reader.GetInt64(reader.GetOrdinal("delivery_fee_ttc_cents"))), [], [])
        {
            Reference = ReadOptionalColumn(reader, "order_reference") ?? string.Empty,
            CardPaymentTtc = Money.FromCents(ReadOptionalInt64(reader, "card_payment_ttc_cents") ?? 0),
            CashPaymentTtc = Money.FromCents(ReadOptionalInt64(reader, "cash_payment_ttc_cents") ?? 0)
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

    public async Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        var hasM05 = await HasColumnAsync(connection, "orders", "order_reference", cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = hasM05
            ? "SELECT order_id,order_reference,planned_fulfilment_date,planned_fulfilment_time,fulfilment_mode,status,total_ttc_cents,advance_order_marker,telephone,card_payment_ttc_cents,cash_payment_ttc_cents FROM orders WHERE planned_fulfilment_date=$date ORDER BY planned_fulfilment_time IS NULL,planned_fulfilment_time,order_id;"
            : "SELECT order_id,planned_fulfilment_date,planned_fulfilment_time,fulfilment_mode,status,total_ttc_cents,telephone FROM orders WHERE planned_fulfilment_date=$date ORDER BY planned_fulfilment_time IS NULL,planned_fulfilment_time,order_id;";
        command.Parameters.AddWithValue("$date", plannedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var result = new List<OrderBrowserRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new OrderBrowserRow(ParseGuid(reader.GetString(0)), DateOnly.ParseExact(reader.GetString(hasM05 ? 2 : 1), "yyyy-MM-dd", CultureInfo.InvariantCulture), ReadNullableTime(reader, hasM05 ? 3 : 2), ParseFulfilment(reader.GetString(hasM05 ? 4 : 3)), ParseStatus(reader.GetString(hasM05 ? 5 : 4)), Money.FromCents(reader.GetInt64(hasM05 ? 6 : 5)), ReadNullableString(reader, hasM05 ? 8 : 6))
            {
                Reference = hasM05 ? reader.GetString(1) : string.Empty,
                AdvanceOrderMarker = hasM05 && reader.GetInt64(7) == 1,
                CardPaymentTtc = hasM05 ? Money.FromCents(reader.GetInt64(9)) : Money.Zero,
                CashPaymentTtc = hasM05 ? Money.FromCents(reader.GetInt64(10)) : Money.Zero
            };
            result.Add(row);
        }
        return result;
    }

    public async Task<IReadOnlyList<OrderBrowserRow>> SearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        var trimmedQuery = (query ?? string.Empty).Trim();
        var normalizedTelephoneQuery = TelephoneNormalization.Normalize(trimmedQuery) ?? trimmedQuery;
        await using var command = connection.CreateCommand(); command.CommandText = """
            SELECT order_id,order_reference,planned_fulfilment_date,planned_fulfilment_time,fulfilment_mode,status,total_ttc_cents,advance_order_marker,telephone,card_payment_ttc_cents,cash_payment_ttc_cents
            FROM orders WHERE $query='' OR order_reference=$query OR telephone LIKE '%' || $query || '%' OR telephone LIKE '%' || $telephoneQuery || '%' OR comment LIKE '%' || $query || '%'
            ORDER BY planned_fulfilment_date,planned_fulfilment_time IS NULL,planned_fulfilment_time,order_id;
            """; command.Parameters.AddWithValue("$query", trimmedQuery); command.Parameters.AddWithValue("$telephoneQuery", normalizedTelephoneQuery);
        var result = new List<OrderBrowserRow>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(ParseGuid(reader.GetString(0)), DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture), ReadNullableTime(reader, 3), ParseFulfilment(reader.GetString(4)), ParseStatus(reader.GetString(5)), Money.FromCents(reader.GetInt64(6)), ReadNullableString(reader, 8)) { Reference = reader.GetString(1), AdvanceOrderMarker = reader.GetInt64(7) == 1, CardPaymentTtc = Money.FromCents(reader.GetInt64(9)), CashPaymentTtc = Money.FromCents(reader.GetInt64(10)) });
        return result;
    }

    public async Task<OrderOperationalSummary> GetOperationalSummaryAsync(DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        var date = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var turnover = await ScalarLongAsync(connection, "SELECT COALESCE(SUM(total_ttc_cents),0) FROM orders WHERE source_type='POS' AND status <> 'CANCELLED' AND planned_fulfilment_date=$date;", date, cancellationToken);
        var received = await ReadReceivedAsync(connection, date, cancellationToken);
        var future = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM orders WHERE status <> 'CANCELLED' AND planned_fulfilment_date > $date;", date, cancellationToken);
        var due = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM orders WHERE status <> 'CANCELLED' AND planned_fulfilment_date=$date AND advance_order_marker=1;", date, cancellationToken);
        var overdue = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM orders WHERE status <> 'CANCELLED' AND planned_fulfilment_date < $date AND NOT (status='CLOSED' AND card_payment_ttc_cents + cash_payment_ttc_cents = total_ttc_cents);", date, cancellationToken);
        return new(Money.FromCents(turnover), Money.FromCents(received.Total), Money.FromCents(received.Card), Money.FromCents(received.Cash), checked((int)future), checked((int)due), checked((int)overdue));
    }

    private async Task SaveLegacyAsync(SqliteApplicationTransaction sqlite, OrderSnapshot snapshot, CancellationToken token)
    {
        Inject("order");
        await ExecuteAsync(sqlite, """
            INSERT INTO orders(order_id,source_type,status,created_at_utc,updated_at_utc,closed_at_utc,cancelled_at_utc,fulfilment_mode,planned_fulfilment_date,planned_fulfilment_time,advance_order_marker,telephone,delivery_address,comment,total_ttc_cents,manual_total_override_active,pickup_discount_applied,pickup_discount_rate,delivery_fee_ttc_cents)
            VALUES ($id,$source,$status,$created,$updated,$closed,$cancelled,$fulfilment,$date,$time,$advance,$telephone,$address,$comment,$total,$manual,$discount,$rate,$fee);
            """, token, Parameters(snapshot, null));
        await WriteChildrenAsync(sqlite, snapshot, token);
    }

    private async Task WriteChildrenAsync(SqliteApplicationTransaction sqlite, OrderSnapshot snapshot, CancellationToken token)
    {
        foreach (var item in snapshot.Items.OrderBy(item => item.Position))
        {
            Inject("item");
            await ExecuteAsync(sqlite, "INSERT INTO order_items(order_item_id,order_id,line_position,source_product_id,product_code_snapshot,product_name_snapshot,category_name_snapshot,product_base_price_ttc_cents,product_vat_rate,product_discount_eligible_snapshot,quantity,extended_base_ttc_cents,calculated_line_total_ttc_cents) VALUES ($id,$order,$position,$product,$code,$name,$category,$base,$vat,$eligible,$quantity,$extended,$total);", token,
                ("$id", item.Id.ToString()), ("$order", snapshot.Id.ToString()), ("$position", item.Position), ("$product", item.SourceProductId?.ToString()), ("$code", item.ProductCode), ("$name", item.ProductName), ("$category", item.CategoryName), ("$base", item.ProductBasePriceTtc.Cents), ("$vat", FormatDecimal(item.ProductVatRate)), ("$eligible", item.ProductDiscountEligible ? 1 : 0), ("$quantity", item.Quantity), ("$extended", item.ExtendedBaseTtc.Cents), ("$total", item.CalculatedLineTotalTtc.Cents));
            foreach (var adjustment in item.Adjustments.OrderBy(adjustment => adjustment.DisplayOrder))
            {
                Inject("adjustment");
                await ExecuteAsync(sqlite, "INSERT INTO order_item_adjustments(order_item_adjustment_id,order_item_id,display_order,adjustment_kind,source_option_id,group_name_snapshot,label_snapshot,adjustment_ttc_per_unit_cents,vat_rate) VALUES ($id,$item,$order,$kind,$option,$groupName,$label,$amount,$vat);", token,
                    ("$id", adjustment.Id.ToString()), ("$item", item.Id.ToString()), ("$order", adjustment.DisplayOrder), ("$kind", AdjustmentName(adjustment.Kind)), ("$option", adjustment.SourceOptionId?.ToString()), ("$groupName", adjustment.GroupName), ("$label", adjustment.Label), ("$amount", adjustment.AdjustmentTtcPerUnit.Cents), ("$vat", FormatDecimalNullable(adjustment.VatRate)));
            }
        }
        foreach (var tax in snapshot.TaxBreakdown.OrderBy(tax => tax.VatRate))
        {
            Inject("tax");
            var id = tax.Id == Guid.Empty ? idGenerator?.NewId() ?? throw new InvalidOperationException("Tax snapshot identities must be allocated before persistence.") : tax.Id;
            await ExecuteAsync(sqlite, "INSERT INTO order_tax_breakdown(order_tax_breakdown_id,order_id,vat_rate,taxable_ttc_cents,included_vat_ttc_cents) VALUES ($id,$order,$vat,$taxable,$included);", token, ("$id", id.ToString()), ("$order", snapshot.Id.ToString()), ("$vat", FormatDecimal(tax.VatRate)), ("$taxable", tax.TaxableTtc.Cents), ("$included", tax.IncludedVatTtc.Cents));
        }
    }

    private async Task<string> AllocateReferenceAsync(SqliteApplicationTransaction sqlite, OrderSnapshot snapshot)
    {
        var zone = clock?.BusinessTimeZone ?? TimeZoneInfo.Utc;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(snapshot.CreatedAt, zone).Date);
        var dateText = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await ExecuteAsync(sqlite, "INSERT OR IGNORE INTO order_reference_sequences(business_date,next_sequence) VALUES($date,1);", CancellationToken.None, ("$date", dateText));
        await ExecuteAsync(sqlite, "UPDATE order_reference_sequences SET next_sequence=next_sequence+1 WHERE business_date=$date;", CancellationToken.None, ("$date", dateText));
        var sequence = await ExecuteScalarAsync(sqlite, "SELECT next_sequence-1 FROM order_reference_sequences WHERE business_date=$date;", ("$date", dateText));
        return OrderReference.Format(date, checked(Convert.ToInt32(sequence, CultureInfo.InvariantCulture)));
    }

    private static async Task<bool> HasColumnAsync(SqliteConnection connection, string table, string column, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.CommandText = $"PRAGMA table_info({table});"; await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static async Task<string?> ReadReferenceAsync(SqliteApplicationTransaction sqlite, Guid orderId, CancellationToken token)
    {
        await using var command = sqlite.Connection.CreateCommand(); command.Transaction = sqlite.Transaction; command.CommandText = "SELECT order_reference FROM orders WHERE order_id=$id;"; command.Parameters.AddWithValue("$id", orderId.ToString());
        var value = await command.ExecuteScalarAsync(token); return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static async Task<(long Total, long Card, long Cash)> ReadReceivedAsync(SqliteConnection connection, string date, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT COALESCE(SUM(pa.delta_cents),0),COALESCE(SUM(CASE WHEN pa.bucket='CB' THEN pa.delta_cents ELSE 0 END),0),COALESCE(SUM(CASE WHEN pa.bucket='ESPECE' THEN pa.delta_cents ELSE 0 END),0) FROM payment_adjustments pa JOIN orders o ON o.order_id=pa.order_id WHERE pa.effective_business_date=$date AND o.source_type='POS' AND o.status <> 'CANCELLED';"; command.Parameters.AddWithValue("$date", date); await using var reader = await command.ExecuteReaderAsync(token); return await reader.ReadAsync(token) ? (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)) : (0, 0, 0);
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql, string date, CancellationToken token) { await using var command = connection.CreateCommand(); command.CommandText = sql; command.Parameters.AddWithValue("$date", date); return Convert.ToInt64(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture); }
    private static async Task<IReadOnlyList<OrderLineAdjustmentSnapshot>> ReadAdjustmentsAsync(SqliteConnection connection, Guid itemId, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT order_item_adjustment_id,display_order,adjustment_kind,source_option_id,group_name_snapshot,label_snapshot,adjustment_ttc_per_unit_cents,vat_rate FROM order_item_adjustments WHERE order_item_id=$item ORDER BY display_order,order_item_adjustment_id;"; command.Parameters.AddWithValue("$item", itemId.ToString());
        var result = new List<OrderLineAdjustmentSnapshot>(); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(new(ParseGuid(reader.GetString(0)), reader.GetInt32(1), ParseAdjustmentKind(reader.GetString(2)), reader.IsDBNull(3) ? null : ParseGuid(reader.GetString(3)), ReadNullableString(reader, 4), reader.GetString(5), Money.FromCents(reader.GetInt64(6)), reader.IsDBNull(7) ? null : ParseDecimal(reader.GetString(7))));
        return result;
    }
    private void Inject(string stage) { if (writeFailureInjector?.Invoke(stage) is { } exception) throw exception; }
    private static async Task<int> ExecuteAsync(SqliteApplicationTransaction sqlite, string sql, CancellationToken token, params (string Name, object? Value)[] parameters) { await using var command = sqlite.Connection.CreateCommand(); command.Transaction = sqlite.Transaction; command.CommandText = sql; foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value); return await command.ExecuteNonQueryAsync(token); }
    private static async Task<object?> ExecuteScalarAsync(SqliteApplicationTransaction sqlite, string sql, params (string Name, object? Value)[] parameters) { await using var command = sqlite.Connection.CreateCommand(); command.Transaction = sqlite.Transaction; command.CommandText = sql; foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value); return await command.ExecuteScalarAsync(); }
    private static SqliteApplicationTransaction RequireSqlite(IApplicationTransaction transaction) => transaction as SqliteApplicationTransaction ?? throw new InvalidOperationException("The configured transaction is not SQLite-backed.");
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string? FormatNullable(DateTimeOffset? value) => value is null ? null : Format(value.Value);
    private static string FormatDecimal(decimal value) => value.ToString("0.#############################", CultureInfo.InvariantCulture);
    private static string? FormatDecimalNullable(decimal? value) => value is null ? null : FormatDecimal(value.Value);
    private static decimal ParseDecimal(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDateTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static DateTimeOffset? ParseNullableDateTime(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : ParseDateTime(reader.GetString(index));
    private static TimeOnly? ReadNullableTime(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : TimeOnly.ParseExact(reader.GetString(index), "HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
    private static string? ReadNullableString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static decimal? ReadNullableDecimal(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : ParseDecimal(reader.GetString(index));
    private static string? ReadOptionalColumn(SqliteDataReader reader, string name) { try { var index = reader.GetOrdinal(name); return reader.IsDBNull(index) ? null : reader.GetString(index); } catch (ArgumentException) { return null; } }
    private static long? ReadOptionalInt64(SqliteDataReader reader, string name) { try { var index = reader.GetOrdinal(name); return reader.IsDBNull(index) ? null : reader.GetInt64(index); } catch (ArgumentException) { return null; } }
    private static Guid ParseGuid(string value) => Guid.TryParse(value, out var id) ? id : throw new InvalidDataException("The database contains an invalid opaque identifier.");
    private static (string Name, object? Value)[] Parameters(OrderSnapshot snapshot, string? reference) =>
    [
        ("$id", snapshot.Id.ToString()), ("$source", SourceName(snapshot.SourceType)), ("$status", StatusName(snapshot.Status)), ("$created", Format(snapshot.CreatedAt)), ("$updated", Format(snapshot.UpdatedAt)), ("$closed", FormatNullable(snapshot.ClosedAt)), ("$cancelled", FormatNullable(snapshot.CancelledAt)), ("$fulfilment", FulfilmentName(snapshot.Fulfilment)), ("$date", snapshot.PlannedFulfilmentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$time", snapshot.PlannedFulfilmentTime?.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)), ("$advance", snapshot.AdvanceOrderMarker ? 1 : 0), ("$telephone", snapshot.Telephone), ("$address", snapshot.DeliveryAddress), ("$comment", snapshot.Comment), ("$total", snapshot.TotalTtc.Cents), ("$manual", snapshot.ManualTotalOverrideActive ? 1 : 0), ("$discount", snapshot.PickupDiscountApplied ? 1 : 0), ("$rate", FormatDecimalNullable(snapshot.PickupDiscountRate)), ("$fee", snapshot.DeliveryFeeTtc.Cents), ("$reference", reference), ("$card", snapshot.CardPaymentTtc.Cents), ("$cash", snapshot.CashPaymentTtc.Cents)
    ];
    private static string SourceName(OrderSourceType value) => value switch { OrderSourceType.Pos => "POS", OrderSourceType.HiboutikPaste => "HIBOUTIK_PASTE", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string StatusName(OrderStatus value) => value switch { OrderStatus.Open => "OPEN", OrderStatus.Closed => "CLOSED", OrderStatus.Cancelled => "CANCELLED", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string FulfilmentName(FulfilmentMode value) => value switch { FulfilmentMode.Retrait => "RETRAIT", FulfilmentMode.Livraison => "LIVRAISON", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string AdjustmentName(OrderAdjustmentKind value) => value switch { OrderAdjustmentKind.PredefinedOption => "PREDEFINED_OPTION", OrderAdjustmentKind.CustomAdjustment => "CUSTOM_ADJUSTMENT", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string BucketName(PaymentBucket value) => value switch { PaymentBucket.Card => "CB", PaymentBucket.Cash => "ESPECE", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static OrderSourceType ParseSource(string value) => value switch { "POS" => OrderSourceType.Pos, "HIBOUTIK_PASTE" => OrderSourceType.HiboutikPaste, _ => throw new InvalidDataException("The database contains an invalid order source.") };
    private static OrderStatus ParseStatus(string value) => value switch { "OPEN" => OrderStatus.Open, "CLOSED" => OrderStatus.Closed, "CANCELLED" => OrderStatus.Cancelled, _ => throw new InvalidDataException("The database contains an invalid order status.") };
    private static FulfilmentMode ParseFulfilment(string value) => value switch { "RETRAIT" => FulfilmentMode.Retrait, "LIVRAISON" => FulfilmentMode.Livraison, _ => throw new InvalidDataException("The database contains an invalid fulfilment mode.") };
    private static OrderAdjustmentKind ParseAdjustmentKind(string value) => value switch { "PREDEFINED_OPTION" => OrderAdjustmentKind.PredefinedOption, "CUSTOM_ADJUSTMENT" => OrderAdjustmentKind.CustomAdjustment, _ => throw new InvalidDataException("The database contains an invalid adjustment kind.") };
}

/// <summary>Production M04/M05 boundary; actual Windows printing is intentionally deferred.</summary>
public sealed class NoOpOrderPrintDispatcher : IOrderPrintDispatcher
{
    public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(committedOrder); cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask;
    }
}
