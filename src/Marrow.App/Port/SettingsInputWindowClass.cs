// PORT-ONLY RR-37: a non-drawing input class, without STATIC's built-in accessibility proxy.
using System.Runtime.InteropServices;

namespace Marrow.App.Port;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SettingsInputWindowClass
{
    internal uint Style;
    internal nint Procedure;
    internal int ClassExtra, WindowExtra;
    internal nint Instance, Icon, Cursor, Brush, Menu;
    [MarshalAs(UnmanagedType.LPWStr)] internal string Name;
}
