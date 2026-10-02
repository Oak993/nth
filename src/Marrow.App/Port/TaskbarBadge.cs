// PORT-ONLY (ED-219): the Windows twin of the Dock badge in AppModel.swift:1270-1273,
//   NSApp.dockTile.badgeLabel = unread > 0 ? String(unread) : nil.
// The badge becomes the main window's taskbar overlay icon (ITaskbarList3::SetOverlayIcon): the count in white on a filled
// red circle, NSColor.systemRed's light value (the Dock badge's own colours, AppTheme.System's Danger, 255,59,48), drawn here
// in code at the system small-icon size for the window's DPI. Above 99 the text stays the decimal count, shrunk to fit (the
// source's String(unread); no "99+" is invented). Zero clears the overlay. The accessible description is the count as text.
// The COM call is behind ITaskbarOverlay: Windows binds WindowsTaskbarOverlay (WindowsTaskbarOverlay.cs) at start-up; a test
// binds a recorder through TaskbarBadge.Overlay, and the process default draws nothing and calls nothing, so a test that
// forgets to bind one can never reach the real taskbar. The drawing is a small rasteriser with its own stroke digits so that
// it needs no Avalonia platform, no font and no window: the model calls it from the main actor in a headless run too.
using System.Globalization;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Port;

/// <summary>The badge as the taskbar is asked to show it: the count, its text, and the pixels (straight alpha, BGRA, top row first).</summary>
public sealed record TaskbarOverlayIcon(long Count, string Description, int Size, byte[] Bgra);

/// <summary>The taskbar button's overlay icon surface. A null icon clears the overlay.</summary>
public interface ITaskbarOverlay
{
    /// <summary>The system small-icon size (SM_CXSMICON) at the main window's current DPI, in pixels.</summary>
    int SmallIconSize { get; }

    void SetOverlay(TaskbarOverlayIcon? icon);
}

/// <summary>The process default: no taskbar. Only the Windows start-up binds a real overlay.</summary>
internal sealed class InertTaskbarOverlay : ITaskbarOverlay
{
    public static readonly InertTaskbarOverlay Instance = new();
    private InertTaskbarOverlay() { }
    public int SmallIconSize => 16;
    public void SetOverlay(TaskbarOverlayIcon? icon) { }
}

/// <summary>PORT-ONLY. NSApp.dockTile.badgeLabel as a taskbar overlay icon.</summary>
public static class TaskbarBadge
{
    /// <summary>R-LIFE-2: the taskbar is a process-wide seam; a test replaces it for its own flow.</summary>
    public static Ambient<ITaskbarOverlay> Overlay { get; } = new(InertTaskbarOverlay.Instance);

    /// <summary>AppModel.swift:1270-1273: <c>unread &gt; 0 ? String(unread) : nil</c>.</summary>
    public static void Show(long unread)
    {
        var overlay = Overlay.Current;
        overlay.SetOverlay(unread > 0 ? Render(unread, overlay.SmallIconSize) : null);
    }

    /// <summary>Swift's <c>String(unread)</c>: the decimal count, never capped.</summary>
    public static string Text(long unread) => unread.ToString(CultureInfo.InvariantCulture);

    // Glyph cells are 4 by 6 units, a gap of 1.4 between digits, strokes 1.1 units wide with round ends.
    private const double GlyphWidth = 4;
    private const double GlyphHeight = 6;
    private const double GlyphGap = 1.4;
    private const double StrokeWidth = 1.1;
    private const int Samples = 4; // per axis, per pixel

    // NSColor.systemRed at its light value, from the colour the port already keeps for it (AppTheme.System's Danger).
    private static readonly Avalonia.Media.Color Red = AppTheme.System.Spec().Danger.Resolve(ThemeVariant.Light);

