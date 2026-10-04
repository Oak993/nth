using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Reads a rectangle of a rendered headless frame (Rgba8888) and compares two of them, for the Customization preview's tests (CZ-3).</summary>
internal static class PreviewPixels
{
    internal static byte[] Region(TopLevel window, Rect region)
    {
        Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var (x0, y0, w, h) = ((int)Math.Round(region.X), (int)Math.Round(region.Y), (int)Math.Round(region.Width), (int)Math.Round(region.Height));
        var result = new byte[w * h * 4];
        var row = new byte[buffer.RowBytes];
        for (var y = 0; y < h; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address + (y0 + y) * buffer.RowBytes, row, 0, buffer.RowBytes);
            Array.Copy(row, x0 * 4, result, y * w * 4, w * 4);
        }
        return result;
    }

    /// <summary>The region with its own top-left pixel taken off every pixel: what is drawn on the plate, whatever tint the plate's material has under it.</summary>
    internal static byte[] AgainstItsBackground(byte[] region)
    {
        var result = new byte[region.Length];
        for (var i = 0; i < region.Length; i++)
            result[i] = i % 4 == 3 ? (byte)255 : (byte)Math.Clamp(128 + region[i] - region[i % 4], 0, 255);
        return result;
    }

    /// <summary>The box (left, top, right, bottom) of the pixels that differ from the region's first pixel by more than <paramref name="threshold"/>, columns before <paramref name="skip"/> ignored (a caret).</summary>
    internal static (int Left, int Top, int Right, int Bottom) InkBounds(byte[] region, int width, int threshold, int skip)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        var height = region.Length / 4 / width;
        for (var y = 0; y < height; y++)
            for (var x = skip; x < width; x++)
            {
                var i = (y * width + x) * 4;
                if (Math.Abs(region[i] - region[0]) <= threshold && Math.Abs(region[i + 1] - region[1]) <= threshold && Math.Abs(region[i + 2] - region[2]) <= threshold) continue;
                left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
        return (left, top, right, bottom);
    }

    /// <summary>The mean absolute difference over all colour channels, 0 to 255.</summary>
    internal static double MeanDifference(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) throw new ArgumentException("The regions differ in size.");
        long total = 0;
        for (var i = 0; i < a.Length; i++) total += Math.Abs(a[i] - b[i]);
        return total / (double)a.Length;
    }
}
