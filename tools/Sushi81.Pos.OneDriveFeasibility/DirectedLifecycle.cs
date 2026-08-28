using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sushi81.Pos.OneDriveFeasibility;

/// <summary>
/// Append-only completion history for the synthetic continuous lifecycle. It
/// prevents a target-to-next-source promotion from deleting or resetting the
/// prior acquisition and provides the monotonic handoff-version boundary.
/// </summary>
public sealed record DirectedLifecycleLedgerEntry(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("lineageId")] string LineageId,
    [property: JsonPropertyName("generation")] long Generation,
    [property: JsonPropertyName("handoffVersion")] long HandoffVersion,
    [property: JsonPropertyName("transferId")] string TransferId,
    [property: JsonPropertyName("sourceDeviceId")] string SourceDeviceId,
    [property: JsonPropertyName("targetDeviceId")] string TargetDeviceId,
    [property: JsonPropertyName("snapshotPath")] string SnapshotPath,
    [property: JsonPropertyName("snapshotChecksum")] string SnapshotChecksum,
    [property: JsonPropertyName("snapshotByteLength")] long SnapshotByteLength,
    [property: JsonPropertyName("completedAtUtc")] DateTimeOffset CompletedAtUtc)
{
    public const int CurrentFormatVersion = 1;

    public bool IsValid => FormatVersion == CurrentFormatVersion
        && Revision >= 1
        && Guid.TryParse(LineageId, out _)
        && Generation >= 0
        && HandoffVersion >= 1
        && Guid.TryParse(TransferId, out _)
        && !string.IsNullOrWhiteSpace(SourceDeviceId)
        && !string.IsNullOrWhiteSpace(TargetDeviceId)
        && !string.Equals(SourceDeviceId, TargetDeviceId, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(SnapshotPath)
        && Path.IsPathFullyQualified(SnapshotPath)
        && SnapshotChecksum is { Length: 64 } checksum
        && checksum.All(Uri.IsHexDigit)
        && SnapshotByteLength > 0
        && CompletedAtUtc > DateTimeOffset.UnixEpoch
        && CompletedAtUtc.Offset == TimeSpan.Zero;

    public bool Matches(DirectedTransferIdentity transfer) =>
        IsValid
        && transfer.IsValid
        && LineageId == transfer.LineageId
        && Generation == transfer.Generation
        && HandoffVersion == transfer.HandoffVersion
        && TransferId == transfer.TransferId
        && SourceDeviceId == transfer.SourceDeviceId
        && TargetDeviceId == transfer.TargetDeviceId;
}

public sealed class DirectedLifecycleLedgerStore(string ledgerPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };

    public string LedgerPath { get; } = Path.GetFullPath(ledgerPath);

    public IReadOnlyList<DirectedLifecycleLedgerEntry> Load()
    {
        if (!File.Exists(LedgerPath)) return [];

        var entries = new List<DirectedLifecycleLedgerEntry>();
        try
        {
            foreach (var line in File.ReadLines(LedgerPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entry = JsonSerializer.Deserialize<DirectedLifecycleLedgerEntry>(line, JsonOptions);
                if (entry is null || !entry.IsValid) throw new InvalidDataException("Lifecycle ledger contains an invalid entry; progression is blocked.");
                entries.Add(entry);
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Lifecycle ledger is malformed; progression is blocked.", exception);
        }

        ValidateHistory(entries);
        return entries;
    }

    public void Append(DirectedLifecycleLedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsValid) throw new InvalidDataException("Refusing to append an invalid lifecycle entry.");

        var entries = Load().ToList();
        var sameTransfer = entries.SingleOrDefault(existing => existing.TransferId == entry.TransferId);
        if (sameTransfer is not null)
        {
            if (sameTransfer == entry) return;
            throw new InvalidDataException("A transfer ID is already recorded with different immutable content.");
        }

        var latest = entries
            .Where(existing => existing.LineageId == entry.LineageId && existing.Generation == entry.Generation)
            .OrderByDescending(existing => existing.HandoffVersion)
            .FirstOrDefault();
        if (latest is not null
            && (entry.HandoffVersion != latest.HandoffVersion + 1
                || !string.Equals(entry.SourceDeviceId, latest.TargetDeviceId, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Lifecycle handoff version or source progression is stale, replayed or not target-directed.");
        }

        var directory = Path.GetDirectoryName(LedgerPath) ?? throw new InvalidDataException("Lifecycle ledger path has no directory.");
        Directory.CreateDirectory(directory);
        using var stream = new FileStream(LedgerPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(JsonSerializer.Serialize(entry, JsonOptions));
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static void ValidateHistory(IReadOnlyList<DirectedLifecycleLedgerEntry> entries)
    {
        var duplicateRevisions = entries.GroupBy(entry => entry.Revision).Any(group => group.Count() > 1);
        if (duplicateRevisions) throw new InvalidDataException("Lifecycle ledger revisions are not unique.");

        var duplicateTransfers = entries.GroupBy(entry => entry.TransferId).Any(group => group.Count() > 1);
        if (duplicateTransfers) throw new InvalidDataException("Lifecycle ledger transfer IDs are not unique.");

        foreach (var group in entries.GroupBy(entry => (entry.LineageId, entry.Generation)))
        {
            var ordered = group.OrderBy(entry => entry.HandoffVersion).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (ordered[index].HandoffVersion != ordered[index - 1].HandoffVersion + 1
                    || ordered[index].SourceDeviceId != ordered[index - 1].TargetDeviceId)
                {
                    throw new InvalidDataException("Lifecycle ledger progression is not monotonic and target-directed.");
                }
            }
        }
    }
}

public sealed class DirectedContinuousLifecycleCoordinator(
    DirectedLifecycleLedgerStore ledger,
    string localDeviceId)
{
    public DirectedLifecycleLedgerStore Ledger { get; } = ledger ?? throw new ArgumentNullException(nameof(ledger));
    public string LocalDeviceId { get; } = string.IsNullOrWhiteSpace(localDeviceId)
        ? throw new ArgumentException("A local device ID is required.", nameof(localDeviceId))
        : localDeviceId;

    public DirectedTransferOperationResult ValidateNextTransfer(DirectedTransferIdentity transfer, IEnumerable<string> pairedDeviceIds)
    {
        var paired = pairedDeviceIds?.ToHashSet(StringComparer.Ordinal)
            ?? throw new ArgumentNullException(nameof(pairedDeviceIds));
        if (!transfer.IsValid || !paired.Contains(transfer.SourceDeviceId) || !paired.Contains(transfer.TargetDeviceId)
            || !paired.Contains(LocalDeviceId) || transfer.SourceDeviceId != LocalDeviceId)
        {
            return new(false, "unpaired-or-invalid-progression", "The next transfer is not a valid exact local-source member of the paired set.");
        }

        IReadOnlyList<DirectedLifecycleLedgerEntry> entries;
        try { entries = Ledger.Load(); }
        catch (InvalidDataException exception) { return new(false, "lifecycle-state-unresolved", exception.Message); }

        var latest = entries
            .Where(entry => entry.LineageId == transfer.LineageId && entry.Generation == transfer.Generation)
            .OrderByDescending(entry => entry.HandoffVersion)
            .FirstOrDefault();
        var expectedVersion = latest?.HandoffVersion + 1 ?? 1;
        if (transfer.HandoffVersion != expectedVersion)
        {
            return new(false, "stale-or-replayed-version", $"Expected monotonic handoff version {expectedVersion}, received {transfer.HandoffVersion}.");
        }

        if (latest is not null && latest.TargetDeviceId != LocalDeviceId)
        {
            return new(false, "wrong-next-source", "Only the previously acquired target may become the next source.");
        }

        return new(true, "next-transfer-valid", "The next transfer is monotonic, same-lineage/generation and target-directed.");
    }

    public DirectedTransferOperationResult RecordCompletedTransfer(
        DurableAuthorityStateStore sourceStateStore,
        DurableTargetAcquisitionState acquisition)
    {
        ArgumentNullException.ThrowIfNull(sourceStateStore);
        ArgumentNullException.ThrowIfNull(acquisition);
        if (!acquisition.IsValid)
        {
            return new(false, "invalid-target-state", "Only valid durable target acquisition may complete a lifecycle leg.");
        }

        DurableAuthorityState source;
        try { source = sourceStateStore.Load(); }
        catch (InvalidDataException exception) { return new(false, "source-state-unresolved", exception.Message); }
        if (source.Mode != DirectedAuthorityMode.Released || source.Transfer is not { IsValid: true } transfer
            || source.SnapshotEvidence is not { IsValid: true } evidence
            || acquisition.LineageId != transfer.LineageId
            || acquisition.Generation != transfer.Generation
            || acquisition.HandoffVersion != transfer.HandoffVersion
            || acquisition.TransferId != transfer.TransferId
            || acquisition.SourceDeviceId != transfer.SourceDeviceId
            || acquisition.TargetDeviceId != transfer.TargetDeviceId
            || acquisition.SnapshotChecksum != evidence.SnapshotChecksum
            || acquisition.SnapshotByteLength != evidence.SnapshotByteLength)
        {
            return new(false, "lifecycle-completion-mismatch", "Released source and durable target acquisition do not describe the same transfer.");
        }

        var entries = Ledger.Load();
        var revision = entries.Count == 0 ? 1 : entries.Max(entry => entry.Revision) + 1;
        var entry = new DirectedLifecycleLedgerEntry(
            DirectedLifecycleLedgerEntry.CurrentFormatVersion,
            revision,
            acquisition.LineageId,
            acquisition.Generation,
            acquisition.HandoffVersion,
            acquisition.TransferId,
            acquisition.SourceDeviceId,
            acquisition.TargetDeviceId,
            acquisition.SnapshotPath,
            acquisition.SnapshotChecksum,
            acquisition.SnapshotByteLength,
            DateTimeOffset.UtcNow);
        try
        {
            Ledger.Append(entry);
            return new(true, "lifecycle-leg-complete", "Released source and durable target acquisition were appended without deleting prior state.");
        }
        catch (InvalidDataException exception)
        {
            return new(false, "lifecycle-ledger-write-failed", exception.Message);
        }
    }

    public DirectedTransferOperationResult PromoteAcquiredTargetToSource(
        DurableTargetAcquisitionState acquisition,
        string authorityStatePath,
        IEnumerable<string> pairedDeviceIds)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        var paired = pairedDeviceIds?.ToArray() ?? throw new ArgumentNullException(nameof(pairedDeviceIds));
        if (!acquisition.IsValid || acquisition.DeviceId != LocalDeviceId || !paired.Contains(LocalDeviceId, StringComparer.Ordinal))
        {
            return new(false, "promotion-forbidden", "Only the exact durably acquired target may become the next source.");
        }

        try
        {
            var latest = Ledger.Load().LastOrDefault(entry => entry.TransferId == acquisition.TransferId);
            if (latest is null)
            {
                return new(false, "lifecycle-completion-required", "Target promotion requires a recorded completed lifecycle leg.");
            }

            var authority = new DurableAuthorityStateStore(authorityStatePath);
            if (File.Exists(authority.StatePath))
            {
                var existing = authority.Load();
                return existing.DeviceId == LocalDeviceId && existing.Mode == DirectedAuthorityMode.Authoritative
                    ? new(true, "already-promoted", "The acquired target is already the authoritative next source.", existing)
                    : new(false, "promotion-state-collision", "An existing authority state cannot be replaced.", existing);
            }

            var result = new DirectedHandoffCoordinator(authority).InitializeAuthoritative(LocalDeviceId, paired);
            return result.Succeeded
                ? result with { Code = "target-promoted-to-source", Message = "Durably acquired target promoted to the next source without deleting acquisition or ledger state." }
                : result;
        }
        catch (InvalidDataException exception)
        {
            return new(false, "lifecycle-state-unresolved", exception.Message);
        }
    }
}
