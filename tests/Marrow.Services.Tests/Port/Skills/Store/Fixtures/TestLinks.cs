// PORT-ONLY: integ-sk4-sk5. A Windows account without SeCreateSymbolicLinkPrivilege (no Developer Mode, not elevated)
// cannot create symbolic links, so on such a host the same reparse-point fixture is made without privilege:
// a directory link becomes an NTFS junction (a name-surrogate mount point), and a file link becomes a file carrying a
// third-party reparse tag. Both carry FileAttributes.ReparsePoint exactly like a symbolic link, which is what the code
// under test inspects. Elsewhere the symbolic link itself is used. No cmd, mklink, CLI or elevation.
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Marrow.Services.Tests.Port.Skills.Store.Fixtures;

public static class TestLinks
{
    private const int PrivilegeNotHeld = 1314;

    /// <summary><paramref name="testRoot"/> is a disposable folder that contains both the link and its target.</summary>
    public static void Directory(string testRoot, string link, string target)
    {
        try { System.IO.Directory.CreateSymbolicLink(link, target); }
        catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == PrivilegeNotHeld)
        {
            WindowsJunction.Create(testRoot, link, target);
        }
    }

    public static void File(string link, string target)
    {
        try { System.IO.File.CreateSymbolicLink(link, target); }
        catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == PrivilegeNotHeld)
        {
            CreateThirdPartyReparseFile(link);
        }
    }

    private static void CreateThirdPartyReparseFile(string path)
    {
        using (System.IO.File.Create(path)) { }
        // REPARSE_GUID_DATA_BUFFER: tag (no Microsoft bit), data length, reserved, GUID, then 4 data bytes.
        var buffer = new byte[8 + 16 + 4];
        BitConverter.GetBytes(0x00004D52u).CopyTo(buffer, 0);
        BitConverter.GetBytes((ushort)4).CopyTo(buffer, 4);
        new Guid("6f0c8f3e-6a43-4f3a-9a52-7d1b2c3e4f50").ToByteArray().CopyTo(buffer, 8);
        using var handle = CreateFileW(path, 0x40000000u, 7, IntPtr.Zero, 3, 0x02200000u, IntPtr.Zero);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900A4u, buffer, (uint)buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new IOException("Could not create the reparse-point file fixture.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint control, byte[] input, uint inputSize, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
}
