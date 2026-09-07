using System.Globalization;

namespace Sushi81.Pos.Application.Foundation.GitHubTransport;

/// <summary>
/// Non-secret configuration for the dedicated private repository that carries POS handoff
/// release assets. The source-code repository is never a valid operational repository.
/// </summary>
public sealed record GitHubHandoffRepositoryOptions(
    string Owner,
    string Repository,
    string ReleaseTag = "sushi81-handoff-v1",
    string ReleaseName = "Sushi81 POS Handoff Transport",
    Uri? ApiBaseUri = null,
    TimeSpan? Timeout = null)
{
    public Uri BaseUri => ApiBaseUri ?? new Uri("https://api.github.com/", UriKind.Absolute);

    public bool IsValid => !string.IsNullOrWhiteSpace(Owner)
        && !string.IsNullOrWhiteSpace(Repository)
        && !ContainsPathSeparator(Owner)
        && !ContainsPathSeparator(Repository)
        && !string.IsNullOrWhiteSpace(ReleaseTag)
        && !string.IsNullOrWhiteSpace(ReleaseName)
        && BaseUri.IsAbsoluteUri
        && BaseUri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(BaseUri.UserInfo)
        && string.IsNullOrEmpty(BaseUri.Query)
        && string.IsNullOrEmpty(BaseUri.Fragment)
        && (Timeout is null || Timeout.Value > TimeSpan.Zero)
        && !IsSourceCodeRepository;

    public void Validate()
    {
        if (IsSourceCodeRepository)
        {
            throw new ArgumentException("The source-code repository cannot be used as the GitHub handoff repository.", nameof(GitHubHandoffRepositoryOptions));
        }

        if (!IsValid)
        {
            throw new ArgumentException("GitHub handoff repository configuration is invalid.", nameof(GitHubHandoffRepositoryOptions));
        }
    }

    private bool IsSourceCodeRepository => Owner.Equals("cimerosef", StringComparison.OrdinalIgnoreCase)
        && Repository.Equals("sushi81-pos", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsPathSeparator(string value) => value.Contains('/') || value.Contains('\\');
}

/// <summary>
/// Protected credential boundary. Implementations own the Windows protected-storage details;
/// callers must treat the returned value as a secret and must never log, serialize or persist it.
/// </summary>
public interface IProtectedGitHubCredentialProvider
{
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>Validated identity of the one long-lived GitHub Release container.</summary>
public sealed record GitHubReleaseContainer(
    long Id,
    string TagName,
    string UploadUrl,
    bool Draft,
    bool Prerelease)
{
    public bool IsValid => Id > 0
        && !string.IsNullOrWhiteSpace(TagName)
        && IsHttpUri(UploadUrl)
        && !Draft
        && !Prerelease;

    private static bool IsHttpUri(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo);
    }
}

/// <summary>Server-side metadata for an asset returned by list/get operations.</summary>
public sealed record GitHubRemoteAsset(
    long Id,
    string Name,
    long Size,
    string State,
    string Digest,
    DateTimeOffset? CreatedAtUtc)
{
    public bool IsComplete => Id > 0
        && !string.IsNullOrWhiteSpace(Name)
        && Size > 0
        && State.Equals("uploaded", StringComparison.OrdinalIgnoreCase)
        && GitHubSha256.IsServerDigest(Digest);
}

/// <summary>Immutable server receipt that can be persisted with a transfer after validation.</summary>
public sealed record GitHubAssetReceipt(
    long ReleaseId,
    long AssetId,
    string Name,
    long Size,
    string Digest,
    DateTimeOffset? CreatedAtUtc,
    string State = "")
{
    public bool IsStrictlyValidFor(long expectedReleaseId, string expectedName, long expectedSize, string expectedSha256)
    {
        return ReleaseId == expectedReleaseId
            && AssetId > 0
            && string.Equals(Name, expectedName, StringComparison.Ordinal)
            && Size == expectedSize
            && expectedSize > 0
            && string.Equals(State, "uploaded", StringComparison.OrdinalIgnoreCase)
            && GitHubSha256.TryNormalizeServerDigest(Digest, out var actualDigest)
            && GitHubSha256.TryNormalizeExpected(expectedSha256, out var localDigest)
            && string.Equals(actualDigest, localDigest, StringComparison.OrdinalIgnoreCase);
    }

    public void EnsureStrictlyValidFor(long expectedReleaseId, string expectedName, long expectedSize, string expectedSha256)
    {
        if (!IsStrictlyValidFor(expectedReleaseId, expectedName, expectedSize, expectedSha256))
        {
            throw new InvalidDataException("The GitHub Release Asset receipt is incomplete or contradictory.");
        }
    }
}

/// <summary>Small semantic transport seam used by later handoff/state-machine work.</summary>
public interface IGitHubHandoffTransport
{
    Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default);

    Task<GitHubAssetReceipt> UploadAssetAsync(
        GitHubReleaseContainer release,
        string name,
        Stream content,
        long contentLength,
        string localSha256,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(
        GitHubReleaseContainer release,
        CancellationToken cancellationToken = default);

    Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default);

    Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default);

    Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default);
}

