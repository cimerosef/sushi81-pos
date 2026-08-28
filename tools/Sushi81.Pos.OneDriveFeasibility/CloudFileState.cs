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

/// <summary>
/// The documented Windows Cloud Files <c>CF_PLACEHOLDER_STATE</c> flags from cfapi.h.
/// Keep these values in one technical source of truth; publication interpretation below requires the
/// placeholder bit, gives partial states precedence, and rejects unknown bits fail-closed.
/// </summary>
[Flags]
public enum CfPlaceholderState : uint
{
    NoStates = 0x00000000,
    Placeholder = 0x00000001,
    SyncRoot = 0x00000002,
    EssentialPropPresent = 0x00000004,
    InSync = 0x00000008,
    Partial = 0x00000010,
    PartiallyOnDisk = 0x00000020,
    Invalid = 0xFFFFFFFF
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

    private const CfPlaceholderState KnownStates = CfPlaceholderState.Placeholder
        | CfPlaceholderState.SyncRoot
        | CfPlaceholderState.EssentialPropPresent
        | CfPlaceholderState.InSync
        | CfPlaceholderState.Partial
        | CfPlaceholderState.PartiallyOnDisk;

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
            return new(fullPath, Interpret((uint)raw), (uint)raw);
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
        var state = (CfPlaceholderState)raw;
        if (state == CfPlaceholderState.NoStates)
        {
            return CloudFilePublicationState.NotCloudPlaceholder;
        }

        if (state == CfPlaceholderState.Invalid)
        {
            return CloudFilePublicationState.Invalid;
        }

        if ((state & ~KnownStates) != 0)
        {
            return CloudFilePublicationState.Unknown;
        }

        if ((state & CfPlaceholderState.Placeholder) == 0)
        {
            return CloudFilePublicationState.NotCloudPlaceholder;
        }

        if ((state & (CfPlaceholderState.Partial | CfPlaceholderState.PartiallyOnDisk)) != 0)
        {
            return CloudFilePublicationState.Partial;
        }

        // SYNC_ROOT and ESSENTIAL_PROP_PRESENT are legal auxiliary flags. They do not invalidate
        // an otherwise valid PLACEHOLDER|IN_SYNC observation; without IN_SYNC the result remains pending.
        return (state & CfPlaceholderState.InSync) != 0
            ? CloudFilePublicationState.InSync
            : CloudFilePublicationState.Pending;
    }

    [DllImport("CldApi.dll", ExactSpelling = true)]
    private static extern CfPlaceholderState CfGetPlaceholderStateFromAttributeTag(uint fileAttributes, uint reparseTag);

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
