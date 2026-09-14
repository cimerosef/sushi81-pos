using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.Configuration;

public enum M07ConfigurationSetupFailureKind
{
    None,
    RootRequired,
    RootNotAbsolute,
    RootUnavailable,
    LineageUnavailable,
    LineageInvalid,
    LineageRequired,
    AuthorityPhaseUnsafe,
    AuthorityStateUnavailable,
    PersistenceFailed
}

/// <summary>
/// Non-authority production onboarding for the M07 shared-root and transport settings.
/// Validation is completed before local configuration is changed; this seam never creates
/// authority, device membership, bootstrap evidence or business data.
/// </summary>
public sealed record M07ConfigurationSetupInput(
    string? OneDriveRoot,
    string? GitHubOwner,
    string? GitHubRepository,
    string? GitHubReleaseTag,
    string? GitHubReleaseName,
    string? GitHubCredentialTarget);

public sealed record M07ConfigurationSetupResult(
    bool Succeeded,
    LocalConfiguration Configuration,
    M07ConfigurationSetupFailureKind FailureKind,
    string? Diagnostic,
    SystemLineageMetadata? ValidatedLineage)
{
    public static M07ConfigurationSetupResult Failure(
        LocalConfiguration current,
        M07ConfigurationSetupFailureKind failureKind,
        string diagnostic) => new(false, current, failureKind, diagnostic, null);
}

