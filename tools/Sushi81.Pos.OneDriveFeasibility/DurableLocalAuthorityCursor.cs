using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sushi81.Pos.OneDriveFeasibility;

/// <summary>
/// The device-local participation/current-authority anchor.  This file is
/// separate from historical source and target evidence so loss of either
/// evidence file cannot silently make a participating device look virgin.
/// </summary>
public enum DurableLocalAuthorityRole
{
    InitializationPending,
    AcquisitionPending,
    InitialAuthoritative,
    Authoritative,
    TransferPrepared,
    RelinquishedBlocked,
    Released,
    AcquiredTarget,
    ClosedWithRetainedAuthority
}

public sealed record DurableLocalAuthorityCursorState(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("lineageId")] string LineageId,
    [property: JsonPropertyName("generation")] long Generation,
    [property: JsonPropertyName("highWaterHandoffVersion")] long HighWaterHandoffVersion,
    [property: JsonPropertyName("currentRole")] DurableLocalAuthorityRole CurrentRole,
    [property: JsonPropertyName("currentTransferId")] string? CurrentTransferId,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc)
{
    public const int CurrentFormatVersion = 1;

    public bool IsValid => FormatVersion == CurrentFormatVersion
        && Revision >= 1
        && !string.IsNullOrWhiteSpace(DeviceId)
        && Guid.TryParse(LineageId, out _)
        && Generation >= 0
        && HighWaterHandoffVersion >= 0
        && UpdatedAtUtc > DateTimeOffset.UnixEpoch
        && UpdatedAtUtc.Offset == TimeSpan.Zero
        && RoleShapeIsValid();

    public bool IsVirginLineage => Guid.TryParse(LineageId, out var lineage) && lineage == Guid.Empty;

    public bool Matches(DirectedTransferIdentity transfer) =>
        IsValid
        && transfer.IsValid
        && LineageId == transfer.LineageId
        && Generation == transfer.Generation
        && CurrentTransferId is { } id
        && id == transfer.TransferId;

    private bool RoleShapeIsValid()
    {
        var transferIdValid = CurrentTransferId is not null && Guid.TryParse(CurrentTransferId, out _);
        return CurrentRole switch
        {
            DurableLocalAuthorityRole.InitializationPending => IsVirginLineage && HighWaterHandoffVersion == 0 && CurrentTransferId is null,
            DurableLocalAuthorityRole.AcquisitionPending => transferIdValid,
            DurableLocalAuthorityRole.InitialAuthoritative => IsVirginLineage && HighWaterHandoffVersion == 0 && CurrentTransferId is null,
            DurableLocalAuthorityRole.Authoritative => CurrentTransferId is null,
            DurableLocalAuthorityRole.ClosedWithRetainedAuthority => CurrentTransferId is null,
            DurableLocalAuthorityRole.TransferPrepared => transferIdValid,
            DurableLocalAuthorityRole.RelinquishedBlocked => transferIdValid && HighWaterHandoffVersion >= 1,
            DurableLocalAuthorityRole.Released => transferIdValid && HighWaterHandoffVersion >= 1,
            DurableLocalAuthorityRole.AcquiredTarget => transferIdValid && HighWaterHandoffVersion >= 1,
            _ => false
        };
    }
}

public interface IDurableLocalAuthorityCursorFailureInjector
{
    void BeforeCommit(DurableLocalAuthorityCursorState nextState);
}

public sealed class DurableLocalAuthorityCursorStore(
    string statePath,
    IDurableLocalAuthorityCursorFailureInjector? failureInjector = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public string StatePath { get; } = Path.GetFullPath(statePath);

    public bool Exists => File.Exists(StatePath);

    public DurableLocalAuthorityCursorState Load()
    {
        if (!File.Exists(StatePath))
        {
            throw new InvalidDataException("Local authority cursor is missing; local authority is unresolved.");
        }

        try
        {
            using var stream = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            var state = JsonSerializer.Deserialize<DurableLocalAuthorityCursorState>(stream, JsonOptions);
            if (state is null || !state.IsValid)
            {
                throw new InvalidDataException("Local authority cursor is invalid; local authority is blocked.");
            }

            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Local authority cursor is malformed; local authority is blocked.", exception);
        }
    }

    public void Save(DurableLocalAuthorityCursorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.IsValid)
        {
            throw new InvalidDataException("Refusing to persist invalid local authority cursor.");
        }

        failureInjector?.BeforeCommit(state);
        var directory = Path.GetDirectoryName(StatePath) ?? throw new InvalidDataException("Local cursor path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = StatePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, state, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(StatePath))
            {
                File.Replace(temporaryPath, StatePath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, StatePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
