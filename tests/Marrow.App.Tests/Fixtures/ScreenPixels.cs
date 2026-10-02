using System.Runtime.InteropServices;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Reads pixels of the real screen (native runs only): what a window on top actually shows.</summary>
public static class ScreenPixels
{
    /// <summary>The colour at a physical screen point as (red, green, blue).</summary>
    public static (byte R, byte G, byte B) At(int x, int y)
    {
        var dc = GetDC(IntPtr.Zero);
        try
        {
            var value = GetPixel(dc, x, y);
            return ((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }

    /// <summary>How saturated a colour is: (max - min) / max over the channels, 0 for black.</summary>
    public static double Saturation((byte R, byte G, byte B) c)
    {
        var max = Math.Max(c.R, Math.Max(c.G, c.B));
        var min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max == 0 ? 0 : (max - min) / (double)max;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
}
