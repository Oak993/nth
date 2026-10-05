using System.Runtime.InteropServices;
using Avalonia;
using Xunit;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

internal static class AuroraBootNativeGeometry
{
    internal static void AssertClip(nint handle, Rect tile, PixelPoint stageOrigin, double scale, double radius)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        Assert.NotEqual(0, region);
        try
        {
            Assert.True(GetWindowRgn(handle, region) > 0, "The growing HWND/glass clip was not applied.");
            Assert.True(GetRgnBox(region, out var box) > 0);
            Assert.True(N.GetWindowRect(handle, out var outer));
            Assert.InRange(outer.Left + box.Left, stageOrigin.X + tile.Left * scale - 1, stageOrigin.X + tile.Left * scale + 1);
            Assert.InRange(outer.Top + box.Top, stageOrigin.Y + tile.Top * scale - 1, stageOrigin.Y + tile.Top * scale + 1);
            Assert.InRange(outer.Left + box.Right, stageOrigin.X + tile.Right * scale - 1, stageOrigin.X + tile.Right * scale + 1);
            Assert.InRange(outer.Top + box.Bottom, stageOrigin.Y + tile.Bottom * scale - 1, stageOrigin.Y + tile.Bottom * scale + 1);
            Assert.False(PtInRegion(region, box.Left, box.Top));
            var diagonal = (int)Math.Round(radius * scale * .2);
            Assert.False(PtInRegion(region, box.Left + diagonal, box.Top + diagonal));
            Assert.True(PtInRegion(region, box.Left + (int)Math.Ceiling(radius * scale), box.Top + 1));
        }
        finally { DeleteObject(region); }
    }
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint handle, nint region);
    [DllImport("gdi32.dll")] private static extern int GetRgnBox(nint region, out NativeWindowRect rect);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint region);
}
