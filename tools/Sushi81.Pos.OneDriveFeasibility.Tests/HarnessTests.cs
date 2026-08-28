using Microsoft.Data.Sqlite;

namespace Sushi81.Pos.OneDriveFeasibility.Tests;

[TestClass]
public sealed class HarnessTests
{
    [TestMethod]
    public void CloudFilesEnumMatchesDocumentedCfapiValues()
    {
        Assert.AreEqual(0x00000000u, ParseEnumValue("NoStates"));
        Assert.AreEqual(0x00000001u, ParseEnumValue("Placeholder"));
        Assert.AreEqual(0x00000002u, ParseEnumValue("SyncRoot"));
        Assert.AreEqual(0x00000004u, ParseEnumValue("EssentialPropPresent"));
        Assert.AreEqual(0x00000008u, ParseEnumValue("InSync"));
        Assert.AreEqual(0x00000010u, ParseEnumValue("Partial"));
        Assert.AreEqual(0x00000020u, ParseEnumValue("PartiallyOnDisk"));
        Assert.AreEqual(0xFFFFFFFFu, ParseEnumValue("Invalid"));
    }

    private static uint ParseEnumValue(string name) => (uint)Enum.Parse<CfPlaceholderState>(name);

    [TestMethod]
    // Numeric literals are copied independently from the documented cfapi.h values.
    [DataRow(0x00000000u, CloudFilePublicationState.NotCloudPlaceholder)]
    [DataRow(0x00000001u, CloudFilePublicationState.Pending)]
    [DataRow(0x00000009u, CloudFilePublicationState.InSync)]
    [DataRow(0x00000011u, CloudFilePublicationState.Partial)]
    [DataRow(0x00000021u, CloudFilePublicationState.Partial)]
    [DataRow(0xFFFFFFFFu, CloudFilePublicationState.Invalid)]
    // Legal SYNC_ROOT/ESSENTIAL_PROP_PRESENT bits alone are not IN_SYNC proof; with IN_SYNC they are retained.
    [DataRow(0x00000002u, CloudFilePublicationState.NotCloudPlaceholder)]
    [DataRow(0x00000004u, CloudFilePublicationState.NotCloudPlaceholder)]
    [DataRow(0x00000003u, CloudFilePublicationState.Pending)]
    [DataRow(0x00000005u, CloudFilePublicationState.Pending)]
    [DataRow(0x0000000Bu, CloudFilePublicationState.InSync)]
    [DataRow(0x0000000Du, CloudFilePublicationState.InSync)]
    [DataRow(0x00001009u, CloudFilePublicationState.Unknown)]
    public void CloudFilesStateInterpretationIsFailClosed(uint raw, CloudFilePublicationState expected) =>
        Assert.AreEqual(expected, CloudFileStateReader.Interpret(raw));

    [TestMethod]
    public void LocalPathIsNotUnderUnrelatedRegisteredRoot()
    {
        Assert.IsFalse(WindowsSyncRootCatalog.IsPathUnderRoot(Path.Combine(Path.GetTempPath(), "probe"), Path.Combine(Path.GetTempPath(), "other")));
        Assert.IsTrue(WindowsSyncRootCatalog.IsPathUnderRoot(Path.Combine(Path.GetTempPath(), "root", "child"), Path.Combine(Path.GetTempPath(), "root")));
    }

    [TestMethod]
    public async Task TransportProbeIsReadOnlyAndNoStatesNeverConfirmsUpload()
    {
        using var fixture = new TempFixture();
        var path = Path.Combine(fixture.DirectoryPath, "directed-synthetic.snapshot.db");
        var bytes = Enumerable.Range(0, 8192).Select(index => (byte)(index % 251)).ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        var beforeAttributes = File.GetAttributes(path);

        var report = await TransportProbe.InspectAsync(fixture.DirectoryPath, path);

        Assert.IsFalse(report.IsLocalOnlyConfirmation);
        Assert.AreEqual("Blocked", report.LocalOnlyConfirmation);
        Assert.AreEqual(CloudFilePublicationState.NotCloudPlaceholder, report.CloudFiles.State);
        Assert.AreEqual(0u, report.CloudFiles.RawPlaceholderState);
        Assert.AreEqual(8192L, report.SizeBytes);
        Assert.IsFalse(report.RootValidation.IsAccepted);
        Assert.AreEqual(beforeAttributes, File.GetAttributes(path));
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
        Assert.IsTrue(report.StorageProviderProperties.ContainsKey("System.StorageProviderId"));
        Assert.IsNotNull(report.StorageProviderStatusUi);
    }

    [TestMethod]
    public async Task ValidSyntheticSnapshotAndMarkerValidate()
    {
        using var fixture = new TempFixture();
        var snapshot = await fixture.CreateDatabaseAsync();
        var checksum = await HandoffMetadataCodec.ComputeSha256Async(snapshot);
        var marker = Path.Combine(fixture.DirectoryPath, "handoff-unit-g0000000001-v00000000000000000001.ready.json");
        await HandoffMetadataCodec.WriteAsync(marker, Metadata(checksum, new FileInfo(snapshot).Length));

        var result = await HandoffUnitValidator.ValidateAsync(snapshot, marker, 1, 1);
        Assert.IsTrue(result.IsValid, result.Message);
    }

