namespace Sushi81.Pos.Infrastructure.Migrations;

public static class M11Migrations
{
    public static IReadOnlyList<SqliteMigration> All { get; } =
    [
        new SqliteMigration(
            8,
            "create-gestion-export-ledger",
            """
            CREATE TABLE export_batches (
                batch_id TEXT NOT NULL PRIMARY KEY,
                schema_version TEXT NOT NULL,
                generated_at_utc TEXT NOT NULL,
                app_version TEXT NOT NULL,
                filter_start_date TEXT NULL,
                filter_end_date TEXT NULL,
                order_count INTEGER NOT NULL CHECK(order_count >= 0),
                order_line_count INTEGER NOT NULL CHECK(order_line_count >= 0),
                tax_breakdown_count INTEGER NOT NULL CHECK(tax_breakdown_count >= 0),
                status TEXT NOT NULL CHECK(status IN ('PREPARED','SUCCESS')),
                payload_json TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                completed_at_utc TEXT NULL
            );
            CREATE TABLE export_emissions (
                emission_id TEXT NOT NULL PRIMARY KEY,
                batch_id TEXT NOT NULL REFERENCES export_batches(batch_id) ON DELETE RESTRICT,
                order_id TEXT NOT NULL,
                action TEXT NOT NULL CHECK(action IN ('CREATE','UPDATE','CANCEL')),
                positive_snapshot_hash TEXT NOT NULL,
                positive_payload_json TEXT NOT NULL,
                fulfilment_date TEXT NOT NULL,
                settlement_date TEXT NOT NULL,
                emitted_at_utc TEXT NOT NULL,
                UNIQUE(batch_id, order_id)
            );
            CREATE INDEX ix_export_emissions_order_time
                ON export_emissions(order_id, emitted_at_utc DESC, emission_id DESC);
            CREATE INDEX ix_export_batches_status_time
                ON export_batches(status, generated_at_utc DESC, batch_id DESC);
            """)
    ];
}