/// <summary>Strict handoff filename and SHA-256 validation shared by application consumers.</summary>
public static class GitHubHandoffAssetNames
{
    public static string CreateSnapshotName(DateTimeOffset localTime) =>
        localTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ".snapshot.db";

    public static string CreateGrantName(string snapshotName)
    {
        if (!IsSnapshotName(snapshotName))
        {
            throw new ArgumentException("The snapshot filename is invalid.", nameof(snapshotName));
        }

        return snapshotName[..14] + ".grant.json";
    }

    public static string CreateActivationName(Guid lineageId, long generation)
    {
        if (lineageId == Guid.Empty || generation < 1)
            throw new ArgumentOutOfRangeException(nameof(generation));
        return $"dr-{lineageId:N}-g-{generation.ToString(CultureInfo.InvariantCulture)}.activation.json";
    }

    public static bool IsSupported(string? name) => IsSnapshotName(name) || IsGrantName(name) || IsActivationName(name);

    public static bool IsSnapshotName(string? name) => HasTimestampSuffix(name, ".snapshot.db");

    public static bool IsGrantName(string? name) => HasTimestampSuffix(name, ".grant.json");

    public static bool IsActivationName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("dr-", StringComparison.Ordinal)
            || !name.EndsWith(".activation.json", StringComparison.Ordinal))
            return false;

        var core = name[3..^16];
        var separator = core.IndexOf("-g-", StringComparison.Ordinal);
        return separator > 0
            && Guid.TryParseExact(core[..separator], "N", out _)
            && long.TryParse(core[(separator + 3)..], NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
            && generation > 0;
    }

    private static bool HasTimestampSuffix(string? name, string suffix)
    {
        if (name is null || name.Length != 14 + suffix.Length || !name.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 0; index < 14; index++)
        {
            if (name[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}

public static class GitHubSha256
{
    public static bool IsServerDigest(string? digest) => TryNormalizeServerDigest(digest, out _);

    public static bool TryNormalizeServerDigest(string? digest, out string normalized)
    {
        normalized = string.Empty;
        if (digest is null || digest.Length != 71 || !digest.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return false;
        }

        var hex = digest[7..];
        if (!IsHex64(hex))
        {
            return false;
        }

        normalized = "sha256:" + hex.ToLowerInvariant();
        return true;
    }

    public static bool TryNormalizeExpected(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (value is null)
        {
            return false;
        }

        var hex = value.StartsWith("sha256:", StringComparison.Ordinal)
            ? value[7..]
            : value;
        if (!IsHex64(hex))
        {
            return false;
        }

        normalized = "sha256:" + hex.ToLowerInvariant();
        return true;
    }

    private static bool IsHex64(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
