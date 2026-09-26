using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Recovery;

namespace Sushi81.Pos.Application.Maintenance;

public sealed record BusinessDataResetPreview(
    long Orders,
    long Products,
    long Categories,
    long OptionGroups,
    long Options,
    long ExportBatches,
    long PreparedExportBatches,
    long AnnualArchiveRecords,
    int ActiveArchiveYears,
    int ActiveArchiveFiles,
    long OrderReferenceSequences = 0)
{
    public bool HasBusinessData => Orders + Products + Categories + OptionGroups + Options + ExportBatches + AnnualArchiveRecords > 0
        || ActiveArchiveFiles > 0
        || OrderReferenceSequences > 0;
}

public enum BusinessDataResetStatus
{
    Reset,
    AlreadyEmpty,
    FailedWithoutMutation,
    FailedAndRestored,
    RecoveryRequired,
    ResetCommittedRecoveryNotificationFailed
}

public sealed record BusinessDataResetStoreResult(
    BusinessDataResetStatus Status,
    string? BackupId = null)
{
    public bool Succeeded => Status == BusinessDataResetStatus.Reset;
}

public interface IBusinessDataResetStore
{
    Task<BusinessDataResetPreview> PreviewAsync(CancellationToken cancellationToken = default);

    Task<BusinessDataResetStoreResult> ResetAsync(CancellationToken cancellationToken = default);
}

public sealed record BusinessDataResetExecutionResult(
    BusinessDataResetStatus Status,
    string? BackupId = null)
{
    public bool Succeeded => Status == BusinessDataResetStatus.Reset;
}

/// <summary>Guards the owner-only, full business-data reset and records its durable change once.</summary>
public sealed class BusinessDataResetService(
    IBusinessDataResetStore store,
    IWriteAuthorityGuard authorityGuard,
    IDurableChangeNotifier notifier)
{
    public const string RequiredConfirmation = "RESET";

    public async Task<BusinessDataResetPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(authorityGuard);
        authorityGuard.RequireWriteAuthority();
        return await store.PreviewAsync(cancellationToken);
    }

    public async Task<BusinessDataResetExecutionResult> ExecuteAsync(
        string? confirmation,
        bool finalConfirmation,
        CancellationToken cancellationToken = default)
    {
        if (!finalConfirmation || !string.Equals(confirmation, RequiredConfirmation, StringComparison.Ordinal))
            return new BusinessDataResetExecutionResult(BusinessDataResetStatus.FailedWithoutMutation);

        await using var authorityScope = await authorityGuard.EnterWriteScopeAsync(cancellationToken);
        var result = await store.ResetAsync(cancellationToken);
        if (result.Succeeded)
        {
            try
            {
                await notifier.NotifyCommittedAsync(CancellationToken.None);
            }
            catch
            {
                // The reset is committed and cannot be rolled back safely after this point.
                // Keep the UI fail-closed and report that recovery publication needs attention.
                return new BusinessDataResetExecutionResult(
                    BusinessDataResetStatus.ResetCommittedRecoveryNotificationFailed,
                    result.BackupId);
            }
        }

        return new BusinessDataResetExecutionResult(result.Status, result.BackupId);
    }
}
