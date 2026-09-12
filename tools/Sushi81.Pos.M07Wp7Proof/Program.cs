using System.Net;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.GitHubTransport;

const string TokenEnvironmentVariable = "SUSHI81_GITHUB_HANDOFF_TOKEN";
const string AuthorizedOwner = "cimerosef";
const string AuthorizedRepository = "sushi81-pos-handoff-m07-proof";
const string ReleaseTag = "m07-wp7-proof-v1";
const string ReleaseName = "M07 WP7 real GitHub proof";

var token = Environment.GetEnvironmentVariable(TokenEnvironmentVariable);
if (string.IsNullOrWhiteSpace(token))
{
    Console.WriteLine("{\"status\":\"prerequisite-blocked\",\"reason\":\"credential-missing\"}");
    return 20;
}

var options = new GitHubHandoffRepositoryOptions(
    AuthorizedOwner,
    AuthorizedRepository,
    ReleaseTag,
    ReleaseName,
    Timeout: TimeSpan.FromSeconds(60));

using var firstTransport = new GitHubReleaseAssetTransport(options, new EnvironmentCredentialProvider(TokenEnvironmentVariable));
GitHubReleaseContainer release;
try
{
    release = await firstTransport.EnsureContainerAsync(createIfMissing: true);
}
catch (GitHubTransportException exception)
{
    Console.WriteLine($"{{\"status\":\"prerequisite-blocked\",\"reason\":\"github-transport\",\"statusCode\":{(int?)exception.StatusCode ?? 0}}}");
    return 21;
}

var race = await RunRaceAsync(options, release);
var unknown = await RunUnknownOutcomeAsync(options, release);

var output = new
{
    status = race.AcceptedCount == 1 && race.RemoteAssetCount == 1 && race.WinnerRetry == "ResumedSameWinner"
        && race.LoserRetry == "LostToExistingWinner"
        && race.LoserWasNotAccepted
        && race.RemoteEvidence is { DigestMatches: true, ArtifactValid: true }
        && unknown.FirstOutcome == "ResumedSameWinner"
        && unknown.ExactRetry == "ResumedSameWinner"
        && unknown.LoserOutcome == "LostToExistingWinner"
        && unknown.RemoteAssetCount == 1
        && unknown.RemoteEvidence is { DigestMatches: true, ArtifactValid: true }
        ? "passed"
        : "failed",
    repository = AuthorizedRepository,
    release = new { id = release.Id, tag = release.TagName },
    race,
    unknown,
    proofAssetsRetained = true
};

Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
return output.status == "passed" ? 0 : 22;

static async Task<RaceEvidence> RunRaceAsync(
    GitHubHandoffRepositoryOptions options,
    GitHubReleaseContainer release)
{
    var recoveryId = Guid.NewGuid();
    var lineageId = Guid.NewGuid();
    var candidateId = "wp7-race-" + Guid.NewGuid().ToString("N");
    var candidateSha256 = new string('A', 64);
    var firstRequest = new RecoveryActivationRequest(
        recoveryId, lineageId, 7, 8,
        Guid.NewGuid(), candidateId, candidateSha256, 7001);
    var secondRequest = firstRequest with { DeviceId = Guid.NewGuid() };
    var gate = new TwoPartyUploadGate();

    using var firstBase = new GitHubReleaseAssetTransport(options, new EnvironmentCredentialProvider(TokenEnvironmentVariable));
    using var secondBase = new GitHubReleaseAssetTransport(options, new EnvironmentCredentialProvider(TokenEnvironmentVariable));
    var first = new RecoveryActivationService(new RaceTransport(firstBase, gate));
    var second = new RecoveryActivationService(new RaceTransport(secondBase, gate));
    var results = await Task.WhenAll(
        SafeAcquireAsync(first, firstRequest),
        SafeAcquireAsync(second, secondRequest));

    var evidence = await CaptureEvidenceAsync(firstBase, release, firstRequest.AssetName);
    var winningRequest = evidence?.WinnerDeviceId == firstRequest.DeviceId ? firstRequest : secondRequest;
    var losingRequest = evidence?.WinnerDeviceId == firstRequest.DeviceId ? secondRequest : firstRequest;
    var winnerRetry = await SafeAcquireAsync(new RecoveryActivationService(firstBase), winningRequest);
    var loserRetry = await SafeAcquireAsync(new RecoveryActivationService(firstBase), losingRequest);

    return new RaceEvidence(
        firstRequest.AssetName,
        evidence,
        results.Select(result => result.Outcome.ToString()).ToArray(),
        results.Count(result => result.Accepted),
        evidence is null ? 0 : 1,
        winnerRetry.Outcome.ToString(),
        loserRetry.Outcome.ToString(),
        results.All(result => !result.Accepted) || results.Count(result => result.Accepted) == 1);
}

