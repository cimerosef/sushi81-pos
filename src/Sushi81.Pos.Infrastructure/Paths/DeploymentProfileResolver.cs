using Sushi81.Pos.Application.Foundation;

namespace Sushi81.Pos.Infrastructure.Paths;

/// <summary>Resolves the fixed deployment profile before any application paths are initialized.</summary>
public static class DeploymentProfileResolver
{
    public const string EvidenceFileName = "deployment-profile.txt";

    public static DeploymentProfile ResolveForCurrentProcess() => Resolve(
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static DeploymentProfile Resolve(
        string applicationBaseDirectory,
        string localAppDataDirectory,
        Func<string, bool>? fileExists = null,
        Func<string, string>? readAllText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataDirectory);

        var applicationDirectory = NormalizeDirectory(applicationBaseDirectory);
        var localAppData = NormalizeDirectory(localAppDataDirectory);
        var productionInstallDirectory = NormalizeDirectory(Path.Combine(localAppData, "Programs", DeploymentProfile.Production.DataRootName));
        var preProductionInstallDirectory = NormalizeDirectory(Path.Combine(localAppData, "Programs", DeploymentProfile.PreProduction.DataRootName));
        var installedProfile = applicationDirectory.Equals(productionInstallDirectory, StringComparison.OrdinalIgnoreCase)
            ? DeploymentProfile.Production
            : applicationDirectory.Equals(preProductionInstallDirectory, StringComparison.OrdinalIgnoreCase)
                ? DeploymentProfile.PreProduction
                : null;

        var evidencePath = Path.Combine(applicationDirectory, EvidenceFileName);
        fileExists ??= File.Exists;
        readAllText ??= File.ReadAllText;

        bool evidenceExists;
        try
        {
            evidenceExists = fileExists(evidencePath);
        }
        catch (Exception exception)
        {
            throw InvalidEvidence(evidencePath, "could not be inspected", exception);
        }

        if (!evidenceExists)
        {
            if (installedProfile is not null)
                throw InvalidEvidence(evidencePath, "is required for an installed deployment");

            // Unpackaged developer and test runs retain the historic production defaults.
            return DeploymentProfile.Production;
        }

        string contents;
        try
        {
            contents = readAllText(evidencePath);
        }
        catch (Exception exception)
        {
            throw InvalidEvidence(evidencePath, "could not be read", exception);
        }

        var profile = ParseEvidence(contents, evidencePath);
        if (installedProfile is not null && profile != installedProfile)
            throw InvalidEvidence(evidencePath, "contradicts the fixed installed application identity");

        return profile;
    }

    private static DeploymentProfile ParseEvidence(string contents, string evidencePath)
    {
        var value = contents.EndsWith("\r\n", StringComparison.Ordinal)
            ? contents[..^2]
            : contents.EndsWith('\n')
                ? contents[..^1]
                : contents;

        if (value.Contains('\r') || value.Contains('\n') || !DeploymentProfile.TryParse(value, out var profile))
            throw InvalidEvidence(evidencePath, "must contain exactly 'prod' or 'preprod' on one line");

        return profile;
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static DeploymentProfileResolutionException InvalidEvidence(string path, string reason, Exception? inner = null) =>
        new($"The Sushi81 POS deployment profile evidence '{path}' {reason}. Startup stopped before business or authority data was opened.", inner);
}

public sealed class DeploymentProfileResolutionException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
