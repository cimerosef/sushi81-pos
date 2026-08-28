using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sushi81.Pos.OneDriveFeasibility;

public sealed record GitHubHandoffTransportOptions(
    string Owner,
    string Repository,
    string ReleaseTag = "sushi81-handoff-v1",
    string ReleaseName = "Sushi81 POS Handoff Transport",
    Uri? ApiBaseUri = null,
    TimeSpan? Timeout = null)
{
    public Uri BaseUri => ApiBaseUri ?? new Uri("https://api.github.com/");
    public bool IsValid => !string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repository)
        && !Owner.Contains('/') && !Repository.Contains('/') && !string.IsNullOrWhiteSpace(ReleaseTag);
}

public sealed record GitHubAssetReceipt(
    long ReleaseId,
    long AssetId,
    string Name,
    long Size,
    string Digest,
    DateTimeOffset? CreatedAtUtc,
    string State = "uploaded")
{
    public bool IsValid => ReleaseId > 0 && AssetId > 0 && !string.IsNullOrWhiteSpace(Name)
        && Size > 0 && State.Equals("uploaded", StringComparison.OrdinalIgnoreCase)
        && GitHubDigest.IsValid(Digest);
}

public sealed record GitHubReleaseContainer(long Id, string TagName, string UploadUrl, string HtmlUrl, bool Draft, bool Prerelease)
{
    public bool IsValid => Id > 0 && !string.IsNullOrWhiteSpace(TagName) && !string.IsNullOrWhiteSpace(UploadUrl) && !Draft && !Prerelease;
}

public sealed record GitHubRemoteAsset(long Id, string Name, long Size, string State, string Digest, string BrowserDownloadUrl, DateTimeOffset? CreatedAtUtc)
{
    public bool IsComplete => Id > 0 && State.Equals("uploaded", StringComparison.OrdinalIgnoreCase) && Size > 0 && GitHubDigest.IsValid(Digest);
}

public sealed record GitHubHandoffGrant(
    [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
    [property: JsonPropertyName("transferId")] string TransferId,
    [property: JsonPropertyName("lineageId")] string LineageId,
    [property: JsonPropertyName("generation")] long Generation,
    [property: JsonPropertyName("handoffVersion")] long HandoffVersion,
    [property: JsonPropertyName("sourceDeviceId")] string SourceDeviceId,
    [property: JsonPropertyName("targetDeviceId")] string TargetDeviceId,
    [property: JsonPropertyName("snapshotReleaseId")] long SnapshotReleaseId,
    [property: JsonPropertyName("snapshotAssetId")] long SnapshotAssetId,
    [property: JsonPropertyName("snapshotAssetName")] string SnapshotAssetName,
    [property: JsonPropertyName("snapshotByteLength")] long SnapshotByteLength,
    [property: JsonPropertyName("snapshotSha256")] string SnapshotSha256,
    [property: JsonPropertyName("snapshotCreatedAtUtc")] DateTimeOffset SnapshotCreatedAtUtc,
    [property: JsonPropertyName("sourceRelinquishedAtUtc")] DateTimeOffset SourceRelinquishedAtUtc,
    [property: JsonPropertyName("grantPublishedAtUtc")] DateTimeOffset GrantPublishedAtUtc)
{
    public bool IsValid => ProtocolVersion == 1 && Guid.TryParse(TransferId, out _) && Guid.TryParse(LineageId, out _)
        && Generation >= 0 && HandoffVersion >= 1 && !string.IsNullOrWhiteSpace(SourceDeviceId)
        && !string.IsNullOrWhiteSpace(TargetDeviceId) && !SourceDeviceId.Equals(TargetDeviceId, StringComparison.Ordinal)
        && SnapshotReleaseId > 0 && SnapshotAssetId > 0 && GitHubSnapshotName.IsValid(SnapshotAssetName)
        && SnapshotByteLength > 0 && GitHubDigest.IsValid("sha256:" + SnapshotSha256)
        && SnapshotCreatedAtUtc.Offset == TimeSpan.Zero && SourceRelinquishedAtUtc.Offset == TimeSpan.Zero
        && GrantPublishedAtUtc.Offset == TimeSpan.Zero;
}

public interface IGitHubHandoffTransport
{
    Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default);
    Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, string filePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default);
    Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default);
    Task<byte[]> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default);
    Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default);
}

