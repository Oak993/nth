using System.Runtime.InteropServices;
using Marrow.Setup.Support;

namespace Marrow.Setup.Port;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8; unit U6, ED-NEW-inst-u6-1). <see cref="ICredentialStore"/> over the signed-in user's
/// Credential Manager: <c>CredEnumerateW</c> with a <c>prefix*</c> filter, read only for each item's type and target name, and
/// <c>CredDeleteW</c> for <c>CRED_TYPE_GENERIC</c>. Constructed only in src/Marrow.Setup/Program.cs; tests use a fake.
/// </summary>
public sealed partial class WindowsCredentialStore : ICredentialStore
{
    private const uint CredTypeGeneric = 1; // wincred.h CRED_TYPE_GENERIC
    private const int ErrorNotFound = 1168;

    // CREDENTIALW on 64-bit Windows: DWORD Flags (0), DWORD Type (4), LPWSTR TargetName (8)
    private const int TypeOffset = 4, TargetNameOffset = 8;

    public IReadOnlyList<string> FindGeneric(string prefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return [];
        if (!CredEnumerateW(prefix + "*", 0, out var count, out var list))
            return [];
        try
        {
            var found = new List<string>((int)Math.Min(count, 4096u));
            for (var i = 0; i < count; i++)
            {
                var credential = Marshal.ReadIntPtr(list, i * IntPtr.Size);
                if (credential == 0 || (uint)Marshal.ReadInt32(credential, TypeOffset) != CredTypeGeneric) continue;
                if (Marshal.PtrToStringUni(Marshal.ReadIntPtr(credential, TargetNameOffset)) is { } target) found.Add(target);
            }
            return found;
        }
        finally
        {
            CredFree(list);
        }
    }

    public bool DeleteGeneric(string targetName)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetName);
        if (!OperatingSystem.IsWindows()) return false;
        return CredDeleteW(targetName, CredTypeGeneric, 0) || Marshal.GetLastPInvokeError() == ErrorNotFound;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredEnumerateW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredEnumerateW(string filter, uint flags, out uint count, out nint credentials);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDeleteW(string targetName, uint type, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(nint buffer);
}
