// PORT-ONLY: Foundation's FileManager.attributesOfItem(atPath:) has no .NET twin that gives a file's identity. This reads
// the three attributes Git.GitFileStamp takes (Git.swift:499-502) from one GetFileInformationByHandle call: the volume
// serial and NTFS file index stand in for systemFileNumber (ED-044, R-PROC-3), the last write time for
// modificationDate, and the file size for size.
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Marrow.Services.Port;

public static class FileInformation
{
    /// <summary>What one GetFileInformationByHandle call answers for a file or folder.</summary>
    public sealed record Info(uint VolumeSerial, ulong FileIndex, DateTimeOffset Modified, long Size);

    private const uint FileReadAttributes = 0x80;
    private const uint FileShareAll = 0x1 | 0x2 | 0x4; // read, write, delete
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000; // a folder opens too
    private const uint FileFlagOpenReparsePoint = 0x00200000; // attributesOfItem does not follow a final link (lstat)

    /// <summary>
    /// The attributes of the item at <paramref name="path"/>, or null when it cannot be opened or asked, as
    /// <c>try? attributesOfItem(atPath:)</c> is nil.
    /// </summary>
    public static Info? Read(string path)
    {
        using var handle = CreateFileW(path, FileReadAttributes, FileShareAll, IntPtr.Zero, OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        if (!GetFileInformationByHandle(handle, out var information)) return null;
        var index = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
        var written = ((long)information.LastWriteTimeHigh << 32) | information.LastWriteTimeLow;
        var size = ((long)information.FileSizeHigh << 32) | information.FileSizeLow;
        return new Info(information.VolumeSerialNumber, index, new DateTimeOffset(DateTime.FromFileTimeUtc(written)), size);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);
}
