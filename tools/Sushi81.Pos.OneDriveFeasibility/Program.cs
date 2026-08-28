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

        Commands only inspect registered sync roots and synthetic files. They never open or alter live.db and never activate POS authority.
        """);

    private sealed record CommandResult(bool Succeeded, string Code, string Message, object? Data = null);
}
