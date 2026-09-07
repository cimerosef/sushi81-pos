using System.Net;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Infrastructure.GitHubTransport;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M07ConnectionTesterTests
{
    [TestMethod]
    public Task ConnectionTestClassifiesUnauthorized()
        => AssertHttpStatusAsync(HttpStatusCode.Unauthorized, GitHubConnectionFailureKind.Unauthorized);

    [TestMethod]
    public Task ConnectionTestClassifiesForbidden()
        => AssertHttpStatusAsync(HttpStatusCode.Forbidden, GitHubConnectionFailureKind.Forbidden);

    [TestMethod]
    public Task ConnectionTestClassifiesNotFound()
        => AssertHttpStatusAsync(HttpStatusCode.NotFound, GitHubConnectionFailureKind.NotFound);

    private static async Task AssertHttpStatusAsync(HttpStatusCode statusCode, GitHubConnectionFailureKind expected)
    {
        var tester = new GitHubHandoffConnectionTester(new ThrowingTransport(
            new GitHubTransportException("synthetic secret-bearing detail", statusCode)));

        var result = await tester.TestAsync();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(expected, result.FailureKind);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public async Task ConnectionTestClassifiesProtectedCredentialFailure()
    {
        var tester = new GitHubHandoffConnectionTester(new ThrowingTransport(
            new GitHubTransportException("The protected GitHub handoff credential is unavailable.")));

        var result = await tester.TestAsync();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(GitHubConnectionFailureKind.CredentialMissing, result.FailureKind);
    }

    private sealed class ThrowingTransport(Exception failure) : IGitHubHandoffTransport
    {
        public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default) => Task.FromException<GitHubReleaseContainer>(failure);
        public Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, Stream content, long contentLength, string localSha256, CancellationToken cancellationToken = default) => Task.FromException<GitHubAssetReceipt>(failure);
        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<GitHubRemoteAsset>>(failure);
        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromException<GitHubRemoteAsset>(failure);
        public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromException<Stream>(failure);
        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromException(failure);
    }
}
