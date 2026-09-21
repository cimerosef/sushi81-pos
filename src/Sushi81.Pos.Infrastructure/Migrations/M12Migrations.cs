namespace Sushi81.Pos.Infrastructure.Migrations;

public static class M12Migrations
{
    public static IReadOnlyList<SqliteMigration> All { get; } =
    [
        new SqliteMigration(
            9,
            "create-annual-archive-completion-ledger",
            """
            CREATE TABLE annual_archive_completions (
                archive_year INTEGER NOT NULL PRIMARY KEY,
                archive_format_version TEXT NOT NULL,
                archive_schema_version TEXT NOT NULL,
                archive_file_name TEXT NOT NULL,
                archive_order_count INTEGER NOT NULL CHECK(archive_order_count >= 0),
                archive_sha256 TEXT NOT NULL,
                completed_at_utc TEXT NOT NULL
            );
            """)
    ];
}