public sealed class GitHubTransportException : IOException
{
    public HttpStatusCode? StatusCode { get; }
    public GitHubTransportException(string message, HttpStatusCode? statusCode = null, Exception? inner = null) : base(message, inner) => StatusCode = statusCode;
}

public static class GitHubSnapshotName
{
    public static string Create(DateTimeOffset localTime) => localTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ".snapshot.db";
    public static bool IsValid(string? name) => name is not null && System.Text.RegularExpressions.Regex.IsMatch(name, "^[0-9]{14}\\.snapshot\\.db$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    public static string GrantName(string snapshotName) => IsValid(snapshotName) ? snapshotName[..14] + ".grant.json" : throw new ArgumentException("Snapshot name is invalid.", nameof(snapshotName));
    public static bool IsGrant(string? name) => name is not null && System.Text.RegularExpressions.Regex.IsMatch(name, "^[0-9]{14}\\.grant\\.json$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}

public static class GitHubDigest
{
    public static bool IsValid(string? digest) => digest is not null && digest.StartsWith("sha256:", StringComparison.Ordinal)
        && digest.Length == 71 && digest[7..].All(Uri.IsHexDigit);
    public static string FromHex(string hex) => IsHex(hex) ? "sha256:" + hex.ToLowerInvariant() : throw new ArgumentException("SHA-256 hex is invalid.", nameof(hex));
    private static bool IsHex(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public sealed class GitHubReleaseAssetTransport : IGitHubHandoffTransport, IDisposable
{
    private readonly HttpClient client;
    private readonly GitHubHandoffTransportOptions options;
    private readonly string token;
    private readonly bool ownsClient;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };

    public GitHubReleaseAssetTransport(GitHubHandoffTransportOptions options, HttpClient? httpClient = null, string? token = null)
    {
        if (options is null || !options.IsValid) throw new ArgumentException("GitHub handoff transport options are invalid.", nameof(options));
        this.options = options;
        this.token = token ?? Environment.GetEnvironmentVariable("SUSHI81_GITHUB_HANDOFF_TOKEN") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(this.token)) throw new GitHubTransportException("SUSHI81_GITHUB_HANDOFF_TOKEN is not set.");
        client = httpClient ?? new HttpClient(); ownsClient = httpClient is null;
        client.BaseAddress = options.BaseUri;
        client.Timeout = options.Timeout ?? TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Sushi81POS-M02/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this.token);
    }

    public async Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default)
    {
        using var repo = await SendAsync(HttpMethod.Get, $"repos/{Uri.EscapeDataString(options.Owner)}/{Uri.EscapeDataString(options.Repository)}", null, cancellationToken);
        if (repo.StatusCode == HttpStatusCode.NotFound) throw new GitHubTransportException("Configured handoff repository was not found.", repo.StatusCode);
        var repository = await ParseAsync<GitHubRepository>(repo, cancellationToken);
        if (repository.Visibility is not null && repository.Visibility.Equals("public", StringComparison.OrdinalIgnoreCase) || repository.Private == false)
            throw new GitHubTransportException("The configured handoff repository must be private.");
        using var release = await SendAsync(HttpMethod.Get, $"repos/{options.Owner}/{options.Repository}/releases/tags/{Uri.EscapeDataString(options.ReleaseTag)}", null, cancellationToken, allowNotFound: true);
        if (release.StatusCode == HttpStatusCode.NotFound)
        {
            if (!createIfMissing) throw new GitHubTransportException("Configured handoff release was not found.", release.StatusCode);
            var body = JsonSerializer.Serialize(new { tag_name = options.ReleaseTag, name = options.ReleaseName, draft = false, prerelease = false, make_latest = false });
            using var created = await SendAsync(HttpMethod.Post, $"repos/{options.Owner}/{options.Repository}/releases", new StringContent(body, Encoding.UTF8, "application/json"), cancellationToken);
            return await ParseReleaseAsync(created, cancellationToken);
        }
        return await ParseReleaseAsync(release, cancellationToken);
    }

    public async Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, string filePath, CancellationToken cancellationToken = default)
    {
        if (!release.IsValid || !GitHubSnapshotName.IsValid(name) && !GitHubSnapshotName.IsGrant(name) || !File.Exists(filePath)) throw new ArgumentException("Release, asset name or file is invalid.");
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var localDigest = GitHubDigest.FromHex(Convert.ToHexString(SHA256.HashData(bytes)));
        var url = release.UploadUrl.Split('{')[0];
        using var content = new ByteArrayContent(bytes); content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await SendAsync(HttpMethod.Post, url + "?name=" + Uri.EscapeDataString(name), content, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Created) throw new GitHubTransportException("GitHub release-asset upload was not acknowledged with HTTP 201.", response.StatusCode);
        var remote = await ParseAsync<GitHubAssetDto>(response, cancellationToken);
        var receipt = new GitHubAssetReceipt(release.Id, remote.Id, remote.Name ?? string.Empty, remote.Size, remote.Digest ?? string.Empty, remote.CreatedAt, remote.State ?? string.Empty);
        if (!receipt.IsValid || receipt.Name != name || receipt.Size != bytes.LongLength || !receipt.Digest.Equals(localDigest, StringComparison.OrdinalIgnoreCase))
            throw new GitHubTransportException("GitHub release-asset receipt is incomplete or contradicts the local immutable asset.");
        return receipt;
    }

