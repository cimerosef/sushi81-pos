namespace Sushi81.Pos.Infrastructure.Migrations;

public sealed class DatabaseMigrationException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
