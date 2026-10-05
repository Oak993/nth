using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// OD-0075 (owner, 2026-10-02: "Window corner: the wedge is filled with the window's scrim") and the owner's intent of 2026-10-05: Windows rounds a
/// window's corner less than the app's glass shape (26 pt; the tour card, 20 pt), so the glass is EXTENDED to the window's rectangle and the whole
/// window reads as one glass surface out to the OS edge (ED-NEW-corner-seam). The proof is the same in every window: a pixel in the wedge (the
/// window's rectangle outside the shape) is the colour of the pixel of the body just inside the shape, in the same column, within
/// <see cref="Tolerance"/> levels of 255 on each channel. Over a backdrop that is uniform down a column (a solid colour, or vertical stripes), the
/// two see the same backdrop, so any layer the body has and the wedge lacks (the tint, the scrim, a material, the detail sheet's wash) shows.
/// </summary>
public static class CornerSeam
{
    /// <summary>Levels of 255 on each channel.</summary>
    public const int Tolerance = 3;

    /// <summary>One corner: a pixel outside the shape (the wedge) and the pixel in its column just inside the shape (the body).</summary>
    public readonly record struct Pair(string Corner, (int X, int Y) Wedge, (int X, int Y) Body);

    /// <summary>
    /// The pairs for a window of <paramref name="width"/> x <paramref name="height"/> pixels whose shape is a continuous rounded rectangle of
    /// <paramref name="radius"/> pixels: at each of the four corners, each x in <paramref name="columns"/> counted from the corner's vertical edge
    /// whose pixel is outside the shape by at least <paramref name="clearance"/> pixels takes its wedge pixel on the window's edge row
    /// <paramref name="edgeInset"/> in, and its body pixel in the same column the first row that is inside the shape by <paramref name="clearance"/>. The columns start at 2 and the
    /// edge inset at 1: the backdrop's hairline is the window's inner stroke, and it covers the outermost pixel of every edge.
    /// </summary>
    public static Pair[] Pairs(int width, int height, double radius, IEnumerable<int> columns, int edgeInset, double clearance = 2.5, double osRadius = 0, int block = 1)
    {
        var shape = SwiftShapes.RoundedRectangleGeometry(new Rect(0, 0, width, height), radius, RoundedCornerStyle.Continuous);
        // a sample beyond the window's rectangle is the rectangle's edge, not the shape's: it is clamped to the outermost pixel's centre
        bool Inside(double x, double y) => shape.FillContains(new Point(Math.Clamp(x, 0.5, width - 0.5), Math.Clamp(y, 0.5, height - 0.5)));
        bool Clear(int x, int y, bool inside)
        {
            foreach (var (dx, dy) in new[] { (0.0, 0.0), (clearance, 0.0), (-clearance, 0.0), (0.0, clearance), (0.0, -clearance), (clearance, clearance), (-clearance, -clearance), (clearance, -clearance), (-clearance, clearance) })
                if (Inside(x + 0.5 + dx, y + 0.5 + dy) != inside) return false;
            return true;
        }
        // A real window is rounded by the OS (DWM, 8 px) and the OS draws its own border on that curve: a wedge pixel is read only where its whole block is inside the rounding,
        // a pixel clear of the OS border (osRadius 0: the headless window, which has none).
        bool InsideOsRounding(int x, int y)
        {
            if (osRadius <= 0) return true;
            var half = block / 2.0;
            foreach (var (dx, dy) in new[] { (-half, -half), (half, -half), (-half, half), (half, half) })
            {
                var sx = x + 0.5 + dx; var sy = y + 0.5 + dy;
                if (sx < osRadius && sy < osRadius && Math.Sqrt((osRadius - sx) * (osRadius - sx) + (osRadius - sy) * (osRadius - sy)) > osRadius - 1.5) return false;
            }
            return true;
        }
        var pairs = new List<Pair>();
        foreach (var x in columns)
        {
            if (!Clear(x, edgeInset, inside: false) || !InsideOsRounding(x, edgeInset)) continue;
            var y = edgeInset;
            while (y < height / 2 && !Clear(x, y, inside: true)) y++;
            if (y >= height / 2) throw new InvalidOperationException($"no body pixel in column {x}");
            pairs.Add(new Pair("top left", (x, edgeInset), (x, y)));
            pairs.Add(new Pair("top right", (width - 1 - x, edgeInset), (width - 1 - x, y)));
            pairs.Add(new Pair("bottom left", (x, height - 1 - edgeInset), (x, height - 1 - y)));
            pairs.Add(new Pair("bottom right", (width - 1 - x, height - 1 - edgeInset), (width - 1 - x, height - 1 - y)));
        }
        return pairs.ToArray();
    }

