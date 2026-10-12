// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-46; DESIGN.md 2.10 the quiet peek). A window's placement, saved before a
// minimized window is shown for a moment and put back after: GetWindowPlacement and SetWindowPlacement, and the bottom of
// the Z-order. Nothing here runs in automated tests.
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.PcControl.Native;

internal static partial class Win32
{
    internal static readonly nint HWND_BOTTOM = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPLACEMENT
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public POINT MinPosition;
        public POINT MaxPosition;
        public RECT NormalPosition;
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(nint window, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPlacement(nint window, ref WINDOWPLACEMENT placement);
}
