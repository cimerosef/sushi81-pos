using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sushi81.Pos.OneDriveFeasibility;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> Main(string[] args)
    {
        var json = args.Contains("--json", StringComparer.OrdinalIgnoreCase);
        var arguments = args.Where(argument => !string.Equals(argument, "--json", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (arguments.Length == 0 || arguments[0] is "help" or "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return arguments[0].ToLowerInvariant() switch
            {
                "roots" => Print(await EnumerateRootsAsync(), json),
                "validate-root" => Print(ValidateRoot(arguments), json),
                "observe" => Print(Observe(arguments), json),
                "publish" => Print(await PublishAsync(arguments), json),
                "inspect" => Print(await InspectAsync(arguments), json),
                "directed-source-run" => Print(await DirectedSourceRunAsync(arguments), json),
                "directed-target-acquire" => Print(await DirectedTargetAcquireAsync(arguments), json),
                "directed-source-state" => Print(DirectedSourceState(arguments), json),
                "claim" => Print(await CreateClaimAsync(arguments), json),
                "observe-claims" => Print(await ObserveClaimsAsync(arguments), json),
                _ => Print(new CommandResult(false, "unknown-command", "Unknown command. Use 'help' for commands."), json)
            };
        }
        catch (ArgumentException exception)
        {
            return Print(new CommandResult(false, "invalid-arguments", exception.Message), json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Print(new CommandResult(false, "blocked", exception.Message), json);
        }
    }

    private static Task<CommandResult> EnumerateRootsAsync()
    {
        var roots = new WindowsSyncRootCatalog().Enumerate();
        return Task.FromResult(new CommandResult(true, "ok", $"Found {roots.Count} registered sync root(s).", roots));
    }

    private static CommandResult ValidateRoot(string[] args)
    {
        var path = Required(args, 1, "path");
        var result = new WindowsSyncRootCatalog().Validate(path);
        return new CommandResult(result.IsAccepted, result.IsAccepted ? "accepted" : "rejected", result.Reason, result);
    }

    private static CommandResult Observe(string[] args)
    {
        var path = Required(args, 1, "file path");
        var observation = new CloudFileStateReader().Observe(path);
        return new CommandResult(observation.IsConfirmedInSync, observation.State.ToString(), Describe(observation), observation);
    }

    private static async Task<CommandResult> PublishAsync(string[] args)
    {
        var root = Required(args, 1, "registered OneDrive root");
        var validation = new WindowsSyncRootCatalog().Validate(root);
        if (!validation.IsAccepted)
        {
            return new CommandResult(false, "rejected-root", validation.Reason, validation);
        }

        var sourceDevice = Value(args, "--device") ?? Environment.MachineName;
        var lineage = Value(args, "--lineage") ?? Guid.NewGuid().ToString("D");
        var generation = ParseLong(args, "--generation", 1);
        var version = ParseLong(args, "--version", 1);
        var timeoutSeconds = ParseLong(args, "--timeout-seconds", 30);
        var request = new SyntheticPublicationRequest(
            Path.Combine(Path.GetFullPath(root), "Sushi81-M02-Synthetic", "Handoff"),
            sourceDevice,
            lineage,
            generation,
            version,
            TimeSpan.FromSeconds(timeoutSeconds),
            TimeSpan.FromMilliseconds(500));
        var result = await SyntheticHandoffPublisher.PublishAsync(request, new CloudFileStateReader());
        return new CommandResult(result.Succeeded, result.Code, result.Message, result);
    }

    private static async Task<CommandResult> InspectAsync(string[] args)
    {
        var directory = Required(args, 1, "handoff directory");
        var snapshot = Value(args, "--snapshot") ?? Directory.EnumerateFiles(directory, "*.snapshot.db", SearchOption.TopDirectoryOnly).SingleOrDefault()
            ?? throw new ArgumentException("Supply --snapshot when the handoff directory does not contain exactly one snapshot.");
        var marker = Value(args, "--marker") ?? Directory.EnumerateFiles(directory, "*.ready.json", SearchOption.TopDirectoryOnly).SingleOrDefault()
            ?? throw new ArgumentException("Supply --marker when the handoff directory does not contain exactly one ready marker.");
        var result = await HandoffUnitValidator.ValidateAsync(snapshot, marker);
        return new CommandResult(result.IsValid, result.Code, result.Message, result);
    }

    private static async Task<CommandResult> DirectedSourceRunAsync(string[] args)
    {
        var root = Required(args, 1, "registered OneDrive root");
        var rootValidation = new WindowsSyncRootCatalog().Validate(root);
        if (!rootValidation.IsAccepted)
        {
            return new CommandResult(false, "rejected-root", rootValidation.Reason, rootValidation);
        }

        var stateDirectory = SyntheticStateDirectory(args);
        var source = RequiredOption(args, "--device", "source device ID");
        var target = RequiredOption(args, "--target", "target device ID");
        var lineage = RequiredOption(args, "--lineage", "lineage ID");
        var generation = ParseLong(args, "--generation", 1);
        var version = ParseLong(args, "--version", 1);
        var transferId = Value(args, "--transfer-id") ?? Guid.NewGuid().ToString("D");
        var timeoutSeconds = ParseLong(args, "--timeout-seconds", 60);
        var pollMs = ParseLong(args, "--poll-ms", 500);
        var transfer = new DirectedTransferIdentity(transferId, lineage, generation, version, source, target);
        if (!transfer.IsValid) throw new ArgumentException("The directed source transfer identity is invalid.");

        var handoffDirectory = Path.Combine(Path.GetFullPath(root), "Sushi81-M02-Synthetic", "DirectedHandoff");
        Directory.CreateDirectory(handoffDirectory);
        var snapshotPath = Path.Combine(handoffDirectory, "directed-" + transfer.TransferId + ".snapshot.db");
        var statePath = Path.Combine(stateDirectory, "source-authority.json");
        var observer = new CloudFileArtifactSyncObserver(new CloudFileStateReader());
        var coordinator = new DirectedHandoffCoordinator(new DurableAuthorityStateStore(statePath), syncObserver: observer);

        var initialized = coordinator.InitializeAuthoritative(source, [source, target]);
        if (!initialized.Succeeded && initialized.Code != "already-initialized") return new CommandResult(false, initialized.Code, initialized.Message, initialized);
        var prepared = coordinator.PrepareTransfer(transfer);
        if (!prepared.Succeeded && prepared.Code != "already-prepared") return new CommandResult(false, prepared.Code, prepared.Message, prepared);
        if (!File.Exists(snapshotPath)) await DirectedSnapshotEvidence.CreateSyntheticAsync(snapshotPath);

        var snapshotSync = await WaitForInSyncAsync(observer, snapshotPath, TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromMilliseconds(pollMs));
        if (!snapshotSync.IsConfirmedInSync)
        {
            return new CommandResult(false, "snapshot-not-synchronized", "Synthetic snapshot did not reach observer-confirmed IN_SYNC before timeout.", new { transfer, snapshotPath, snapshotSync });
        }

        var evidence = await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshotPath, syncConfirmed: true);
        var relinquished = await coordinator.DurablyRelinquishAsync(evidence);
        if (!relinquished.Succeeded && relinquished.Code != "already-relinquished") return new CommandResult(false, relinquished.Code, relinquished.Message, relinquished);
        if (coordinator.MayBusinessWrite(source)) return new CommandResult(false, "source-write-gate-failed", "Source remained writable after durable relinquishment.", coordinator.Current);

        var published = await coordinator.PublishReleaseMarkersAsync(handoffDirectory, snapshotPath);
        if (!published.Succeeded && published.Code != "already-released")
        {
            var readyPending = Path.Combine(handoffDirectory, "directed-" + transfer.TransferId + ".ready.json");
            var grantPending = Path.Combine(handoffDirectory, "directed-" + transfer.TransferId + ".grant.json");
            var readyObservation = await WaitForInSyncAsync(observer, readyPending, TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromMilliseconds(pollMs));
            var grantObservation = await WaitForInSyncAsync(observer, grantPending, TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromMilliseconds(pollMs));
            if (!readyObservation.IsConfirmedInSync || !grantObservation.IsConfirmedInSync)
                return new CommandResult(false, published.Code, published.Message, new { transfer, snapshotPath, readySync = readyObservation, grantSync = grantObservation, state = coordinator.Current });
            published = await coordinator.PublishReleaseMarkersAsync(handoffDirectory, snapshotPath);
            if (!published.Succeeded && published.Code != "already-released") return new CommandResult(false, published.Code, published.Message, published);
        }

        var readyPath = Path.Combine(handoffDirectory, "directed-" + transfer.TransferId + ".ready.json");
        var grantPath = Path.Combine(handoffDirectory, "directed-" + transfer.TransferId + ".grant.json");
        var state = coordinator.Current;
        return new CommandResult(
            state.Mode == DirectedAuthorityMode.Released,
            state.Mode == DirectedAuthorityMode.Released ? "directed-source-released" : "source-not-released",
            state.Mode == DirectedAuthorityMode.Released ? "Device A durably relinquished authority and recorded Released after both target-bound markers were observer-confirmed IN_SYNC." : "Device A did not reach durable Released.",
            new
            {
                transfer, sourceDeviceId = source, targetDeviceId = target, lineageId = lineage, generation, handoffVersion = version,
                snapshotPath, snapshotChecksum = state.SnapshotEvidence?.SnapshotChecksum, snapshotByteLength = state.SnapshotEvidence?.SnapshotByteLength,
                readyMarkerPath = readyPath, grantMarkerPath = grantPath, snapshotSync, readySync = observer.Observe(readyPath), grantSync = observer.Observe(grantPath),
                sourceMode = state.Mode, sourceMayBusinessWrite = coordinator.MayBusinessWrite(source), statePath, handoffDirectory
            });
    }

    private static async Task<CommandResult> DirectedTargetAcquireAsync(string[] args)
    {
        var root = Required(args, 1, "registered OneDrive root");
        var rootValidation = new WindowsSyncRootCatalog().Validate(root);
        if (!rootValidation.IsAccepted) return new CommandResult(false, "rejected-root", rootValidation.Reason, rootValidation);
        var stateDirectory = SyntheticStateDirectory(args);
        var localDevice = RequiredOption(args, "--device", "local device ID");
        var source = RequiredOption(args, "--source", "source device ID");
        var target = RequiredOption(args, "--target", "target device ID");
        var lineage = RequiredOption(args, "--lineage", "lineage ID");
        var transferId = RequiredOption(args, "--transfer-id", "transfer ID");
        var generation = ParseLong(args, "--generation", 1);
        var version = ParseLong(args, "--version", 1);
        var transfer = new DirectedTransferIdentity(transferId, lineage, generation, version, source, target);
        if (!transfer.IsValid) throw new ArgumentException("The directed target transfer identity is invalid.");

        var handoffDirectory = Path.Combine(Path.GetFullPath(root), "Sushi81-M02-Synthetic", "DirectedHandoff");
        var snapshotPath = Value(args, "--snapshot") ?? Path.Combine(handoffDirectory, "directed-" + transfer.TransferId + ".snapshot.db");
        var statePath = Path.Combine(stateDirectory, "target-" + localDevice + ".json");
        var coordinator = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(statePath), localDevice);
        var result = await coordinator.AcquireAsync(handoffDirectory, snapshotPath, transfer);
        var restarted = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(statePath), localDevice);
        var mayWrite = restarted.MayBusinessWrite(transfer);
        return new CommandResult(result.Succeeded && mayWrite, result.Code, result.Message, new
        {
            transfer, localDeviceId = localDevice, sourceDeviceId = source, targetDeviceId = target, handoffDirectory, snapshotPath, statePath,
            validation = result.Validation, durableTargetState = restarted.Current, restarted = true, mayBusinessWrite = mayWrite
        });
    }

    private static CommandResult DirectedSourceState(string[] args)
    {
        var path = Required(args, 1, "source authority state file");
        var device = RequiredOption(args, "--device", "source device ID");
        try
        {
            var state = new DurableAuthorityStateStore(path).Load();
            var mayWrite = state.Mode == DirectedAuthorityMode.Authoritative && !state.ClosedWithAuthority && state.DeviceId == device;
            return new CommandResult(true, "source-state", "Durable source state loaded.", new { state, deviceId = device, mayBusinessWrite = mayWrite });
        }
        catch (InvalidDataException exception) { return new CommandResult(false, "state-unresolved", exception.Message); }
    }

    private static string SyntheticStateDirectory(string[] args)
    {
        var value = RequiredOption(args, "--state-dir", "synthetic state directory");
        var full = Path.GetFullPath(value);
        if (full.Contains("live.db", StringComparison.OrdinalIgnoreCase) || full.Contains("appdata", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The M02 harness state directory must be synthetic and must not be live.db or an application-data path.");
        Directory.CreateDirectory(full);
        return full;
    }

    private static string RequiredOption(string[] args, string name, string label) => Value(args, name) ?? throw new ArgumentException($"A {label} is required ({name}).");

    private static async Task<ArtifactSyncObservation> WaitForInSyncAsync(CloudFileArtifactSyncObserver observer, string path, TimeSpan timeout, TimeSpan pollInterval, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var observation = observer.Observe(path);
            if (observation.IsConfirmedInSync || stopwatch.Elapsed >= timeout) return observation;
            await Task.Delay(pollInterval, cancellationToken);
        }
    }

    private static async Task<CommandResult> CreateClaimAsync(string[] args)
    {
        var directory = Required(args, 1, "claims directory");
        var lineage = Value(args, "--lineage") ?? throw new ArgumentException("--lineage is required.");
        var device = Value(args, "--device") ?? Environment.MachineName;
        var claim = new AcquisitionClaim(
            HandoffMetadataCodec.CurrentFormatVersion,
            lineage,
            ParseLong(args, "--generation", 1),
            ParseLong(args, "--version", 1),
            device,
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow);
        var path = await AcquisitionClaimStore.CreateAsync(directory, claim);
        return new CommandResult(true, "claim-published", "Synthetic acquisition claim published; no authority was activated.", new { path, claim });
    }

    private static async Task<CommandResult> ObserveClaimsAsync(string[] args)
    {
        var directory = Required(args, 1, "claims directory");
        var lineage = Value(args, "--lineage") ?? throw new ArgumentException("--lineage is required.");
        var result = await AcquisitionClaimStore.EvaluateAsync(directory, lineage, ParseLong(args, "--generation", 1), ParseLong(args, "--version", 1));
        return new CommandResult(result.Observation == AcquisitionObservation.Uncontested, result.Observation.ToString(), result.Reason, result);
    }

    private static int Print(object value, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
            return value is CommandResult { Succeeded: false } ? 2 : 0;
        }

        if (value is CommandResult result)
        {
            Console.WriteLine($"[{(result.Succeeded ? "PASS" : "BLOCKED")}] {result.Code}: {result.Message}");
            if (result.Data is not null)
            {
                Console.WriteLine(JsonSerializer.Serialize(result.Data, JsonOptions));
            }
            return result.Succeeded ? 0 : 2;
        }

        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
        return 0;
    }

    private static string Describe(CloudFileObservation observation) => observation.Error is null
        ? $"Cloud Files placeholder interpretation: {observation.State} (raw=0x{observation.RawPlaceholderState:X})."
        : $"Cloud Files state is {observation.State}: {observation.Error}";

    private static string Required(string[] args, int index, string label) => args.Length > index && !args[index].StartsWith("--", StringComparison.Ordinal)
        ? args[index]
        : throw new ArgumentException($"A {label} is required.");

    private static string? Value(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal) ? args[index + 1] : null;
    }

    private static long ParseLong(string[] args, string name, long defaultValue)
    {
        var value = Value(args, name);
        return value is null ? defaultValue : long.TryParse(value, out var parsed) && parsed >= 0 ? parsed : throw new ArgumentException($"{name} must be a non-negative integer.");
    }

    private static void PrintUsage() => Console.WriteLine("""
        Sushi81 POS M02 OneDrive feasibility harness (synthetic only)

        roots [--json]
        validate-root <path> [--json]
        observe <file> [--json]
        publish <registered-OneDrive-root> [--device id] [--lineage guid] [--generation n] [--version n] [--timeout-seconds n]
        inspect <handoff-directory> [--snapshot path] [--marker path]
        claim <claims-directory> --lineage guid [--device id] [--generation n] [--version n]
        observe-claims <claims-directory> --lineage guid [--generation n] [--version n]
        directed-source-run <registered-OneDrive-root> --state-dir <synthetic-dir> --device <source> --target <target> --lineage <guid> --generation <n> --version <n> [--transfer-id <guid>] [--timeout-seconds n] [--poll-ms n] [--json]
        directed-target-acquire <registered-OneDrive-root> --state-dir <synthetic-dir> --device <local> --source <source> --target <target> --lineage <guid> --generation <n> --version <n> --transfer-id <guid> [--snapshot path] [--json]
        directed-source-state <source-state-file> --device <source> [--json]

        Directed commands are the M02 Device A/Device B proof flow. They require an explicitly registered sync root and synthetic state directory, and never open or alter live.db or activate POS authority. The older publish/inspect commands remain historical compatibility commands only.
        """);

    private sealed record CommandResult(bool Succeeded, string Code, string Message, object? Data = null);
}