    /// <summary>The badge for a count: a red disc filling the square, the count's digits in white, centred and shrunk to fit.</summary>
    public static TaskbarOverlayIcon Render(long count, int size)
    {
        var text = Text(count);
        var segments = TextSegments(text, size, out var strokeRadius);
        var pixels = new byte[size * size * 4];
        var radius = size / 2.0;
        var radiusSquared = radius * radius;
        var reach = strokeRadius * strokeRadius;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var disc = 0;
                var ink = 0;
                for (var j = 0; j < Samples; j++)
                {
                    for (var i = 0; i < Samples; i++)
                    {
                        var sx = x + (i + 0.5) / Samples;
                        var sy = y + (j + 0.5) / Samples;
                        var dx = sx - radius;
                        var dy = sy - radius;
                        if (dx * dx + dy * dy > radiusSquared) continue;
                        disc++;
                        if (Touches(segments, sx, sy, reach)) ink++;
                    }
                }
                if (disc == 0) continue;
                var white = (double)ink / disc;
                var offset = (y * size + x) * 4;
                pixels[offset] = Mix(Red.B, white);
                pixels[offset + 1] = Mix(Red.G, white);
                pixels[offset + 2] = Mix(Red.R, white);
                pixels[offset + 3] = (byte)Math.Round(255.0 * disc / (Samples * Samples));
            }
        }
        return new TaskbarOverlayIcon(count, text, size, pixels);
    }

    private static byte Mix(byte red, double white) => (byte)Math.Round(red + (255 - red) * white);

    // The digits as pixel-space segments, centred in the square. The scale is the smaller of two limits: the text may fill
    // 62% of the height, and its bounding box's diagonal 86% of the diameter, so a long count gets smaller instead of
    // leaving the disc.
    private static (double X0, double Y0, double X1, double Y1)[] TextSegments(string text, int size, out double strokeRadius)
    {
        var width = text.Length * GlyphWidth + (text.Length - 1) * GlyphGap;
        var boxWidth = width + StrokeWidth;
        var boxHeight = GlyphHeight + StrokeWidth;
        var scale = Math.Min(0.62 * size / boxHeight, 0.86 * size / Math.Sqrt(boxWidth * boxWidth + boxHeight * boxHeight));
        strokeRadius = StrokeWidth * scale / 2;
        var left = size / 2.0 - width * scale / 2;
        var top = size / 2.0 - GlyphHeight * scale / 2;
        var segments = new List<(double, double, double, double)>();
        for (var index = 0; index < text.Length; index++)
        {
            var originX = left + index * (GlyphWidth + GlyphGap) * scale;
            foreach (var stroke in Glyphs[text[index] - '0'])
            {
                for (var point = 1; point < stroke.Length; point++)
                {
                    segments.Add((
                        originX + stroke[point - 1].X * scale, top + stroke[point - 1].Y * scale,
                        originX + stroke[point].X * scale, top + stroke[point].Y * scale));
                }
            }
        }
        return segments.ToArray();
    }

    private static bool Touches((double X0, double Y0, double X1, double Y1)[] segments, double x, double y, double reachSquared)
    {
        foreach (var (x0, y0, x1, y1) in segments)
        {
            var vx = x1 - x0;
            var vy = y1 - y0;
            var length = vx * vx + vy * vy;
            var t = length == 0 ? 0 : Math.Clamp(((x - x0) * vx + (y - y0) * vy) / length, 0, 1);
            var dx = x - (x0 + t * vx);
            var dy = y - (y0 + t * vy);
            if (dx * dx + dy * dy <= reachSquared) return true;
        }
        return false;
    }

    // Each digit as polylines in a 4 by 6 cell, y down. An arc runs its angles clockwise on screen: 180 is left, 270 top,
    // 360 right, 450 bottom.
    private static readonly (double X, double Y)[][][] Glyphs =
    [
        /* 0 */ [Arc(2, 3, 2, 3, 0, 360)],
        /* 1 */ [[(0.4, 1.5), (2, 0), (2, 6)]],
        /* 2 */ [Arc(2, 1.8, 2, 1.8, 180, 380).Concat([(0, 6), (4, 6)]).ToArray()],
        /* 3 */ [Arc(2, 1.5, 1.9, 1.5, 200, 450).Concat(Arc(2, 4.5, 2, 1.5, 270, 520)).ToArray()],
        /* 4 */ [[(3.2, 6), (3.2, 0), (0, 4.4), (4, 4.4)]],
        /* 5 */ [[(3.6, 0), (0.6, 0), (0.5, 2.6)], Arc(2, 4, 2, 2, 240, 500)],
        /* 6 */ [Arc(2, 4, 2, 2, 0, 360), [(0.1, 4), (0.6, 1.4), (3.2, 0)]],
        /* 7 */ [[(0, 0), (4, 0), (1.4, 6)]],
        /* 8 */ [Arc(2, 1.5, 1.7, 1.5, 0, 360), Arc(2, 4.5, 2, 1.5, 0, 360)],
        /* 9 */ [Arc(2, 2, 2, 2, 0, 360), [(3.9, 2), (3.4, 4.6), (0.8, 6)]],
    ];

    private static (double X, double Y)[] Arc(double cx, double cy, double rx, double ry, double from, double to)
    {
        var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(to - from) / 20));
        var points = new (double, double)[steps + 1];
        for (var step = 0; step <= steps; step++)
        {
            var angle = (from + (to - from) * step / steps) * Math.PI / 180;
            points[step] = (cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle));
        }
        return points;
    }
}