    [TestMethod]
    public async Task HandoffValidationRejectsMissingAndCorruptUnits()
    {
        using var fixture = new TempFixture();
        var snapshot = await fixture.CreateDatabaseAsync();
        var missing = await HandoffUnitValidator.ValidateAsync(snapshot, Path.Combine(fixture.DirectoryPath, "missing.json"));
        Assert.AreEqual("missing-marker", missing.Code);

        var marker = Path.Combine(fixture.DirectoryPath, "handoff-unit-g0000000001-v00000000000000000001.ready.json");
        await HandoffMetadataCodec.WriteAsync(marker, Metadata(new string('A', 64), new FileInfo(snapshot).Length));
        var checksum = await HandoffUnitValidator.ValidateAsync(snapshot, marker);
        Assert.AreEqual("checksum-mismatch", checksum.Code);

        await File.WriteAllTextAsync(marker, "{\"formatVersion\":99}");
        var malformed = await HandoffUnitValidator.ValidateAsync(snapshot, marker);
        StringAssert.Contains(malformed.Message, "Unsupported metadata format version");
    }

    [TestMethod]
    public async Task HandoffValidationRejectsMismatchedUnitNames()
    {
        using var fixture = new TempFixture();
        var snapshot = await fixture.CreateDatabaseAsync();
        var checksum = await HandoffMetadataCodec.ComputeSha256Async(snapshot);
        var marker = Path.Combine(fixture.DirectoryPath, "handoff-other-g0000000001-v00000000000000000001.ready.json");
        await HandoffMetadataCodec.WriteAsync(marker, Metadata(checksum, new FileInfo(snapshot).Length));

        var result = await HandoffUnitValidator.ValidateAsync(snapshot, marker);
        Assert.AreEqual("identity-mismatch", result.Code);
    }

    [TestMethod]
    public async Task HandoffValidationRejectsSizeLineageGenerationAndVersionMismatches()
    {
        using var fixture = new TempFixture();
        var snapshot = await fixture.CreateDatabaseAsync();
        var checksum = await HandoffMetadataCodec.ComputeSha256Async(snapshot);
        var length = new FileInfo(snapshot).Length;
        var lineage = Guid.NewGuid().ToString();
        var marker = Path.Combine(fixture.DirectoryPath, "handoff-unit-g0000000001-v00000000000000000001.ready.json");

        async Task<HandoffValidationResult> ValidateAsync(HandoffMetadata metadata, long expectedGeneration = 1, long expectedVersion = 1, string? expectedLineage = null)
        {
            if (File.Exists(marker))
            {
                File.SetAttributes(marker, FileAttributes.Normal);
                File.Delete(marker);
            }

            await HandoffMetadataCodec.WriteAsync(marker, metadata);
            return await HandoffUnitValidator.ValidateAsync(snapshot, marker, expectedGeneration, expectedVersion, expectedLineage);
        }

        var valid = Metadata(checksum, length) with { LineageId = lineage };
        Assert.AreEqual("size-mismatch", (await ValidateAsync(valid with { SnapshotByteLength = length + 1 })).Code);
        Assert.AreEqual("generation-mismatch", (await ValidateAsync(valid with { Generation = 2 })).Code);
        Assert.AreEqual("handoff-version-mismatch", (await ValidateAsync(valid with { HandoffVersion = 2 })).Code);
        Assert.AreEqual("lineage-mismatch", (await ValidateAsync(valid with { LineageId = Guid.NewGuid().ToString() }, expectedLineage: lineage)).Code);
    }

    [TestMethod]
    public async Task MarkerIsNotCreatedBeforeSnapshotReachesInSync()
    {
        using var fixture = new TempFixture();
        var probe = new OrderingProbe();
        var request = new SyntheticPublicationRequest(fixture.DirectoryPath, "device-a", Guid.NewGuid().ToString(), 1, 1, TimeSpan.FromSeconds(1), TimeSpan.Zero);
        var result = await SyntheticHandoffPublisher.PublishAsync(request, probe);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsTrue(probe.SnapshotReachedInSync);
        Assert.IsTrue(probe.MarkerWasCreatedAfterSnapshotSync);
        Assert.IsNotNull(result.MarkerPath);
    }

    [TestMethod]
    public async Task FailedSnapshotPublicationNeverCreatesReadyMarker()
    {
        using var fixture = new TempFixture();
        var probe = new AlwaysPendingProbe();
        var request = new SyntheticPublicationRequest(fixture.DirectoryPath, "device-a", Guid.NewGuid().ToString(), 1, 2, TimeSpan.Zero, TimeSpan.Zero);
        var result = await SyntheticHandoffPublisher.PublishAsync(request, probe);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("snapshot-not-synchronized", result.Code);
        Assert.IsFalse(Directory.EnumerateFiles(fixture.DirectoryPath, "*.ready.json").Any());
    }

