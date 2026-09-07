using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sushi81.Pos.Application.Foundation.GitHubTransport;

namespace Sushi81.Pos.Infrastructure.GitHubTransport;

public sealed class GitHubTransportException : IOException
{
    public GitHubTransportException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}

/// <summary>
/// Direct GitHub REST Release Asset transport. It contains no authority-transition logic; callers
/// must decide when a validated receipt is eligible for a durable protocol transition.
/// </summary>
public sealed class GitHubReleaseAssetTransport : IGitHubHandoffTransport, IDisposable
{
    private const string ApiVersion = "2022-11-28";
    private const int PageSize = 100;
    private const int MaximumAssetPages = 10_000;
    private readonly GitHubHandoffRepositoryOptions options;
    private readonly IProtectedGitHubCredentialProvider credentialProvider;
    private readonly HttpClient client;
    private readonly bool ownsClient;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    public GitHubReleaseAssetTransport(
        GitHubHandoffRepositoryOptions options,
        IProtectedGitHubCredentialProvider credentialProvider,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credentialProvider);
        options.Validate();

        this.options = options;
        this.credentialProvider = credentialProvider;
        client = httpClient ?? new HttpClient();
        ownsClient = httpClient is null;
        client.Timeout = options.Timeout ?? TimeSpan.FromSeconds(60);
    }

    public async Task<GitHubReleaseContainer> EnsureContainerAsync(
        bool createIfMissing,
        CancellationToken cancellationToken = default)
    {
        var repositoryUri = ApiUri($"repos/{Uri.EscapeDataString(options.Owner)}/{Uri.EscapeDataString(options.Repository)}");
        using var repositoryResponse = await SendAsync(
            HttpMethod.Get,
            repositoryUri,
            content: null,
            [HttpStatusCode.OK, HttpStatusCode.NotFound],
            cancellationToken);

        if (repositoryResponse.StatusCode == HttpStatusCode.NotFound)
        {
            throw new GitHubTransportException("The configured GitHub handoff repository was not found.", repositoryResponse.StatusCode);
        }

        var repository = await ReadJsonAsync<GitHubRepositoryDto>(repositoryResponse, cancellationToken);
        if (!repository.Private || string.Equals(repository.Visibility, "public", StringComparison.OrdinalIgnoreCase))
        {
            throw new GitHubTransportException("The configured GitHub handoff repository must be private.");
        }

        var releaseUri = ApiUri($"repos/{Uri.EscapeDataString(options.Owner)}/{Uri.EscapeDataString(options.Repository)}/releases/tags/{Uri.EscapeDataString(options.ReleaseTag)}");
        using var releaseResponse = await SendAsync(
            HttpMethod.Get,
            releaseUri,
            content: null,
            [HttpStatusCode.OK, HttpStatusCode.NotFound],
            cancellationToken);

        if (releaseResponse.StatusCode == HttpStatusCode.NotFound)
        {
            if (!createIfMissing)
            {
                throw new GitHubTransportException("The configured GitHub handoff release was not found.", releaseResponse.StatusCode);
            }

            var body = JsonSerializer.Serialize(new
            {
                tag_name = options.ReleaseTag,
                name = options.ReleaseName,
                draft = false,
                prerelease = false,
                make_latest = "false"
            }, JsonOptions);
            using var requestContent = new StringContent(body, Encoding.UTF8, "application/json");
            using var createdResponse = await SendAsync(
                HttpMethod.Post,
                ApiUri($"repos/{Uri.EscapeDataString(options.Owner)}/{Uri.EscapeDataString(options.Repository)}/releases"),
                requestContent,
                [HttpStatusCode.Created],
                cancellationToken);
            return await ParseReleaseAsync(createdResponse, cancellationToken);
        }

        return await ParseReleaseAsync(releaseResponse, cancellationToken);
    }

    public async Task<GitHubAssetReceipt> UploadAssetAsync(
        GitHubReleaseContainer release,
        string name,
        Stream content,
        long contentLength,
        string localSha256,
        CancellationToken cancellationToken = default)
    {
        ValidateRelease(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(content);
        if (!GitHubHandoffAssetNames.IsSupported(name))
        {
            throw new ArgumentException("The GitHub handoff asset filename is invalid.", nameof(name));
        }

        if (!content.CanRead || !GitHubSha256.TryNormalizeExpected(localSha256, out _))
        {
            throw new ArgumentException("The GitHub handoff asset content metadata is invalid.");
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contentLength);

        using var requestContent = new StreamContent(content);
        requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        requestContent.Headers.ContentLength = contentLength;
        using var response = await SendAsync(
            HttpMethod.Post,
            CreateUploadUri(release.UploadUrl, name),
            requestContent,
            [HttpStatusCode.Created],
            cancellationToken);

        var remote = await ReadJsonAsync<GitHubAssetDto>(response, cancellationToken);
        var receipt = new GitHubAssetReceipt(
            release.Id,
            remote.Id,
            remote.Name ?? string.Empty,
            remote.Size,
            remote.Digest ?? string.Empty,
            remote.CreatedAt,
            remote.State ?? string.Empty);

        try
        {
            receipt.EnsureStrictlyValidFor(release.Id, name, contentLength, localSha256);
        }
        catch (InvalidDataException exception)
        {
            throw new GitHubTransportException(exception.Message, response.StatusCode);
        }

        return receipt;
    }

    public async Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(
        GitHubReleaseContainer release,
        CancellationToken cancellationToken = default)
    {
        ValidateRelease(release);
        var all = new List<GitHubRemoteAsset>();
        for (var page = 1; page <= MaximumAssetPages; page++)
        {
            var uri = ApiUri($"repos/{Uri.EscapeDataString(options.Owner)}/{Uri.EscapeDataString(options.Repository)}/releases/{release.Id}/assets?per_page={PageSize}&page={page}");
            using var response = await SendAsync(HttpMethod.Get, uri, null, [HttpStatusCode.OK], cancellationToken);
            var assets = await ReadJsonAsync<List<GitHubAssetDto>>(response, cancellationToken);
            if (assets is null)
            {
                throw new GitHubTransportException("GitHub returned no release-asset list.", response.StatusCode);
            }

            all.AddRange(assets.Select(ToRemoteAsset));
            if (assets.Count < PageSize)
            {
                return all;
            }
        }

        throw new GitHubTransportException("GitHub release-asset pagination exceeded the safe limit.");
    }

    public async Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(assetId);

        using var response = await SendAsync(
            HttpMethod.Get,
            ApiUri($"repos/{Uri.EscapeDataString(options.Owner)}/{Uri.EscapeDataString(options.Repository)}/releases/assets/{assetId}"),
            null,
            [HttpStatusCode.OK],
            cancellationToken);
        var asset = await ReadJsonAsync<GitHubAssetDto>(response, cancellationToken);
        if (asset.Id != assetId)
        {
            throw new GitHubTransportException("GitHub returned a release asset with a contradictory identity.", response.StatusCode);
        }

        return ToRemoteAsset(asset);
    }

    public async Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(assetId);

        HttpResponseMessage? response = null;
        try
        {
            response = await SendAsync(
                HttpMethod.Get,
                ApiUri($"repos/{Uri.EscapeDataString(options.Owner)}/{Uri.EscapeDataString(options.Repository)}/releases/assets/{assetId}"),
                null,
                [HttpStatusCode.OK],
                cancellationToken,
                acceptBinary: true);
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new ResponseOwnedStream(response, stream);
        }
        catch
        {
            response?.Dispose();
            throw;
        }
    }

    public async Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(assetId);

        using var response = await SendAsync(
            HttpMethod.Delete,
            ApiUri($"repos/{Uri.EscapeDataString(options.Owner)}/{Uri.EscapeDataString(options.Repository)}/releases/assets/{assetId}"),
            null,
            [HttpStatusCode.NoContent],
            cancellationToken);
    }

    public void Dispose()
    {
        if (ownsClient)
        {
            client.Dispose();
        }
    }

    private Uri ApiUri(string relativePath) => new(options.BaseUri, relativePath);

    private void ValidateRelease(GitHubReleaseContainer release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (!release.IsValid || !string.Equals(release.TagName, options.ReleaseTag, StringComparison.Ordinal))
        {
            throw new ArgumentException("The GitHub handoff release is invalid or is not the configured release.", nameof(release));
        }
    }

    private static Uri CreateUploadUri(string uploadUrl, string name)
    {
        var templateStart = uploadUrl.IndexOf('{');
        var baseUrl = templateStart >= 0 ? uploadUrl[..templateStart] : uploadUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(parsed.UserInfo))
        {
            throw new ArgumentException("The GitHub release upload URL is invalid.", nameof(uploadUrl));
        }

        var builder = new UriBuilder(parsed);
        var existingQuery = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(existingQuery)
            ? "name=" + Uri.EscapeDataString(name)
            : existingQuery + "&name=" + Uri.EscapeDataString(name);
        return builder.Uri;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        Uri uri,
        HttpContent? content,
        HttpStatusCode[] expectedStatuses,
        CancellationToken cancellationToken,
        bool acceptBinary = false)
    {
        string? token;
        try
        {
            token = await credentialProvider.GetAccessTokenAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            throw new GitHubTransportException("The protected GitHub handoff credential could not be loaded.");
        }

        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsControl))
        {
            throw new GitHubTransportException("The protected GitHub handoff credential is unavailable.");
        }

        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.UserAgent.ParseAdd("Sushi81POS-M07/1.0");
        request.Headers.Accept.ParseAdd(acceptBinary ? "application/octet-stream" : "application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        try
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        catch (FormatException exception)
        {
            throw new GitHubTransportException("The protected GitHub handoff credential is invalid.", null, exception);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubTransportException("The GitHub API request timed out.", null, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new GitHubTransportException("The GitHub API network request failed.", null, exception);
        }

        if (!expectedStatuses.Contains(response.StatusCode))
        {
            var statusCode = response.StatusCode;
            response.Dispose();
            throw new GitHubTransportException($"The GitHub API request failed with HTTP {(int)statusCode}.", statusCode);
        }

        return response;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                ?? throw new GitHubTransportException("GitHub returned an empty JSON response.", response.StatusCode);
        }
        catch (JsonException exception)
        {
            throw new GitHubTransportException("GitHub returned malformed JSON.", response.StatusCode, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubTransportException("The GitHub API response timed out.", null, exception);
        }
    }

    private async Task<GitHubReleaseContainer> ParseReleaseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var release = await ReadJsonAsync<GitHubReleaseDto>(response, cancellationToken);
        var result = new GitHubReleaseContainer(
            release.Id,
            release.TagName ?? string.Empty,
            release.UploadUrl ?? string.Empty,
            release.Draft,
            release.Prerelease);
        if (!result.IsValid || !string.Equals(result.TagName, options.ReleaseTag, StringComparison.Ordinal))
        {
            throw new GitHubTransportException("GitHub returned an invalid or contradictory handoff release.", response.StatusCode);
        }

        return result;
    }

    private static GitHubRemoteAsset ToRemoteAsset(GitHubAssetDto asset) => new(
        asset.Id,
        asset.Name ?? string.Empty,
        asset.Size,
        asset.State ?? string.Empty,
        asset.Digest ?? string.Empty,
        asset.CreatedAt);

    private sealed record GitHubRepositoryDto(bool Private, string? Visibility);

    private sealed record GitHubReleaseDto(
        long Id,
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("upload_url")] string? UploadUrl,
        bool Draft,
        bool Prerelease);

    private sealed record GitHubAssetDto(
        long Id,
        string? Name,
        long Size,
        string? State,
        string? Digest,
        [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt);

    private sealed class ResponseOwnedStream(HttpResponseMessage response, Stream inner) : Stream
    {
        private readonly HttpResponseMessage response = response;
        private readonly Stream inner = inner;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            response.Dispose();
            await base.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
