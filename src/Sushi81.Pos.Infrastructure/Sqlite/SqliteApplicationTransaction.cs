using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Transactions;

namespace Sushi81.Pos.Infrastructure.Sqlite;

public sealed class SqliteApplicationTransaction(SqliteConnection connection, SqliteTransaction transaction) : IApplicationTransaction
{
    public SqliteConnection Connection { get; } = connection;

    public SqliteTransaction Transaction { get; } = transaction;
}