static async Task<UnknownOutcomeEvidence> RunUnknownOutcomeAsync(
    GitHubHandoffRepositoryOptions options,
    GitHubReleaseContainer release)
{
    var recoveryId = Guid.NewGuid();
    var lineageId = Guid.NewGuid();
    var candidateId = "wp7-unknown-" + Guid.NewGuid().ToString("N");
    var request = new RecoveryActivationRequest(
        recoveryId, lineageId, 11, 12,
        Guid.NewGuid(), candidateId, new string('B', 64), 11001);

    using var baseTransport = new GitHubReleaseAssetTransport(options, new EnvironmentCredentialProvider(TokenEnvironmentVariable));
    var unknownService = new RecoveryActivationService(new UnknownCreateOutcomeTransport(baseTransport));
    var first = await SafeAcquireAsync(unknownService, request);
    var exactRetry = await SafeAcquireAsync(new RecoveryActivationService(baseTransport), request);
    var loser = await SafeAcquireAsync(new RecoveryActivationService(baseTransport), request with { DeviceId = Guid.NewGuid() });
    var evidence = await CaptureEvidenceAsync(baseTransport, release, request.AssetName);

    return new UnknownOutcomeEvidence(
        request.AssetName,
        evidence,
        first.Outcome.ToString(),
        exactRetry.Outcome.ToString(),
        loser.Outcome.ToString(),
        evidence is null ? 0 : 1);
}

static async Task<RecoveryActivationResult> SafeAcquireAsync(
    RecoveryActivationService service,
    RecoveryActivationRequest request)
{
    try
    {
        return await service.TryAcquireAsync(request);
    }
    catch (GitHubTransportException exception)
    {
        var status = exception.StatusCode is { } code
            ? ((int)code).ToString(CultureInfo.InvariantCulture)
            : "unknown";
        return new(RecoveryActivationOutcome.BlockedNoWinner, null, "transport-failure-" + status);
    }
    catch (InvalidDataException)
    {
        return new(RecoveryActivationOutcome.BlockedNoWinner, null, "invalid-remote-evidence");
    }
    catch (HttpRequestException)
    {
        return new(RecoveryActivationOutcome.BlockedNoWinner, null, "network-failure");
    }
}

static async Task<RemoteEvidence?> CaptureEvidenceAsync(
    IGitHubHandoffTransport transport,
    GitHubReleaseContainer release,
    string assetName)
{
    var matches = (await transport.ListAssetsAsync(release))
        .Where(asset => string.Equals(asset.Name, assetName, StringComparison.Ordinal))
        .ToArray();
    if (matches.Length != 1 || !matches[0].IsComplete)
    {
        return null;
    }

    var remote = await transport.GetAssetAsync(matches[0].Id);
    await using var stream = await transport.DownloadAssetAsync(remote.Id);
    using var memory = new MemoryStream();
    await stream.CopyToAsync(memory);
    var bytes = memory.ToArray();
    var localSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    var serverDigest = GitHubSha256.TryNormalizeServerDigest(remote.Digest, out var normalized)
        ? normalized[7..]
        : string.Empty;
    RecoveryActivationArtifact? artifact = null;
    var artifactValid = false;
    try
    {
        artifact = JsonSerializer.Deserialize<RecoveryActivationArtifact>(
            bytes,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        artifact?.Validate();
        artifactValid = artifact is not null;
    }
    catch (JsonException)
    {
        // The evidence record below marks malformed payloads as failed proof evidence.
    }
    catch (InvalidDataException)
    {
        // The evidence record below marks contradictory payloads as failed proof evidence.
    }

    return new RemoteEvidence(
        remote.Id,
        remote.Name,
        remote.State,
        remote.Size,
        serverDigest,
        localSha256,
        string.Equals(serverDigest, localSha256, StringComparison.OrdinalIgnoreCase),
        artifactValid,
        artifact?.RecoveryId,
        artifact?.LineageId,
        artifact?.PriorGeneration,
        artifact?.NextGeneration,
        artifact?.WinnerDeviceId,
        artifact?.CandidateId,
        artifact?.BusinessRevision);
}

sealed class EnvironmentCredentialProvider(string variableName) : IProtectedGitHubCredentialProvider
{
    public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Environment.GetEnvironmentVariable(variableName));
}

