using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
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
public sealed class M12Wp2AnnualArchiveIntegrationTests
{
    [TestMethod]
    public async Task FinalizationPublishesArchivePreservesCreateUpdateCancelAndRemovesOnlyExactTargets()
    {
        using var fixture = await Fixture.CreateAsync();
        var updateId = Guid.Parse("52000000-0000-0000-0000-000000000001");
        var cancelId = Guid.Parse("52000000-0000-0000-0000-000000000002");
        var createId = Guid.Parse("52000000-0000-0000-0000-000000000003");
        var hiboutikId = Guid.Parse("52000000-0000-0000-0000-000000000004");

        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(updateId, OrderSourceType.Pos, "before update"), [Payment(updateId)]);
        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(cancelId, OrderSourceType.Pos, "before cancel"), [Payment(cancelId)]);
        var exported = await fixture.ExportService.PrepareBatchAsync(new ExportSelectionOptions(), "m12-wp2-test");
        Assert.IsNotNull(exported.Batch);
        await fixture.ExportStore.MarkBatchSucceededAsync(exported.Batch!.Payload.Meta.BatchId, fixture.Clock.UtcNow);

        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(createId, OrderSourceType.Pos, "new order"), [Payment(createId)]);
        await fixture.OrderStore.SaveLifecycleAsync(
            ClosedOrder(updateId, OrderSourceType.Pos, "after update") with { UpdatedAt = fixture.Clock.UtcNow.AddMinutes(1) },
            []);
        await fixture.OrderStore.SaveLifecycleAsync(
            ClosedOrder(cancelId, OrderSourceType.Pos, "before cancel") with
            {
                Status = OrderStatus.Cancelled,
                CancelledAt = new DateTimeOffset(2026, 12, 31, 19, 0, 0, TimeSpan.Zero),
                UpdatedAt = fixture.Clock.UtcNow.AddMinutes(2)
            },
            []);
        await fixture.OrderStore.SaveAsync(ClosedOrder(hiboutikId, OrderSourceType.HiboutikPaste, "Hiboutik historical order"));

        var successfulHistoryBefore = await ReadSuccessfulLedgerDumpAsync(fixture.Factory);
        var revisionBefore = await ScalarAsync(fixture.Factory, "SELECT value FROM foundation_metadata WHERE key='business_data_revision';");
        var result = await fixture.Service.FinalizeNextArchiveAsync();

        Assert.AreEqual(AnnualArchiveFinalizationOutcome.CompletedNow, result.Outcome);
        Assert.AreEqual(2026, result.ArchiveYear);
        Assert.AreEqual(4, result.ArchivedOrderCount);
        Assert.IsTrue(File.Exists(result.CanonicalArchivePath));
        Assert.IsNotNull(result.ArchiveSha256);
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders WHERE order_id IN ('52000000-0000-0000-0000-000000000001','52000000-0000-0000-0000-000000000002','52000000-0000-0000-0000-000000000003','52000000-0000-0000-0000-000000000004');"));
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM annual_archive_completions WHERE archive_year=2026;"));
        Assert.IsGreaterThan(revisionBefore, await ScalarAsync(fixture.Factory, "SELECT value FROM foundation_metadata WHERE key='business_data_revision';"));
        Assert.AreEqual(2, fixture.Notifier.Count);
        Assert.AreEqual(successfulHistoryBefore, await ReadSuccessfulLedgerDumpAsync(fixture.Factory));
        var independentArchiveHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.CanonicalArchivePath)));
        Assert.AreEqual(independentArchiveHash, result.ArchiveSha256);
        Assert.AreEqual(independentArchiveHash, await ScalarTextAsync(fixture.Factory, "SELECT archive_sha256 FROM annual_archive_completions WHERE archive_year=2026;"));

        var prepared = await fixture.ExportStore.ListPreparedBatchesAsync();
        Assert.HasCount(1, prepared);
        CollectionAssert.AreEquivalent(
            new[] { ExportAction.Create, ExportAction.Update, ExportAction.Cancel },
            prepared.Single().Payload.Orders.Select(order => order.Action).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { createId, updateId, cancelId },
            prepared.Single().Payload.Orders.Select(order => order.OrderId).ToArray());
        Assert.IsFalse(prepared.Single().Payload.Orders.Any(order => order.OrderId == hiboutikId));

        await using var archive = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(result.CanonicalArchivePath);
        Assert.AreEqual(4L, await ScalarAsync(archive, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(3L, await ScalarAsync(archive, "SELECT COUNT(*) FROM payment_adjustments;"));
        Assert.AreEqual(0L, await ScalarAsync(archive, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='annual_archive_completions';"));
        Assert.AreEqual("M12-WP1-1", await ScalarTextAsync(archive, "SELECT value FROM archive_metadata WHERE key='archive_format_version';"));
    }

    [TestMethod]
    public async Task UnchangedSuccessfulUpdateAndCancelCreateNoNewPreparedActions()
    {
        using var fixture = await Fixture.CreateAsync();
        var updateId = Guid.Parse("52600000-0000-0000-0000-000000000001");
        var cancelId = Guid.Parse("52600000-0000-0000-0000-000000000002");

        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(updateId, OrderSourceType.Pos, "update before export"), [Payment(updateId)]);
        var updateCreate = (await fixture.ExportService.PrepareBatchAsync(new ExportSelectionOptions(), "m12-wp2-test")).Batch!;
        await fixture.ExportStore.MarkBatchSucceededAsync(updateCreate.Payload.Meta.BatchId, fixture.Clock.UtcNow);
        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(updateId, OrderSourceType.Pos, "update after first export") with { UpdatedAt = fixture.Clock.UtcNow.AddMinutes(1) }, []);
        var update = (await fixture.ExportService.PrepareBatchAsync(new ExportSelectionOptions(), "m12-wp2-test")).Batch!;
        Assert.AreEqual(ExportAction.Update, update.Payload.Orders.Single().Action);
        await fixture.ExportStore.MarkBatchSucceededAsync(update.Payload.Meta.BatchId, fixture.Clock.UtcNow.AddMinutes(1));

        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(cancelId, OrderSourceType.Pos, "cancel before export"), [Payment(cancelId)]);
        var cancelCreate = (await fixture.ExportService.PrepareBatchAsync(new ExportSelectionOptions(), "m12-wp2-test")).Batch!;
        await fixture.ExportStore.MarkBatchSucceededAsync(cancelCreate.Payload.Meta.BatchId, fixture.Clock.UtcNow.AddMinutes(2));
        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(cancelId, OrderSourceType.Pos, "cancel after first export") with
        {
            Status = OrderStatus.Cancelled,
            CancelledAt = new DateTimeOffset(2026, 12, 31, 19, 0, 0, TimeSpan.Zero),
            UpdatedAt = fixture.Clock.UtcNow.AddMinutes(3)
        }, []);
        var cancel = (await fixture.ExportService.PrepareBatchAsync(new ExportSelectionOptions(), "m12-wp2-test")).Batch!;
        Assert.AreEqual(ExportAction.Cancel, cancel.Payload.Orders.Single().Action);
        await fixture.ExportStore.MarkBatchSucceededAsync(cancel.Payload.Meta.BatchId, fixture.Clock.UtcNow.AddMinutes(3));

        Assert.IsEmpty(await fixture.ExportStore.ListPreparedBatchesAsync());
        var result = await fixture.Service.FinalizeNextArchiveAsync();

        Assert.AreEqual(AnnualArchiveFinalizationOutcome.CompletedNow, result.Outcome);
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE status='PREPARED';"));
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders;"));
    }

    [TestMethod]
    public async Task ExactPreparedIsReusedStalePreparedDoesNotSatisfyCurrentActionAndPreparedHistorySurvivesDeletion()
    {
        using var fixture = await Fixture.CreateAsync();
        var exactId = Guid.Parse("52700000-0000-0000-0000-000000000001");
        var staleId = Guid.Parse("52700000-0000-0000-0000-000000000002");

        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(exactId, OrderSourceType.Pos, "exact before export"), [Payment(exactId)]);
        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(staleId, OrderSourceType.Pos, "stale before export"), [Payment(staleId)]);
        var initialCreate = (await fixture.ExportService.PrepareBatchAsync(new ExportSelectionOptions(), "m12-wp2-test")).Batch!;
        await fixture.ExportStore.MarkBatchSucceededAsync(initialCreate.Payload.Meta.BatchId, fixture.Clock.UtcNow);
        var exactSnapshot = ClosedOrder(exactId, OrderSourceType.Pos, "exact current") with { UpdatedAt = fixture.Clock.UtcNow.AddMinutes(1) };
        await fixture.OrderStore.SaveLifecycleAsync(exactSnapshot, []);
        var exactAction = (await fixture.ExportService.SelectAsync(new ExportSelectionOptions())).Actions.Single(action => action.OrderId == exactId);
        var exactPrepared = await fixture.ExportStore.PrepareMissingActionsAsync([exactAction], "M12-WP2-ARCHIVE-PRESERVATION-1", fixture.Clock.UtcNow);
        Assert.IsNotNull(exactPrepared);

        var staleFirstSnapshot = ClosedOrder(staleId, OrderSourceType.Pos, "stale first change") with { UpdatedAt = fixture.Clock.UtcNow.AddMinutes(3) };
        await fixture.OrderStore.SaveLifecycleAsync(staleFirstSnapshot, []);
        var staleFirstAction = (await fixture.ExportService.SelectAsync(new ExportSelectionOptions())).Actions.Single(action => action.OrderId == staleId);
        var stalePrepared = await fixture.ExportStore.PrepareMissingActionsAsync([staleFirstAction], "M12-WP2-ARCHIVE-PRESERVATION-1", fixture.Clock.UtcNow.AddMinutes(3));
        Assert.IsNotNull(stalePrepared);
        var staleFirstHash = ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializeAction(staleFirstAction));
        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(staleId, OrderSourceType.Pos, "stale current change") with { UpdatedAt = fixture.Clock.UtcNow.AddMinutes(4) }, []);
        var staleCurrentAction = (await fixture.ExportService.SelectAsync(new ExportSelectionOptions())).Actions.Single(action => action.OrderId == staleId);
        var staleCurrentHash = ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializeAction(staleCurrentAction));
        Assert.AreNotEqual(staleFirstHash, staleCurrentHash);

        var result = await fixture.Service.FinalizeNextArchiveAsync();
        Assert.AreEqual(AnnualArchiveFinalizationOutcome.CompletedNow, result.Outcome);
        var prepared = await fixture.ExportStore.ListPreparedBatchesAsync();
        Assert.HasCount(3, prepared);
        Assert.AreEqual(1, prepared.Count(batch => batch.Payload.Orders.Any(order => order.OrderId == exactId)));
        Assert.AreEqual(1, prepared.Count(batch => batch.Payload.Orders.Any(order => order.OrderId == staleId && ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializeAction(order)) == staleFirstHash)));
        var currentBatch = prepared.Single(batch => batch.Payload.Orders.Any(order => order.OrderId == staleId && ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializeAction(order)) == staleCurrentHash));

        var exactBatch = prepared.Single(batch => batch.Payload.Orders.Any(order => order.OrderId == exactId));
        await fixture.ExportStore.MarkBatchSucceededAsync(exactBatch.Payload.Meta.BatchId, fixture.Clock.UtcNow.AddMinutes(5));
        await fixture.ExportStore.MarkBatchSucceededAsync(currentBatch.Payload.Meta.BatchId, fixture.Clock.UtcNow.AddMinutes(6));
        var staleBatch = prepared.Single(batch => batch.Payload.Orders.Any(order => order.OrderId == staleId && ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializeAction(order)) == staleFirstHash));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ExportStore.MarkBatchSucceededAsync(staleBatch.Payload.Meta.BatchId, fixture.Clock.UtcNow.AddMinutes(7)));
        Assert.AreEqual(3L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE status='SUCCESS';"));
    }

    [TestMethod]
    public async Task ZeroOrderYearPublishesCanonicalAndCompletesExactlyOnce()
    {
        using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Service.FinalizeNextArchiveAsync();
        var revisionAfterFirst = await ScalarAsync(fixture.Factory, "SELECT value FROM foundation_metadata WHERE key='business_data_revision';");
        var independentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(first.CanonicalArchivePath)));
        var second = await fixture.Service.FinalizeNextArchiveAsync();

        Assert.AreEqual(AnnualArchiveFinalizationOutcome.CompletedNow, first.Outcome);
        Assert.AreEqual(0, first.ArchivedOrderCount);
        Assert.IsTrue(File.Exists(first.CanonicalArchivePath));
        Assert.AreEqual(independentHash, first.ArchiveSha256);
        Assert.AreEqual(independentHash, await ScalarTextAsync(fixture.Factory, "SELECT archive_sha256 FROM annual_archive_completions WHERE archive_year=2026;"));
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT archive_order_count FROM annual_archive_completions WHERE archive_year=2026;"));
        Assert.IsEmpty(await fixture.ExportStore.ListPreparedBatchesAsync());
        Assert.AreEqual(AnnualArchiveFinalizationOutcome.AlreadyCompleted, second.Outcome);
        Assert.AreEqual(independentHash, second.ArchiveSha256);
        Assert.AreEqual(revisionAfterFirst, await ScalarAsync(fixture.Factory, "SELECT value FROM foundation_metadata WHERE key='business_data_revision';"));
        Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM annual_archive_completions WHERE archive_year=2026;"));
    }

    [TestMethod]
    public async Task ExactRemovalPreservesOpenAndOtherYearAggregates()
    {
        using var fixture = await Fixture.CreateAsync();
        var targetClosedId = Guid.Parse("52800000-0000-0000-0000-000000000001");
        var targetCancelledId = Guid.Parse("52800000-0000-0000-0000-000000000002");
        var openId = Guid.Parse("52800000-0000-0000-0000-000000000003");
        var otherClosedId = Guid.Parse("52800000-0000-0000-0000-000000000004");
        var otherCancelledId = Guid.Parse("52800000-0000-0000-0000-000000000005");

        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(targetClosedId, OrderSourceType.Pos, "target closed"), [Payment(targetClosedId)]);
        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(targetCancelledId, OrderSourceType.Pos, "target cancelled") with
        {
            Status = OrderStatus.Cancelled,
            ClosedAt = null,
            CancelledAt = new DateTimeOffset(2026, 12, 31, 19, 0, 0, TimeSpan.Zero)
        }, [Payment(targetCancelledId)]);
        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(openId, OrderSourceType.Pos, "still open") with { Status = OrderStatus.Open, ClosedAt = null }, []);
        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(otherClosedId, OrderSourceType.Pos, "other year") with
        {
            ClosedAt = new DateTimeOffset(2025, 12, 31, 18, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2025, 12, 31, 18, 0, 0, TimeSpan.Zero)
        }, [Payment(otherClosedId)]);
        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(otherCancelledId, OrderSourceType.Pos, "other year cancelled") with
        {
            Status = OrderStatus.Cancelled,
            ClosedAt = null,
            CancelledAt = new DateTimeOffset(2025, 12, 31, 19, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2025, 12, 31, 19, 0, 0, TimeSpan.Zero)
        }, [Payment(otherCancelledId)]);

        var result = await fixture.Service.FinalizeNextArchiveAsync();

        Assert.AreEqual(AnnualArchiveFinalizationOutcome.CompletedNow, result.Outcome);
        Assert.AreEqual(2, result.ArchivedOrderCount);
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders WHERE order_id IN ($id1,$id2);", targetClosedId, targetCancelledId));
        Assert.AreEqual(3L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders WHERE order_id=$id;", openId));
        Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders WHERE order_id=$id;", otherClosedId));
        Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders WHERE order_id=$id;", otherCancelledId));
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM payment_adjustments WHERE order_id IN ($id1,$id2);", targetClosedId, targetCancelledId));
        Assert.AreEqual(3L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM order_items;"));
    }

    [TestMethod]
    public async Task PublicationFailureMatrixLeavesLiveStateAndRetryable()
    {
        var cases = new[]
        {
            ("flush", typeof(IOException)),
            ("incoming-validation", typeof(InvalidDataException)),
            ("rename", typeof(IOException)),
            ("post-rename-validation", typeof(InvalidDataException))
        };

        foreach (var (stage, expectedType) in cases)
        {
            using var fixture = await Fixture.CreateAsync();
            var id = Guid.Parse($"52900000-0000-0000-0000-{Array.IndexOf(cases, (stage, expectedType)) + 1:D12}");
            await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(id, OrderSourceType.Pos, $"failure {stage}"), [Payment(id)]);
            var failing = fixture.CreateService(currentStage => currentStage == stage ? Activator.CreateInstance(expectedType, $"injected {stage}") as Exception : null);

            var exception = await CaptureExceptionAsync(() => failing.FinalizeNextArchiveAsync());
            Assert.IsNotNull(exception);
            Assert.AreEqual(expectedType, exception.GetType());
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders;"));
            Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM annual_archive_completions;"));
            var retried = await fixture.Service.FinalizeNextArchiveAsync();
            Assert.AreEqual(AnnualArchiveFinalizationOutcome.CompletedNow, retried.Outcome);
            Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders;"));
        }
    }

    [TestMethod]
    public async Task ExistingCanonicalWithoutMarkerIsReusedOrFailsClosedOnMismatch()
    {
        using (var fixture = await Fixture.CreateAsync())
        {
            var id = Guid.Parse("52a00000-0000-0000-0000-000000000001");
            await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(id, OrderSourceType.Pos, "valid existing canonical"), [Payment(id)]);
            var staged = await fixture.StageAsync();
            Directory.CreateDirectory(fixture.Paths.ArchiveDirectory);
            var canonical = Path.Combine(fixture.Paths.ArchiveDirectory, "sushi81-archive-2026.db");
            File.Copy(staged!.StagedDatabasePath, canonical);

            var result = await fixture.Service.FinalizeNextArchiveAsync();

            Assert.AreEqual(AnnualArchiveFinalizationOutcome.CompletedNow, result.Outcome);
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM annual_archive_completions;"));
            Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders;"));
        }

        using (var fixture = await Fixture.CreateAsync())
        {
            var id = Guid.Parse("52a00000-0000-0000-0000-000000000002");
            await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(id, OrderSourceType.Pos, "mismatch canonical"), [Payment(id)]);
            var staged = await fixture.StageAsync();
            Directory.CreateDirectory(fixture.Paths.ArchiveDirectory);
            var canonical = Path.Combine(fixture.Paths.ArchiveDirectory, "sushi81-archive-2026.db");
            File.Copy(staged!.StagedDatabasePath, canonical);
            var originalBytes = File.ReadAllBytes(canonical);
            await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(id, OrderSourceType.Pos, "live changed after canonical") with { UpdatedAt = fixture.Clock.UtcNow.AddMinutes(1) }, []);

            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.FinalizeNextArchiveAsync());

            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(canonical));
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders;"));
            Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM annual_archive_completions;"));
        }
    }

    [TestMethod]
    public async Task RepeatedCallValidatesCompletionAndMissingCanonicalIsNotRecreated()
    {
        using var fixture = await Fixture.CreateAsync();
        var id = Guid.Parse("52100000-0000-0000-0000-000000000001");
        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(id, OrderSourceType.Pos, "repeatable"), [Payment(id)]);

        var first = await fixture.Service.FinalizeNextArchiveAsync();
        var revisionAfterFirst = await ScalarAsync(fixture.Factory, "SELECT value FROM foundation_metadata WHERE key='business_data_revision';");
        var second = await fixture.Service.FinalizeNextArchiveAsync();

        Assert.AreEqual(AnnualArchiveFinalizationOutcome.AlreadyCompleted, second.Outcome);
        Assert.AreEqual(first.ArchiveSha256, second.ArchiveSha256);
        Assert.AreEqual(revisionAfterFirst, await ScalarAsync(fixture.Factory, "SELECT value FROM foundation_metadata WHERE key='business_data_revision';"));
        Assert.AreEqual(1, fixture.Notifier.Count);

        File.Delete(first.CanonicalArchivePath);
        var third = await fixture.Service.FinalizeNextArchiveAsync();
        Assert.AreEqual(AnnualArchiveFinalizationOutcome.AlreadyCompleted, third.Outcome);
        Assert.IsFalse(File.Exists(first.CanonicalArchivePath));
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders;"));
    }

    [TestMethod]
    public async Task CopyFailureAndPostDeleteFailureLeaveRetryableLiveState()
    {
        using var copyFixture = await Fixture.CreateAsync();
        var copyId = Guid.Parse("52200000-0000-0000-0000-000000000001");
        await copyFixture.OrderStore.SaveLifecycleAsync(ClosedOrder(copyId, OrderSourceType.Pos, "copy failure"), [Payment(copyId)]);
        var copyFailure = copyFixture.CreateService(stage => stage == "copy" ? new IOException("copy failure") : null);
        await Assert.ThrowsAsync<IOException>(() => copyFailure.FinalizeNextArchiveAsync());
        Assert.AreEqual(1L, await ScalarAsync(copyFixture.Factory, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(0L, await ScalarAsync(copyFixture.Factory, "SELECT COUNT(*) FROM annual_archive_completions;"));
        Assert.IsFalse(File.Exists(Path.Combine(copyFixture.Paths.ArchiveDirectory, "sushi81-archive-2026.db")));
        var copied = await copyFixture.Service.FinalizeNextArchiveAsync();
        Assert.AreEqual(AnnualArchiveFinalizationOutcome.CompletedNow, copied.Outcome);

        using var transactionFixture = await Fixture.CreateAsync();
        var transactionId = Guid.Parse("52200000-0000-0000-0000-000000000002");
        await transactionFixture.OrderStore.SaveLifecycleAsync(ClosedOrder(transactionId, OrderSourceType.Pos, "transaction failure"), [Payment(transactionId)]);
        var transactionFailure = transactionFixture.CreateService(stage => stage == "after-delete-before-commit" ? new InvalidOperationException("rollback") : null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => transactionFailure.FinalizeNextArchiveAsync());
        Assert.AreEqual(1L, await ScalarAsync(transactionFixture.Factory, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(0L, await ScalarAsync(transactionFixture.Factory, "SELECT COUNT(*) FROM annual_archive_completions;"));
        Assert.AreEqual(1L, await ScalarAsync(transactionFixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE status='PREPARED';"));
        var retried = await transactionFixture.Service.FinalizeNextArchiveAsync();
        Assert.AreEqual(AnnualArchiveFinalizationOutcome.CompletedNow, retried.Outcome);
        Assert.AreEqual(0L, await ScalarAsync(transactionFixture.Factory, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(1L, await ScalarAsync(transactionFixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE status='PREPARED';"));
    }

    [TestMethod]
    public async Task CorruptCanonicalArchiveFailsClosedWithoutReplacingIt()
    {
        using var fixture = await Fixture.CreateAsync();
        var id = Guid.Parse("52300000-0000-0000-0000-000000000001");
        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(id, OrderSourceType.Pos, "corrupt canonical"), [Payment(id)]);
        var first = await fixture.Service.FinalizeNextArchiveAsync();
        var originalBytes = File.ReadAllBytes(first.CanonicalArchivePath);
        File.WriteAllBytes(first.CanonicalArchivePath, [0x53, 0x51, 0x4c, 0x69, 0x74, 0x65]);

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.FinalizeNextArchiveAsync());
        CollectionAssert.AreEqual(new byte[] { 0x53, 0x51, 0x4c, 0x69, 0x74, 0x65 }, File.ReadAllBytes(first.CanonicalArchivePath));
        Assert.AreNotEqual(originalBytes.Length, File.ReadAllBytes(first.CanonicalArchivePath).Length);
    }

    [TestMethod]
    public async Task JanuaryAndNonAuthoritativeRunsDoNotMutate()
    {
        using var paths = new TestPaths();
        var clock = new TestClock(new DateOnly(2027, 1, 31));
        var factory = await InitializeAsync(paths, clock);
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
        var transactionRunner = new SqliteTransactionRunner(factory);
        var ids = new DeterministicIds();
        var orderStore = new SqliteOrderStore(factory, transactionRunner, idGenerator: ids, clock: clock);
        var exportStore = new SqliteGestionExportStore(factory, orderStore, transactionRunner, ids);
        var service = new SqliteAnnualArchiveFinalizationService(paths, clock, guard, factory, transactionRunner, exportStore, ids, new NoOpDurableChangeNotifier());

        var january = await service.FinalizeNextArchiveAsync();
        Assert.AreEqual(AnnualArchiveFinalizationOutcome.NoTargetYet, january.Outcome);
        clock.BusinessDateValue = new DateOnly(2027, 2, 1);
        await Assert.ThrowsAsync<WriteAuthorityException>(() => service.FinalizeNextArchiveAsync());
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM annual_archive_completions;"));
        Assert.IsFalse(Directory.Exists(paths.ArchiveDirectory));
    }

    [TestMethod]
    public async Task Wp4AccessDiscoversValidatedArchiveSearchesSnapshotsAndCopiesSafely()
    {
        using var fixture = await Fixture.CreateAsync();
        var id = Guid.Parse("52b00000-0000-0000-0000-000000000001");
        var original = ClosedOrder(id, OrderSourceType.HiboutikPaste, "historical M12 search");
        await fixture.OrderStore.SaveLifecycleAsync(original, [Payment(id)]);
        var finalized = await fixture.Service.FinalizeNextArchiveAsync();
        var canonicalBytes = File.ReadAllBytes(finalized.CanonicalArchivePath);

        File.WriteAllText(Path.Combine(fixture.Paths.ArchiveDirectory, "not-an-archive.db"), "ignored");
        File.WriteAllBytes(Path.Combine(fixture.Paths.ArchiveDirectory, "sushi81-archive-2025.db"), [0x53, 0x51, 0x4c]);
        var access = new SqliteAnnualArchiveAccess(fixture.Paths, fixture.Clock);

        var available = await access.DiscoverAsync();
        Assert.HasCount(1, available);
        Assert.AreEqual(2026, available.Single().ArchiveYear);
        Assert.AreEqual(1, available.Single().OrderCount);

        var rows = await access.SearchAsync(2026, new AnnualArchiveSearchCriteria("historical M12"));
        Assert.HasCount(1, rows);
        Assert.AreEqual(id, rows.Single().Id);
        Assert.AreEqual(OrderSourceType.HiboutikPaste, rows.Single().SourceType);
        Assert.AreEqual(original.TotalTtc, rows.Single().TotalTtc);

        var loaded = await access.GetOrderAsync(2026, id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(original.Comment, loaded!.Comment);
        Assert.AreEqual(original.Items.Single().ProductName, loaded.Items.Single().ProductName);
        Assert.AreEqual(original.TaxBreakdown.Single().IncludedVatTtc, loaded.TaxBreakdown.Single().IncludedVatTtc);

        var exported = Path.Combine(fixture.Paths.TempDirectory, "selected-archive.db");
        File.WriteAllBytes(exported, [0x6f, 0x6c, 0x64]);
        var copied = await access.CopyAsync(2026, exported);
        CollectionAssert.AreEqual(canonicalBytes, File.ReadAllBytes(exported));
        CollectionAssert.AreEqual(canonicalBytes, File.ReadAllBytes(finalized.CanonicalArchivePath));
        Assert.AreEqual(canonicalBytes.LongLength, copied.Length);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(canonicalBytes)), copied.Sha256);

        var failure = new SqliteAnnualArchiveAccess(fixture.Paths, fixture.Clock, stage => stage == "copy" ? new IOException("synthetic copy failure") : null);
        var preserved = File.ReadAllBytes(exported);
        await Assert.ThrowsAsync<IOException>(() => failure.CopyAsync(2026, exported));
        CollectionAssert.AreEqual(preserved, File.ReadAllBytes(exported));
        CollectionAssert.AreEqual(canonicalBytes, File.ReadAllBytes(finalized.CanonicalArchivePath));
    }

    [TestMethod]
    public async Task MigrationNineUpgradesWithoutResetAndBrokenUpgradeRollsBack()
    {
        using (var paths = new TestPaths())
        {
            var clock = new TestClock(new DateOnly(2027, 2, 1));
            var factory = new SqliteConnectionFactory(paths);
            var beforeM12 = ProductionMigrations.All.Where(migration => migration.Version < 9).ToArray();
            await new SqliteMigrationRunner(factory, beforeM12, clock).InitializeAsync();
            var runner = new SqliteTransactionRunner(factory);
            var orderStore = new SqliteOrderStore(factory, runner, idGenerator: new DeterministicIds(), clock: clock);
            var id = Guid.Parse("52400000-0000-0000-0000-000000000001");
            await orderStore.SaveLifecycleAsync(ClosedOrder(id, OrderSourceType.Pos, "upgrade"), [Payment(id)]);
            await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock, new FixedSnapshotService()).InitializeAsync();
            Assert.AreEqual(9L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
            Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM annual_archive_completions;"));
            Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders WHERE order_id=$id;", id));
        }

        using (var paths = new TestPaths())
        {
            var clock = new TestClock(new DateOnly(2027, 2, 1));
            var factory = new SqliteConnectionFactory(paths);
            var beforeM12 = ProductionMigrations.All.Where(migration => migration.Version < 9).ToArray();
            await new SqliteMigrationRunner(factory, beforeM12, clock).InitializeAsync();
            var runner = new SqliteTransactionRunner(factory);
            var orderStore = new SqliteOrderStore(factory, runner, idGenerator: new DeterministicIds(), clock: clock);
            var id = Guid.Parse("52400000-0000-0000-0000-000000000002");
            await orderStore.SaveLifecycleAsync(ClosedOrder(id, OrderSourceType.Pos, "broken upgrade"), [Payment(id)]);
            var broken = beforeM12.Append(new SqliteMigration(9, "broken-annual-archive-completion-ledger", "CREATE TABLE annual_archive_completions (archive_year INTEGER PRIMARY KEY, broken syntax); INVALID SQL;")).ToArray();
            await Assert.ThrowsAsync<DatabaseMigrationException>(() => new SqliteMigrationRunner(factory, broken, clock, new FixedSnapshotService()).InitializeAsync());
            Assert.AreEqual(8L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
            Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='annual_archive_completions';"));
            Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders WHERE order_id=$id;", id));
        }
    }

    private static async Task<SqliteConnectionFactory> InitializeAsync(TestPaths paths, TestClock clock)
    {
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        return factory;
    }

    private static OrderSnapshot ClosedOrder(Guid id, OrderSourceType source, string comment) => new(
        id,
        source,
        OrderStatus.Closed,
        new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 12, 31, 18, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 12, 31, 18, 0, 0, TimeSpan.Zero),
        null,
        FulfilmentMode.Retrait,
        new DateOnly(2026, 12, 31),
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
        Reference = $"M12-{id.ToString("N")[..8]}",
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

    private static async Task<long> ScalarAsync(SqliteConnectionFactory factory, string sql, Guid? id = null)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(factory.LiveDatabasePath);
        return await ScalarAsync(connection, sql, id);
    }

    private static async Task<long> ScalarAsync(SqliteConnectionFactory factory, string sql, Guid firstId, Guid secondId)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(factory.LiveDatabasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id1", firstId.ToString());
        command.Parameters.AddWithValue("$id2", secondId.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql, Guid? id = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (id is not null) command.Parameters.AddWithValue("$id", id.Value.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ScalarTextAsync(SqliteConnectionFactory factory, string sql)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(factory.LiveDatabasePath);
        return await ScalarTextAsync(connection, sql);
    }

    private static async Task<string> ReadSuccessfulLedgerDumpAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(factory.LiveDatabasePath);
        var builder = new StringBuilder();
        foreach (var (name, sql) in new[]
        {
            ("export_batches", "SELECT b.* FROM export_batches b WHERE b.status='SUCCESS' ORDER BY b.batch_id;"),
            ("export_batch_orders", "SELECT o.* FROM export_batch_orders o JOIN export_batches b ON b.batch_id=o.batch_id WHERE b.status='SUCCESS' ORDER BY o.batch_id,o.order_id;"),
            ("export_emissions", "SELECT e.* FROM export_emissions e JOIN export_batches b ON b.batch_id=e.batch_id WHERE b.status='SUCCESS' ORDER BY e.batch_id,e.emission_id;")
        })
        {
            builder.Append(name).Append(':');
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                builder.Append('|');
                for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                {
                    if (ordinal > 0) builder.Append(',');
                    builder.Append(reader.IsDBNull(ordinal)
                        ? "<NULL>"
                        : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture));
                }
            }
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static async Task<Exception?> CaptureExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class Fixture(TestPaths paths, TestClock clock, SqliteConnectionFactory factory, WriteAuthorityGuard guard, DeterministicIds ids, SqliteOrderStore orderStore, SqliteGestionExportStore exportStore, GestionExportService exportService, RecordingNotifier notifier, SqliteAnnualArchiveFinalizationService service) : IDisposable
    {
        public TestPaths Paths { get; } = paths;
        public TestClock Clock { get; } = clock;
        public SqliteConnectionFactory Factory { get; } = factory;
        public SqliteOrderStore OrderStore { get; } = orderStore;
        public SqliteGestionExportStore ExportStore { get; } = exportStore;
        public GestionExportService ExportService { get; } = exportService;
        public RecordingNotifier Notifier { get; } = notifier;
        public SqliteAnnualArchiveFinalizationService Service { get; } = service;
        private WriteAuthorityGuard Guard { get; } = guard;
        private DeterministicIds Ids { get; } = ids;

        public static async Task<Fixture> CreateAsync()
        {
            var paths = new TestPaths();
            var clock = new TestClock(new DateOnly(2027, 2, 1));
            var factory = await InitializeAsync(paths, clock);
            var transactionRunner = new SqliteTransactionRunner(factory);
            var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            var ids = new DeterministicIds();
            var orderStore = new SqliteOrderStore(factory, transactionRunner, idGenerator: ids, clock: clock);
            var exportStore = new SqliteGestionExportStore(factory, orderStore, transactionRunner, ids);
            var notifier = new RecordingNotifier();
            var exportService = new GestionExportService(exportStore, exportStore, clock, guard, notifier, ids);
            var service = new SqliteAnnualArchiveFinalizationService(paths, clock, guard, factory, transactionRunner, exportStore, ids, notifier);
            return new Fixture(paths, clock, factory, guard, ids, orderStore, exportStore, exportService, notifier, service);
        }

        public SqliteAnnualArchiveFinalizationService CreateService(Func<string, Exception?> injector) =>
            new(Paths, Clock, Guard, Factory, new SqliteTransactionRunner(Factory), ExportStore, Ids, Notifier, injector);

        public async Task<AnnualArchiveStagingResult?> StageAsync() =>
            await new SqliteAnnualArchiveStagingService(Paths, Clock, Guard, Factory).StageNextArchiveAsync();

        public void Dispose()
        {
            Guard.Dispose();
            Paths.Dispose();
        }
    }

    private sealed class TestClock(DateOnly businessDate) : IBusinessClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2027, 2, 1, 8, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDateValue { get; set; } = businessDate;
        public DateOnly BusinessDate => BusinessDateValue;
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int count;
        public Guid NewId() => Guid.Parse($"52500000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
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

    private sealed class FixedSnapshotService : ILocalRecoverySnapshotService
    {
        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecoverySnapshotResult("snapshot.db", "snapshot.json", "synthetic", DateTimeOffset.UtcNow, change.Sequence, 8));
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M12.WP2.Tests", Guid.NewGuid().ToString("N"));
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
