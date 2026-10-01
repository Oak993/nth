// PORT-ONLY. SwiftUI's `ProgressView()` (indeterminate, .controlSize(.small) at the port's sites) is AppKit's spinning
// NSProgressIndicator: the system's own busy indicator. The port's sites build an Avalonia ProgressBar (IsIndeterminate),
// which has no template in this theme-less app and drew nothing (probe 2026-10-01; 8 sites: the write-for-me button,
// the message editor's file check, running tool rows, the downloads popover, root view, wallpaper install, usage
// reset, Hydra reports).
//
// ENGINEERING DECISION (proposal ED-229 (3), E:\Droppy\work\reference-video\ED_PROPOSAL_system_controls.md): the Windows
// counterpart of the system busy indicator is the system's progress ring: an accent arc turning once a second, its
// stroke an eighth of its size (at least 1.5 pt). Recorded deviation: not the macOS spoked spinner. Driven by the
// port's frame clock (ED-117) so tests can step it; with reduce motion, or with no frame source bound, the arc stands
// still. PortControlThemes gives every ProgressBar built without a template this spinner (indeterminate) or a plain
// track-and-fill bar (determinate, for completeness; no site uses one yet).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>PORT-ONLY. The system busy indicator (see the file note).</summary>
public sealed class SystemSpinner : Control
{
    /// <summary>One turn of the arc (seconds).</summary>
    public const double Period = 1.0;

    /// <summary>The arc's sweep (degrees).</summary>
    public const double Sweep = 100;

    private readonly Action<TimeSpan> frame;
    private IFrameSource? source;
    private bool waiting;

    /// <summary>Where the arc starts (degrees clockwise from the top), as last drawn.</summary>
    public double Angle { get; private set; }

    public SystemSpinner()
    {
        frame = OnFrame;
        IsHitTestVisible = false;
        AttachedToVisualTree += (_, _) => Start();
        DetachedFromVisualTree += (_, _) => Stop();
    }

    private void Start()
    {
        if (EnvironmentValues.GetAccessibilityReduceMotion(this)) return;
        source = FrameClock.Source.Current;
        Request();
    }

    private void Stop()
    {
        if (waiting) source?.CancelFrame(frame);
        waiting = false;
        source = null;
    }

    private void Request()
    {
        if (source is null || waiting) return;
        try
        {
            source.RequestFrame(frame);
            waiting = true;
        }
        catch (SwiftTrapException)
        {
            source = null; // no frame source bound (a headless test that did not ask for one): the arc stands still
        }
    }

    private void OnFrame(TimeSpan now)
    {
        waiting = false;
        Angle = now.TotalSeconds % Period / Period * 360;
        InvalidateVisual();
        Request();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var stroke = Math.Max(1.5, size / 8);
        var radius = (size - stroke) / 2;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        Point At(double degrees)
        {
            var radians = (degrees - 90) * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(At(Angle), isFilled: false);
            path.ArcTo(At(Angle + Sweep), new Size(radius, radius), 0, isLargeArc: false, SweepDirection.Clockwise);
            path.EndFigure(isClosed: false);
        }
        var pen = new Pen(Marrow.UI.Chrome.Chrome.Accent.Brush(this), stroke, lineCap: PenLineCap.Round);
        context.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>PORT-ONLY. A determinate ProgressBar's track and fill (see SystemSpinner's file note).</summary>
public sealed class SystemProgressTrack : Control
{
    private readonly RangeBase owner;

    public SystemProgressTrack(RangeBase owner)
    {
        this.owner = owner;
        owner.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty || e.Property == RangeBase.MinimumProperty || e.Property == RangeBase.MaximumProperty) InvalidateVisual();
        };
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var height = Math.Min(Bounds.Height, 4);
        var top = (Bounds.Height - height) / 2;
        var span = owner.Maximum - owner.Minimum;
        var fraction = span <= 0 ? 0 : Math.Clamp((owner.Value - owner.Minimum) / span, 0, 1);
        context.DrawRectangle(Marrow.UI.Chrome.Chrome.Overlay(0.12).Brush(this), null, new RoundedRect(new Rect(0, top, Bounds.Width, height), height / 2));
        if (fraction > 0)
            context.DrawRectangle(Marrow.UI.Chrome.Chrome.Accent.Brush(this), null, new RoundedRect(new Rect(0, top, Bounds.Width * fraction, height), height / 2));
    }
}
