using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Sushi81.Pos.OneDriveFeasibility;

public enum CloudFilePublicationState
{
    Unknown,
    NotCloudPlaceholder,
    Pending,
    InSync,
    Partial,
    Invalid,
    Missing
}

public sealed record CloudFileObservation(
    string Path,
    CloudFilePublicationState State,
    uint RawPlaceholderState,
    string? Error = null)
{
    public bool IsConfirmedInSync => State == CloudFilePublicationState.InSync;
}

public interface ICloudFileStateReader
{
    CloudFileObservation Observe(string path);
}

/// <summary>
/// Reads the documented Cloud Files placeholder state from a file's attributes and reparse tag.
/// This is deliberately an observation-only adapter: it never hydrates, pins, modifies or claims a file.
/// </summary>
public sealed class CloudFileStateReader : ICloudFileStateReader
{
    private const uint FileAttributeTagInfo = 9;
    private const uint FileReadAttributes = 0x00000080;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint ShareDelete = 0x00000004;

    private const uint Placeholder = 0x0001;
    private const uint InSync = 0x0002;
    private const uint Partial = 0x0004;
    private const uint PartiallyOnDisk = 0x0008;
    private const uint Invalid = 0x0000;
    private const uint KnownStates = Placeholder | InSync | Partial | PartiallyOnDisk;

    public CloudFileObservation Observe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return new(fullPath, CloudFilePublicationState.Missing, 0, "The observed path does not exist.");
        }

        try
        {
            using var handle = CreateFile(
                fullPath,
                FileReadAttributes,
                ShareRead | ShareWrite | ShareDelete,
                IntPtr.Zero,
                OpenExisting,
                FileFlagOpenReparsePoint,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                return new(fullPath, CloudFilePublicationState.Unknown, 0, new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }

            if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfo, out var info, (uint)Marshal.SizeOf<FileAttributeTagInfoData>()))
            {
                return new(fullPath, CloudFilePublicationState.Unknown, 0, new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }

            var raw = CfGetPlaceholderStateFromAttributeTag(info.FileAttributes, info.ReparseTag);
            return new(fullPath, Interpret(raw), raw);
        }
        catch (DllNotFoundException exception)
        {
            return new(fullPath, CloudFilePublicationState.Unknown, 0, exception.Message);
        }
        catch (EntryPointNotFoundException exception)
        {
            return new(fullPath, CloudFilePublicationState.Unknown, 0, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(fullPath, CloudFilePublicationState.Unknown, 0, exception.Message);
        }
    }

    public static CloudFilePublicationState Interpret(uint raw)
    {
        if (raw == Invalid)
        {
            return CloudFilePublicationState.NotCloudPlaceholder;
        }

        if ((raw & ~KnownStates) != 0)
        {
            return CloudFilePublicationState.Unknown;
        }

        if ((raw & Placeholder) == 0)
        {
            return CloudFilePublicationState.Unknown;
        }

        if ((raw & (Partial | PartiallyOnDisk)) != 0)
        {
            return CloudFilePublicationState.Partial;
        }

        return (raw & InSync) != 0
            ? CloudFilePublicationState.InSync
            : CloudFilePublicationState.Pending;
    }

    [DllImport("CldApi.dll", ExactSpelling = true)]
    private static extern uint CfGetPlaceholderStateFromAttributeTag(uint fileAttributes, uint reparseTag);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        uint fileInformationClass,
        out FileAttributeTagInfoData fileInformation,
        uint bufferSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfoData
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }
}
