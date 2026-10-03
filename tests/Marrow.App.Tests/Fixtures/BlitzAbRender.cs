using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// Headless renders of a timeline row for the TimelineRows closing tests (OD-0098: where no reference frame exists the pinned Swift
// source is the proof, shown against a rendered frame of the port). One frame at 1 pixel per point, in a window of the theme
// variant asked for over an opaque backdrop, measured the way the reference frames were (TimelineRowsCaptureTests).
internal sealed class BlitzAbFrame(int width, int height, byte[] pixels)
{
    internal int Width => width;
    internal int Height => height;

    internal Color At(int x, int y)
    {
        var at = (y * width + x) * 4;
        return Color.FromRgb(pixels[at], pixels[at + 1], pixels[at + 2]);
    }

    // The box of pixels that differ from the backdrop by more than the threshold (summed over the three channels) inside the
    // area, in frame pixels, or null when the area is bare backdrop.
    internal (int Left, int Top, int Right, int Bottom)? Ink(Rect area, Color backdrop, int threshold = 24)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var (x, y) in Pixels(area))
        {
            if (Distance(At(x, y), backdrop) <= threshold) continue;
            left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        return left == int.MaxValue ? null : (left, top, right, bottom);
    }

    internal int InkCount(Rect area, Color backdrop, int threshold = 24) =>
        Pixels(area).Count(pixel => Distance(At(pixel.X, pixel.Y), backdrop) > threshold);

    // The inked pixels of the area as a mask cut to its own ink box, so the same mark drawn at two places compares equal.
    internal bool[,]? Mask(Rect area, Color backdrop, int threshold = 24)
    {
        if (Ink(area, backdrop, threshold) is not { } box) return null;
        var mask = new bool[box.Right - box.Left + 1, box.Bottom - box.Top + 1];
        for (var x = box.Left; x <= box.Right; x++)
            for (var y = box.Top; y <= box.Bottom; y++)
                mask[x - box.Left, y - box.Top] = Distance(At(x, y), backdrop) > threshold;
        return mask;
    }

    // The ink in the area as a count of fully covered pixels of the colour (the sum of every pixel's distance from the backdrop over the
    // colour's own): unlike a count of inked pixels it does not change when the mark is drawn half a pixel to one side.
    internal double InkMass(Rect area, Color backdrop, Color colour)
    {
        var full = Distance(Over(colour, backdrop), backdrop);
        return Pixels(area).Sum(pixel => (double)Distance(At(pixel.X, pixel.Y), backdrop)) / full;
    }

    private IEnumerable<(int X, int Y)> Pixels(Rect area)
    {
        var left = Math.Max(0, (int)Math.Floor(area.Left)); var right = Math.Min(width - 1, (int)Math.Ceiling(area.Right) - 1);
        var top = Math.Max(0, (int)Math.Floor(area.Top)); var bottom = Math.Min(height - 1, (int)Math.Ceiling(area.Bottom) - 1);
        for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
                yield return (x, y);
    }

    internal static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    // The colour a source colour (with its own alpha) leaves over a backdrop.
    internal static Color Over(Color source, Color under)
    {
        var alpha = source.A / 255d;
        byte Mix(byte top, byte bottom) => (byte)Math.Round(top * alpha + bottom * (1 - alpha));
        return Color.FromRgb(Mix(source.R, under.R), Mix(source.G, under.G), Mix(source.B, under.B));
    }
}

internal static class BlitzAbRender
{
    // The control's box in the window, in points.
    // Its layout box, taken through its parent so a control turned by a render transform is still measured where it was laid out;
    // the box can be grown on every side for a mark that turns about its centre.
    internal static Rect BoxIn(Control control, Visual window, double inflate = 0)
    {
        var parent = control.GetVisualParent() ?? throw new InvalidOperationException("not mounted");
        var origin = parent.TranslatePoint(control.Bounds.Position, window) ?? throw new InvalidOperationException("not mounted");
        return new Rect(origin, control.Bounds.Size).Inflate(inflate);
    }