sealed class RaceTransport(IGitHubHandoffTransport inner, TwoPartyUploadGate gate) : IGitHubHandoffTransport
{
    public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default) =>
        inner.EnsureContainerAsync(createIfMissing, cancellationToken);

    public async Task<GitHubAssetReceipt> UploadAssetAsync(
        GitHubReleaseContainer release,
        string name,
        Stream content,
        long contentLength,
        string localSha256,
        CancellationToken cancellationToken = default)
    {
        await gate.ArriveAsync(cancellationToken);
        return await inner.UploadAssetAsync(release, name, content, contentLength, localSha256, cancellationToken);
    }

    public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default) =>
        inner.ListAssetsAsync(release, cancellationToken);

    public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default) =>
        inner.GetAssetAsync(assetId, cancellationToken);

    public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) =>
        inner.DownloadAssetAsync(assetId, cancellationToken);

    public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default) =>
        inner.DeleteAssetAsync(assetId, cancellationToken);
}

sealed class UnknownCreateOutcomeTransport(IGitHubHandoffTransport inner) : IGitHubHandoffTransport
{
    private int uploadCount;

    public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default) =>
        inner.EnsureContainerAsync(createIfMissing, cancellationToken);

    public async Task<GitHubAssetReceipt> UploadAssetAsync(
        GitHubReleaseContainer release,
        string name,
        Stream content,
        long contentLength,
        string localSha256,
        CancellationToken cancellationToken = default)
    {
        var receipt = await inner.UploadAssetAsync(release, name, content, contentLength, localSha256, cancellationToken);
        if (Interlocked.Increment(ref uploadCount) == 1)
        {
            throw new GitHubTransportException("Synthetic unknown outcome after server commit.");
        }

        return receipt;
    }

    public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default) =>
        inner.ListAssetsAsync(release, cancellationToken);

    public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default) =>
        inner.GetAssetAsync(assetId, cancellationToken);

    public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) =>
        inner.DownloadAssetAsync(assetId, cancellationToken);

    public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default) =>
        inner.DeleteAssetAsync(assetId, cancellationToken);
}

sealed class TwoPartyUploadGate
{
    private readonly TaskCompletionSource<bool> first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> second = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;

    public async Task ArriveAsync(CancellationToken cancellationToken)
    {
        var arrival = Interlocked.Increment(ref arrivals);
        if (arrival == 1)
        {
            first.TrySetResult(true);
            await second.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        else if (arrival == 2)
        {
            second.TrySetResult(true);
            await first.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("The proof race gate received more than two upload contenders.");
        }
    }
}

sealed record RemoteEvidence(
    long AssetId,
    string Name,
    string State,
    long Size,
    string ServerSha256,
    string LocalSha256,
    bool DigestMatches,
    bool ArtifactValid,
    Guid? RecoveryId,
    Guid? LineageId,
    long? PriorGeneration,
    long? NextGeneration,
    Guid? WinnerDeviceId,
    string? CandidateId,
    long? BusinessRevision);

sealed record RaceEvidence(
    string AssetName,
    RemoteEvidence? RemoteEvidence,
    string[] ContenderOutcomes,
    int AcceptedCount,
    int RemoteAssetCount,
    string WinnerRetry,
    string LoserRetry,
    bool LoserWasNotAccepted);

sealed record UnknownOutcomeEvidence(
    string AssetName,
    RemoteEvidence? RemoteEvidence,
    string FirstOutcome,
    string ExactRetry,
    string LoserOutcome,
    int RemoteAssetCount);