    [TestMethod]
    public async Task FailedMarkerPublicationNeverReportsReleased()
    {
        using var fixture = new TempFixture();
        var probe = new MarkerPendingProbe();
        var request = new SyntheticPublicationRequest(fixture.DirectoryPath, "device-a", Guid.NewGuid().ToString(), 1, 3, TimeSpan.FromSeconds(1), TimeSpan.Zero);
        var result = await SyntheticHandoffPublisher.PublishAsync(request, probe);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("marker-not-synchronized", result.Code);
        Assert.IsTrue(File.Exists(result.MarkerPath));
    }

    [TestMethod]
    public async Task RetryingAnExistingHandoffDoesNotMutateTheImmutableUnit()
    {
        using var fixture = new TempFixture();
        var request = new SyntheticPublicationRequest(fixture.DirectoryPath, "device-a", Guid.NewGuid().ToString(), 1, 4, TimeSpan.FromSeconds(1), TimeSpan.Zero);
        var first = await SyntheticHandoffPublisher.PublishAsync(request, new AlwaysInSyncProbe());
        var second = await SyntheticHandoffPublisher.PublishAsync(request, new AlwaysInSyncProbe());

        Assert.IsTrue(first.Succeeded, first.Message);
        Assert.IsFalse(second.Succeeded);
        Assert.AreEqual("immutable-unit-exists", second.Code);
    }

    [TestMethod]
    public async Task ThreeDistinctClaimsRemainContentionAndNeverWritable()
    {
        using var fixture = new TempFixture();
        var lineage = Guid.NewGuid().ToString();
        for (var i = 0; i < 3; i++)
        {
            await AcquisitionClaimStore.CreateAsync(fixture.DirectoryPath, new AcquisitionClaim(1, lineage, 1, 1, $"device-{i}", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow));
        }

        var result = await AcquisitionClaimStore.EvaluateAsync(fixture.DirectoryPath, lineage, 1, 1);
        Assert.AreEqual(AcquisitionObservation.Contention, result.Observation);
    }

    [TestMethod]
    public async Task MissingClaimsTransportIsUnknownAndFailClosed()
    {
        var path = Path.Combine(Path.GetTempPath(), "Sushi81-M02-missing", Guid.NewGuid().ToString("N"));
        var result = await AcquisitionClaimStore.EvaluateAsync(path, Guid.NewGuid().ToString(), 1, 1);
        Assert.AreEqual(AcquisitionObservation.Unknown, result.Observation);
    }

    private static HandoffMetadata Metadata(string checksum, long length) => new(
        1, Guid.NewGuid().ToString(), 1, 1, "device-a", "SHA-256", checksum, length,
        DateTimeOffset.UtcNow, "ready", "released");

    private sealed class OrderingProbe : ICloudFileStateReader
    {
        private bool _snapshotSync;
        public bool SnapshotReachedInSync { get; private set; }
        public bool MarkerWasCreatedAfterSnapshotSync { get; private set; }

        public CloudFileObservation Observe(string path)
        {
            if (path.EndsWith(".snapshot.db", StringComparison.Ordinal))
            {
                if (!_snapshotSync)
                {
                    _snapshotSync = true;
                    return new(path, CloudFilePublicationState.Pending, 1);
                }

                SnapshotReachedInSync = true;
                return new(path, CloudFilePublicationState.InSync, 0x00000009u);
            }

            MarkerWasCreatedAfterSnapshotSync = SnapshotReachedInSync && File.Exists(path);
            return new(path, CloudFilePublicationState.InSync, 0x00000009u);
        }
    }

    private sealed class AlwaysPendingProbe : ICloudFileStateReader
    {
        public CloudFileObservation Observe(string path) => new(path, CloudFilePublicationState.Pending, 1);
    }

    private sealed class MarkerPendingProbe : ICloudFileStateReader
    {
        public CloudFileObservation Observe(string path) => path.EndsWith(".snapshot.db", StringComparison.Ordinal)
            ? new(path, CloudFilePublicationState.InSync, 0x00000009u)
            : new(path, CloudFilePublicationState.Pending, 1);
    }

    private sealed class AlwaysInSyncProbe : ICloudFileStateReader
    {
        public CloudFileObservation Observe(string path) => new(path, CloudFilePublicationState.InSync, 0x00000009u);
    }

    private sealed class TempFixture : IDisposable
    {
        public TempFixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "Sushi81-M02-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public async Task<string> CreateDatabaseAsync()
        {
            var path = Path.Combine(DirectoryPath, "handoff-unit-g0000000001-v00000000000000000001.snapshot.db");
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE synthetic (id INTEGER PRIMARY KEY, value TEXT NOT NULL); INSERT INTO synthetic(value) VALUES ('test');";
            await command.ExecuteNonQueryAsync();
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(DirectoryPath, true);
            }
        }
    }
}
