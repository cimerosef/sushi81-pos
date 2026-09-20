using ClosedXML.Excel;
using Sushi81.Pos.Application.Export;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Export;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M11Wp2ExportWorkflowTests
{
    [TestMethod]
    public async Task FinalizationFailureCanRetrySamePreparedBatchAndRegenerationUsesImmutablePayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M11.WP2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var clock = new FixedClock();
            var ids = new DeterministicIds();
            var source = new InMemorySourceReader(ClosedOrder());
            var ledger = new InMemoryLedger();
            using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            var exportService = new GestionExportService(source, ledger, clock, guard, new NoOpDurableChangeNotifier(), ids);
            var workflow = new GestionExportWorkbookService(exportService, ledger, new ClosedXmlGestionExportWorkbookGateway(), clock, guard);
            var firstPath = Path.Combine(root, "first.xlsx");

            var prepared = (await exportService.PrepareBatchAsync(new ExportSelectionOptions(), "test-version")).Batch!;
            guard.SetState(WriteAuthorityState.NonAuthoritativeReadOnly);
            await Assert.ThrowsAsync<WriteAuthorityException>(async () => await workflow.FinalizePreparedBatchAsync(prepared.Payload.Meta.BatchId, firstPath));
            Assert.IsFalse(File.Exists(firstPath), "A read-only device must not stage or finalize a new workbook.");
            guard.SetState(WriteAuthorityState.Authoritative);
            ledger.FailNextSuccessCommit = true;
            await Assert.ThrowsAsync<IOException>(async () => await workflow.FinalizePreparedBatchAsync(prepared.Payload.Meta.BatchId, firstPath));
            Assert.IsTrue(File.Exists(firstPath), "The finalized workbook remains available for the ledger retry.");
            Assert.AreEqual(ExportBatchStatus.Prepared, (await ledger.GetBatchAsync(prepared.Payload.Meta.BatchId))!.Status);

            await workflow.FinalizePreparedBatchAsync(prepared.Payload.Meta.BatchId, firstPath);
            Assert.AreEqual(ExportBatchStatus.Success, (await ledger.GetBatchAsync(prepared.Payload.Meta.BatchId))!.Status);
            Assert.AreEqual(1, ledger.SuccessfulEmissionCount);

            var originalBytes = await File.ReadAllBytesAsync(firstPath);
            source.Current = source.Current with
            {
                Items = [source.Current.Items[0] with { ProductName = "Catalogue changed later" }]
            };
            var regeneratedPath = Path.Combine(root, "regenerated.xlsx");
            await workflow.RegenerateAsync(prepared.Payload.Meta.BatchId, regeneratedPath);
            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(firstPath));

            using var regenerated = new XLWorkbook(regeneratedPath);
            Assert.AreEqual("M11 produit", regenerated.Worksheet("OrderLines").Cell(2, 5).GetString());
            Assert.AreEqual(1, ledger.SuccessfulEmissionCount, "Regeneration must not create a second emission.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task NewBatchCannotDestroyExistingGoodFileWhenFinalizationIsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M11.WP2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var clock = new FixedClock();
            var ids = new DeterministicIds();
            var source = new InMemorySourceReader(ClosedOrder());
            var ledger = new InMemoryLedger();
            using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            var exportService = new GestionExportService(source, ledger, clock, guard, new NoOpDurableChangeNotifier(), ids);
            var workflow = new GestionExportWorkbookService(exportService, ledger, new ClosedXmlGestionExportWorkbookGateway(), clock, guard);
            var target = Path.Combine(root, "stable.xlsx");
            await workflow.GenerateAsync(new ExportSelectionOptions(), "test-version", target);
            var before = await File.ReadAllBytesAsync(target);

            source.Current = source.Current with
            {
                Items = [source.Current.Items[0] with { ProductName = "new pending update" }]
            };
            await Assert.ThrowsAsync<InvalidDataException>(async () => await workflow.GenerateAsync(new ExportSelectionOptions(), "test-version", target));

            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(target));
            Assert.AreEqual(1, ledger.SuccessfulEmissionCount);
            Assert.AreEqual(1, ledger.PreparedBatchCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static OrderSnapshot ClosedOrder() => new(
        Guid.Parse("32000000-0000-0000-0000-000000000001"),
        OrderSourceType.Pos,
        OrderStatus.Closed,
        new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 18, 8, 30, 0, TimeSpan.Zero),
        null,
        FulfilmentMode.Retrait,
        new DateOnly(2026, 9, 18),
        new TimeOnly(12, 0),
        false,
        null,
        null,
        "M11 workflow test",
        Money.FromCents(1000),
        false,
        false,
        null,
        Money.Zero,
        [new(Guid.Parse("32000000-0000-0000-0000-000000000002"), 0, Guid.Parse("32000000-0000-0000-0000-000000000003"), "P-M11", "M11 produit", "Plats", Money.FromCents(1000), 10m, true, 1, Money.FromCents(1000), Money.FromCents(1000), [])],
        [new(10m, Money.FromCents(1000), Money.Zero, Guid.Parse("32000000-0000-0000-0000-000000000004"))])
    {
        CardPaymentTtc = Money.FromCents(1000),
        CashPaymentTtc = Money.Zero
    };

    private sealed class InMemorySourceReader(OrderSnapshot initial) : IExportOrderSourceReader
    {
        public OrderSnapshot Current { get; set; } = initial;

        public Task<IReadOnlyList<ExportOrderSourceRecord>> ListExportOrderSourcesAsync(CancellationToken cancellationToken = default)
        {
            var adjustment = new PaymentAdjustment(
                Guid.Parse("32000000-0000-0000-0000-000000000005"),
                Current.Id,
                PaymentBucket.Card,
                Money.FromCents(1000),
                new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero));
            return Task.FromResult<IReadOnlyList<ExportOrderSourceRecord>>([new(Current, [adjustment])]);
        }
    }

    private sealed class InMemoryLedger : IExportLedgerStore
    {
        private readonly Dictionary<Guid, ExportBatchRecord> batches = [];
        private readonly List<ExportEmissionRecord> emissions = [];

        public bool FailNextSuccessCommit { get; set; }
        public int SuccessfulEmissionCount => emissions.Count;
        public int PreparedBatchCount => batches.Values.Count(batch => batch.Status == ExportBatchStatus.Prepared);

        public Task<IReadOnlyList<ExportEmissionRecord>> ListLatestSuccessfulEmissionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExportEmissionRecord>>(emissions.ToArray());

        public Task<ExportBatchRecord?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken = default) =>
            Task.FromResult(batches.TryGetValue(batchId, out var batch) ? batch : null);

        public Task PrepareBatchAsync(ExportBatchRecord batch, CancellationToken cancellationToken = default)
        {
            batches.Add(batch.Payload.Meta.BatchId, batch);
            return Task.CompletedTask;
        }

        public Task MarkBatchSucceededAsync(Guid batchId, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default)
        {
            if (FailNextSuccessCommit)
            {
                FailNextSuccessCommit = false;
                throw new IOException("Synthetic ledger commit failure after workbook finalization.");
            }

            var batch = batches[batchId];
            batches[batchId] = batch with { Status = ExportBatchStatus.Success, CompletedAtUtc = completedAtUtc };
            foreach (var order in batch.Payload.Orders)
            {
                var positive = order with { Action = ExportAction.Create };
                emissions.Add(new ExportEmissionRecord(
                    batchId,
                    order.OrderId,
                    order.Action,
                    ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializePositive(positive)),
                    positive,
                    order.FulfilmentDate,
                    order.SettlementDate,
                    completedAtUtc));
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 20);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int count;
        public Guid NewId() => Guid.Parse($"32000000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
    }
}
