namespace Sushi81.Pos.Infrastructure.Migrations;

public sealed record SqliteMigration(int Version, string Name, string Sql)
{
    public void Validate()
    {
        if (Version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Version), "Migration versions must be positive.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("Migration names are required.", nameof(Name));
        }

        if (string.IsNullOrWhiteSpace(Sql))
        {
            throw new ArgumentException("Migration SQL is required.", nameof(Sql));
        }
    }
}
