using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Sushi81.Pos.Application.Archive;
using Sushi81.Pos.Application.Export;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Archive;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Export;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Order;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class M12Wp3StartupArchiveTests
{
    [TestMethod]
    public async Task JanuaryStartupHasNoTargetAndDoesNotCreateArchive()
    {
        using var fixture = await Fixture.CreateAsync(new DateOnly(2027, 1, 31));
        var target = ClosedOrder(2026, "January target");
        await fixture.OrderStore.SaveLifecycleAsync(target, [Payment(target.Id)]);

        var result = await fixture.CreateCoordinator().RunAsync();

        Assert.AreEqual(AnnualArchiveStartupOutcome.NoTargetYet, result.Outcome);
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM annual_archive_completions;"));
        Assert.IsFalse(Directory.Exists(fixture.Paths.ArchiveDirectory));
    }

    [TestMethod]
    public async Task FebruaryAndDelayedStartupArchivePreviousYearOnly()
    {
        foreach (var date in new[] { new DateOnly(2027, 2, 1), new DateOnly(2027, 3, 15), new DateOnly(2027, 12, 31) })
        {
            using var fixture = await Fixture.CreateAsync(date);
            var previousYear = ClosedOrder(2026, $"previous year {date}");
            var currentYear = ClosedOrder(2027, "current year");
            await fixture.OrderStore.SaveLifecycleAsync(previousYear, [Payment(previousYear.Id)]);
            await fixture.OrderStore.SaveLifecycleAsync(currentYear, [Payment(currentYear.Id)]);

            var result = await fixture.CreateCoordinator().RunAsync();

            Assert.AreEqual(AnnualArchiveStartupOutcome.CompletedNow, result.Outcome);
            Assert.AreEqual(2026, result.Finalization!.ArchiveYear);
            Assert.AreEqual(1, result.Finalization.ArchivedOrderCount);
            Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM orders WHERE planned_fulfilment_date LIKE '2026-%';"));
            Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM orders WHERE planned_fulfilment_date LIKE '2027-%';"));
            Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM annual_archive_completions WHERE archive_year=2026;"));
        }
    }

    [TestMethod]
    public async Task NonAuthoritativeStatesSkipWithoutInvokingFinalizer()
    {
        foreach (var state in new[]
        {
            WriteAuthorityState.NonAuthoritativeReadOnly,
            WriteAuthorityState.Transitioning,
            WriteAuthorityState.RecoveryRequired
        })
        {
            using var guard = new WriteAuthorityGuard(state);
            var invocations = 0;
            var coordinator = new AnnualArchiveStartupCoordinator(
                guard,
                _ =>
                {
                    invocations++;
                    return Task.FromResult(Result(AnnualArchiveFinalizationOutcome.CompletedNow));
                },
                NullLogger<AnnualArchiveStartupCoordinator>.Instance);

            var result = await coordinator.RunAsync();

            Assert.AreEqual(AnnualArchiveStartupOutcome.SkippedNotAuthoritative, result.Outcome);
            Assert.AreEqual(0, invocations);
        }
    }

    [TestMethod]
    public async Task RepeatedAuthoritativeStartupIsIdempotentAndNotifiesOnce()
    {
        using var fixture = await Fixture.CreateAsync(new DateOnly(2027, 2, 1));
        var target = ClosedOrder(2026, "idempotent");
        await fixture.OrderStore.SaveLifecycleAsync(target, [Payment(target.Id)]);
        var coordinator = fixture.CreateCoordinator();

        var first = await coordinator.RunAsync();
        var revision = await fixture.ScalarTextAsync("SELECT value FROM foundation_metadata WHERE key='business_data_revision';");
        var second = await coordinator.RunAsync();

        Assert.AreEqual(AnnualArchiveStartupOutcome.CompletedNow, first.Outcome);
        Assert.AreEqual(AnnualArchiveStartupOutcome.AlreadyCompleted, second.Outcome);
        Assert.AreEqual(1, fixture.Notifier.Count);
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM annual_archive_completions WHERE archive_year=2026;"));
        Assert.AreEqual(revision, await fixture.ScalarTextAsync("SELECT value FROM foundation_metadata WHERE key='business_data_revision';"));
    }

    [TestMethod]
    public async Task RetryableFailureLeavesLiveDataAndLaterStartupCompletes()
    {
        using var fixture = await Fixture.CreateAsync(new DateOnly(2027, 2, 1));
        var target = ClosedOrder(2026, "retryable");
        await fixture.OrderStore.SaveLifecycleAsync(target, [Payment(target.Id)]);
        var failing = fixture.CreateCoordinator(stage => stage == "copy" ? new IOException("copy failure") : null);

        var first = await failing.RunAsync();
        var second = await fixture.CreateCoordinator().RunAsync();

        Assert.AreEqual(AnnualArchiveStartupOutcome.FailedRetryable, first.Outcome);
        Assert.AreEqual(AnnualArchiveStartupOutcome.CompletedNow, second.Outcome);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM annual_archive_completions;"));
        Assert.AreEqual(1, fixture.Notifier.Count);
    }

    [TestMethod]
    public async Task CanonicalPublishedBeforeTransactionFailureIsReusedWithoutDuplicatePreservation()
    {
        using var fixture = await Fixture.CreateAsync(new DateOnly(2027, 2, 1));
        var target = ClosedOrder(2026, "canonical reuse");
        await fixture.OrderStore.SaveLifecycleAsync(target, [Payment(target.Id)]);
        var failing = fixture.CreateCoordinator(stage => stage == "after-delete-before-commit" ? new InvalidOperationException("rollback") : null);

        var first = await failing.RunAsync();
        var liveAfterFailure = await fixture.ScalarAsync("SELECT COUNT(*) FROM orders;");
        var preparedAfterFailure = await fixture.ExportStore.ListPreparedBatchesAsync();
        var second = await fixture.CreateCoordinator().RunAsync();
        var preparedAfterRetry = await fixture.ExportStore.ListPreparedBatchesAsync();

        Assert.AreEqual(AnnualArchiveStartupOutcome.FailedRetryable, first.Outcome);
        Assert.AreEqual(AnnualArchiveStartupOutcome.CompletedNow, second.Outcome);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Paths.ArchiveDirectory, "sushi81-archive-2026.db")));
        Assert.AreEqual(1L, liveAfterFailure);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM orders;"));
        Assert.HasCount(1, preparedAfterFailure);
        Assert.HasCount(1, preparedAfterRetry);
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM annual_archive_completions;"));
    }

    [TestMethod]
    public async Task CoordinatorMapsFinalizerOutcomesAndDoesNotRetryWithinOneStartup()
    {
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var calls = 0;
        var coordinator = new AnnualArchiveStartupCoordinator(
            guard,
            _ =>
            {
                calls++;
                if (calls == 1) throw new IOException("later startup retry only");
                return Task.FromResult(Result(AnnualArchiveFinalizationOutcome.AlreadyCompleted));
            },
            NullLogger<AnnualArchiveStartupCoordinator>.Instance);

        var first = await coordinator.RunAsync();
        var second = await coordinator.RunAsync();

        Assert.AreEqual(AnnualArchiveStartupOutcome.FailedRetryable, first.Outcome);
        Assert.AreEqual(AnnualArchiveStartupOutcome.AlreadyCompleted, second.Outcome);
        Assert.AreEqual(2, calls);
    }

    private static AnnualArchiveFinalizationResult Result(AnnualArchiveFinalizationOutcome outcome) =>
        new(outcome, 2026, 0, string.Empty, null);

    private static OrderSnapshot ClosedOrder(int year, string comment) =>
        new(
            Guid.NewGuid(),
            OrderSourceType.Pos,
            OrderStatus.Closed,
            new DateTimeOffset(year, 12, 1, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(year, 12, 31, 18, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(year, 12, 31, 18, 0, 0, TimeSpan.Zero),
            null,
            FulfilmentMode.Retrait,
            new DateOnly(year, 12, 31),
            new TimeOnly(12, 0),
            false,
            null,
            null,
            comment,
            Money.FromCents(1000),
            false,
            false,
            null,
            Money.Zero,
            [new(Guid.NewGuid(), 0, Guid.NewGuid(), "P-M12", "M12 produit", "Plats", Money.FromCents(1000), 10m, true, 1, Money.FromCents(1000), Money.FromCents(1000), [])],
            [new(10m, Money.FromCents(1000), Money.FromCents(91), Guid.NewGuid())])
        {
            Reference = $"M12-{Guid.NewGuid():N}"[..12],
            CardPaymentTtc = Money.FromCents(1000),
            CashPaymentTtc = Money.Zero,
            SourceTotalTtc = Money.FromCents(1000)
        };

    private static PaymentAdjustment Payment(Guid orderId) => new(
        Guid.NewGuid(),
        orderId,
        PaymentBucket.Card,
        Money.FromCents(1000),
        new DateTimeOffset(2026, 12, 31, 18, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 12, 31, 18, 1, 0, TimeSpan.Zero));

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            TestPaths paths,
            TestClock clock,
            SqliteConnectionFactory factory,
            WriteAuthorityGuard guard,
            DeterministicIds ids,
            SqliteOrderStore orderStore,
            SqliteGestionExportStore exportStore,
            RecordingNotifier notifier)
        {
            Paths = paths;
            Clock = clock;
            Factory = factory;
            Guard = guard;
            Ids = ids;
            OrderStore = orderStore;
            ExportStore = exportStore;
            Notifier = notifier;
        }

        public TestPaths Paths { get; }
        public TestClock Clock { get; }
        public SqliteConnectionFactory Factory { get; }
        private WriteAuthorityGuard Guard { get; }
        private DeterministicIds Ids { get; }
        public SqliteOrderStore OrderStore { get; }
        public SqliteGestionExportStore ExportStore { get; }
        public RecordingNotifier Notifier { get; }

        public static async Task<Fixture> CreateAsync(DateOnly businessDate)
        {
            var paths = new TestPaths();
            var clock = new TestClock(businessDate);
            var factory = new SqliteConnectionFactory(paths);
            await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
            var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            var ids = new DeterministicIds();
            var runner = new SqliteTransactionRunner(factory);
            var orderStore = new SqliteOrderStore(factory, runner, idGenerator: ids, clock: clock);
            var exportStore = new SqliteGestionExportStore(factory, orderStore, runner, ids);
            return new Fixture(paths, clock, factory, guard, ids, orderStore, exportStore, new RecordingNotifier());
        }

        public AnnualArchiveStartupCoordinator CreateCoordinator(Func<string, Exception?>? injector = null)
        {
            var service = new SqliteAnnualArchiveFinalizationService(
                Paths,
                Clock,
                Guard,
                Factory,
                new SqliteTransactionRunner(Factory),
                ExportStore,
                Ids,
                Notifier,
                injector);
            return new AnnualArchiveStartupCoordinator(
                Guard,
                service.FinalizeNextArchiveAsync,
                NullLogger<AnnualArchiveStartupCoordinator>.Instance);
        }

        public async Task<long> ScalarAsync(string sql)
        {
            await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(Factory.LiveDatabasePath);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        public async Task<string?> ScalarTextAsync(string sql)
        {
            await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(Factory.LiveDatabasePath);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        public void Dispose()
        {
            Guard.Dispose();
            Paths.Dispose();
        }
    }

    private sealed class TestClock(DateOnly businessDate) : IBusinessClock
    {
        public DateOnly BusinessDate => businessDate;
        public DateTimeOffset UtcNow { get; } = new(2027, 2, 1, 8, 0, 0, TimeSpan.Zero);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int count;
        public Guid NewId() => Guid.Parse($"52b00000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
    }

    private sealed class RecordingNotifier : IDurableChangeNotifier
    {
        public int Count { get; private set; }
        public Task NotifyCommittedAsync(CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M12.WP3.Tests", Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(RootDirectory, "Data");
            RecoveryDirectory = Path.Combine(RootDirectory, "Recovery");
            CacheDirectory = Path.Combine(RootDirectory, "Cache");
            LogsDirectory = Path.Combine(RootDirectory, "Logs");
            ConfigDirectory = Path.Combine(RootDirectory, "Config");
            TempDirectory = Path.Combine(RootDirectory, "Temp");
            LiveDatabasePath = Path.Combine(DataDirectory, "live.db");
            foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory })
                Directory.CreateDirectory(path);
        }

        public string RootDirectory { get; }
        public string DataDirectory { get; }
        public string RecoveryDirectory { get; }
        public string CacheDirectory { get; }
        public string LogsDirectory { get; }
        public string ConfigDirectory { get; }
        public string TempDirectory { get; }
        public string ArchiveDirectory => Path.Combine(RootDirectory, "Archive");
        public string LiveDatabasePath { get; }
        public void EnsureInitialized() { }
        public void Dispose()
        {
            if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
