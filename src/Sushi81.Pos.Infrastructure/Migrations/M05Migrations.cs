using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Infrastructure.Migrations;

public static class M05Migrations
{
    public static SqliteMigration Migration { get; } = new(
        5,
        "add-lifecycle-payments-references-and-live-search",
        """
        ALTER TABLE orders ADD COLUMN order_reference TEXT NULL;
        ALTER TABLE orders ADD COLUMN card_payment_ttc_cents INTEGER NOT NULL DEFAULT 0 CHECK(card_payment_ttc_cents >= 0);
        ALTER TABLE orders ADD COLUMN cash_payment_ttc_cents INTEGER NOT NULL DEFAULT 0 CHECK(cash_payment_ttc_cents >= 0);

        CREATE TABLE order_reference_sequences (
            business_date TEXT NOT NULL PRIMARY KEY,
            next_sequence INTEGER NOT NULL CHECK(next_sequence > 0)
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
        CREATE INDEX ix_payment_adjustments_effective
            ON payment_adjustments(effective_business_date, order_id, bucket);
        CREATE INDEX ix_payment_adjustments_order
            ON payment_adjustments(order_id, recorded_at, payment_adjustment_id);
        CREATE UNIQUE INDEX ux_orders_reference
            ON orders(order_reference)
            WHERE order_reference IS NOT NULL;
        CREATE INDEX ix_orders_live_search
            ON orders(telephone, comment, status, order_id);
        CREATE INDEX ix_orders_lifecycle_date
            ON orders(status, planned_fulfilment_date, advance_order_marker, source_type);
        """,
        BackfillAsync);

    public static IReadOnlyList<SqliteMigration> All { get; } = [Migration];

    private static async Task BackfillAsync(SqliteConnection connection, SqliteTransaction transaction, IBusinessClock clock, CancellationToken cancellationToken)
    {
        var rows = new List<(Guid Id, DateTimeOffset CreatedAt)>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT order_id, created_at_utc FROM orders WHERE order_reference IS NULL OR TRIM(order_reference) = '';";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!Guid.TryParse(reader.GetString(0), out var id))
                    throw new InvalidDataException("The database contains an invalid order identifier during M05 reference backfill.");
                rows.Add((id, DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
            }
        }

        foreach (var group in rows
            .Select(row => (row.Id, row.CreatedAt, Date: DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(row.CreatedAt, clock.BusinessTimeZone).Date)))
            .GroupBy(row => row.Date)
            .OrderBy(group => group.Key))
        {
            var sequence = 1;
            foreach (var row in group.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id.ToString("D"), StringComparer.Ordinal))
            {
                await ExecuteAsync(connection, transaction,
                    "UPDATE orders SET order_reference=$reference WHERE order_id=$id AND (order_reference IS NULL OR TRIM(order_reference) = '');",
                    cancellationToken,
                    ("$reference", OrderReference.Format(group.Key, sequence)), ("$id", row.Id.ToString()));
                sequence++;
            }

            await ExecuteAsync(connection, transaction,
                "INSERT INTO order_reference_sequences(business_date,next_sequence) VALUES($date,$next) ON CONFLICT(business_date) DO UPDATE SET next_sequence=MAX(next_sequence, excluded.next_sequence);",
                cancellationToken,
                ("$date", group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$next", sequence));
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
