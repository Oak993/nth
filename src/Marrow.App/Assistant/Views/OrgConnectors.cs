// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 9.4 "joined by thin connectors"; the canvas's .org svg.lines path (1.2 px, the second hairline), work/design/assistant/index.html lines
// 421-425 and 2876-2886; ED-NEW-dextra-as17-1). No Swift twin.
// The thin lines between Dextra's node and the Asturs', drawn from the layout's plan: each figure a polyline whose bends are rounded (8 px, less where a leg is short). A
// drawing only: it takes no pointer, no focus and no part in the accessibility tree, and it never lays anything out again. When the team changes the lines wait for the
// nodes to land and then come in (a fade, 260 ms), so they never join nodes that are still on their way; with Reduce motion they are simply there.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public sealed class OrgConnectors : Control
{
    public const double Thickness = 1.2;

    private readonly IPen pen;
    private OrgPlan? plan;
    private Geometry? geometry;
    private OrgPlan? pendingPlan;
    private bool settling;
    private long settleSerial;

    public OrgConnectors()
    {
        IsHitTestVisible = false;
        Focusable = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        var brush = TaskLook.Line2.Brush(this);
        pen = new Pen(brush, Thickness, lineCap: PenLineCap.Flat, lineJoin: PenLineJoin.Round);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The plan being drawn, in the chart's own coordinates.</summary>
    public OrgPlan? Plan => plan;

    /// <summary>How many figures are drawn (tests).</summary>
    public int Figures => plan?.Connectors.Count ?? 0;

    public void Show(OrgPlan next)
    {
        if (settling) { pendingPlan = next; return; }
        if (ReferenceEquals(plan, next)) return;
        plan = next;
        geometry = Build(next);
        InvalidateVisual();
    }

    private ITimer? coming;

    /// <summary>Takes the lines out and, after the nodes' move, brings them back in (a fade); at once when <paramref name="still"/>. The timer runs on SwiftClock's provider.</summary>
    public void Settle(bool still, TimeSpan wait)
    {
        coming?.Dispose();
        coming = null;
        var serial = ++settleSerial;
        if (still)
        {
            settling = false;
            FlushPlan();
            Transitions = null;
            Opacity = 1;
            return;
        }
        settling = true;
        // Keep a running transition on interruptions so its current opacity is the new start.
        Transitions ??= [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = new Avalonia.Animation.Easings.SplineEasing(.32, .72, 0, 1) }];
        Opacity = 0;
        coming = SwiftClock.Provider.Current.CreateTimer(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (serial != settleSerial) return;
            coming?.Dispose();
            coming = null;
            settling = false;
            FlushPlan();
            Opacity = 1;
        }), null, wait, Timeout.InfiniteTimeSpan);
    }

    private void FlushPlan()
    {
        var next = pendingPlan;
        pendingPlan = null;
        if (next is not null) Show(next);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        coming?.Dispose();
        coming = null;
        ++settleSerial;
        settling = false;
        FlushPlan();
        Transitions = null;
        Opacity = 1;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // the plan is laid out left to right; in a right-to-left window the whole tree is mirrored, these lines with it
        if (geometry is not null) context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>The path of a plan: every figure a polyline with its bends rounded.</summary>
    public static Geometry Build(OrgPlan plan)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        foreach (var line in plan.Connectors)
        {
            var pts = line.Points;
            if (pts.Count < 2) continue;
            ctx.BeginFigure(new Point(pts[0].X, pts[0].Y), false);
            for (var i = 1; i < pts.Count - 1; i++)
            {
                OrgPoint before = pts[i - 1], bend = pts[i], after = pts[i + 1];
                var r = OrgChartLayout.BendRadius(before, bend, after);
                double inX = Math.Sign(bend.X - before.X), inY = Math.Sign(bend.Y - before.Y), outX = Math.Sign(after.X - bend.X), outY = Math.Sign(after.Y - bend.Y);
                var straight = inX == outX && inY == outY;
                if (r <= .01 || straight)
                {
                    ctx.LineTo(new Point(bend.X, bend.Y));
                    continue;
                }
                ctx.LineTo(new Point(bend.X - inX * r, bend.Y - inY * r));
                // a left turn when the cross product is negative (y points down)
                var cross = inX * outY - inY * outX;
                ctx.ArcTo(new Point(bend.X + outX * r, bend.Y + outY * r), new Size(r, r), 0, false, cross > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
            }
            ctx.LineTo(new Point(pts[^1].X, pts[^1].Y));
            ctx.EndFigure(false);
        }
        return geometry;
    }
}
