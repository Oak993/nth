// Derive every held ROI from the same BitBlt, avoiding separate captures on either side of a DWM repaint.
using System.Reflection;
using Marrow.App.Tests.Fixtures;

internal static class ProbeScreenCrop
{
    internal static ScreenGrab Crop(ScreenGrab source, int left, int top, int width, int height)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = source.At(left + x, top + y);
                var i = (y * width + x) * 4;
                bgra[i] = b; bgra[i + 1] = g; bgra[i + 2] = r; bgra[i + 3] = 255;
            }
        return (ScreenGrab)Activator.CreateInstance(typeof(ScreenGrab), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [width, height, bgra], null)!;
    }
}
