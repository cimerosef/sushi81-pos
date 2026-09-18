using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class M10Wp3CatalogueImportApplicationTests
{
    [TestMethod]
    public async Task ChangedCommitUsesOneAuthorityScopeStoreCallAndNotifier()
    {
        var store = new RecordingStore(CatalogueImportCommitResult.Success(changed: true));
        var notifier = new RecordingNotifier();
        var service = CreateService(store, new TestGuard(WriteAuthorityState.Authoritative), notifier);
        var result = await service.CommitAsync(Preview(store));
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, store.CommitCalls);
        Assert.AreEqual(1, notifier.Calls);
    }

    [TestMethod]
    public async Task EveryNonAuthoritativeStateRejectsBeforeStoreOrNotifier()
    {
        foreach (var state in Enum.GetValues<WriteAuthorityState>().Where(value => value != WriteAuthorityState.Authoritative))
        {
            var store = new RecordingStore(CatalogueImportCommitResult.Success(changed: true));
            var notifier = new RecordingNotifier();
            var result = await CreateService(store, new TestGuard(state), notifier).CommitAsync(Preview(store));
            Assert.IsFalse(result.Succeeded, state.ToString());
            Assert.AreEqual(0, store.CommitCalls, state.ToString());
            Assert.AreEqual(0, notifier.Calls, state.ToString());
        }
    }

    [TestMethod]
    public async Task StoreFailureDoesNotNotifyAndNotifierFailureDoesNotUndoCommit()
    {
        var failedStore = new RecordingStore(CatalogueImportCommitResult.Failure(new CatalogueImportIssue(CatalogueImportIssueSeverity.Error, "conflict", "synthetic")));
        var failedNotifier = new RecordingNotifier();
        var failed = await CreateService(failedStore, new TestGuard(WriteAuthorityState.Authoritative), failedNotifier).CommitAsync(Preview(failedStore));
        Assert.IsFalse(failed.Succeeded);
        Assert.AreEqual(0, failedNotifier.Calls);

        var committedStore = new RecordingStore(CatalogueImportCommitResult.Success(changed: true));
        var throwingNotifier = new RecordingNotifier { Throw = true };
        var committed = await CreateService(committedStore, new TestGuard(WriteAuthorityState.Authoritative), throwingNotifier).CommitAsync(Preview(committedStore));
        Assert.IsTrue(committed.Succeeded);
        Assert.AreEqual(1, throwingNotifier.Calls);
    }

    [TestMethod]
    public async Task NoOpCommitRequiresAuthorityButDoesNotNotify()
    {
        var store = new RecordingStore(CatalogueImportCommitResult.Success(changed: false));
        var notifier = new RecordingNotifier();
        var result = await CreateService(store, new TestGuard(WriteAuthorityState.Authoritative), notifier).CommitAsync(Preview(store));
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, store.CommitCalls);
        Assert.AreEqual(0, notifier.Calls);
    }

    private static CatalogueImportService CreateService(RecordingStore store, IWriteAuthorityGuard guard, RecordingNotifier notifier) =>
        new(new NoOpGateway(), store, store, guard, notifier);

    private static CatalogueImportResult Preview(RecordingStore store)
    {
        var plan = new CatalogueImportPlan(CatalogueImportMode.Update, [], [], [], "synthetic");
        return new(new CatalogueImportPreview(CatalogueImportMode.Update, "synthetic", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], []), plan, CatalogueImportBaseline.Empty);
    }

    private sealed class NoOpGateway : ICatalogueWorkbookImportGateway
    {
        public Task<CatalogueImportWorkbook> ReadAsync(Stream source, string? sourceName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingStore(CatalogueImportCommitResult result) : ICatalogueImportStore
    {
        public int CommitCalls { get; private set; }
        public Task<CatalogueImportBaseline> ReadCatalogueImportBaselineAsync(CancellationToken cancellationToken = default) => Task.FromResult(CatalogueImportBaseline.Empty);
        public Task<CatalogueImportCommitResult> CommitAsync(CatalogueImportCommitRequest request, CancellationToken cancellationToken = default) { CommitCalls++; return Task.FromResult(result); }
    }

    private sealed class RecordingNotifier : IDurableChangeNotifier
    {
        public int Calls { get; private set; }
        public bool Throw { get; init; }
        public Task NotifyCommittedAsync(CancellationToken cancellationToken = default) { Calls++; if (Throw) throw new InvalidOperationException("synthetic notifier failure"); return Task.CompletedTask; }
    }

    private sealed class TestGuard(WriteAuthorityState state) : IWriteAuthorityGuard
    {
        public WriteAuthorityState State => state;
        public void RequireWriteAuthority() { if (state != WriteAuthorityState.Authoritative) throw new WriteAuthorityException(state); }
    }
}
