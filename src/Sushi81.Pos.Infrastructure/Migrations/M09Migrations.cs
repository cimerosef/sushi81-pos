namespace Sushi81.Pos.Infrastructure.Migrations;

public static class M09Migrations
{
    public static IReadOnlyList<SqliteMigration> All { get; } =
    [
        new SqliteMigration(
            7,
            "add-order-source-total-reference",
            "ALTER TABLE orders ADD COLUMN source_total_ttc_cents INTEGER NULL;")
    ];
}
