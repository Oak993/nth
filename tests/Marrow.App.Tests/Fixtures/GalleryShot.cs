using System.Globalization;
using System.Runtime.InteropServices;
using Marrow.App.Tests.Fixtures;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// One screenshot of one window's own pixels from the composed screen (BitBlt with CAPTUREBLT, as ScreenGrab reads it), cropped
// to the window's visible frame (DWM's extended frame bounds: the frame without the drop shadow), waited on until the frames
// stop changing, and written as a PNG with a line in the folder's manifest.
internal static class GalleryShot
{
    private const int ExtendedFrameBounds = 9; // DWMWA_EXTENDED_FRAME_BOUNDS

    /// <summary>The window's visible frame in physical screen pixels.</summary>
    internal static (int X, int Y, int Width, int Height) FrameOf(nint window)
    {
        if (DwmGetWindowAttribute(window, ExtendedFrameBounds, out var rect, Marshal.SizeOf<NativeRect>()) != 0)
        {
            N.GetWindowRect(window, out var fallback);
            return (fallback.Left, fallback.Top, fallback.Right - fallback.Left, fallback.Bottom - fallback.Top);
        }
        return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>The visible top-level windows of a thread other than the ones named (the popover windows of the app's popups).</summary>
    internal static List<(int X, int Y, int Width, int Height)> OtherFrames(uint thread, params nint[] except)
    {
        var frames = new List<(int X, int Y, int Width, int Height)>();
        _ = N.EnumThreadWindows(thread, (window, _) =>
        {
            if (!N.IsWindowVisible(window) || N.IsIconic(window) || except.Contains(window)) return true;
            var frame = FrameOf(window);
            if (frame.Width > 8 && frame.Height > 8) frames.Add(frame);
            return true;
        }, 0);
        return frames;
    }

    /// <summary>The smallest rectangle holding every frame.</summary>
    internal static (int X, int Y, int Width, int Height) Union(IEnumerable<(int X, int Y, int Width, int Height)> frames)
    {
        var all = frames.ToList();
        var left = all.Min(f => f.X);
        var top = all.Min(f => f.Y);
        return (left, top, all.Max(f => f.X + f.Width) - left, all.Max(f => f.Y + f.Height) - top);
    }

    /// <summary>
    /// Grabs the frame until two grabs in a row differ by less than a hair (first paint, shader compile and layout done, no
    /// animation under way) or the bound runs out, and keeps the last. The driver thread calls this, never the STA.
    /// </summary>
    internal static (ScreenGrab Grab, bool Settled, double Change) Settle(Func<(int X, int Y, int Width, int Height)> frame, int minimumMilliseconds, int boundMilliseconds)
    {
        var started = Environment.TickCount64;
        Thread.Sleep(minimumMilliseconds);
        var (x, y, width, height) = frame();
        var previous = ScreenGrab.Of(x, y, width, height);
        var change = double.MaxValue;
        while (Environment.TickCount64 - started < boundMilliseconds)
        {
            Thread.Sleep(350);
            (x, y, width, height) = frame();
            var next = ScreenGrab.Of(x, y, width, height);
            change = next.Width == previous.Width && next.Height == previous.Height ? Difference(previous, next) : double.MaxValue;
            previous = next;
            if (change < 0.02) return (previous, true, change);
        }
        return (previous, false, change);
    }

    /// <summary>The mean absolute luma step per sampled pixel between two grabs of the same size.</summary>
    internal static double Difference(ScreenGrab a, ScreenGrab b)
    {
        double sum = 0;
        var count = 0;
        for (var y = 0; y < a.Height; y += 3)
            for (var x = 0; x < a.Width; x += 3)
            {
                sum += Math.Abs(a.Luma(x, y) - b.Luma(x, y));
                count++;
            }
        return sum / count;
    }

    /// <summary>The mean and the spread of the luma over a sample of the grab: a blank or black grab has a spread near 0.</summary>
    internal static (double Mean, double Spread) Statistics(ScreenGrab grab)
    {
        double sum = 0, square = 0;
        var count = 0;
        for (var y = 0; y < grab.Height; y += 4)
            for (var x = 0; x < grab.Width; x += 4)
            {
                var luma = grab.Luma(x, y);
                sum += luma;
                square += luma * luma;
                count++;
            }
        var mean = sum / count;
        return (mean, Math.Sqrt(Math.Max(0, square / count - mean * mean)));
    }

    /// <summary>Writes the PNG and appends its line to manifest.txt in the same folder.</summary>
    internal static void Save(string folder, string name, ScreenGrab grab, bool settled, double change, bool animated = false)
    {
        var (mean, spread) = Statistics(grab);
        var path = Path.Combine(folder, name + ".png");
        grab.SavePng(path);
        var line = string.Join('\t', name + ".png", $"{grab.Width}x{grab.Height}", "meanLuma=" + mean.ToString("F1", CultureInfo.InvariantCulture),
            "spread=" + spread.ToString("F1", CultureInfo.InvariantCulture), settled ? "settled" : animated ? "animated(change=" + change.ToString("F2", CultureInfo.InvariantCulture) + ")" : "UNSETTLED(change=" + change.ToString("F2", CultureInfo.InvariantCulture) + ")",
            spread < 1 ? "BLANK" : "ok");
        var manifest = Path.Combine(folder, "manifest.txt");
        var kept = File.Exists(manifest) ? File.ReadAllLines(manifest).Where(existing => !existing.StartsWith(name + ".png\t", StringComparison.Ordinal)) : [];
        File.WriteAllLines(manifest, kept.Append(line));
        Console.WriteLine("Gallery: " + line);
        if (spread < 1) throw new InvalidOperationException("The capture is blank or flat (spread " + spread + "): " + path);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect value, int size);
}
