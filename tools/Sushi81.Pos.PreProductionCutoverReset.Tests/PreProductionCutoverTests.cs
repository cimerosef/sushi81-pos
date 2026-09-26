using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sushi81.Pos.PreProductionCutoverReset;
using Sushi81.Pos.Infrastructure.Recovery;

namespace Sushi81.Pos.PreProductionCutoverReset.Tests;

[TestClass]
public sealed class PreProductionCutoverTests
{
    [TestMethod]
    public void DefaultRequestIsReadOnlyAndProducesNoBackupOrDataMutation()
    {
        using var fixture = new CutoverFixture();
        var databaseHash = fixture.HashDatabase();
        var configAndSystem = fixture.HashConfigurationAndSystem();
        var archive = fixture.HashArchive();
        var result = fixture.Service.Run(new(false, null, null));

        Assert.IsFalse(result.Changed);
        Assert.AreEqual("DRY RUN", result.Preflight.Mode);
        Assert.AreEqual("ClosedRetainedAuthority", result.Preflight.AuthorityPhase);
        Assert.AreEqual(11, result.Preflight.DatabaseSchemaVersion);
        Assert.AreEqual(0, result.Preflight.ForeignKeyViolationCount);
        Assert.IsTrue(result.Preflight.OrdersByStatusAndSource.Any(row => row.Status == "OPEN" && row.SourceType == "POS" && row.Count == 1));
        Assert.IsTrue(result.Preflight.OrdersByStatusAndSource.Any(row => row.Status == "CLOSED" && row.SourceType == "HIBOUTIK_PASTE" && row.Count == 1));
        Assert.AreEqual(41L, fixture.ReadRevision());
        Assert.AreEqual(databaseHash, fixture.HashDatabase());
        Assert.IsTrue(DictionaryEqual(configAndSystem, fixture.HashConfigurationAndSystem()));
        Assert.IsTrue(DictionaryEqual(archive, fixture.HashArchive()));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "CutoverBackups")));
    }

    [TestMethod]
    public void AuthorityBusinessRevisionAheadOfDatabaseRefusesBeforeCreatingBackups()
    {
        using var fixture = new CutoverFixture(authorityBusinessRevision: 42);
        var databaseHash = fixture.HashDatabase();
        var authorityHash = fixture.HashAuthorityState();
        var archive = fixture.HashArchive();

        Assert.Throws<InvalidDataException>(() => fixture.Execute());

        Assert.AreEqual(databaseHash, fixture.HashDatabase());
        Assert.AreEqual(authorityHash, fixture.HashAuthorityState());
        Assert.IsTrue(DictionaryEqual(archive, fixture.HashArchive()));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "CutoverBackups")));
    }

    [TestMethod]
    public async Task LaggingAuthorityBusinessRevisionSynchronizesToCutoverDatabaseRevisionBeforeRestart()
    {
        using var fixture = new CutoverFixture(authorityProtocolRevision: 12, authorityBusinessRevision: 40);
        var before = fixture.ReadAuthorityProtocol();

        var result = fixture.Execute();
        var after = fixture.ReadAuthorityProtocol();
        var productionSeedRevision = await SqliteBusinessRevisionStore.ReadFromDatabaseAsync(fixture.DatabasePath);

        Assert.AreEqual(41L, result.BusinessDataRevisionBefore);
        Assert.AreEqual(42L, result.BusinessDataRevisionAfter);
        Assert.AreEqual(12L, result.AuthorityProtocolRevisionBefore);
        Assert.AreEqual(13L, result.AuthorityProtocolRevisionAfter);
        Assert.AreEqual(40L, result.AuthorityBusinessRevisionBefore);
        Assert.AreEqual(42L, result.AuthorityBusinessRevisionAfter);
        Assert.AreEqual(before.Revision + 1, after.Revision);
        Assert.AreEqual(42L, after.BusinessRevision);
        Assert.AreEqual(productionSeedRevision, after.BusinessRevision,
            "M07 startup must seed its business revision from the synchronized production database contract.");
        Assert.AreEqual(before.DeviceId, after.DeviceId);
        Assert.AreEqual(before.DisplayName, after.DisplayName);
        Assert.AreEqual(before.LineageId, after.LineageId);
        Assert.AreEqual(before.Generation, after.Generation);
        Assert.AreEqual(before.HandoffVersion, after.HandoffVersion);
        Assert.AreEqual(before.Phase, after.Phase);
    }

    [TestMethod]
    public void ExactRootAndConfirmationTokenAreBothRequiredBeforeExecution()
    {
        using var fixture = new CutoverFixture();
        var hash = fixture.HashDatabase();
        Assert.Throws<InvalidOperationException>(() => fixture.Service.Run(new(true, "wrong", fixture.Root)));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.Run(new(true, PreProductionCutoverService.ConfirmationToken, fixture.Root + "-other")));
        Assert.AreEqual(hash, fixture.HashDatabase());
        Assert.AreEqual(41L, fixture.ReadRevision());
    }

    [TestMethod]
    public void RunningDesktopProcessRefusesExecutionWithoutMutation()
    {
        using var fixture = new CutoverFixture();
        fixture.Host.DesktopRunning = true;
        var hash = fixture.HashDatabase();
        Assert.Throws<InvalidOperationException>(() => fixture.Execute());
        Assert.AreEqual(hash, fixture.HashDatabase());
        Assert.AreEqual(41L, fixture.ReadRevision());
    }

    [TestMethod]
    public void WrongInstalledApplicationProvenanceRefusesExecution()
    {
        using var fixture = new CutoverFixture();
        fixture.Host.WrongProvenance = true;
        var hash = fixture.HashDatabase();
        Assert.Throws<InvalidDataException>(() => fixture.Execute());
        Assert.AreEqual(hash, fixture.HashDatabase());
        Assert.AreEqual(41L, fixture.ReadRevision());
    }

    [TestMethod]
    public void ReadOnlyAndTransitioningAuthorityPhasesRefuseExecutionWithoutMutation()
    {
        foreach (var phase in new[] { 0, 1, 4, 5, 6, 7, 8, 9, 10, 11, 12 })
        {
            using var fixture = new CutoverFixture(authorityPhase: phase);
            var hash = fixture.HashDatabase();
            Assert.Throws<InvalidDataException>(() => fixture.Execute());
            Assert.AreEqual(hash, fixture.HashDatabase());
            Assert.AreEqual(41L, fixture.ReadRevision());
        }
    }

    [TestMethod]
    public void ActualAuthoritativeCloseRetainPhaseSupportsDryRunAndExecute()
    {
        using var fixture = new CutoverFixture(authorityPhase: 2);
        var dryRun = fixture.Service.Run(new(false, null, null));

        Assert.IsFalse(dryRun.Changed);
        Assert.AreEqual("Authoritative", dryRun.Preflight.AuthorityPhase);
        Assert.AreEqual(41L, fixture.ReadRevision());

        var result = fixture.Execute();

        Assert.IsTrue(result.Changed);
        Assert.AreEqual("Authoritative", result.Preflight.AuthorityPhase);
        Assert.AreEqual(42L, fixture.ReadRevision());
        Assert.AreEqual(0, result.Preflight.ForeignKeyViolationCount);
    }

    [TestMethod]
    public void ActiveTransferOrRecoveryEvidenceRefusesExecution()
    {
        using var transferFixture = new CutoverFixture();
        transferFixture.RewriteAuthority(transfer: new { transferId = Guid.NewGuid() });
        Assert.Throws<InvalidDataException>(() => transferFixture.Execute());

        using var recoveryFixture = new CutoverFixture();
        recoveryFixture.RewriteAuthority(recovery: new { recoveryId = Guid.NewGuid() });
        Assert.Throws<InvalidDataException>(() => recoveryFixture.Execute());
        Assert.AreEqual(41L, transferFixture.ReadRevision());
        Assert.AreEqual(41L, recoveryFixture.ReadRevision());
    }

    [TestMethod]
    public void MissingLiveDatabaseRefusesWithoutRecreatingIt()
    {
        using var fixture = new CutoverFixture();
        File.Delete(fixture.DatabasePath);
        Assert.Throws<InvalidDataException>(() => fixture.Execute());
        Assert.IsFalse(File.Exists(fixture.DatabasePath));
    }

    [TestMethod]
    public void WrongSchemaAndCorruptDatabaseRefuseWithoutReset()
    {
        using (var schemaFixture = new CutoverFixture())
        {
            using (var connection = new SqliteConnection($"Data Source={schemaFixture.DatabasePath};Mode=ReadWrite;Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE schema_migrations SET version=12 WHERE version=11;";
                command.ExecuteNonQuery();
            }
            var hash = schemaFixture.HashDatabase();
            Assert.Throws<InvalidDataException>(() => schemaFixture.Execute());
            Assert.AreEqual(hash, schemaFixture.HashDatabase());
        }

        using (var corruptFixture = new CutoverFixture())
        {
            File.WriteAllBytes(corruptFixture.DatabasePath, [0, 1, 2, 3, 4]);
            var bytes = File.ReadAllBytes(corruptFixture.DatabasePath);
            Assert.Throws<SqliteException>(() => corruptFixture.Execute());
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(corruptFixture.DatabasePath));
        }
    }

    [TestMethod]
    public async Task PopulatedSyntheticSchemaIsClearedInOneRevisionWhilePreservingSettingsMigrationsAndIdentity()
    {
        using var fixture = new CutoverFixture();
        var settingsLinesBefore = fixture.ReadOnlyStateSnapshot().Split('\n').Where(line => line.StartsWith("settings:", StringComparison.Ordinal)).ToArray();
        var migrationLinesBefore = fixture.ReadOnlyStateSnapshot().Split('\n').Where(line => line.StartsWith("migration:", StringComparison.Ordinal)).ToArray();
        var configAndSystemBefore = fixture.HashConfigurationAndSystem()
            .Where(pair => !string.Equals(pair.Key, Path.GetFullPath(fixture.AuthorityStatePath), StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var authorityBefore = fixture.ReadAuthorityProtocol();
        var authorityHashBefore = fixture.HashAuthorityState();
        var result = fixture.Execute();
        var authorityAfter = fixture.ReadAuthorityProtocol();
        var m07SeedRevision = await SqliteBusinessRevisionStore.ReadFromDatabaseAsync(fixture.DatabasePath);

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(41L, result.BusinessDataRevisionBefore);
        Assert.AreEqual(42L, result.BusinessDataRevisionAfter);
        Assert.AreEqual(42L, fixture.ReadRevision());
        Assert.HasCount(CutoverTables.All.Length, result.Preflight.TableCounts);
        Assert.IsTrue(result.Preflight.TableCounts.Values.All(count => count > 0), "Every reset table is populated in the synthetic fixture.");
        var snapshotAfter = fixture.ReadOnlyStateSnapshot().Split('\n');
        foreach (var table in CutoverTables.All) Assert.IsTrue(snapshotAfter.Contains(table + "=0"), table + " should be empty.");
        CollectionAssert.AreEqual(settingsLinesBefore, snapshotAfter.Where(line => line.StartsWith("settings:", StringComparison.Ordinal)).ToArray());
        CollectionAssert.AreEqual(migrationLinesBefore, snapshotAfter.Where(line => line.StartsWith("migration:", StringComparison.Ordinal)).ToArray());
        Assert.IsTrue(DictionaryEqual(configAndSystemBefore, fixture.HashConfigurationAndSystem()
            .Where(pair => !string.Equals(pair.Key, Path.GetFullPath(fixture.AuthorityStatePath), StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)));
        Assert.AreEqual(authorityBefore.Revision + 1, authorityAfter.Revision);
        Assert.AreEqual(42L, authorityAfter.BusinessRevision);
        Assert.AreEqual(m07SeedRevision, authorityAfter.BusinessRevision);
        Assert.AreEqual(0, Directory.EnumerateFiles(fixture.Archive, "*", SearchOption.AllDirectories).Count());
        Assert.IsTrue(Directory.Exists(fixture.Archive));

        Assert.IsNotNull(result.BackupDirectory);
        var backupDb = Path.Combine(result.BackupDirectory!, "live.db");
        var backupArchive = Path.Combine(result.BackupDirectory!, "Archive");
        Assert.IsTrue(File.Exists(backupDb));
        Assert.IsTrue(File.Exists(Path.Combine(backupArchive, "2025.json")));
        Assert.IsTrue(File.Exists(Path.Combine(backupArchive, "nested", "2024.json")));
        Assert.AreEqual(HashFile(backupDb), result.LiveDatabaseBackupSha256);
        var backupAuthority = Path.Combine(result.BackupDirectory!, "authority-state.json");
        Assert.IsTrue(File.Exists(backupAuthority));
        Assert.AreEqual(result.AuthorityStateBackupSha256, HashFile(backupAuthority));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(result.BackupDirectory!, "manifest.json")));
        Assert.AreEqual("Complete", manifest.RootElement.GetProperty("state").GetString());
        Assert.AreEqual(result.LiveDatabaseBackupSha256, manifest.RootElement.GetProperty("liveDatabaseBackupSha256").GetString());
        Assert.AreEqual("authority-state.json", manifest.RootElement.GetProperty("authorityStateBackupFileName").GetString());
        Assert.AreEqual(result.AuthorityStateBackupSha256, manifest.RootElement.GetProperty("authorityStateBackupSha256").GetString());
        Assert.AreEqual(authorityHashBefore, manifest.RootElement.GetProperty("authorityStateBeforeSha256").GetString());
        Assert.AreEqual(fixture.HashAuthorityState(), manifest.RootElement.GetProperty("authorityStateAfterSha256").GetString());
        Assert.AreEqual(8L, manifest.RootElement.GetProperty("authorityProtocolRevisionBefore").GetInt64());
        Assert.AreEqual(9L, manifest.RootElement.GetProperty("authorityProtocolRevisionAfter").GetInt64());
        Assert.AreEqual(41L, manifest.RootElement.GetProperty("authorityBusinessRevisionBefore").GetInt64());
        Assert.AreEqual(42L, manifest.RootElement.GetProperty("authorityBusinessRevisionAfter").GetInt64());
        Assert.IsFalse(string.IsNullOrWhiteSpace(manifest.RootElement.GetProperty("rollbackProvenance").GetString()));
        using var backupConnection = new SqliteConnection($"Data Source={backupDb};Mode=ReadOnly;Pooling=False");
        backupConnection.Open();
        using var integrity = backupConnection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        Assert.AreEqual("ok", integrity.ExecuteScalar());
    }

    [TestMethod]
    public void FailureAfterVerifiedBackupBeforeMutationLeavesDatabaseAndArchiveUntouched()
    {
        using var fixture = new CutoverFixture();
        var databaseHash = fixture.HashDatabase();
        var archive = fixture.HashArchive();
        fixture.Host.ThrowAt = CutoverCheckpoint.AfterArchiveStaging;
        Assert.Throws<InvalidOperationException>(() => fixture.Execute());
        Assert.AreEqual(databaseHash, fixture.HashDatabase());
        Assert.IsTrue(DictionaryEqual(archive, fixture.HashArchive()));
        Assert.AreEqual(41L, fixture.ReadRevision());
    }

    [TestMethod]
    public void FailureInsideSqliteTransactionRollsBackEveryBusinessTable()
    {
        using var fixture = new CutoverFixture();
        var before = fixture.ReadOnlyStateSnapshot();
        var archive = fixture.HashArchive();
        fixture.Host.ThrowAt = CutoverCheckpoint.DuringDatabaseTransaction;
        Assert.Throws<InvalidOperationException>(() => fixture.Execute());
        Assert.AreEqual(before, fixture.ReadOnlyStateSnapshot());
        Assert.IsTrue(DictionaryEqual(archive, fixture.HashArchive()));
        Assert.AreEqual(41L, fixture.ReadRevision());
    }

    [TestMethod]
    public void FailureAfterArchiveClearBeforeCommitRestoresArchiveAndRollsBackDatabase()
    {
        using var fixture = new CutoverFixture();
        var before = fixture.ReadOnlyStateSnapshot();
        var archive = fixture.HashArchive();
        fixture.Host.ThrowAt = CutoverCheckpoint.BeforeDatabaseCommit;
        Assert.Throws<InvalidOperationException>(() => fixture.Execute());
        Assert.AreEqual(before, fixture.ReadOnlyStateSnapshot());
        Assert.IsTrue(DictionaryEqual(archive, fixture.HashArchive()));
        Assert.AreEqual(41L, fixture.ReadRevision());
    }

    [TestMethod]
    public void DesktopStartingBeforeDatabaseCommitRestoresArchiveAndRollsBackDatabase()
    {
        using var fixture = new CutoverFixture();
        var before = fixture.ReadOnlyStateSnapshot();
        var databaseHash = fixture.HashDatabase();
        var archive = fixture.HashArchive();
        fixture.Host.DesktopStartsAt = CutoverCheckpoint.BeforeDatabaseCommit;

        Assert.Throws<InvalidOperationException>(() => fixture.Execute());

        Assert.AreEqual(before, fixture.ReadOnlyStateSnapshot());
        Assert.AreEqual(databaseHash, fixture.HashDatabase());
        Assert.IsTrue(DictionaryEqual(archive, fixture.HashArchive()));
        Assert.AreEqual(41L, fixture.ReadRevision());
    }

    [TestMethod]
    public void FailuresAtAuthorityReplacementAndReadbackBoundariesRestoreExactDatabaseArchiveAndAuthority()
    {
        foreach (var checkpoint in new[]
        {
            CutoverCheckpoint.BeforeAuthorityReplacement,
            CutoverCheckpoint.AfterAuthorityReplacement,
            CutoverCheckpoint.BeforeAuthorityReadBack,
            CutoverCheckpoint.AfterAuthorityReadBack
        })
        {
            using var fixture = new CutoverFixture();
            var databaseHash = fixture.HashDatabase();
            var authorityBytes = File.ReadAllBytes(fixture.AuthorityStatePath);
            var authorityHash = fixture.HashAuthorityState();
            var archive = fixture.HashArchive();
            fixture.Host.ThrowAt = checkpoint;

            Assert.Throws<InvalidOperationException>(() => fixture.Execute(), checkpoint.ToString());

            Assert.AreEqual(databaseHash, fixture.HashDatabase(), checkpoint + " should restore exact live.db bytes.");
            CollectionAssert.AreEqual(authorityBytes, File.ReadAllBytes(fixture.AuthorityStatePath), checkpoint + " should restore exact authority bytes.");
            Assert.AreEqual(authorityHash, fixture.HashAuthorityState(), checkpoint + " should restore authority hash.");
            Assert.IsTrue(DictionaryEqual(archive, fixture.HashArchive()), checkpoint + " should restore the Archive set and hashes.");
            Assert.AreEqual(41L, fixture.ReadRevision());
            var manifest = ReadOnlyLatestManifest(fixture.Root);
            Assert.AreEqual("RolledBack", manifest.GetProperty("state").GetString());
            Assert.AreEqual(authorityHash, manifest.GetProperty("authorityStateAfterSha256").GetString());
            Assert.AreEqual(authorityHash, manifest.GetProperty("authorityStateBeforeSha256").GetString());
            Assert.AreEqual(8L, manifest.GetProperty("authorityProtocolRevisionAfter").GetInt64());
            Assert.AreEqual(41L, manifest.GetProperty("authorityBusinessRevisionAfter").GetInt64());
            Assert.IsFalse(string.IsNullOrWhiteSpace(manifest.GetProperty("rollbackProvenance").GetString()));
        }
    }

    [TestMethod]
    public void CorruptAuthorityReadbackRestoresExactPreCutoverState()
    {
        using var fixture = new CutoverFixture();
        var databaseHash = fixture.HashDatabase();
        var authorityBytes = File.ReadAllBytes(fixture.AuthorityStatePath);
        var authorityHash = fixture.HashAuthorityState();
        var archive = fixture.HashArchive();
        fixture.Host.OnCheckpoint = checkpoint =>
        {
            if (checkpoint == CutoverCheckpoint.BeforeAuthorityReadBack)
                File.WriteAllText(fixture.AuthorityStatePath, "{\"tampered\":true}");
        };

        Assert.Throws<InvalidOperationException>(() => fixture.Execute());

        Assert.AreEqual(databaseHash, fixture.HashDatabase());
        CollectionAssert.AreEqual(authorityBytes, File.ReadAllBytes(fixture.AuthorityStatePath));
        Assert.AreEqual(authorityHash, fixture.HashAuthorityState());
        Assert.IsTrue(DictionaryEqual(archive, fixture.HashArchive()));
        Assert.AreEqual("RolledBack", ReadOnlyLatestManifest(fixture.Root).GetProperty("state").GetString());
    }

    [TestMethod]
    public void MissingBusinessDataRevisionRefusesWithoutMutation()
    {
        using var fixture = new CutoverFixture();
        using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Mode=ReadWrite;Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM foundation_metadata WHERE key='business_data_revision';";
            command.ExecuteNonQuery();
        }

        var before = fixture.HashDatabase();
        Assert.Throws<InvalidDataException>(() => fixture.Execute());
        Assert.AreEqual(before, fixture.HashDatabase());
    }

    [TestMethod]
    public void RepeatedExecutionAfterSuccessfulResetFailsClosedWithoutSecondRevisionBump()
    {
        using var fixture = new CutoverFixture();
        fixture.Execute();
        var revision = fixture.ReadRevision();
        Assert.AreEqual(42L, revision);
        Assert.Throws<InvalidOperationException>(() => fixture.Execute());
        Assert.AreEqual(revision, fixture.ReadRevision());
    }

    [TestMethod]
    public void SuccessfulCutoverRecordPreventsResetAfterBusinessRowsAreReintroduced()
    {
        using var fixture = new CutoverFixture();
        fixture.Execute();
        using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Mode=ReadWrite;Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO categories(category_id,name) VALUES('later-category','Synthetic later catalogue');";
            command.ExecuteNonQuery();
        }
        var databaseHash = fixture.HashDatabase();

        Assert.Throws<InvalidOperationException>(() => fixture.Execute());

        Assert.AreEqual(databaseHash, fixture.HashDatabase());
        Assert.AreEqual(42L, fixture.ReadRevision());
    }

    private static bool DictionaryEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        => left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value)
            && string.Equals(pair.Value, value, StringComparison.OrdinalIgnoreCase));

    private static JsonElement ReadOnlyLatestManifest(string root)
    {
        var backupDirectory = Directory.EnumerateDirectories(Path.Combine(root, "CutoverBackups"), "m13-preproduction-cutover-*", SearchOption.TopDirectoryOnly).Single();
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(backupDirectory, "manifest.json")));
        return document.RootElement.Clone();
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }
}
