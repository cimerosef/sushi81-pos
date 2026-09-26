namespace Sushi81.Pos.Infrastructure.Migrations;

public static class M13Migrations
{
    public static IReadOnlyList<SqliteMigration> All { get; } =
    [
        new SqliteMigration(
            10,
            "create-annual-archive-order-proof-ledger",
            """
            CREATE TABLE annual_archive_order_proofs (
                order_id TEXT NOT NULL PRIMARY KEY CHECK(length(order_id) = 36),
                archive_year INTEGER NOT NULL CHECK(archive_year BETWEEN 1 AND 9999),
                archive_sha256 TEXT NOT NULL CHECK(length(archive_sha256) = 64 AND archive_sha256 NOT GLOB '*[^0-9A-F]*'),
                archive_completed_at_utc TEXT NOT NULL,
                FOREIGN KEY (archive_year) REFERENCES annual_archive_completions(archive_year) ON DELETE RESTRICT
            );
            CREATE INDEX ix_annual_archive_order_proofs_year
                ON annual_archive_order_proofs(archive_year, order_id);
            """),
        new SqliteMigration(
            11,
            "add-hot-read-ordering-indexes",
            """
            CREATE INDEX ix_products_code_nocase_product_id
                ON products(code COLLATE NOCASE, product_id);
            CREATE INDEX ix_orders_planned_date_nulls_last
                ON orders(
                    planned_fulfilment_date,
                    (planned_fulfilment_time IS NULL),
                    planned_fulfilment_time,
                    order_id);
            """)
    ];
}
