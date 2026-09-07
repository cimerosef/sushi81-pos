using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
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

public sealed class M07ConfigurationSetupService(ILocalConfigurationService configurationService)
{
    public async Task<M07ConfigurationSetupResult> ValidateAndPersistAsync(
        LocalConfiguration current,
        M07ConfigurationSetupInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(input);

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

        SystemLineageMetadata lineage;
        try
        {
            var metadataStore = new JsonSystemMetadataStore(normalizedRoot, TimeProvider.System);
            lineage = await metadataStore.ReadLineageAsync(cancellationToken);
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

        var updated = current with
        {
            OneDriveRoot = normalizedRoot,
            GitHubOwner = NormalizeOptional(input.GitHubOwner),
            GitHubRepository = NormalizeOptional(input.GitHubRepository),
            GitHubReleaseTag = NormalizeWithDefault(input.GitHubReleaseTag, current.GitHubReleaseTag),
            GitHubReleaseName = NormalizeWithDefault(input.GitHubReleaseName, current.GitHubReleaseName),
            GitHubCredentialTarget = NormalizeOptional(input.GitHubCredentialTarget)
        };

        try
        {
            await configurationService.SaveAsync(updated, cancellationToken);
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

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeWithDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
