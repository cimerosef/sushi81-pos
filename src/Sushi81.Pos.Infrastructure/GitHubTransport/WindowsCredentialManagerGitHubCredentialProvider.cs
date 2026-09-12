using System.ComponentModel;
using System.Runtime.InteropServices;
using Sushi81.Pos.Application.Foundation.GitHubTransport;

namespace Sushi81.Pos.Infrastructure.GitHubTransport;

/// <summary>
/// Reads the production GitHub token from the Windows Credential Manager generic-secret
/// store. The secret is kept in unmanaged memory only long enough to copy it to the caller;
/// it is never serialized or logged by this class.
/// </summary>
public sealed class WindowsCredentialManagerGitHubCredentialProvider(string targetName)
    : IProtectedGitHubCredentialProvider
{
    private const uint GenericCredentialType = 1;
    private const int NotFoundError = 1168;

    public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(targetName))
            throw new InvalidOperationException("The GitHub credential target is not configured.");

        if (!CredRead(targetName, GenericCredentialType, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFoundError) return ValueTask.FromResult<string?>(null);
            throw new Win32Exception(error, "Windows Credential Manager could not read the GitHub credential.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return ValueTask.FromResult<string?>(null);

            var byteCount = checked((int)credential.CredentialBlobSize);
            var bytes = new byte[byteCount];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, byteCount);
            var token = byteCount % 2 == 0
                ? Marshal.PtrToStringUni(credential.CredentialBlob, byteCount / 2)
                : System.Text.Encoding.UTF8.GetString(bytes);
            return ValueTask.FromResult<string?>(string.IsNullOrWhiteSpace(token) ? null : token);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint flags,
        out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern bool CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
