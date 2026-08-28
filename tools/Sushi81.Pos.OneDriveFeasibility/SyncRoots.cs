using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Windows.Storage.Provider;

namespace Sushi81.Pos.OneDriveFeasibility;

public sealed record RegisteredSyncRoot(
    string Path,
    [property: JsonIgnore] string Id,
    Guid ProviderId,
    bool IsOneDrive,
    string? Error = null);

public sealed record RootValidationResult(
    string SuppliedPath,
    bool IsAccepted,
    RegisteredSyncRoot? MatchedRoot,
    string Reason);

public interface ISyncRootCatalog
{
    IReadOnlyList<RegisteredSyncRoot> Enumerate();
    RootValidationResult Validate(string path);
}

/// <summary>Uses the first-party StorageProvider sync-root registry; a folder name is never treated as proof.</summary>
public sealed class WindowsSyncRootCatalog : ISyncRootCatalog
{
    public IReadOnlyList<RegisteredSyncRoot> Enumerate()
    {
        try
        {
            if (!StorageProviderSyncRootManager.IsSupported())
            {
                return [];
            }

            return StorageProviderSyncRootManager.GetCurrentSyncRoots()
                .Select(root =>
                {
                    var rootPath = root.Path?.Path ?? string.Empty;
                    var id = root.Id ?? string.Empty;
                    return new RegisteredSyncRoot(rootPath, id, root.ProviderId, IsOneDriveId(id));
                })
                .Where(root => !string.IsNullOrWhiteSpace(root.Path))
                .OrderBy(root => root.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            return [new RegisteredSyncRoot(string.Empty, string.Empty, Guid.Empty, false, exception.Message)];
        }
    }

    public RootValidationResult Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var roots = Enumerate().Where(root => string.IsNullOrWhiteSpace(root.Error)).ToArray();
        var match = roots
            .Where(root => IsPathUnderRoot(fullPath, root.Path))
            .OrderByDescending(root => root.Path.Length)
            .FirstOrDefault();

        if (match is null)
        {
            return new(fullPath, false, null, "The path is not beneath a registered Windows cloud sync root.");
        }

        if (!match.IsOneDrive)
        {
            return new(fullPath, false, match, "The matching registered root is not identified as OneDrive by its provider metadata.");
        }

        return new(fullPath, true, match, "The path is beneath a registered root identified as OneDrive.");
    }

    public static bool IsPathUnderRoot(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        try
        {
            var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsOneDriveId(string id) => id.StartsWith("OneDrive!", StringComparison.OrdinalIgnoreCase);
}
