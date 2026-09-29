using System.Text.Json;
using Sushi81.Pos.Application.Foundation;

namespace Sushi81.Pos.Infrastructure.Configuration;

internal sealed record PreProductionRemoteIsolationResult(
    M07ConfigurationSetupFailureKind FailureKind,
    string? Diagnostic)
{
    public bool Succeeded => FailureKind == M07ConfigurationSetupFailureKind.None;

    public static PreProductionRemoteIsolationResult Success() =>
        new(M07ConfigurationSetupFailureKind.None, null);

    public static PreProductionRemoteIsolationResult Failure(
        M07ConfigurationSetupFailureKind kind,
        string diagnostic) => new(kind, diagnostic);
}

/// <summary>
/// Verifies that an accepted PreProd M07 selection cannot address Production's OneDrive,
/// runtime GitHub repository, or Credential Manager target. Production settings are opened
/// read-only and only the non-secret comparison fields are parsed.
/// </summary>
internal static class PreProductionRemoteIsolationGuard
{
    private const string SourceRepositoryOwner = "cimerosef";
    private const string SourceRepositoryName = "sushi81-pos";

    public static async Task<PreProductionRemoteIsolationResult> ValidateAsync(
        DeploymentProfile profile,
        string selectedOneDriveRoot,
        string? selectedGitHubOwner,
        string? selectedGitHubRepository,
        string? selectedCredentialTarget,
        string? productionSettingsFilePath,
        CancellationToken cancellationToken)
    {
        if (!profile.IsPreProduction)
            return PreProductionRemoteIsolationResult.Success();

        var normalizedOwner = TrimOptional(selectedGitHubOwner);
        var normalizedRepository = TrimOptional(selectedGitHubRepository);
        var normalizedCredentialTarget = TrimOptional(selectedCredentialTarget);
        if (string.Equals(normalizedOwner, SourceRepositoryOwner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(normalizedRepository, SourceRepositoryName, StringComparison.OrdinalIgnoreCase))
        {
            return PreProductionRemoteIsolationResult.Failure(
                M07ConfigurationSetupFailureKind.GitHubRepositoryCollision,
                "The source/build repository cannot be used as the PreProd runtime handoff repository.");
        }

        ProductionRemoteSettings? production;
        try
        {
            production = await ReadProductionSettingsAsync(
                productionSettingsFilePath ?? GetProductionSettingsFilePath(),
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or JsonException
            or ArgumentException
            or NotSupportedException
            or InvalidOperationException
            or System.Security.SecurityException)
        {
            return PreProductionRemoteIsolationResult.Failure(
                M07ConfigurationSetupFailureKind.ProductionSettingsUnavailable,
                "Production local settings exist but cannot be read unambiguously; PreProd configuration was not saved.");
        }

        if (production is null)
            return PreProductionRemoteIsolationResult.Success();

        if (!string.IsNullOrWhiteSpace(production.OneDriveRoot))
        {
            try
            {
                var selectedRoot = NormalizeAbsoluteDirectory(selectedOneDriveRoot);
                var productionRoot = NormalizeAbsoluteDirectory(production.OneDriveRoot);
                if (HasReparsePointOnExistingPath(selectedRoot) || HasReparsePointOnExistingPath(productionRoot))
                {
                    return PreProductionRemoteIsolationResult.Failure(
                        M07ConfigurationSetupFailureKind.OneDriveIsolationUnproven,
                        "The OneDrive roots contain a symbolic link or reparse point, so their independence cannot be verified.");
                }

                if (PathsOverlap(selectedRoot, productionRoot))
                {
                    return PreProductionRemoteIsolationResult.Failure(
                        M07ConfigurationSetupFailureKind.OneDriveRootCollision,
                        "The PreProd OneDrive root must be separate from and outside the Production OneDrive root in both directions.");
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or InvalidDataException
                or System.Security.SecurityException)
            {
                return PreProductionRemoteIsolationResult.Failure(
                    M07ConfigurationSetupFailureKind.OneDriveIsolationUnproven,
                    "The OneDrive roots cannot be normalized and checked for independent storage.");
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedOwner)
            && !string.IsNullOrWhiteSpace(normalizedRepository)
            && string.Equals(normalizedOwner, TrimOptional(production.GitHubOwner), StringComparison.OrdinalIgnoreCase)
            && string.Equals(normalizedRepository, TrimOptional(production.GitHubRepository), StringComparison.OrdinalIgnoreCase))
        {
            return PreProductionRemoteIsolationResult.Failure(
                M07ConfigurationSetupFailureKind.GitHubRepositoryCollision,
                "The PreProd runtime handoff repository must differ from the Production GitHub owner and repository.");
        }

        if (!string.IsNullOrWhiteSpace(normalizedCredentialTarget)
            && string.Equals(normalizedCredentialTarget, TrimOptional(production.GitHubCredentialTarget), StringComparison.OrdinalIgnoreCase))
        {
            return PreProductionRemoteIsolationResult.Failure(
                M07ConfigurationSetupFailureKind.CredentialTargetCollision,
                "The PreProd GitHub Credential Manager target must differ from the Production target.");
        }

        return PreProductionRemoteIsolationResult.Success();
    }

    internal static string GetProductionSettingsFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new InvalidOperationException("The current user's local application data directory is unavailable.");

        return Path.Combine(
            localAppData,
            DeploymentProfile.Production.DataRootName,
            "Config",
            "local-settings.json");
    }

    private static async Task<ProductionRemoteSettings?> ReadProductionSettingsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Production settings must be a JSON object.");

            return new ProductionRemoteSettings(
                ReadOptionalString(root, "oneDriveRoot"),
                ReadOptionalString(root, "githubOwner"),
                ReadOptionalString(root, "githubRepository"),
                ReadOptionalString(root, "githubCredentialTarget"));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Production local settings contain malformed JSON.", exception);
        }
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        JsonElement? found = null;
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (found is not null)
                throw new InvalidDataException($"Production settings contain an ambiguous {propertyName} value.");

            found = property.Value;
        }

        if (found is null || found.Value.ValueKind == JsonValueKind.Null)
            return null;

        if (found.Value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Production settings contain a non-text {propertyName} value.");

        return TrimOptional(found.Value.GetString());
    }

    private static string? TrimOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string NormalizeAbsoluteDirectory(string value)
    {
        var trimmed = value.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
            throw new InvalidDataException("A configured OneDrive root is not absolute.");

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
    }

    private static bool PathsOverlap(string first, string second) =>
        IsSameOrDescendant(first, second) || IsSameOrDescendant(second, first);

    private static bool IsSameOrDescendant(string candidate, string possibleParent)
    {
        if (string.Equals(candidate, possibleParent, StringComparison.OrdinalIgnoreCase))
            return true;

        var parentPrefix = Path.EndsInDirectorySeparator(possibleParent)
            ? possibleParent
            : possibleParent + Path.DirectorySeparatorChar;
        return candidate.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasReparsePointOnExistingPath(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidDataException("A OneDrive root has no filesystem root.");
        var remainder = fullPath[root.Length..];
        var current = root;

        foreach (var segment in remainder.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (FileNotFoundException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return true;
        }

        return false;
    }

    private sealed record ProductionRemoteSettings(
        string? OneDriveRoot,
        string? GitHubOwner,
        string? GitHubRepository,
        string? GitHubCredentialTarget);
}
