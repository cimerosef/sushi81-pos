using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Foundation.Transactions;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.Settings;

public sealed class SqliteBusinessSettingsStore(
    SqliteConnectionFactory connectionFactory,
    ITransactionRunner transactionRunner,
    IBusinessClock clock) : IBusinessSettingsStore
{
    private readonly SqliteConnectionFactory connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    private readonly ITransactionRunner transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    private readonly IBusinessClock clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        var hasReceiptIdentity = await HasReceiptIdentityColumnsAsync(connection, null, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = hasReceiptIdentity
            ? "SELECT pickup_discount_rate,pickup_discount_min_total_ttc_cents,delivery_min_merchandise_total_ttc_cents,delivery_fee_enabled,delivery_fee_amount_ttc_cents,updated_at_utc,receipt_business_name,receipt_address_line_1,receipt_address_line_2,receipt_siret,receipt_vat_number,receipt_activity_code FROM business_settings WHERE singleton_id=1;"
            : "SELECT pickup_discount_rate,pickup_discount_min_total_ttc_cents,delivery_min_merchandise_total_ttc_cents,delivery_fee_enabled,delivery_fee_amount_ttc_cents,updated_at_utc FROM business_settings WHERE singleton_id=1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidDataException("The singleton business settings row is missing.");
        var settings = new BusinessSettings(ParseDecimal(reader.GetString(0)), Money.FromCents(reader.GetInt64(1)), Money.FromCents(reader.GetInt64(2)), reader.GetInt64(3) == 1, Money.FromCents(reader.GetInt64(4)), DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
        {
            ReceiptIdentity = hasReceiptIdentity
                ? new ReceiptIdentity(reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11))
                : ReceiptIdentity.Default
        };
        return settings;
    }

    public async Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default)
    {
        var error = CatalogueValidation.ValidateSettings(settings);
        if (error is not null) return OperationResult.Failure(new ValidationIssue("settings", error, ValidationCodes.SettingsRange));
        try
        {
            var count = await transactionRunner.ExecuteAsync(async (transaction, token) =>
            {
                var sqlite = transaction as SqliteApplicationTransaction ?? throw new InvalidOperationException("The configured transaction is not SQLite-backed.");
                var hasReceiptIdentity = await HasReceiptIdentityColumnsAsync(sqlite.Connection, sqlite.Transaction, token);
                await using var command = sqlite.Connection.CreateCommand();
                command.Transaction = sqlite.Transaction;
                command.CommandText = hasReceiptIdentity
                    ? "UPDATE business_settings SET pickup_discount_rate=$rate,pickup_discount_min_total_ttc_cents=$pickup,delivery_min_merchandise_total_ttc_cents=$delivery,delivery_fee_enabled=$enabled,delivery_fee_amount_ttc_cents=$fee,updated_at_utc=$updated,receipt_business_name=$businessName,receipt_address_line_1=$address1,receipt_address_line_2=$address2,receipt_siret=$siret,receipt_vat_number=$vat,receipt_activity_code=$activity WHERE singleton_id=1;"
                    : "UPDATE business_settings SET pickup_discount_rate=$rate,pickup_discount_min_total_ttc_cents=$pickup,delivery_min_merchandise_total_ttc_cents=$delivery,delivery_fee_enabled=$enabled,delivery_fee_amount_ttc_cents=$fee,updated_at_utc=$updated WHERE singleton_id=1;";
                command.Parameters.AddWithValue("$rate", FormatDecimal(settings.PickupDiscountRate));
                command.Parameters.AddWithValue("$pickup", settings.PickupDiscountMinTotalTtc.Cents);
                command.Parameters.AddWithValue("$delivery", settings.DeliveryMinMerchandiseTotalTtc.Cents);
                command.Parameters.AddWithValue("$enabled", settings.DeliveryFeeEnabled ? 1 : 0);
                command.Parameters.AddWithValue("$fee", settings.DeliveryFeeAmountTtc.Cents);
                command.Parameters.AddWithValue("$updated", Format(clock.UtcNow));
                if (hasReceiptIdentity)
                {
                    command.Parameters.AddWithValue("$businessName", settings.ReceiptIdentity.BusinessName);
                    command.Parameters.AddWithValue("$address1", settings.ReceiptIdentity.AddressLine1);
                    command.Parameters.AddWithValue("$address2", settings.ReceiptIdentity.AddressLine2);
                    command.Parameters.AddWithValue("$siret", settings.ReceiptIdentity.Siret);
                    command.Parameters.AddWithValue("$vat", settings.ReceiptIdentity.VatNumber);
                    command.Parameters.AddWithValue("$activity", settings.ReceiptIdentity.ActivityCode);
                }
                return await command.ExecuteNonQueryAsync(token);
            }, cancellationToken);
            return count == 0 ? OperationResult.Failure(new ValidationIssue("settings", "Business settings are unavailable.", ValidationCodes.Generic)) : OperationResult.Success();
        }
        catch (SqliteException) { return OperationResult.Failure(new ValidationIssue("settings", "Business settings could not be saved.", ValidationCodes.Conflict)); }
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static async Task<bool> HasReceiptIdentityColumnsAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('business_settings') WHERE name='receipt_business_name';";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }
    private static string FormatDecimal(decimal value) => value.ToString("0.#############################", CultureInfo.InvariantCulture);
    private static decimal ParseDecimal(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}
