using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Storage;
using IoFileAttributes = System.IO.FileAttributes;

namespace Sushi81.Pos.OneDriveFeasibility;

/// <summary>
/// A read-only diagnostic snapshot of the Windows/OneDrive signals that are
/// available for a handoff artifact.  This type is deliberately not an
/// <see cref="IArtifactSyncObserver"/>: none of the signals below is treated
/// as a remote receipt for a locally-created non-placeholder file.
/// </summary>
public sealed record TransportProbeReport(
    string Path,
    RootValidationResult RootValidation,
    string? RegisteredRootId,
    Guid? RegisteredProviderId,
    bool Exists,
    long? SizeBytes,
    DateTimeOffset? LastWriteTimeUtc,
    uint? FileAttributes,
    IReadOnlyList<string> FileAttributeFlags,
    bool? IsReparsePoint,
    CloudFileObservation CloudFiles,
    IReadOnlyDictionary<string, DiagnosticSignal> StorageProviderProperties,
    DiagnosticSignal SyncRootProviderStatus,
    DiagnosticSignal StorageProviderStatusUi,
    string LocalOnlyConfirmation,
    string ConfirmationReason,
    IReadOnlyList<string> ApiErrors)
{
    public bool IsLocalOnlyConfirmation => string.Equals(LocalOnlyConfirmation, "Confirmed", StringComparison.Ordinal);
}

public sealed record DiagnosticSignal(
    string Status,
    string? Value = null,
    string? Error = null);

/// <summary>
/// Captures transport diagnostics without creating, opening for write, pinning,
/// hydrating, deleting, or otherwise changing any artifact or authority state.
/// </summary>
public sealed class TransportProbe
{
    private static readonly string[] PropertyNames =
    [
        "System.StorageProviderId",
        "System.StorageProviderFileRemoteUri",
        "System.FilePlaceholderStatus"
    ];

    public static async Task<TransportProbeReport> InspectAsync(string registeredRoot, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registeredRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        var validation = new WindowsSyncRootCatalog().Validate(registeredRoot);
        var errors = new List<string>();
        var exists = File.Exists(fullPath) || Directory.Exists(fullPath);
        long? size = null;
        DateTimeOffset? lastWrite = null;
        uint? attributes = null;
        var attributeFlags = new List<string>();
        bool? reparsePoint = null;

        if (exists)
        {
            try
            {
                var info = new FileInfo(fullPath);
                if (info.Exists)
                {
                    size = info.Length;
                    lastWrite = info.LastWriteTimeUtc;
                }
                else if (Directory.Exists(fullPath))
                {
                    lastWrite = Directory.GetLastWriteTimeUtc(fullPath);
                }

                var fileAttributes = File.GetAttributes(fullPath);
                attributes = unchecked((uint)fileAttributes);
                attributeFlags.AddRange(DescribeAttributes((uint)fileAttributes));
                reparsePoint = (fileAttributes & IoFileAttributes.ReparsePoint) != 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"file-metadata: {exception.Message}");
            }
        }
        else
        {
            errors.Add("file-metadata: the observed path does not exist");
        }

        var cloudFiles = new CloudFileStateReader().Observe(fullPath);
        if (cloudFiles.Error is not null)
        {
            errors.Add($"cloud-files: {cloudFiles.Error}");
        }

        var properties = await ReadStorageProviderPropertiesAsync(fullPath, errors);
        var providerStatus = CfSyncRootProviderStatusReader.Read(fullPath);
        if (providerStatus.Error is not null)
        {
            errors.Add($"cf-provider-status: {providerStatus.Error}");
        }

        // StorageProviderStatusUI is a provider-authored status flyout model.
        // Microsoft documents the values, but not a path-specific read that
        // proves a newly-created artifact reached the remote service. Keep it
        // visible as unavailable rather than treating a provider-wide InSync
        // value as an artifact receipt.
        var statusUi = new DiagnosticSignal(
            "Unavailable",
            Error: "No documented path-specific read API; provider-wide UI state is not an artifact acknowledgement.");

