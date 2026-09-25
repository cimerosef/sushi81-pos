using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sushi81.Pos.PreProductionCutoverReset;

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
    public void UnsafeAuthorityPhaseRefusesExecution()
    {
        using var fixture = new CutoverFixture();
        fixture.RewriteAuthority(phase: 2);
        var hash = fixture.HashDatabase();
        Assert.Throws<InvalidDataException>(() => fixture.Execute());
        Assert.AreEqual(hash, fixture.HashDatabase());
        Assert.AreEqual(41L, fixture.ReadRevision());
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
    public void PopulatedSyntheticSchemaIsClearedInOneRevisionWhilePreservingSettingsMigrationsAndIdentity()
    {
        using var fixture = new CutoverFixture();
        var settingsLinesBefore = fixture.ReadOnlyStateSnapshot().Split('\n').Where(line => line.StartsWith("settings:", StringComparison.Ordinal)).ToArray();
        var migrationLinesBefore = fixture.ReadOnlyStateSnapshot().Split('\n').Where(line => line.StartsWith("migration:", StringComparison.Ordinal)).ToArray();
        var configAndSystemBefore = fixture.HashConfigurationAndSystem();
        var result = fixture.Execute();

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
        Assert.IsTrue(DictionaryEqual(configAndSystemBefore, fixture.HashConfigurationAndSystem()));
        Assert.AreEqual(0, Directory.EnumerateFiles(fixture.Archive, "*", SearchOption.AllDirectories).Count());
        Assert.IsTrue(Directory.Exists(fixture.Archive));

        Assert.IsNotNull(result.BackupDirectory);
        var backupDb = Path.Combine(result.BackupDirectory!, "live.db");
        var backupArchive = Path.Combine(result.BackupDirectory!, "Archive");
        Assert.IsTrue(File.Exists(backupDb));
        Assert.IsTrue(File.Exists(Path.Combine(backupArchive, "2025.json")));
        Assert.IsTrue(File.Exists(Path.Combine(backupArchive, "nested", "2024.json")));
        Assert.AreEqual(HashFile(backupDb), result.LiveDatabaseBackupSha256);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(result.BackupDirectory!, "manifest.json")));
        Assert.AreEqual("Complete", manifest.RootElement.GetProperty("state").GetString());
        Assert.AreEqual(result.LiveDatabaseBackupSha256, manifest.RootElement.GetProperty("liveDatabaseBackupSha256").GetString());
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

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }
}
