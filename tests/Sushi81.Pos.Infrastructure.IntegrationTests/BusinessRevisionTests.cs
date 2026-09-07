using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Foundation.Transactions;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class BusinessRevisionTests
{
    [TestMethod]
    public async Task AcceptedDurableMutationsAdvanceCanonicalRevisionAcrossRestart()
    {
        using var fixture = new RevisionFixture();
        var reader = new SqliteBusinessRevisionStore(fixture.Paths, fixture.Factory);
        Assert.AreEqual(0L, await reader.ReadAsync());

        await InsertCategoryAsync(fixture, Guid.NewGuid());
        Assert.AreEqual(1L, await reader.ReadAsync());

        await InsertCategoryAsync(fixture, Guid.NewGuid());
        var restartedReader = new SqliteBusinessRevisionStore(fixture.Paths, new SqliteConnectionFactory(fixture.Paths));
        Assert.AreEqual(2L, await restartedReader.ReadAsync());
    }

    [TestMethod]
    public async Task FailedOrCommitFailedMutationDoesNotAdvanceRevision()
    {
        using var fixture = new RevisionFixture();
        var reader = new SqliteBusinessRevisionStore(fixture.Paths, fixture.Factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runner.ExecuteAsync(async (transaction, token) =>
        {
            await ExecuteAsync(transaction, "INSERT INTO categories(category_id,name,normalized_name,created_at_utc,updated_at_utc) VALUES ($id,'Failed','failed','2026-09-07T12:00:00Z','2026-09-07T12:00:00Z');", token, ("$id", Guid.NewGuid().ToString()));
            throw new InvalidOperationException("synthetic rollback");
        }));
        Assert.AreEqual(0L, await reader.ReadAsync());

        var commitFailing = new SqliteTransactionRunner(fixture.Factory, () => new IOException("synthetic commit failure"));
        await Assert.ThrowsAsync<IOException>(() => commitFailing.ExecuteAsync(async (transaction, token) =>
            await ExecuteAsync(transaction, "INSERT INTO categories(category_id,name,normalized_name,created_at_utc,updated_at_utc) VALUES ($id,'CommitFail','commit-fail','2026-09-07T12:00:00Z','2026-09-07T12:00:00Z');", token, ("$id", Guid.NewGuid().ToString()))));
        Assert.AreEqual(0L, await reader.ReadAsync());
    }

    [TestMethod]
    public async Task PostCommitNotifierCarriesCanonicalRevision()
    {
        using var fixture = new RevisionFixture();
        await InsertCategoryAsync(fixture, Guid.NewGuid());
        var reader = new SqliteBusinessRevisionStore(fixture.Paths, fixture.Factory);
        var scheduler = new RecordingScheduler();
        var clock = new FixedClock();
        using var notifier = await DurableChangeNotifier.CreateAsync(fixture.Paths, clock, scheduler, NullLogger.Instance, reader);

        await notifier.NotifyCommittedAsync();

        Assert.HasCount(1, scheduler.Changes);
        Assert.AreEqual(1L, scheduler.Changes[0].Sequence);
    }

    private static Task InsertCategoryAsync(RevisionFixture fixture, Guid id) => fixture.Runner.ExecuteAsync(async (transaction, token) =>
        await ExecuteAsync(transaction, "INSERT INTO categories(category_id,name,normalized_name,created_at_utc,updated_at_utc) VALUES ($id,$name,$normalized,'2026-09-07T12:00:00Z','2026-09-07T12:00:00Z');", token,
            ("$id", id.ToString()), ("$name", id.ToString("N")), ("$normalized", id.ToString("N"))));

    private static async Task ExecuteAsync(IApplicationTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        var sqlite = (SqliteApplicationTransaction)transaction;
        await using var command = sqlite.Connection.CreateCommand();
        command.Transaction = sqlite.Transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class RevisionFixture : IDisposable
    {
        public RevisionFixture()
        {
            Paths = new TestPaths(Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.Revision", Guid.NewGuid().ToString("N")));
            Paths.EnsureInitialized();
            Factory = new SqliteConnectionFactory(Paths);
            new SqliteMigrationRunner(Factory, ProductionMigrations.All, new FixedClock()).InitializeAsync().GetAwaiter().GetResult();
            Runner = new SqliteTransactionRunner(Factory);
        }

        public TestPaths Paths { get; }
        public SqliteConnectionFactory Factory { get; }
        public SqliteTransactionRunner Runner { get; }

        public void Dispose()
        {
            if (Directory.Exists(Paths.RootDirectory)) Directory.Delete(Paths.RootDirectory, recursive: true);
        }
    }

    private sealed class RecordingScheduler : IRecoveryScheduler
    {
        public List<DurableChange> Changes { get; } = [];
        public void NotifyCommitted(DurableChange change) => Changes.Add(change);
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 7);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class TestPaths(string root) : IAppPaths
    {
        public string RootDirectory { get; } = root;
        public string DataDirectory { get; } = Path.Combine(root, "Data");
        public string RecoveryDirectory { get; } = Path.Combine(root, "Recovery");
        public string CacheDirectory { get; } = Path.Combine(root, "Cache");
        public string LogsDirectory { get; } = Path.Combine(root, "Logs");
        public string ConfigDirectory { get; } = Path.Combine(root, "Config");
        public string TempDirectory { get; } = Path.Combine(root, "Temp");
        public string LiveDatabasePath { get; } = Path.Combine(root, "Data", "live.db");

        public void EnsureInitialized()
        {
            foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory })
                Directory.CreateDirectory(path);
        }
    }
}
