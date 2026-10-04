// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Skills.Install;

internal static class WindowsDataStreams
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StreamData
    {
        public long Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] public string Name;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstStreamW(string path, int level, out StreamData data, int flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextStreamW(IntPtr handle, out StreamData data);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr handle);
    public static void RefuseNamedStreams(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = FindFirstStreamW(path, 0, out var data, 0);
        if (handle == new IntPtr(-1))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 38) return; // ERROR_HANDLE_EOF: no streams (directories).
            throw new IOException("Could not inspect Windows data streams.", new Win32Exception(error));
        }
        try
        {
            do { if (!data.Name.Equals("::$DATA", StringComparison.Ordinal)) throw new InvalidDataException("Named data stream refused."); }
            while (FindNextStreamW(handle, out data));
            if (Marshal.GetLastWin32Error() != 38) throw new IOException("Could not enumerate Windows data streams.");
        }
        finally { FindClose(handle); }
    }
}
