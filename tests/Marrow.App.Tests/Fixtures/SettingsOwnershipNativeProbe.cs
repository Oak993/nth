using System.Runtime.InteropServices;
using Avalonia;

namespace Marrow.App.Tests.Window.Native;

internal static class SettingsOwnershipNativeProbe
{
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint window, uint command);
    internal static bool Above(nint window, nint owner)
    {
        for (var next = GetWindow(window, 2); next != 0; next = GetWindow(next, 2)) if (next == owner) return true;
        return false;
    }
    internal static void Send(nint window, uint message, nint value, nint point = 0) =>
        Assert.NotEqual((nint)0, WindowManagerNativeMethods.SendMessageTimeout(window, message, value, point, 2, 1000, out _));
    internal static nint Pack(PixelPoint point) => unchecked((nint)((uint)(ushort)point.X | ((uint)(ushort)point.Y << 16)));
}
