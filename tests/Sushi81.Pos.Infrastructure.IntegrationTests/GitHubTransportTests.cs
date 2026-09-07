using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Infrastructure.GitHubTransport;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class GitHubTransportTests
{
    private const string SecretToken = "synthetic-pat-must-not-leak";
    private static readonly GitHubReleaseContainer Release = new(
        9,
        "sushi81-handoff-v1",
        "https://uploads.example/repos/acme/handoff/releases/9/assets{?name,label}",
        Draft: false,
        Prerelease: false);

    [TestMethod]
    public async Task ValidUploadedReceiptIsReturnedWithExactRequestAndDigestValidation()
    {
        var bytes = Encoding.UTF8.GetBytes("synthetic snapshot");
        var name = "20260907120000.snapshot.db";
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        var handler = new StubHandler(request => JsonResponse(HttpStatusCode.Created, new
        {
            id = 42,
            name,
            size = bytes.Length,
            state = "uploaded",
            digest = "sha256:" + digest.ToLowerInvariant(),
            created_at = "2026-09-07T12:00:00Z"
        }));

        using var client = new HttpClient(handler);
        using var transport = CreateTransport(client);
        var receipt = await transport.UploadAssetAsync(Release, name, new MemoryStream(bytes), bytes.Length, digest);

        Assert.AreEqual(Release.Id, receipt.ReleaseId);
        Assert.AreEqual(42L, receipt.AssetId);
        Assert.AreEqual(name, receipt.Name);
        Assert.AreEqual(bytes.Length, receipt.Size);
        Assert.AreEqual("uploaded", receipt.State);
        Assert.HasCount(1, handler.Observations);
        Assert.AreEqual(HttpMethod.Post, handler.Observations[0].Method);
        StringAssert.Contains(handler.Observations[0].Uri.Query, "name=20260907120000.snapshot.db");
        Assert.AreEqual("Bearer " + SecretToken, handler.Observations[0].Authorization);
        Assert.AreEqual(bytes.Length, handler.Observations[0].ContentLength);
    }

    [TestMethod]
    [DataRow("missing-state")]
    [DataRow("starter")]
    [DataRow("partial")]
    [DataRow("missing-digest")]
    [DataRow("name-mismatch")]
    [DataRow("size-mismatch")]
    [DataRow("asset-id-missing")]
    [DataRow("digest-mismatch")]
    public async Task ContradictoryOrIncompleteReceiptsAreRejected(string variant)
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var name = "20260907120001.snapshot.db";
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        var receipt = variant switch
        {
            "missing-state" => $"{{\"id\":42,\"name\":\"{name}\",\"size\":4,\"digest\":\"sha256:{digest.ToLowerInvariant()}\"}}",
            "starter" => ReceiptJson(42, name, bytes.Length, "starter", "sha256:" + digest),
            "partial" => ReceiptJson(42, name, bytes.Length, "partial", "sha256:" + digest),
            "missing-digest" => $"{{\"id\":42,\"name\":\"{name}\",\"size\":4,\"state\":\"uploaded\"}}",
            "name-mismatch" => ReceiptJson(42, "20260907120002.snapshot.db", bytes.Length, "uploaded", "sha256:" + digest),
            "size-mismatch" => ReceiptJson(42, name, bytes.Length + 1, "uploaded", "sha256:" + digest),
            "asset-id-missing" => ReceiptJson(0, name, bytes.Length, "uploaded", "sha256:" + digest),
            "digest-mismatch" => ReceiptJson(42, name, bytes.Length, "uploaded", "sha256:" + new string('a', 64)),
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(receipt, Encoding.UTF8, "application/json")
        });

        using var client = new HttpClient(handler);
        using var transport = CreateTransport(client);
        var exception = await Assert.ThrowsAsync<GitHubTransportException>(() =>
            transport.UploadAssetAsync(Release, name, new MemoryStream(bytes), bytes.Length, digest));

        StringAssert.Contains(exception.Message, "receipt");
        Assert.AreEqual(HttpStatusCode.Created, exception.StatusCode);
        Assert.IsFalse(exception.ToString().Contains(SecretToken, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(HttpStatusCode.UnprocessableEntity)]
    [DataRow(HttpStatusCode.BadGateway)]
    [DataRow(HttpStatusCode.Unauthorized)]
    public async Task GitHubApiFailuresFailClosedWithoutEchoingCredential(HttpStatusCode statusCode)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent($"{{\"message\":\"{SecretToken}\"}}", Encoding.UTF8, "application/json")
        });
        using var client = new HttpClient(handler);
        using var transport = CreateTransport(client);

        var exception = await Assert.ThrowsAsync<GitHubTransportException>(() =>
            transport.UploadAssetAsync(
                Release,
                "20260907120003.snapshot.db",
                new MemoryStream([9]),
                1,
                Convert.ToHexString(SHA256.HashData([9]))));

        Assert.AreEqual(statusCode, exception.StatusCode);
        Assert.IsFalse(exception.ToString().Contains(SecretToken, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CredentialProviderFailureIsRedactedAtTransportBoundary()
    {
        using var client = new HttpClient(new StubHandler(_ => throw new InvalidOperationException("Bearer " + SecretToken)));
        using var transport = new GitHubReleaseAssetTransport(
            Options(),
            new ThrowingCredentialProvider(),
            client);

        var exception = await Assert.ThrowsAsync<GitHubTransportException>(() =>
            transport.UploadAssetAsync(
                Release,
                "20260907120004.snapshot.db",
                new MemoryStream([1]),
                1,
                Convert.ToHexString(SHA256.HashData([1]))));

        StringAssert.Contains(exception.Message, "credential");
        Assert.IsFalse(exception.ToString().Contains(SecretToken, StringComparison.Ordinal));
    }

    [TestMethod]
    public void ConfigurationRejectsSourceRepositoryAndFilenameRulesRemainStrict()
    {
        Assert.IsFalse(new GitHubHandoffRepositoryOptions("cimerosef", "sushi81-pos").IsValid);
        Assert.Throws<ArgumentException>(() => new GitHubHandoffRepositoryOptions("cimerosef", "sushi81-pos").Validate());

        var snapshot = GitHubHandoffAssetNames.CreateSnapshotName(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        Assert.AreEqual("20260907120000.snapshot.db", snapshot);
        Assert.AreEqual("20260907120000.grant.json", GitHubHandoffAssetNames.CreateGrantName(snapshot));
        Assert.IsTrue(GitHubHandoffAssetNames.IsSupported("20260907120000.snapshot.db"));
        Assert.IsTrue(GitHubHandoffAssetNames.IsSupported("20260907120000.grant.json"));
        Assert.IsTrue(GitHubHandoffAssetNames.IsSupported("dr-0123456789abcdef0123456789abcdef-g-2.activation.json"));
        Assert.IsTrue(GitHubHandoffAssetNames.IsActivationName("dr-0123456789abcdef0123456789abcdef-g-2.activation.json"));
        Assert.IsFalse(GitHubHandoffAssetNames.IsSupported("2026090712000.snapshot.db"));
        Assert.IsFalse(GitHubHandoffAssetNames.IsSupported("20260907120000.snapshot.db.bak"));
        Assert.IsFalse(GitHubHandoffAssetNames.IsSupported("20260907120000.SNAPSHOT.DB"));
        Assert.IsFalse(GitHubHandoffAssetNames.IsActivationName("dr-0123456789abcdef0123456789abcdef-g-0.activation.json"));
    }

    [TestMethod]
    public async Task EnsureContainerRejectsPublicRepositoryAndAcceptsPrivateRelease()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases/tags/sushi81-handoff-v1", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, new
                {
                    id = 9,
                    tag_name = "sushi81-handoff-v1",
                    upload_url = Release.UploadUrl,
                    draft = false,
                    prerelease = false
                });
            }

            return JsonResponse(HttpStatusCode.OK, new { @private = true, visibility = "private" });
        });
        using var client = new HttpClient(handler);
        using var transport = CreateTransport(client);

        var release = await transport.EnsureContainerAsync(createIfMissing: false);

        Assert.AreEqual(Release, release);
        Assert.IsTrue(handler.Observations.All(observation => observation.Authorization == "Bearer " + SecretToken));
        Assert.IsTrue(handler.Observations.All(observation => observation.Accept == "application/vnd.github+json"));
    }

    [TestMethod]
    public async Task EnsureContainerRejectsPublicRepository()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, new { @private = false, visibility = "public" }));
        using var client = new HttpClient(handler);
        using var transport = CreateTransport(client);

        var exception = await Assert.ThrowsAsync<GitHubTransportException>(() => transport.EnsureContainerAsync(createIfMissing: false));

        StringAssert.Contains(exception.Message, "private");
        Assert.IsNull(exception.StatusCode);
    }

    [TestMethod]
    public async Task PrivateRepositoryConfigurationDoesNotPermitSourceRepository()
    {
        var exception = Assert.Throws<ArgumentException>(() => new GitHubReleaseAssetTransport(
            new GitHubHandoffRepositoryOptions("cimerosef", "sushi81-pos"),
            new FixedCredentialProvider(SecretToken),
            new HttpClient(new StubHandler(_ => throw new InvalidOperationException()))));

        StringAssert.Contains(exception.Message, "source-code");
    }

    private static GitHubReleaseAssetTransport CreateTransport(HttpClient client) =>
        new(Options(), new FixedCredentialProvider(SecretToken), client);

    private static GitHubHandoffRepositoryOptions Options() =>
        new("acme", "handoff", ApiBaseUri: new Uri("https://api.example/"));

    private static string ReceiptJson(long id, string name, long size, string state, string digest) =>
        JsonSerializer.Serialize(new { id, name, size, state, digest, created_at = "2026-09-07T12:00:00Z" });

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object value) => new(statusCode)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private sealed class FixedCredentialProvider(string token) : IProtectedGitHubCredentialProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(token);
    }

    private sealed class ThrowingCredentialProvider : IProtectedGitHubCredentialProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromException<string?>(new InvalidOperationException("Bearer " + SecretToken));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RequestObservation> Observations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Observations.Add(new RequestObservation(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.Accept.SingleOrDefault()?.MediaType,
                request.Content?.Headers.ContentLength));
            return Task.FromResult(responder(request));
        }
    }

    private sealed record RequestObservation(
        HttpMethod Method,
        Uri Uri,
        string? Authorization,
        string? Accept,
        long? ContentLength);
}
