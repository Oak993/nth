using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Marrow.Setup.Install;

namespace Marrow.Setup.Port;

// PORT-ONLY: ED-NEW-inst-c7-1. Stable metadata identity, without reading data or retaining a handle.
internal static partial class WindowsInstallFileIdentity
{
    internal static InstallFileSnapshot? Read(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        // FILE_READ_ATTRIBUTES, share read/write/delete, OPEN_EXISTING, backup semantics + open reparse point.
        using var handle = CreateFileW(path, 0x80, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, 18, out var identity, 24)
            || !GetFileSizeEx(handle, out var length) || length < 0 || identity.Low == 0 && identity.High == 0) return null;
        return new InstallFileSnapshot(identity, length);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(string path, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out InstallFileIdentity identity, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileSizeEx(SafeFileHandle handle, out long length);
}
