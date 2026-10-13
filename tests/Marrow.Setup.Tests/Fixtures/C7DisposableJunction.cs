// PORT-ONLY: Marrow-made NTFS junction fixture. Native API only, no cmd/mklink/CLI or elevation.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Marrow.Setup.Tests.Fixtures;

public static class C7DisposableJunction
{
    public static void Create(string testRoot, string link, string target)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WINDOWS-ONLY NTFS fixture.");
        var root = Path.GetFullPath(testRoot) + Path.DirectorySeparatorChar;
        var absoluteLink = Path.GetFullPath(link);
        var absoluteTarget = Path.GetFullPath(target);
        if (!absoluteLink.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !absoluteTarget.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The junction and target must stay in the disposable test root.");
        Directory.CreateDirectory(absoluteLink);
        var substitute = absoluteTarget.StartsWith("\\\\", StringComparison.Ordinal) ? "\\??\\UNC\\" + absoluteTarget[2..] : "\\??\\" + absoluteTarget;
        var names = Encoding.Unicode.GetBytes(substitute + "\0" + absoluteTarget + "\0");
        var buffer = new byte[16 + names.Length];
        BitConverter.GetBytes(0xA0000003u).CopyTo(buffer, 0); // IO_REPARSE_TAG_MOUNT_POINT.
        BitConverter.GetBytes(checked((ushort)(buffer.Length - 8))).CopyTo(buffer, 4);
        var substituteBytes = checked((ushort)(substitute.Length * 2));
        BitConverter.GetBytes(substituteBytes).CopyTo(buffer, 10);
        BitConverter.GetBytes(checked((ushort)(substituteBytes + 2))).CopyTo(buffer, 12);
        BitConverter.GetBytes(checked((ushort)(absoluteTarget.Length * 2))).CopyTo(buffer, 14);
        names.CopyTo(buffer, 16);
        using var handle = CreateFileW(absoluteLink, 0x40000000u, 7, IntPtr.Zero, 3, 0x02200000u, IntPtr.Zero);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900A4u, buffer, checked((uint)buffer.Length), IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new IOException("Could not create the NTFS junction fixture.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint control, byte[] input, uint inputSize, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
}
