using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Sushi81.Pos.PreProductionCutoverReset;

public sealed class WindowsCutoverHost : ICutoverHost
{
    public static readonly string AcceptedApplicationSourceHead = "9f4521627b21f44c2dc5452f03db840a143e1ee4";
    public const string AcceptedApplicationVersion = "1.0.0";

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public bool IsDesktopRunning()
    {
        foreach (var process in Process.GetProcessesByName("Sushi81.Pos.Desktop"))
        {
            using (process) return true;
        }
        return false;
    }

    public InstalledApplicationProvenance ReadInstalledApplicationProvenance(string installDirectory)
    {
        var executablePath = Path.Combine(installDirectory, "Sushi81.Pos.Desktop.exe");
        var provenancePath = Path.Combine(installDirectory, "release-provenance.json");
        if (!File.Exists(executablePath) || !File.Exists(provenancePath))
            throw new InvalidDataException("The accepted installed Sushi81 POS application and its release-provenance.json are required.");

        var version = FileVersionInfo.GetVersionInfo(executablePath);
        using var document = JsonDocument.Parse(File.ReadAllBytes(provenancePath));
        var root = document.RootElement;
        var result = new InstalledApplicationProvenance(
            ReadString(root, "productName"),
            ReadString(root, "productVersion"),
            version.FileVersion ?? string.Empty,
            ReadString(root, "sourceHeadSha").ToLowerInvariant(),
            ReadString(root, "runtimeIdentifier"),
            root.TryGetProperty("selfContained", out var selfContained) && selfContained.ValueKind == JsonValueKind.True);

        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(result.ProductName, "Sushi81 POS", StringComparison.Ordinal)
            || !string.Equals(version.ProductName, "Sushi81 POS", StringComparison.Ordinal)
            || !string.Equals(result.ProductVersion, AcceptedApplicationVersion, StringComparison.Ordinal)
            || !string.Equals(version.ProductVersion, $"{AcceptedApplicationVersion}+{AcceptedApplicationSourceHead}", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(result.FileVersion, "1.0.0.0", StringComparison.Ordinal)
            || !string.Equals(result.SourceHeadSha, AcceptedApplicationSourceHead, StringComparison.Ordinal)
            || !string.Equals(result.RuntimeIdentifier, "win-x64", StringComparison.Ordinal)
            || !result.SelfContained)
        {
            throw new InvalidDataException("Installed Sushi81 POS provenance does not match the manually accepted 1.0.0 win-x64 application source head.");
        }

        return result;
    }

    public void Checkpoint(CutoverCheckpoint checkpoint)
    {
        _ = checkpoint;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Installed application provenance is missing string field '{propertyName}'.");
        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"Installed application provenance field '{propertyName}' is empty.");
        return value;
    }
}