    public async Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"repos/{options.Owner}/{options.Repository}/releases/{release.Id}/assets", null, cancellationToken);
        var assets = await ParseAsync<List<GitHubAssetDto>>(response, cancellationToken);
        return assets.Select(ToAsset).ToArray();
    }

    public async Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"repos/{options.Owner}/{options.Repository}/releases/assets/{assetId}", null, cancellationToken);
        return ToAsset(await ParseAsync<GitHubAssetDto>(response, cancellationToken));
    }

    public async Task<byte[]> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"repos/{options.Owner}/{options.Repository}/releases/assets/{assetId}", null, cancellationToken, acceptBinary: true);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"repos/{options.Owner}/{options.Repository}/releases/assets/{assetId}", null, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NoContent) throw new GitHubTransportException("GitHub release-asset deletion was not acknowledged with HTTP 204.", response.StatusCode);
    }

    public void Dispose() { if (ownsClient) client.Dispose(); }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken, bool allowNotFound = false, bool acceptBinary = false)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Accept.Clear(); request.Headers.Accept.ParseAdd(acceptBinary ? "application/octet-stream" : "application/vnd.github+json");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode >= 400 && !(allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
        {
            response.Dispose(); throw new GitHubTransportException($"GitHub API request failed with HTTP {(int)response.StatusCode}.", response.StatusCode);
        }
        return response;
    }

    private static async Task<T> ParseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken) ?? throw new GitHubTransportException("GitHub API returned an empty JSON response.");
        }
        catch (JsonException ex) { throw new GitHubTransportException("GitHub API returned malformed JSON.", response.StatusCode, ex); }
    }
    private static async Task<GitHubReleaseContainer> ParseReleaseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var dto = await ParseAsync<GitHubReleaseDto>(response, cancellationToken);
        var result = new GitHubReleaseContainer(dto.Id, dto.TagName ?? string.Empty, dto.UploadUrl ?? string.Empty, dto.HtmlUrl ?? string.Empty, dto.Draft, dto.Prerelease);
        if (!result.IsValid) throw new GitHubTransportException("GitHub handoff release metadata is incomplete or not a published release.");
        return result;
    }
    private static GitHubRemoteAsset ToAsset(GitHubAssetDto dto) => new(dto.Id, dto.Name ?? string.Empty, dto.Size, dto.State ?? string.Empty, dto.Digest ?? string.Empty, dto.BrowserDownloadUrl ?? string.Empty, dto.CreatedAt);
    private sealed record GitHubRepository(bool Private, string? Visibility);
    private sealed record GitHubReleaseDto(long Id, [property: JsonPropertyName("tag_name")] string? TagName, [property: JsonPropertyName("upload_url")] string? UploadUrl, [property: JsonPropertyName("html_url")] string? HtmlUrl, bool Draft, bool Prerelease);
    private sealed record GitHubAssetDto(long Id, string? Name, long Size, string? State, string? Digest, [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl, [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt);
}