        return new TransportProbeReport(
            fullPath,
            validation,
            validation.MatchedRoot?.Id,
            validation.MatchedRoot?.ProviderId,
            exists,
            size,
            lastWrite,
            attributes,
            attributeFlags,
            reparsePoint,
            cloudFiles,
            properties,
            providerStatus,
            statusUi,
            "Blocked",
            "No documented local-only per-artifact confirmation exists for a locally-created non-placeholder. "
                + "CF IN_SYNC is defined only for placeholders; file attributes, provider properties, and sync-root status "
                + "do not provide a remote receipt. Unknown or unsupported signals remain fail-closed.",
            errors);
    }

    private static async Task<IReadOnlyDictionary<string, DiagnosticSignal>> ReadStorageProviderPropertiesAsync(
        string path,
        List<string> errors)
    {
        var result = PropertyNames.ToDictionary(name => name, _ => new DiagnosticSignal("Unavailable"), StringComparer.Ordinal);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return result.ToDictionary(pair => pair.Key, pair => pair.Value with { Error = "Path does not exist." }, StringComparer.Ordinal);
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var values = await file.Properties.RetrievePropertiesAsync(PropertyNames);
            foreach (var name in PropertyNames)
            {
                if (values.TryGetValue(name, out var value) && value is not null)
                {
                    result[name] = new DiagnosticSignal("Available", FormatValue(value));
                }
                else
                {
                    result[name] = new DiagnosticSignal("Unavailable", Error: "The storage provider did not expose this property.");
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            var message = exception.Message;
            errors.Add($"storage-properties: {message}");
            foreach (var name in PropertyNames)
            {
                result[name] = new DiagnosticSignal("Unavailable", Error: message);
            }
        }

        return result;
    }

    private static string FormatValue(object value) => value switch
    {
        byte[] bytes => Convert.ToHexString(bytes),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.ToString() ?? string.Empty
    };

    private static List<string> DescribeAttributes(uint attributes)
    {
        var flags = new List<string>();
        Add(0x00000001, "ReadOnly");
        Add(0x00000002, "Hidden");
        Add(0x00000004, "System");
        Add(0x00000010, "Directory");
        Add(0x00000400, "ReparsePoint");
        Add(0x00000800, "Compressed");
        Add(0x00004000, "Encrypted");
        Add(0x00040000, "RecallOnOpen");
        Add(0x00080000, "Pinned");
        Add(0x00100000, "Unpinned");
        Add(0x00400000, "RecallOnDataAccess");
        return flags;

        void Add(uint mask, string name)
        {
            if ((attributes & mask) != 0) flags.Add(name);
        }
    }
}

internal static class CfSyncRootProviderStatusReader
{
    private const uint ProviderStatusDisconnected = 0x00000000;
    private const uint ProviderStatusIdle = 0x00000001;
    private const uint ProviderStatusPopulateNamespace = 0x00000002;
    private const uint ProviderStatusPopulateMetadata = 0x00000004;
    private const uint ProviderStatusPopulateContent = 0x00000008;
    private const uint ProviderStatusSyncIncremental = 0x00000010;
    private const uint ProviderStatusSyncFull = 0x00000020;
    private const uint ProviderStatusConnectivityLost = 0x00000040;
    private const uint ProviderStatusTerminated = 0xC0000001;
    private const uint ProviderStatusError = 0xC0000002;

    public static DiagnosticSignal Read(string path)
    {
        var size = Marshal.SizeOf<CfSyncRootProviderInfo>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var hr = CfGetSyncRootInfoByPath(path, 2, buffer, (uint)size, out _);
            if (hr < 0)
            {
                return new DiagnosticSignal("Unavailable", Error: $"CfGetSyncRootInfoByPath failed with HRESULT 0x{unchecked((uint)hr):X8}.");
            }

            var info = Marshal.PtrToStructure<CfSyncRootProviderInfo>(buffer);
            var status = DescribeStatus(info.ProviderStatus);
            return new DiagnosticSignal("Available", $"{status} (raw=0x{info.ProviderStatus:X8}; provider={info.ProviderName.TrimEnd('\0')})");
        }
        catch (DllNotFoundException exception)
        {
            return new DiagnosticSignal("Unavailable", Error: exception.Message);
        }
        catch (EntryPointNotFoundException exception)
        {
            return new DiagnosticSignal("Unavailable", Error: exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ExternalException)
        {
            return new DiagnosticSignal("Unavailable", Error: exception.Message);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string DescribeStatus(uint status) => status switch
    {
        ProviderStatusDisconnected => "Disconnected",
        ProviderStatusIdle => "Idle",
        ProviderStatusPopulateNamespace => "PopulateNamespace",
        ProviderStatusPopulateMetadata => "PopulateMetadata",
        ProviderStatusPopulateContent => "PopulateContent",
        ProviderStatusSyncIncremental => "SyncIncremental",
        ProviderStatusSyncFull => "SyncFull",
        ProviderStatusConnectivityLost => "ConnectivityLost",
        ProviderStatusTerminated => "Terminated",
        ProviderStatusError => "Error",
        _ => $"Unknown"
    };

    [DllImport("CldApi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CfGetSyncRootInfoByPath(
        string filePath,
        uint infoClass,
        IntPtr infoBuffer,
        uint infoBufferLength,
        out uint returnedLength);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CfSyncRootProviderInfo
    {
        public uint ProviderStatus;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProviderName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProviderVersion;
    }
}
