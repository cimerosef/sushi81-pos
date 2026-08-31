namespace Sushi81.Pos.Infrastructure.Migrations;

public static class M03Migrations
{
    public static IReadOnlyList<SqliteMigration> All { get; } =
    [
        new SqliteMigration(
            2,
            "create-catalogue-and-business-settings",
            """
            CREATE TABLE categories (
                category_id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                normalized_name TEXT NOT NULL UNIQUE,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            CREATE TABLE products (
                product_id TEXT NOT NULL PRIMARY KEY,
                code TEXT NOT NULL,
                normalized_code TEXT NOT NULL UNIQUE,
                name TEXT NOT NULL,
                category_id TEXT NOT NULL REFERENCES categories(category_id) ON DELETE RESTRICT,
                price_ttc_cents INTEGER NOT NULL CHECK(price_ttc_cents >= 0),
                vat_rate TEXT NOT NULL,
                is_active INTEGER NOT NULL CHECK(is_active IN (0,1)),
                discount_eligible INTEGER NOT NULL CHECK(discount_eligible IN (0,1)),
                options_enabled INTEGER NOT NULL CHECK(options_enabled IN (0,1)),
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            CREATE INDEX ix_products_category ON products(category_id);
            CREATE INDEX ix_products_active ON products(is_active);
            CREATE TABLE option_groups (
                option_group_id TEXT NOT NULL PRIMARY KEY,
                product_id TEXT NOT NULL REFERENCES products(product_id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                selection_mode TEXT NOT NULL CHECK(selection_mode IN ('SINGLE','MULTI')),
                is_required INTEGER NOT NULL CHECK(is_required IN (0,1)),
                min_selections INTEGER NULL CHECK(min_selections IS NULL OR min_selections >= 0),
                max_selections INTEGER NULL CHECK(max_selections IS NULL OR max_selections >= 0),
                display_order INTEGER NOT NULL CHECK(display_order >= 0),
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                UNIQUE(product_id, display_order),
                CHECK((selection_mode = 'SINGLE' AND min_selections IS NULL AND max_selections IS NULL)
                   OR (selection_mode = 'MULTI' AND min_selections IS NOT NULL AND max_selections IS NOT NULL AND max_selections >= 1 AND min_selections <= max_selections))
            );
            CREATE TABLE options (
                option_id TEXT NOT NULL PRIMARY KEY,
                option_group_id TEXT NOT NULL REFERENCES option_groups(option_group_id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                price_adjustment_ttc_cents INTEGER NOT NULL,
                is_active INTEGER NOT NULL CHECK(is_active IN (0,1)),
                display_order INTEGER NOT NULL CHECK(display_order >= 0),
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                UNIQUE(option_group_id, display_order)
            );
            CREATE TABLE business_settings (
                singleton_id INTEGER NOT NULL PRIMARY KEY CHECK(singleton_id = 1),
                pickup_discount_rate TEXT NOT NULL,
                pickup_discount_min_total_ttc_cents INTEGER NOT NULL CHECK(pickup_discount_min_total_ttc_cents >= 0),
                delivery_min_merchandise_total_ttc_cents INTEGER NOT NULL CHECK(delivery_min_merchandise_total_ttc_cents >= 0),
                delivery_fee_enabled INTEGER NOT NULL CHECK(delivery_fee_enabled IN (0,1)),
                delivery_fee_amount_ttc_cents INTEGER NOT NULL CHECK(delivery_fee_amount_ttc_cents >= 0),
                updated_at_utc TEXT NOT NULL
            );
            INSERT INTO business_settings(singleton_id, pickup_discount_rate, pickup_discount_min_total_ttc_cents, delivery_min_merchandise_total_ttc_cents, delivery_fee_enabled, delivery_fee_amount_ttc_cents, updated_at_utc)
            VALUES (1, '0.10', 1500, 3000, 0, 0, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
            """)
    ];
}
