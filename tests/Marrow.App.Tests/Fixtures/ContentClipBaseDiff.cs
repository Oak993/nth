using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// REVIEW (unit rec-cornerclip). The unit's base (d128af285) is the same clip code with every `.continuous` Border that has a child marked ContentBeyondSides, so a
/// mounted window is read twice: as it is (HEAD) and with every such Border marked (BASE). Reports the pixels that differ outside every such Border's rectangle (content
/// the exact outline cut beyond a side), those inside (the corners the unit fixes), and the hits lost 2 px outside each side's middle.
/// </summary>
internal static class ContentClipBaseDiff
{
    private static byte[] Pixels(TopLevel window, out int width, out int height)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = HeadlessWindowExtensions.CaptureRenderedFrame(window) ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        width = buffer.Size.Width; height = buffer.Size.Height;
        var bytes = new byte[buffer.RowBytes * height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    private static List<Border> Tracked(Visual root) =>
        root.GetVisualDescendants().OfType<Border>()
            .Where(b => SwiftShapes.GetCornerStyle(b) == RoundedCornerStyle.Continuous && b.Child is not null && !b.ClipToBounds && !SwiftShapes.GetContentBeyondSides(b) && b.Clip is not null && b.IsEffectivelyVisible)
            .ToList();

    internal static string Owner(Visual v)
    {
        for (var x = v; x is not null; x = x.GetVisualParent())
            if (x.GetType().Namespace is { } ns && (ns.StartsWith("Marrow.App", StringComparison.Ordinal) || ns.StartsWith("Marrow.UI.Chrome", StringComparison.Ordinal) || ns.StartsWith("Marrow.UI.Markdown", StringComparison.Ordinal)))
                return x.GetType().Name;
        return "?";
    }

    /// <summary>Returns the report line; <paramref name="pixelsOutside"/> counts the pixels outside every such Border that changed, <paramref name="lostHits"/> the hits lost beyond a side.</summary>
    internal static string Read(Avalonia.Controls.Window window, Visual root, double scale, Action settle, out int pixelsOutside, out int lostHits)
    {
        var tracked = Tracked(root);
        var head = Pixels(window, out var w, out var hgt);
        var probes = tracked.SelectMany(b =>
        {
            var o = b.TranslatePoint(default, window)!.Value; var s = b.Bounds.Size;
            return new[] { new Point(o.X + s.Width / 2, o.Y - 2), new Point(o.X + s.Width / 2, o.Y + s.Height + 2), new Point(o.X - 2, o.Y + s.Height / 2), new Point(o.X + s.Width + 2, o.Y + s.Height / 2) }.Select(p => (P: p, B: b));
        }).ToArray();
        static bool Inside(IInputElement? hit, Border b) => hit is Visual v && v != b && b.IsVisualAncestorOf(v);
        var hitsHead = probes.Select(p => Inside(window.InputHitTest(p.P), p.B)).ToArray();
        var rects = tracked.Select(b => (B: b, R: new Rect(b.TranslatePoint(default, window)!.Value, b.Bounds.Size))).ToList();
        foreach (var b in tracked) b.ContentBeyondSides();
        settle();
        var baseFrame = Pixels(window, out _, out _);
        var hitsBase = probes.Select(p => Inside(window.InputHitTest(p.P), p.B)).ToArray();
        foreach (var b in tracked) b.ContentBeyondSides(false);
        settle();

        int insideCount = 0, insideMax = 0, outsideCount = 0, outsideMax = 0;
        var outsideOwners = new Dictionary<string, (int Count, int Max)>();
        var rowBytes = head.Length / hgt;
        for (var y = 0; y < hgt; y++)
            for (var x = 0; x < w; x++)
            {
                var i = y * rowBytes + x * 4;
                var d = Math.Max(Math.Max(Math.Abs(head[i] - baseFrame[i]), Math.Abs(head[i + 1] - baseFrame[i + 1])), Math.Abs(head[i + 2] - baseFrame[i + 2]));
                if (d <= 2) continue;
                var pt = new Point((x + 0.5) / scale, (y + 0.5) / scale);
                if (rects.Any(r => r.R.Inflate(0.01).Contains(pt))) { insideCount++; insideMax = Math.Max(insideMax, d); continue; }
                outsideCount++; outsideMax = Math.Max(outsideMax, d);
                var near = rects.OrderBy(r => Math.Max(Math.Max(r.R.X - pt.X, pt.X - r.R.Right), Math.Max(r.R.Y - pt.Y, pt.Y - r.R.Bottom))).First();
                var key = Owner(near.B) + $" {near.R.Width:F0}x{near.R.Height:F0}";
                outsideOwners[key] = outsideOwners.TryGetValue(key, out var c) ? (c.Count + 1, Math.Max(c.Max, d)) : (1, d);
            }
        var lost = probes.Where((p, k) => hitsBase[k] && !hitsHead[k]).Select(p => Owner(p.B) + $" at {p.P}").ToList();
        pixelsOutside = outsideCount; lostHits = lost.Count;
        return $"scale {scale}: {tracked.Count} Borders with content cut exactly ({string.Join(", ", tracked.GroupBy(Owner).Select(g => g.Key + " x" + g.Count()))}); "
            + $"inside their rectangles {insideCount} px differ (max {insideMax}); outside {outsideCount} px (max {outsideMax}) {string.Join("; ", outsideOwners.Select(kv => $"{kv.Key}: {kv.Value.Count} px max {kv.Value.Max}"))}; "
            + $"hits lost 2 px outside a side: {lost.Count} {string.Join("; ", lost)}";
    }

    internal static void Report(IEnumerable<string> lines)
    {
        if (Environment.GetEnvironmentVariable("MARROW_REVIEW_REPORT") is { Length: > 0 } path) File.AppendAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }
}
