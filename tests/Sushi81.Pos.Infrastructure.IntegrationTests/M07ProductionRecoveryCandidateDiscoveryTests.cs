using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Infrastructure.Authority;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M07ProductionRecoveryCandidateDiscoveryTests
{
    [TestMethod]
    public async Task GitHubCompleteUploadedGrantAndExactSnapshotIsEligible()
    {
        using var fixture = await DiscoveryFixture.CreateAsync();
        fixture.Transport.AddGithubUnit("20260907120000", fixture.LineageId, 1, 12, 4);

        var result = await fixture.DiscoverAsync();

        Assert.HasCount(1, result.Candidates);
        Assert.AreEqual(12, result.Recommended!.BusinessRevision);
        Assert.AreEqual(4, result.Recommended.HandoffVersion);
        Assert.AreEqual(RecoveryCandidateType.GitHubHandoff, result.Recommended.Type);
    }

    [TestMethod]
    public async Task GitHubSnapshotWithoutMatchingGrantIsIneligible()
    {
        using var fixture = await DiscoveryFixture.CreateAsync();
        fixture.Transport.AddSnapshotOnly("20260907120001", revision: 12);

        var result = await fixture.DiscoverAsync();

        Assert.IsEmpty(result.Candidates);
    }

    [TestMethod]
    public async Task GitHubWrongTargetGrantRemainsRecoveryEvidenceOnlyWhenTheUnitIsValid()
    {
        using var fixture = await DiscoveryFixture.CreateAsync();
        var wrongTarget = Guid.NewGuid();
        fixture.Transport.AddGithubUnit("20260907120002", fixture.LineageId, 1, 12, 4, targetDeviceId: wrongTarget);

        var result = await fixture.DiscoverAsync();

        Assert.HasCount(1, result.Candidates);
        Assert.AreNotEqual(fixture.LocalDeviceId, wrongTarget);
        Assert.AreEqual(RecoveryCandidateType.GitHubHandoff, result.Candidates[0].Type);
    }

    [TestMethod]
    public async Task GitHubDuplicateGrantNameIsRejectedWithoutBroadCleanup()
    {
        using var fixture = await DiscoveryFixture.CreateAsync();
        fixture.Transport.AddGithubUnit("20260907120003", fixture.LineageId, 1, 12, 4);
        fixture.Transport.DuplicateAssetByName(GitHubHandoffAssetNames.CreateGrantName("20260907120003.snapshot.db"));

        var result = await fixture.DiscoverAsync();

        Assert.IsEmpty(result.Candidates);
        Assert.IsGreaterThan(0, fixture.Transport.AssetCount, "Discovery is read-only and must not delete contradictory evidence.");
    }

    [TestMethod]
    public async Task GitHubStarterAndMalformedGrantAreRejected()
    {
        using var fixture = await DiscoveryFixture.CreateAsync();
        fixture.Transport.AddRawAsset(
            7101,
            "20260907120004.grant.json",
            [1, 2, 3],
            state: "starter");
        fixture.Transport.AddRawAsset(
            7102,
            "20260907120005.grant.json",
            System.Text.Encoding.UTF8.GetBytes("not-json"));

        var result = await fixture.DiscoverAsync();

        Assert.IsEmpty(result.Candidates);
    }

    [TestMethod]
    public async Task GitHubContradictorySnapshotDigestAndReceiptIdentityAreRejected()
    {
        using var fixture = await DiscoveryFixture.CreateAsync();
        var unit = fixture.Transport.AddGithubUnit("20260907120006", fixture.LineageId, 1, 12, 4);
        fixture.Transport.ReplaceBytes(unit.SnapshotAssetId, [8, 8, 8]);

        var result = await fixture.DiscoverAsync();

        Assert.IsEmpty(result.Candidates);
    }

    [TestMethod]
    public async Task GitHubWrongLineageGenerationAndExactReceiptMetadataAreRejected()
    {
        using var fixture = await DiscoveryFixture.CreateAsync();
        fixture.Transport.AddGithubUnit("20260907120007", Guid.NewGuid(), 1, 12, 4);
        fixture.Transport.AddGithubUnit("20260907120008", fixture.LineageId, 2, 12, 4);

        var result = await fixture.DiscoverAsync();

        Assert.IsEmpty(result.Candidates);
    }

    [TestMethod]
    public async Task GitHubCorruptSqliteMissingSchemaAndEmbeddedRevisionMismatchAreRejected()
    {
        using var fixture = await DiscoveryFixture.CreateAsync();
        fixture.Transport.AddRawGithubUnit("20260907120009", fixture.LineageId, 1, 12, 4, CreateBytesWithoutSchema());
        fixture.Transport.AddRawGithubUnit("20260907120010", fixture.LineageId, 1, 12, 4, [4, 5, 6]);
        fixture.Transport.AddGithubUnit("20260907120011", fixture.LineageId, 1, 12, 4, databaseRevision: 11);

        var result = await fixture.DiscoverAsync();

        Assert.IsEmpty(result.Candidates);
    }

    [TestMethod]
    public async Task GitHubFreshnessIgnoresCreatedTimestampAndUsesRevisionThenHandoffThenStableId()
    {
        using var fixture = await DiscoveryFixture.CreateAsync();
        fixture.Transport.AddGithubUnit("20000101000000", fixture.LineageId, 1, 20, 1, createdAtUtc: new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero), transferId: new Guid("00000000-0000-0000-0000-000000000001"));
        fixture.Transport.AddGithubUnit("20991231235959", fixture.LineageId, 1, 19, 99, createdAtUtc: new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), transferId: new Guid("00000000-0000-0000-0000-000000000002"));
        fixture.Transport.AddGithubUnit("20260907120012", fixture.LineageId, 1, 20, 2, transferId: new Guid("00000000-0000-0000-0000-000000000003"));

        var result = await fixture.DiscoverAsync();

        Assert.HasCount(3, result.Candidates);
        Assert.AreEqual(20, result.Recommended!.BusinessRevision);
        Assert.AreEqual(2, result.Recommended.HandoffVersion);
        Assert.IsGreaterThanOrEqualTo(result.Candidates[1].HandoffVersion, result.Candidates[2].HandoffVersion);
    }

    [TestMethod]
    public async Task OneDriveValidCheckpointIsEligible()
    {
        using var fixture = await DiscoveryFixture.CreateAsync(includeGitHub: false);
        fixture.AddCheckpoint("valid", fixture.LineageId, 1, 14, 3);

        var result = await fixture.DiscoverAsync();

        Assert.HasCount(1, result.Candidates);
        Assert.AreEqual(RecoveryCandidateType.OneDriveCheckpoint, result.Recommended!.Type);
        Assert.AreEqual(14, result.Recommended.BusinessRevision);
    }

    [TestMethod]
    public async Task OneDriveIncompleteWrongBindingAndContradictoryMetadataAreRejected()
    {
        using var fixture = await DiscoveryFixture.CreateAsync(includeGitHub: false);
        fixture.AddCheckpoint("wrong-lineage", Guid.NewGuid(), 1, 14, 3);
        fixture.AddCheckpoint("wrong-generation", fixture.LineageId, 2, 14, 3);
        fixture.AddCheckpoint("wrong-directory-id", fixture.LineageId, 1, 14, 3, directoryId: Guid.NewGuid());
        fixture.AddCheckpoint("missing-db", fixture.LineageId, 1, 14, 3, writeDatabase: false);

        var result = await fixture.DiscoverAsync();

        Assert.IsEmpty(result.Candidates);
    }

    [TestMethod]
    public async Task OneDriveWrongHashSizeCorruptSqliteMissingSchemaAndRevisionAreRejected()
    {
        using var fixture = await DiscoveryFixture.CreateAsync(includeGitHub: false);
        fixture.AddCheckpoint("wrong-hash", fixture.LineageId, 1, 14, 3, hashOverride: new string('A', 64));
        fixture.AddCheckpoint("wrong-size", fixture.LineageId, 1, 14, 3, sizeDelta: 1);
        fixture.AddCheckpoint("corrupt", fixture.LineageId, 1, 14, 3, databaseBytes: [7, 7, 7]);
        fixture.AddCheckpoint("missing-schema", fixture.LineageId, 1, 14, 3, databaseBytes: CreateBytesWithoutSchema());
        fixture.AddCheckpoint("wrong-revision", fixture.LineageId, 1, 14, 3, databaseRevision: 13);

        var result = await fixture.DiscoverAsync();

        Assert.IsEmpty(result.Candidates);
    }

    [TestMethod]
    public async Task OneDriveTimestampSkewCannotOverrideRevisionHighWaterOrStableIdentity()
    {
        using var fixture = await DiscoveryFixture.CreateAsync(includeGitHub: false);
        fixture.AddCheckpoint("old-business", fixture.LineageId, 1, 20, 1, createdAtUtc: new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero), checkpointId: new Guid("00000000-0000-0000-0000-000000000001"));
        fixture.AddCheckpoint("future-clock", fixture.LineageId, 1, 19, 99, createdAtUtc: new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), checkpointId: new Guid("00000000-0000-0000-0000-000000000002"));
        fixture.AddCheckpoint("equal-higher-handoff", fixture.LineageId, 1, 20, 2, checkpointId: new Guid("00000000-0000-0000-0000-000000000003"));

        var result = await fixture.DiscoverAsync();

        Assert.HasCount(3, result.Candidates);
        Assert.AreEqual(20, result.Recommended!.BusinessRevision);
        Assert.AreEqual(2, result.Recommended.HandoffVersion);
    }

    private static byte[] CreateBytesWithoutSchema()
    {
        var path = Path.Combine(Path.GetTempPath(), $"m07-no-schema-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE other_table(value TEXT NOT NULL);";
                command.ExecuteNonQuery();
            }

            return File.ReadAllBytes(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class DiscoveryFixture : IDisposable
    {
        private DiscoveryFixture(bool includeGitHub)
        {
            Root = Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.CandidateDiscovery", Guid.NewGuid().ToString("N"));
            OneDriveRoot = Path.Combine(Root, "OneDrive");
            Directory.CreateDirectory(Root);
            LineageId = Guid.NewGuid();
            LocalDeviceId = Guid.NewGuid();
            Paths = new DiscoveryPaths(Root);
            Transport = new CandidateTransport(Root, includeGitHub);
        }

        public string Root { get; }
        public string OneDriveRoot { get; }
        public Guid LineageId { get; }
        public Guid LocalDeviceId { get; }
        public DiscoveryPaths Paths { get; }
        public CandidateTransport Transport { get; }

        public static Task<DiscoveryFixture> CreateAsync(bool includeGitHub = true) => Task.FromResult(new DiscoveryFixture(includeGitHub));

        public async Task<RecoveryCandidateDiscoveryResult> DiscoverAsync()
        {
            var state = new AuthorityProtocolState(1, LocalDeviceId, "Recovery", LineageId, 1, 0, 0, AuthorityPhase.NonAuthoritativeReadOnly);
            var discovery = new RecoveryCandidateDiscovery(Paths, Transport.IncludeGitHub ? Transport : null, OneDriveRoot);
            return await discovery.DiscoverAsync(state);
        }

        public void AddCheckpoint(
            string label,
            Guid lineageId,
            long generation,
            long revision,
            long handoff,
            DateTimeOffset? createdAtUtc = null,
            Guid? checkpointId = null,
            Guid? directoryId = null,
            bool writeDatabase = true,
            byte[]? databaseBytes = null,
            string? hashOverride = null,
            long sizeDelta = 0,
            long? databaseRevision = null)
        {
            var id = checkpointId ?? Guid.NewGuid();
            var directory = Path.Combine(OneDriveRoot, "DisasterRecovery", "Checkpoints", "1", (directoryId ?? id).ToString("N"));
            Directory.CreateDirectory(directory);
            var bytes = databaseBytes ?? CreateValidDatabase(databaseRevision ?? revision);
            var databasePath = Path.Combine(directory, "checkpoint.db");
            if (writeDatabase) File.WriteAllBytes(databasePath, bytes);
            var metadata = new RecoveryCheckpointMetadata(
                1, "M07", id, lineageId, generation, Guid.NewGuid(), revision, handoff,
                createdAtUtc ?? new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero),
                "checkpoint.db", bytes.LongLength + sizeDelta,
                hashOverride ?? Convert.ToHexString(SHA256.HashData(bytes)));
            File.WriteAllText(Path.Combine(directory, $"{label}.metadata.json"), JsonSerializer.Serialize(metadata, JsonOptions));
            File.Move(Path.Combine(directory, $"{label}.metadata.json"), Path.Combine(directory, "metadata.json"));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    }

    private sealed class CandidateTransport(string root, bool includeGitHub) : IGitHubHandoffTransport
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        private readonly GitHubReleaseContainer release = new(91, "sushi81-handoff-v1", "https://uploads.example/releases/91/assets{?name}", false, false);
        private readonly List<GitHubRemoteAsset> assets = [];
        private readonly Dictionary<long, byte[]> contents = [];
        private long nextId = 1000;
        private readonly string root = root;

        public bool IncludeGitHub { get; } = includeGitHub;
        public int AssetCount => assets.Count;

        public GitHubUnit AddGithubUnit(
            string timestamp,
            Guid lineageId,
            long generation,
            long revision,
            long handoff,
            Guid? targetDeviceId = null,
            DateTimeOffset? createdAtUtc = null,
            Guid? transferId = null,
            long? databaseRevision = null)
        {
            return AddRawGithubUnit(timestamp, lineageId, generation, revision, handoff,
                CreateValidDatabase(databaseRevision ?? revision), targetDeviceId, createdAtUtc, transferId);
        }

        public GitHubUnit AddRawGithubUnit(
            string timestamp,
            Guid lineageId,
            long generation,
            long revision,
            long handoff,
            byte[] databaseBytes,
            Guid? targetDeviceId = null,
            DateTimeOffset? createdAtUtc = null,
            Guid? transferId = null)
        {
            var snapshotName = timestamp + ".snapshot.db";
            var grantName = timestamp + ".grant.json";
            var snapshotId = nextId++;
            var snapshotHash = Convert.ToHexString(SHA256.HashData(databaseBytes)).ToLowerInvariant();
            AddRawAsset(snapshotId, snapshotName, databaseBytes, "uploaded");
            var grant = new NormalHandoffGrant(
                "M07", transferId ?? Guid.NewGuid(), lineageId, generation, handoff,
                Guid.NewGuid(), targetDeviceId ?? Guid.NewGuid(), revision,
                new RemoteAssetEvidence(release.Id, snapshotId, snapshotName, databaseBytes.LongLength, snapshotHash),
                createdAtUtc ?? new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero),
                createdAtUtc ?? new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
            var grantBytes = JsonSerializer.SerializeToUtf8Bytes(grant, JsonOptions);
            var grantId = nextId++;
            AddRawAsset(grantId, grantName, grantBytes, "uploaded");
            return new GitHubUnit(snapshotId, grantId, snapshotName, grantName);
        }

        public void AddSnapshotOnly(string timestamp, long revision)
        {
            AddRawAsset(nextId++, timestamp + ".snapshot.db", CreateValidDatabase(revision), "uploaded");
        }

        public void AddRawAsset(long id, string name, byte[] bytes, string state = "uploaded")
        {
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            assets.Add(new GitHubRemoteAsset(id, name, bytes.LongLength, state, "sha256:" + digest, new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero)));
            contents[id] = bytes;
        }

        public void DuplicateAssetByName(string name)
        {
            var existing = assets.Single(asset => asset.Name == name);
            AddRawAsset(nextId++, existing.Name, contents[existing.Id], existing.State);
        }

        public void ReplaceBytes(long assetId, byte[] bytes)
        {
            contents[assetId] = bytes;
        }

        public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default) => Task.FromResult(release);
        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubRemoteAsset>>(assets.ToArray());
        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromResult(assets.Single(asset => asset.Id == assetId));
        public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(contents[assetId], writable: false));
        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException("Discovery must not delete evidence.");
        public Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, Stream content, long contentLength, string localSha256, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record GitHubUnit(long SnapshotAssetId, long GrantAssetId, string SnapshotName, string GrantName);

    private sealed class DiscoveryPaths(string root) : IAppPaths
    {
        public string RootDirectory { get; } = root;
        public string DataDirectory { get; } = Path.Combine(root, "Data");
        public string RecoveryDirectory { get; } = Path.Combine(root, "Recovery");
        public string CacheDirectory { get; } = Path.Combine(root, "Cache");
        public string LogsDirectory { get; } = Path.Combine(root, "Logs");
        public string ConfigDirectory { get; } = Path.Combine(root, "Config");
        public string TempDirectory { get; } = Path.Combine(root, "Temp");
        public string LiveDatabasePath { get; } = Path.Combine(root, "Data", "live.db");
        public void EnsureInitialized() => Directory.CreateDirectory(TempDirectory);
    }

    private static byte[] CreateValidDatabase(long revision)
    {
        var path = Path.Combine(Path.GetTempPath(), $"m07-valid-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES (5); CREATE TABLE foundation_metadata(key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL); INSERT INTO foundation_metadata(key,value) VALUES ('business_data_revision','{revision}');";
                command.ExecuteNonQuery();
            }

            return File.ReadAllBytes(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
