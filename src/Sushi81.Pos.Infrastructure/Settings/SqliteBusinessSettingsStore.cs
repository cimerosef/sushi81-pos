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
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pickup_discount_rate,pickup_discount_min_total_ttc_cents,delivery_min_merchandise_total_ttc_cents,delivery_fee_enabled,delivery_fee_amount_ttc_cents,updated_at_utc FROM business_settings WHERE singleton_id=1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidDataException("The singleton business settings row is missing.");
        return new(ParseDecimal(reader.GetString(0)), Money.FromCents(reader.GetInt64(1)), Money.FromCents(reader.GetInt64(2)), reader.GetInt64(3) == 1, Money.FromCents(reader.GetInt64(4)), DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
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
                await using var command = sqlite.Connection.CreateCommand();
                command.Transaction = sqlite.Transaction;
                command.CommandText = "UPDATE business_settings SET pickup_discount_rate=$rate,pickup_discount_min_total_ttc_cents=$pickup,delivery_min_merchandise_total_ttc_cents=$delivery,delivery_fee_enabled=$enabled,delivery_fee_amount_ttc_cents=$fee,updated_at_utc=$updated WHERE singleton_id=1;";
                command.Parameters.AddWithValue("$rate", FormatDecimal(settings.PickupDiscountRate));
                command.Parameters.AddWithValue("$pickup", settings.PickupDiscountMinTotalTtc.Cents);
                command.Parameters.AddWithValue("$delivery", settings.DeliveryMinMerchandiseTotalTtc.Cents);
                command.Parameters.AddWithValue("$enabled", settings.DeliveryFeeEnabled ? 1 : 0);
                command.Parameters.AddWithValue("$fee", settings.DeliveryFeeAmountTtc.Cents);
                command.Parameters.AddWithValue("$updated", Format(clock.UtcNow));
                return await command.ExecuteNonQueryAsync(token);
            }, cancellationToken);
            return count == 0 ? OperationResult.Failure(new ValidationIssue("settings", "Business settings are unavailable.", ValidationCodes.Generic)) : OperationResult.Success();
        }
        catch (SqliteException) { return OperationResult.Failure(new ValidationIssue("settings", "Business settings could not be saved.", ValidationCodes.Conflict)); }
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string FormatDecimal(decimal value) => value.ToString("0.#############################", CultureInfo.InvariantCulture);
    private static decimal ParseDecimal(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}