public sealed class M07ConfigurationSetupService(
    ILocalConfigurationService configurationService,
    IAuthorityStateStore? authorityStateStore = null)
{
    public async Task<M07ConfigurationSetupResult> ValidateAndPersistAsync(
        LocalConfiguration current,
        M07ConfigurationSetupInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(input);

        AuthorityProtocolState? authority = null;
        if (authorityStateStore is not null)
        {
            try
            {
                var document = await authorityStateStore.LoadAsync(cancellationToken);
                authority = document?.Protocol;
                if (authority is not null)
                    authority.Validate();

                var phase = authority?.Phase ?? document?.EffectiveState switch
                {
                    WriteAuthorityState.Authoritative => AuthorityPhase.Authoritative,
                    WriteAuthorityState.NonAuthoritativeReadOnly => AuthorityPhase.NonAuthoritativeReadOnly,
                    WriteAuthorityState.Transitioning => AuthorityPhase.RecoveryRequired,
                    WriteAuthorityState.RecoveryRequired => AuthorityPhase.RecoveryRequired,
                    _ => null
                };
                if (IsConfigurationLocked(phase))
                {
                    return M07ConfigurationSetupResult.Failure(
                        current,
                        M07ConfigurationSetupFailureKind.AuthorityPhaseUnsafe,
                        "Technical configuration is unavailable during the current authority phase.");
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
            {
                return M07ConfigurationSetupResult.Failure(
                    current,
                    M07ConfigurationSetupFailureKind.AuthorityStateUnavailable,
                    "The current authority state could not be validated; technical configuration remains locked.");
            }
        }

        var rootText = input.OneDriveRoot?.Trim();
        if (string.IsNullOrWhiteSpace(rootText))
        {
            return M07ConfigurationSetupResult.Failure(
                current,
                M07ConfigurationSetupFailureKind.RootRequired,
                "The Sushi81 shared root is required.");
        }

        if (!Path.IsPathFullyQualified(rootText))
        {
            return M07ConfigurationSetupResult.Failure(
                current,
                M07ConfigurationSetupFailureKind.RootNotAbsolute,
                "The Sushi81 shared root must be an absolute path.");
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(rootText);
            if (!Directory.Exists(normalizedRoot))
            {
                return M07ConfigurationSetupResult.Failure(
                    current,
                    M07ConfigurationSetupFailureKind.RootUnavailable,
                    "The Sushi81 shared root does not exist or is not accessible.");
            }

            // Force an access check before parsing the lineage. Directory.Exists alone
            // can report false for an inaccessible directory without explaining why.
            _ = Directory.EnumerateFileSystemEntries(normalizedRoot).Take(1).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return M07ConfigurationSetupResult.Failure(
                current,
                M07ConfigurationSetupFailureKind.RootUnavailable,
                "The Sushi81 shared root does not exist or is not accessible.");
        }

        SystemLineageMetadata? lineage = null;
        var lineagePath = Path.Combine(
            normalizedRoot,
            SystemMetadataContract.SystemDirectoryName,
            SystemMetadataContract.LineageDirectoryName,
            SystemMetadataContract.LineageFileName);
        bool lineageIsMissing;
        try
        {
            lineageIsMissing = !File.Exists(lineagePath) && IsMissingLineageTree(normalizedRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return M07ConfigurationSetupResult.Failure(
                current,
                M07ConfigurationSetupFailureKind.LineageUnavailable,
                "The shared lineage metadata is not currently readable.");
        }

        if (lineageIsMissing)
        {
            if (authority?.Phase is AuthorityPhase.Authoritative or AuthorityPhase.ClosedRetainedAuthority
                && authority.LineageId is not null
                && authority.Generation >= 1)
            {
                // The first authoritative setup may bind an empty root. The existing
                // startup coordinator publishes this exact lineage after restart.
            }
            else
            {
                return M07ConfigurationSetupResult.Failure(
                    current,
                    M07ConfigurationSetupFailureKind.LineageRequired,
                    "An existing shared lineage is required for this device.");
            }
        }
        else
        {
            try
            {
                var metadataStore = new JsonSystemMetadataStore(normalizedRoot, TimeProvider.System);
                lineage = await metadataStore.ReadLineageAsync(cancellationToken);
                if (authority?.LineageId is { } localLineage
                    && (lineage.LineageId != localLineage || lineage.CurrentGeneration != authority.Generation))
                {
                    return M07ConfigurationSetupResult.Failure(
                        current,
                        M07ConfigurationSetupFailureKind.LineageInvalid,
                        "The selected shared lineage does not match the local authority state.");
                }
            }
            catch (SystemMetadataUnavailableException exception)
            {
                return M07ConfigurationSetupResult.Failure(
                    current,
                    M07ConfigurationSetupFailureKind.LineageUnavailable,
                    exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return M07ConfigurationSetupResult.Failure(
                    current,
                    M07ConfigurationSetupFailureKind.LineageInvalid,
                    exception.Message);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return M07ConfigurationSetupResult.Failure(
                    current,
                    M07ConfigurationSetupFailureKind.LineageUnavailable,
                    "The shared lineage metadata is not currently readable.");
            }
        }

        LocalConfiguration updated;
        try
        {
            updated = await configurationService.UpdateAsync(
                latest => latest with
                {
                    OneDriveRoot = normalizedRoot,
                    GitHubOwner = NormalizeOptional(input.GitHubOwner),
                    GitHubRepository = NormalizeOptional(input.GitHubRepository),
                    GitHubReleaseTag = NormalizeWithDefault(input.GitHubReleaseTag, latest.GitHubReleaseTag),
                    GitHubReleaseName = NormalizeWithDefault(input.GitHubReleaseName, latest.GitHubReleaseName),
                    GitHubCredentialTarget = NormalizeOptional(input.GitHubCredentialTarget)
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return M07ConfigurationSetupResult.Failure(
                current,
                M07ConfigurationSetupFailureKind.PersistenceFailed,
                "The technical setup could not be saved. The previous configuration remains active.");
        }

        return new M07ConfigurationSetupResult(true, updated, M07ConfigurationSetupFailureKind.None, null, lineage);
    }

    private static bool IsConfigurationLocked(AuthorityPhase? phase) => phase is
        AuthorityPhase.TransferPreparing
        or AuthorityPhase.RelinquishedPendingGrant
        or AuthorityPhase.TargetAcquisitionPending
        or AuthorityPhase.DisasterRecoveryPreparing
        or AuthorityPhase.DisasterRecoveryPending
        or AuthorityPhase.RecoveryRequired
        or AuthorityPhase.StaleGeneration;

    private static bool IsMissingLineageTree(string root)
    {
        try
        {
            var lineageDirectory = Path.Combine(
                root,
                SystemMetadataContract.SystemDirectoryName,
                SystemMetadataContract.LineageDirectoryName);
            var entries = Directory.EnumerateFileSystemEntries(lineageDirectory).ToArray();
            return !entries.Any(entry => string.Equals(
                Path.GetFileName(entry),
                SystemMetadataContract.LineageFileName,
                StringComparison.OrdinalIgnoreCase));
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeWithDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