    /// <summary>The colour of each point of a headless window's rendered frame, as (red, green, blue) levels of 255.</summary>
    public static (int R, int G, int B)[] Rgb(TopLevel window, params (int X, int Y)[] points)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var row = new byte[buffer.RowBytes];
        var colours = new List<(int, int, int)>();
        foreach (var (x, y) in points)
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, buffer.RowBytes);
            colours.Add((row[x * 4], row[x * 4 + 1], row[x * 4 + 2])); // Rgba8888 on this headless surface
        }
        return colours.ToArray();
    }

    /// <summary>Whether two colours are within <see cref="Tolerance"/> on each channel.</summary>
    public static bool Near((double R, double G, double B) a, (double R, double G, double B) b, int tolerance = Tolerance) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

    /// <summary>
    /// Asserts that each pair of a headless window agrees: its wedge pixel is its body pixel. With <paramref name="bareBackdrop"/> (a solid backdrop's
    /// colour) the body is also not that colour: the window draws something there, so equality is not two bare backdrops.
    /// </summary>
    public static void AssertSeamless(TopLevel window, Pair[] pairs, string when, (int R, int G, int B)? bareBackdrop = null)
    {
        Assert.NotEmpty(pairs);
        var wedge = Rgb(window, pairs.Select(pair => pair.Wedge).ToArray());
        var body = Rgb(window, pairs.Select(pair => pair.Body).ToArray());
        for (var index = 0; index < pairs.Length; index++)
            Assert.True(Near(wedge[index], body[index]),
                $"{when}: the {pairs[index].Corner} wedge pixel {pairs[index].Wedge} is {wedge[index]} but the body just inside the shape, {pairs[index].Body}, is {body[index]} (within {Tolerance} on each channel)");
        if (bareBackdrop is { } bare)
            Assert.All(body, colour => Assert.False(Near(colour, bare, 8), $"{when}: the body {colour} is the bare backdrop {bare}"));
    }

    /// <summary>
    /// The pairs of a real window whose visible rectangle on the screen is <paramref name="visible"/> (physical pixels), the shape being
    /// <paramref name="radiusPoints"/> points of <paramref name="scale"/> pixels each. Each pair is in screen pixels. A pixel is read as the mean of
    /// the <paramref name="block"/> x <paramref name="block"/> block around it, so a pair keeps <paramref name="clearance"/> pixels clear of the shape's edge.
    /// </summary>
    public static Pair[] OnScreen(int left, int top, int right, int bottom, double radiusPoints, double scale, int[] columns, int[] edgeInsets, double clearance, int block = 3)
    {
        var width = right - left;
        var height = bottom - top;
        var pairs = edgeInsets.SelectMany(inset => Pairs(width, height, radiusPoints * scale, columns, edgeInset: inset, clearance: clearance, osRadius: 8 * scale, block: block)).ToArray();
        return pairs.Select(pair => new Pair(pair.Corner, (left + pair.Wedge.X, top + pair.Wedge.Y), (left + pair.Body.X, top + pair.Body.Y))).ToArray();
    }

    /// <summary>
    /// Waits until the screen at each of the pairs' pixels has stopped changing (two reads 300 ms apart agree to within 1 on each channel): the window's
    /// colours ease (OD-0088) and the system's Acrylic follows the theme, both over a moment after a change of appearance.
    /// </summary>
    public static void WaitSettled(Pair[] pairs, int block, int milliseconds = 8000)
    {
        var spots = pairs.Take(2).SelectMany(pair => new[] { pair.Wedge, pair.Body }).ToArray();
        (double R, double G, double B)[] Read() => spots.Select(spot => ScreenMean(spot.X, spot.Y, block)).ToArray();
        var deadline = Environment.TickCount64 + milliseconds;
        var before = Read();
        while (Environment.TickCount64 < deadline)
        {
            Thread.Sleep(300);
            var now = Read();
            if (before.Zip(now).All(pair => Near(pair.First, pair.Second, 1))) return;
            before = now;
        }
        throw new TimeoutException("the screen at the corner never settled");
    }

    /// <summary>The mean colour of the <paramref name="block"/> x <paramref name="block"/> block (1 or 3) of the real screen around the physical pixel (x, y).</summary>
    public static (double R, double G, double B) ScreenMean(int x, int y, int block = 3)
    {
        var half = block / 2;
        var grab = ScreenGrab.Of(x - half, y - half, block, block);
        double r = 0, g = 0, b = 0;
        for (var j = 0; j < block; j++)
            for (var i = 0; i < block; i++)
            {
                var (pr, pg, pb) = grab.At(i, j);
                r += pr; g += pg; b += pb;
            }
        return (r / (block * block), g / (block * block), b / (block * block));
    }

    /// <summary>
    /// Asserts, on the real screen, that every pair's wedge pixel is its body pixel; returns the pairs' (wedge, body) colours for the caller's
    /// further premises. Every one of the four corners must have been measured. With <paramref name="problems"/> a corner whose pairs differ
    /// adds one line (its widest difference) to the list instead of failing at once, so one run reports every theme and backdrop.
    /// </summary>
    public static ((double R, double G, double B) Wedge, (double R, double G, double B) Body)[] AssertOnScreen(Pair[] pairs, string when, int block = 3, List<string>? problems = null, string[]? corners = null)
    {
        foreach (var corner in corners ?? ["top left", "top right", "bottom left", "bottom right"])
            Assert.True(pairs.Any(pair => pair.Corner == corner), $"{when}: no pair measures the {corner} corner");
        var seen = new List<((double, double, double), (double, double, double))>();
        var widest = new Dictionary<string, (double Gap, string Line)>();
        foreach (var pair in pairs.Where(pair => corners is null || corners.Contains(pair.Corner)))
        {
            var wedge = ScreenMean(pair.Wedge.X, pair.Wedge.Y, block);
            var body = ScreenMean(pair.Body.X, pair.Body.Y, block);
            var gap = Math.Max(Math.Abs(wedge.R - body.R), Math.Max(Math.Abs(wedge.G - body.G), Math.Abs(wedge.B - body.B)));
            var line = $"{when}: the {pair.Corner} wedge pixel {pair.Wedge} shows ({wedge.R:F1}, {wedge.G:F1}, {wedge.B:F1}); the body just inside the shape, {pair.Body}, shows ({body.R:F1}, {body.G:F1}, {body.B:F1}); within {Tolerance} on each channel";
            if (!widest.TryGetValue(pair.Corner, out var best) || gap > best.Gap) widest[pair.Corner] = (gap, line);
            seen.Add((wedge, body));
        }
        foreach (var (_, (gap, line)) in widest)
            if (gap > Tolerance)
            {
                if (problems is null) Assert.Fail(line);
                else problems.Add(line);
            }
        return seen.ToArray();
    }
}
