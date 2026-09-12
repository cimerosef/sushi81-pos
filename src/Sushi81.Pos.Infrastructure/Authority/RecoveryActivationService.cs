using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Infrastructure.GitHubTransport;

namespace Sushi81.Pos.Infrastructure.Authority;

/// <summary>
/// Proof-only server activation primitive. It returns an immutable receipt/result and never
/// enables local writes; the later DR state machine must still perform authority-last commit.
/// </summary>
public sealed class RecoveryActivationService(IGitHubHandoffTransport transport)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<RecoveryActivationResult> TryAcquireAsync(
        RecoveryActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        GitHubReleaseContainer release;
        try
        {
            release = await transport.EnsureContainerAsync(createIfMissing: true, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return Blocked($"Activation container could not be validated: {exception.Message}");
        }

        var observed = await ObserveAsync(release, request, cancellationToken);
        if (observed is not null) return observed;

        var artifact = new RecoveryActivationArtifact(
            "M07", request.RecoveryId, request.LineageId, request.PriorGeneration, request.NextGeneration,
            request.DeviceId, request.CandidateId, request.CandidateSha256, request.BusinessRevision, DateTimeOffset.UtcNow);
        artifact.Validate();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(artifact, JsonOptions);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));

        try
        {
            await using var content = new MemoryStream(bytes, writable: false);
            var receipt = await transport.UploadAssetAsync(
                release, request.AssetName, content, bytes.LongLength, sha256, cancellationToken);
            receipt.EnsureStrictlyValidFor(release.Id, request.AssetName, bytes.LongLength, sha256);
            var revalidated = await ObserveAsync(release, request, cancellationToken);
            return revalidated?.Outcome is RecoveryActivationOutcome.ResumedSameWinner
                ? revalidated with { Outcome = RecoveryActivationOutcome.Won, Diagnostic = "This contender created and revalidated the immutable activation." }
                : Blocked("The activation upload receipt could not be revalidated from the remote asset.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (IsUnknownCreateOutcome(exception) || exception is InvalidDataException)
        {
            // Duplicate, timeout and 5xx outcomes are never interpreted as a win from
            // the local response. Re-observe the exact deterministic name first.
            var afterRetry = await ObserveAsync(release, request, cancellationToken);
            return afterRetry ?? Blocked($"Activation create outcome is unresolved: {exception.Message}");
        }
    }

    private async Task<RecoveryActivationResult?> ObserveAsync(
        GitHubReleaseContainer release,
        RecoveryActivationRequest request,
        CancellationToken cancellationToken)
    {
        var matches = (await transport.ListAssetsAsync(release, cancellationToken))
            .Where(asset => string.Equals(asset.Name, request.AssetName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1 || !matches[0].IsComplete)
            return Blocked("The deterministic activation name is occupied by contradictory or incomplete remote evidence.");

        var remote = await transport.GetAssetAsync(matches[0].Id, cancellationToken);
        if (!remote.IsComplete || remote.Id != matches[0].Id || remote.Name != request.AssetName)
            return Blocked("The remote activation identity is contradictory.");

        await using var stream = await transport.DownloadAssetAsync(remote.Id, cancellationToken);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        if (bytes.LongLength != remote.Size
            || !GitHubSha256.TryNormalizeServerDigest(remote.Digest, out var serverDigest)
            || !string.Equals(serverDigest[7..], Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
            return Blocked("The remote activation bytes do not match the strict server receipt.");

        RecoveryActivationArtifact artifact;
        try
        {
            artifact = JsonSerializer.Deserialize<RecoveryActivationArtifact>(bytes, JsonOptions)
                ?? throw new InvalidDataException("The activation artifact is empty.");
            artifact.Validate();
        }
        catch (JsonException exception)
        {
            return Blocked($"The remote activation artifact is malformed: {exception.Message}");
        }

        if (artifact.RecoveryId != request.RecoveryId || artifact.LineageId != request.LineageId
            || artifact.PriorGeneration != request.PriorGeneration || artifact.NextGeneration != request.NextGeneration
            || artifact.CandidateId != request.CandidateId
            || !string.Equals(artifact.CandidateSha256, request.CandidateSha256, StringComparison.OrdinalIgnoreCase)
            || artifact.BusinessRevision != request.BusinessRevision)
            return Blocked("The remote activation artifact does not match the requested recovery identity.");

        var receipt = ToEvidence(release.Id, remote);
        return artifact.WinnerDeviceId == request.DeviceId
            ? new RecoveryActivationResult(RecoveryActivationOutcome.ResumedSameWinner, receipt, "The same recovery identity is already activated.")
            : new RecoveryActivationResult(RecoveryActivationOutcome.LostToExistingWinner, receipt, "Another device already owns this generation activation.");
    }

    private static RemoteAssetEvidence ToEvidence(long releaseId, GitHubRemoteAsset remote)
    {
        if (!remote.IsComplete || !GitHubSha256.TryNormalizeServerDigest(remote.Digest, out var digest))
            throw new InvalidDataException("The remote activation receipt is not strict.");
        return new RemoteAssetEvidence(releaseId, remote.Id, remote.Name, remote.Size, digest[7..], remote.State);
    }

    private static bool IsUnknownCreateOutcome(Exception exception) =>
        exception is TimeoutException
        || exception is HttpRequestException
        || exception is GitHubTransportException transportException
            && transportException.StatusCode is null or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity
                or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.RequestTimeout or HttpStatusCode.ServiceUnavailable;

    private static RecoveryActivationResult Blocked(string diagnostic) =>
        new(RecoveryActivationOutcome.BlockedNoWinner, null, diagnostic);
}
