namespace Sushi81.Pos.Infrastructure.Migrations;

public static class M08Migrations
{
    public static IReadOnlyList<SqliteMigration> All { get; } =
    [
        new SqliteMigration(
            6,
            "add-authoritative-customer-receipt-identity",
            """
            ALTER TABLE business_settings ADD COLUMN receipt_business_name TEXT NOT NULL DEFAULT 'Sushi 81';
            ALTER TABLE business_settings ADD COLUMN receipt_address_line_1 TEXT NOT NULL DEFAULT '12 Rue Gaston Darley';
            ALTER TABLE business_settings ADD COLUMN receipt_address_line_2 TEXT NOT NULL DEFAULT '77140 Nemours - FRA';
            ALTER TABLE business_settings ADD COLUMN receipt_siret TEXT NOT NULL DEFAULT '90805211100014';
            ALTER TABLE business_settings ADD COLUMN receipt_vat_number TEXT NOT NULL DEFAULT 'FR03908052111';
            ALTER TABLE business_settings ADD COLUMN receipt_activity_code TEXT NOT NULL DEFAULT '5610C';
            """)
    ];
}
