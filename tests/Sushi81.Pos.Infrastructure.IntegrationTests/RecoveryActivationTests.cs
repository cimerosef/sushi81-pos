using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.GitHubTransport;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class RecoveryActivationTests
{
    [TestMethod]
    public async Task ConcurrentContendersHaveAtMostOneAcceptedWinner()
    {
        var transport = new AtomicActivationTransport(synchronizeInitialListing: true);
        var service = new RecoveryActivationService(transport);
        var first = Request(Guid.Parse("10000000-0000-0000-0000-000000000001"));
        var second = first with { DeviceId = Guid.Parse("20000000-0000-0000-0000-000000000002") };

        var results = await Task.WhenAll(service.TryAcquireAsync(first), service.TryAcquireAsync(second));

        Assert.HasCount(1, results.Where(result => result.Accepted));
        Assert.HasCount(1, results.Where(result => result.Outcome == RecoveryActivationOutcome.LostToExistingWinner));
        Assert.AreEqual(1, transport.UploadCount);
        Assert.HasCount(1, transport.Assets);
    }

    [TestMethod]
    public async Task SameWinnerRetryIsIdempotentAndDifferentDeviceCannotAcquire()
    {
        var transport = new AtomicActivationTransport();
        var service = new RecoveryActivationService(transport);
        var winner = Request(Guid.Parse("10000000-0000-0000-0000-000000000001"));

        var created = await service.TryAcquireAsync(winner);
        var resumed = await service.TryAcquireAsync(winner);
        var loser = await service.TryAcquireAsync(winner with { DeviceId = Guid.Parse("20000000-0000-0000-0000-000000000002") });

        Assert.AreEqual(RecoveryActivationOutcome.Won, created.Outcome);
        Assert.AreEqual(RecoveryActivationOutcome.ResumedSameWinner, resumed.Outcome);
        Assert.AreEqual(RecoveryActivationOutcome.LostToExistingWinner, loser.Outcome);
        Assert.AreEqual(1, transport.UploadCount);
    }

    [TestMethod]
    public async Task StarterOccupancyGrantsNobodyAuthorityUntilExactObservedAssetIsRemoved()
    {
        var transport = new AtomicActivationTransport();
        var request = Request(Guid.Parse("10000000-0000-0000-0000-000000000001"));
        transport.AddStarter(request.AssetName, 77);
        var service = new RecoveryActivationService(transport);

        var blocked = await service.TryAcquireAsync(request);
        Assert.AreEqual(RecoveryActivationOutcome.BlockedNoWinner, blocked.Outcome);
        Assert.AreEqual(0, transport.UploadCount);

        await transport.DeleteAssetAsync(77);
        var created = await service.TryAcquireAsync(request);

        Assert.AreEqual(RecoveryActivationOutcome.Won, created.Outcome);
        Assert.AreEqual(1, transport.UploadCount);
        Assert.HasCount(1, transport.Assets);
    }

    [TestMethod]
    public async Task DuplicateCreateAndUnknownOutcomeReobserveBeforeReturningWinner()
    {
        var transport = new AtomicActivationTransport { ThrowDuplicateAfterServerCreate = true };
        var service = new RecoveryActivationService(transport);
        var request = Request(Guid.Parse("10000000-0000-0000-0000-000000000001"));

        var result = await service.TryAcquireAsync(request);

        Assert.AreEqual(RecoveryActivationOutcome.ResumedSameWinner, result.Outcome);
        Assert.AreEqual(1, transport.UploadCount);
        Assert.HasCount(1, transport.Assets);
    }

    private static RecoveryActivationRequest Request(Guid deviceId) => new(
        Guid.Parse("30000000-0000-0000-0000-000000000003"),
        Guid.Parse("40000000-0000-0000-0000-000000000004"),
        3,
        4,
        deviceId,
        "checkpoint-17",
        new string('A', 64),
        17);

    private sealed class AtomicActivationTransport : IGitHubHandoffTransport
    {
        private readonly object sync = new();
        private readonly TaskCompletionSource<bool>? initialListingRelease;
        private GitHubRemoteAsset? asset;
        private byte[]? bytes;
        private int listCalls;

        public AtomicActivationTransport(bool synchronizeInitialListing = false)
        {
            initialListingRelease = synchronizeInitialListing
                ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
                : null;
        }

        public bool ThrowDuplicateAfterServerCreate { get; init; }
        public int UploadCount { get; private set; }
        public IReadOnlyList<GitHubRemoteAsset> Assets => asset is { } current ? [current] : [];

        public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubReleaseContainer(91, "proof-release", "https://uploads.example/releases/91/assets{?name}", false, false));

        public async Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, Stream content, long contentLength, string localSha256, CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            var candidateBytes = memory.ToArray();
            lock (sync)
            {
                if (asset is not null)
                    throw new GitHubTransportException("duplicate activation name", HttpStatusCode.UnprocessableEntity);
                var id = 1001L;
                var digest = Convert.ToHexString(SHA256.HashData(candidateBytes));
                asset = new GitHubRemoteAsset(id, name, candidateBytes.LongLength, "uploaded", "sha256:" + digest.ToLowerInvariant(), DateTimeOffset.UtcNow);
                bytes = candidateBytes;
                UploadCount++;
                var receipt = new GitHubAssetReceipt(release.Id, id, name, candidateBytes.LongLength, asset.Digest, asset.CreatedAtUtc, "uploaded");
                if (ThrowDuplicateAfterServerCreate)
                    throw new GitHubTransportException("synthetic timeout after server create");
                return receipt;
            }
        }

        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref listCalls);
            return ListAfterInitialSynchronizationAsync(call, cancellationToken);
        }

        private async Task<IReadOnlyList<GitHubRemoteAsset>> ListAfterInitialSynchronizationAsync(int call, CancellationToken cancellationToken)
        {
            if (initialListingRelease is not null && call <= 2)
            {
                if (call == 2) initialListingRelease.TrySetResult(true);
                await initialListingRelease.Task.WaitAsync(cancellationToken);
            }
            lock (sync)
            {
                return asset is { } current ? [current] : [];
            }
        }

        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                if (asset is not { } current || current.Id != assetId)
                    throw new FileNotFoundException("synthetic asset not found");
                return Task.FromResult(current);
            }
        }

        public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                if (asset is not { } current || current.Id != assetId || bytes is null)
                    throw new FileNotFoundException("synthetic asset not found");
                return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
            }
        }

        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                if (asset?.Id == assetId)
                {
                    asset = null;
                    bytes = null;
                }
                return Task.CompletedTask;
            }
        }

        public void AddStarter(string name, long id)
        {
            lock (sync)
            {
                asset = new GitHubRemoteAsset(id, name, 0, "starter", string.Empty, DateTimeOffset.UtcNow);
                bytes = null;
            }
        }
    }
}
