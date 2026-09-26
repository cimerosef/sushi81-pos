namespace Sushi81.Pos.PreProductionCutoverReset;

public sealed record CutoverRequest(
    bool Execute,
    string? Confirmation,
    string? ConfirmRoot);

public sealed record OrderStatusSourceCount(
    string Status,
    string SourceType,
    long Count);

public sealed record CutoverPreflightReport(
    string DataRoot,
    string Mode,
    string ApplicationSourceHead,
    string ApplicationVersion,
    int AuthoritySchemaVersion,
    string AuthorityPhase,
    long AuthorityProtocolRevision,
    long AuthorityBusinessRevision,
    int DatabaseSchemaVersion,
    string DatabaseIntegrity,
    int ForeignKeyViolationCount,
    IReadOnlyList<OrderStatusSourceCount> OrdersByStatusAndSource,
    IReadOnlyDictionary<string, long> TableCounts,
    long BusinessSettingsRows,
    long BusinessDataRevision,
    int LocalArchiveFileCount,
    IReadOnlyList<string> LocalConfigurationRelativePaths);

public sealed record CutoverRunResult(
    bool Changed,
    CutoverPreflightReport Preflight,
    string? BackupDirectory,
    string? LiveDatabaseBackupSha256,
    long BusinessDataRevisionBefore,
    long BusinessDataRevisionAfter,
    string Message,
    long AuthorityProtocolRevisionBefore,
    long AuthorityProtocolRevisionAfter,
    long AuthorityBusinessRevisionBefore,
    long AuthorityBusinessRevisionAfter,
    string? AuthorityStateBackupSha256);

public sealed record InstalledApplicationProvenance(
    string ProductName,
    string ProductVersion,
    string FileVersion,
    string SourceHeadSha,
    string RuntimeIdentifier,
    bool SelfContained);

public enum CutoverCheckpoint
{
    BeforeBackupCreation,
    AfterBackupCreation,
    BeforeArchiveStaging,
    AfterArchiveStaging,
    DuringDatabaseTransaction,
    BeforeDatabaseCommit,
    BeforeAuthorityReplacement,
    AfterAuthorityReplacement,
    BeforeAuthorityReadBack,
    AfterAuthorityReadBack
}

public interface ICutoverHost
{
    bool IsDesktopRunning();
    InstalledApplicationProvenance ReadInstalledApplicationProvenance(string installDirectory);
    DateTimeOffset UtcNow { get; }
    void Checkpoint(CutoverCheckpoint checkpoint);
}
