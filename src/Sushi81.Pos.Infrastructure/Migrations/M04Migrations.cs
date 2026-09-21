namespace Sushi81.Pos.Infrastructure.Migrations;

public static class M04Migrations
{
    public static IReadOnlyList<SqliteMigration> All { get; } =
    [
        new SqliteMigration(
            3,
            "create-orders-and-sale-snapshots",
            """
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
                delivery_fee_ttc_cents INTEGER NOT NULL CHECK(delivery_fee_ttc_cents >= 0)
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
            CREATE INDEX ix_order_items_order ON order_items(order_id, line_position);
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
            CREATE INDEX ix_order_tax_order ON order_tax_breakdown(order_id);
            """),
        new SqliteMigration(
            4,
            "add-category-short-codes-and-planned-order-index",
            """
            ALTER TABLE categories ADD COLUMN short_code TEXT NULL;
            ALTER TABLE categories ADD COLUMN normalized_short_code TEXT NULL;
            CREATE UNIQUE INDEX ux_categories_normalized_short_code
                ON categories(normalized_short_code)
                WHERE normalized_short_code IS NOT NULL;
            CREATE INDEX ix_orders_planned_date_time
                ON orders(planned_fulfilment_date, planned_fulfilment_time, order_id);
            """)
    ];
}

public static class ProductionMigrations
{
    public static IReadOnlyList<SqliteMigration> All { get; } = M01Migrations.All.Concat(M03Migrations.All).Concat(M04Migrations.All).Concat(M05Migrations.All).Concat(M08Migrations.All).Concat(M09Migrations.All).Concat(M11Migrations.All).Concat(M12Migrations.All).ToArray();
}
