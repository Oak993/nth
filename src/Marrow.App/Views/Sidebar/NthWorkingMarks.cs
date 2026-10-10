// PORT-ONLY: OD-0136 amendment 2, step 1 option B (ED-NEW-nth-motion). The working mark of the sidebar's thread rows and
// helper rows (and the helper box of the subagent panel): the nth tile-trace spinner in place of the old 2 x 3 MiniSpinner.
// Hydra keeps MiniSpinner; this file never touches it.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using NthMotion = Marrow.UI.Port.Nth.NthMotion;

namespace Marrow.App.Views.Sidebar;

/// <summary>PORT-ONLY. Builds the working marks (see the file note).</summary>
internal static class NthWorkingMarks
{
    /// <summary>The spinner's size in a row: the smallest step 1 B draws (12 pt).</summary>
    public const double Size = 12;

    /// <summary>The moving mark. It keeps MiniSpinner's accessible name ("Working", PR-THEME-171), so it is a content element.</summary>
    public static NthSpinner Working()
    {
        var spinner = new NthSpinner { Width = Size, Height = Size, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(spinner, "Working");
        AutomationProperties.SetAccessibilityView(spinner, AccessibilityView.Content);
        return spinner;
    }

    /// <summary>The still mark MiniSpinner's isStill drew (a thread row's ghost, carried while it glides or genies as a
    /// picture): the calm trace of step 1 B frozen, with no movement and no composition visual, so it also shows in a snapshot.</summary>
    public static NthStillSpinner Still()
    {
        var spinner = new NthStillSpinner { Width = Size, Height = Size, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(spinner, "Working");
        return spinner;
    }
}

/// <summary>PORT-ONLY. Step 1 B at rest: the tile track in --track with 30 % of it, from 12 o'clock, in --mark (the calm pose of
/// the spinner at full strength). Drawn with ordinary rendering, so a snapshot of the row carries it.</summary>
internal sealed class NthStillSpinner : Control
{
    public NthStillSpinner()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsNaN(Width) ? 16 : Width, double.IsNaN(Height) ? 16 : Height);

    /// <summary>The tile track's corner points for a spinner of <paramref name="size"/>, from 12 o'clock clockwise.</summary>
    internal static Point[] Track(double size)
    {
        var half = NthMotion.TileHalf(size);
        var points = new Point[NthMotion.TilePoints];
        for (var i = 0; i < points.Length; i++)
        {
            var p = NthMotion.TilePoint(half, i);
            points[i] = new Point(p.X, p.Y);
        }
        return points;
    }

    /// <summary>The first <paramref name="share"/> of the closed track's length, as points.</summary>
    internal static List<Point> Lead(Point[] track, double share)
    {
        double total = 0;
        for (var i = 0; i < track.Length; i++) total += Distance(track[i], track[(i + 1) % track.Length]);
        var reach = total * share;
        var line = new List<Point> { track[0] };
        double walked = 0;
        for (var i = 0; i < track.Length && walked < reach; i++)
        {
            var from = track[i];
            var to = track[(i + 1) % track.Length];
            var step = Distance(from, to);
            if (walked + step >= reach)
            {
                var t = step <= 0 ? 0 : (reach - walked) / step;
                line.Add(new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t));
                break;
            }
            walked += step;
            line.Add(to);
        }
        return line;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var palette = NthPalette.For(PlainControls.IsDark(ActualThemeVariant));
        var stroke = NthMotion.SpinnerStroke(size);
        var track = Track(size);
        using var centre = context.PushTransform(Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2));
        var ring = new StreamGeometry();
        using (var g = ring.Open())
        {
            g.BeginFigure(track[0], false);
            for (var i = 1; i < track.Length; i++) g.LineTo(track[i]);
            g.EndFigure(true);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(NthPalette.ToColor(palette.Track)), stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), ring);
        var trace = Lead(track, .3);
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(trace[0], false);
            for (var i = 1; i < trace.Count; i++) g.LineTo(trace[i]);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(NthPalette.ToColor(palette.Mark)), stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
    }
}
