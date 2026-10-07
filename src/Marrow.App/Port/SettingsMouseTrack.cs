// PORT-ONLY RR-37: TRACKMOUSEEVENT for the non-drawing inner input HWND.
using System.Runtime.InteropServices;

namespace Marrow.App.Port;

[StructLayout(LayoutKind.Sequential)]
internal struct SettingsMouseTrack
{
    internal uint Size, Flags;
    internal nint Window;
    internal uint Hover;
}
