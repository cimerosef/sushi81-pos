namespace Sushi81.Pos.Infrastructure.Migrations;

public static class M01Migrations
{
    public static IReadOnlyList<SqliteMigration> All { get; } =
    [
        new SqliteMigration(
            1,
            "create-foundation-metadata",
            "CREATE TABLE foundation_metadata (key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL);")
    ];
}