    // Mounts the content in a window of the given theme over an opaque backdrop, lays it out, renders one frame and hands the
    // window and the frame to the visitor while it is still open. Called with the main actor bound (OnMain).
    internal static void Render(Control content, ThemeVariant theme, Color backdrop, double width, double height, Action<AvaloniaWindow, BlitzAbFrame> visit)
    {
        var window = new AvaloniaWindow
        {
            Width = width, Height = height, RequestedThemeVariant = theme,
            Background = new SolidColorBrush(backdrop), Content = content,
        };
        window.Show();
        try { visit(window, Capture(window)); }
        finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    // The same window, left open for the caller to drive and capture again (close it with Shut).
    internal static AvaloniaWindow Open(Control content, ThemeVariant theme, Color backdrop, double width, double height, Action<AvaloniaWindow>? beforeShow = null)
    {
        var window = new AvaloniaWindow
        {
            Width = width, Height = height, RequestedThemeVariant = theme,
            Background = new SolidColorBrush(backdrop), Content = content,
        };
        beforeShow?.Invoke(window);
        window.Show();
        return window;
    }

    internal static void Shut(AvaloniaWindow window)
    {
        window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs();
    }

    // A new frame of a window already open (after a state change).
    internal static BlitzAbFrame Capture(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 4; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var bitmap = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = bitmap.Lock();
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        Assert.Equal(buffer.Size.Width * 4, buffer.RowBytes);
        return new BlitzAbFrame(buffer.Size.Width, buffer.Size.Height, pixels);
    }

    // Every strongly inked pixel of the area is the colour asked for, whatever its coverage: a pixel's distance from the backdrop is
    // the same fraction of (colour over backdrop - backdrop) in every channel that has contrast to read (a pixel at coverage c is
    // backdrop + c (colour - backdrop)). Returns the strongest coverage seen, so a test can ask the ink was really drawn. A translucent
    // icon draws its strokes one element at a time, so where two overlap (a corner) the pixel is composited twice and reads up to
    // about 1.5 of the single colour; `most` allows that and no more.
    internal static double InkIsColour(BlitzAbFrame frame, Rect area, Color backdrop, Color colour, string because, double spread = 0.14, double most = 1.5)
    {
        var target = BlitzAbFrame.Over(colour, backdrop);
        var channels = new (Func<Color, int> Get, int Delta)[]
        {
            (c => c.R, target.R - backdrop.R), (c => c.G, target.G - backdrop.G), (c => c.B, target.B - backdrop.B),
        }.Where(channel => Math.Abs(channel.Delta) >= 50).ToArray();
        Assert.True(channels.Length > 0, $"{because}: the colour {target} has no contrast against the backdrop {backdrop} to read");
        var strongest = 0d;
        for (var x = (int)Math.Floor(area.Left); x < (int)Math.Ceiling(area.Right); x++)
        {
            for (var y = (int)Math.Floor(area.Top); y < (int)Math.Ceiling(area.Bottom); y++)
            {
                var pixel = frame.At(x, y);
                var ratios = channels.Select(channel => (channel.Get(pixel) - channel.Get(backdrop)) / (double)channel.Delta).ToArray();
                if (ratios.Max() < 0.25) continue; // faint edge pixels carry no colour to read
                Assert.True(ratios.Max() - ratios.Min() <= spread && ratios.Max() <= most,
                    $"{because}: the pixel ({x}, {y}) is {pixel} over {backdrop}, which is not the ink {target} at any coverage; area {area}" + Environment.NewLine + Dump(frame, area));
                strongest = Math.Max(strongest, ratios.Max());
            }
        }
        return strongest;
    }

    internal static string Dump(BlitzAbFrame frame, Rect area)
    {
        var rows = new List<string>();
        for (var y = (int)Math.Floor(area.Top); y < (int)Math.Ceiling(area.Bottom); y++)
            rows.Add(string.Join(" ", Enumerable.Range((int)Math.Floor(area.Left), (int)Math.Ceiling(area.Width)).Select(x => frame.At(x, y).R.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))));
        return string.Join(Environment.NewLine, rows);
    }

    // The same for text, which the renderer draws with per-channel (sub-pixel) coverage: each channel of every pixel lies between the
    // backdrop's and the colour's (a channel's coverage is its own), and some pixel of a stem is the colour itself. Returns how close
    // that pixel is (the sum of the channel differences).
    internal static int TextIsColour(BlitzAbFrame frame, Rect area, Color backdrop, Color colour, string because, int slack = 14, int closest = 54)
    {
        var target = BlitzAbFrame.Over(colour, backdrop);
        var best = int.MaxValue;
        for (var x = (int)Math.Floor(area.Left); x < (int)Math.Ceiling(area.Right); x++)
        {
            for (var y = (int)Math.Floor(area.Top); y < (int)Math.Ceiling(area.Bottom); y++)
            {
                var pixel = frame.At(x, y);
                Assert.True(Within(pixel.R, backdrop.R, target.R, slack) && Within(pixel.G, backdrop.G, target.G, slack) && Within(pixel.B, backdrop.B, target.B, slack),
                    $"{because}: the pixel ({x}, {y}) is {pixel} over {backdrop}, outside the range the ink {target} can leave");
                if (BlitzAbFrame.Distance(pixel, backdrop) > 24) best = Math.Min(best, BlitzAbFrame.Distance(pixel, target));
            }
        }
        Assert.True(best <= closest, $"{because}: no pixel is the ink {target} (the nearest is {best} away)");
        return best;

        static bool Within(byte value, byte a, byte b, int slack) => value >= Math.Min(a, b) - slack && value <= Math.Max(a, b) + slack;
    }
}
