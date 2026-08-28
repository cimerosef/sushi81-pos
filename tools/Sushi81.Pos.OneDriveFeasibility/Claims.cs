using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sushi81.Pos.OneDriveFeasibility;

public sealed record AcquisitionClaim(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("lineageId")] string LineageId,
    [property: JsonPropertyName("generation")] long Generation,
    [property: JsonPropertyName("handoffVersion")] long HandoffVersion,
    [property: JsonPropertyName("claimantDeviceId")] string ClaimantDeviceId,
    [property: JsonPropertyName("claimId")] string ClaimId,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc);

public enum AcquisitionObservation
{
    Unknown,
    Uncontested,
    Contention,
    Stale,
    Invalid
}

public sealed record AcquisitionEvaluation(AcquisitionObservation Observation, string Reason, IReadOnlyList<AcquisitionClaim> Claims);

public static class AcquisitionClaimStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };

    public static async Task<string> CreateAsync(string claimsDirectory, AcquisitionClaim claim, CancellationToken cancellationToken = default)
    {
        Validate(claim);
        Directory.CreateDirectory(claimsDirectory);
        var path = Path.Combine(claimsDirectory, $"claim-{claim.ClaimId}.json");
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, claim, Options, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        return path;
    }

    public static async Task<AcquisitionEvaluation> EvaluateAsync(
        string claimsDirectory,
        string lineageId,
        long generation,
        long handoffVersion,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(claimsDirectory))
        {
            return new(AcquisitionObservation.Unknown, "Claims transport is unavailable.", []);
        }

        var claims = new List<AcquisitionClaim>();
        foreach (var path in Directory.EnumerateFiles(claimsDirectory, "claim-*.json", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
                var claim = await JsonSerializer.DeserializeAsync<AcquisitionClaim>(stream, Options, cancellationToken);
                if (claim is null)
                {
                    throw new InvalidDataException("A claim is empty.");
                }
                Validate(claim);
                if (!string.Equals(claim.LineageId, lineageId, StringComparison.Ordinal)
                    || claim.Generation != generation
                    || claim.HandoffVersion != handoffVersion)
                {
                    return new(AcquisitionObservation.Stale, "A claim targets a different lineage, generation or handoff version; acquisition is blocked.", claims);
                }

                claims.Add(claim);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException)
            {
                return new(AcquisitionObservation.Invalid, $"A claim is malformed or unreadable: {exception.Message}", claims);
            }
        }

        var distinctDevices = claims.Select(claim => claim.ClaimantDeviceId).Distinct(StringComparer.Ordinal).ToArray();
        return distinctDevices.Length switch
        {
            0 => new(AcquisitionObservation.Unknown, "No valid claim is visible; transport state is unresolved.", claims),
            1 => new(AcquisitionObservation.Uncontested, "Exactly one valid claimant is visible; this harness still does not activate write authority.", claims),
            _ => new(AcquisitionObservation.Contention, "Multiple distinct claimants are visible; unresolved contention remains read-only.", claims)
        };
    }

    public static void Validate(AcquisitionClaim? claim)
    {
        if (claim is null
            || claim.FormatVersion != HandoffMetadataCodec.CurrentFormatVersion
            || !Guid.TryParse(claim.LineageId, out _)
            || claim.Generation < 0
            || claim.HandoffVersion < 1
            || string.IsNullOrWhiteSpace(claim.ClaimantDeviceId)
            || string.IsNullOrWhiteSpace(claim.ClaimId)
            || !Guid.TryParse(claim.ClaimId, out _)
            || claim.CreatedAtUtc <= DateTimeOffset.UnixEpoch
            || claim.CreatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException("Acquisition claim metadata is invalid or incomplete.");
        }
    }
}
