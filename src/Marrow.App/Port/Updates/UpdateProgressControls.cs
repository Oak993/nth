// PORT-ONLY: first-release feature unit UE-5 (OD-0100 place D). The two small drawings of the update pill: the run's ring and a row's bar. Design: update-everything/src/ue-page.css (.vD .spill .ring, the row bar).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Chrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Updates;

/// <summary>
/// The run's progress ring in the pill: an 18 pt circle, a 10 percent track under an accent arc with a round cap that starts at 12 o'clock and grows clockwise (the design page's ring:
/// r 7.5 of 20, stroke 2.2, scaled to 18 pt). The arc eases to a new fraction (<see cref="Set"/>), OD-0076.
/// </summary>
internal sealed class UpdateRing : Control
{
    public const double Size = 18;
    public const double Stroke = 2;

    private const double Radius = 6.75;
    private readonly IBrush track;
    private readonly IBrush arc;
    private readonly AnimatedScalar fraction;

    public UpdateRing()
    {
        track = Chrome.Overlay(0.10).Brush(this);
        arc = Chrome.Accent.Brush(this);
        IsHitTestVisible = false;
        fraction = new AnimatedScalar(this, 0, _ => InvalidateVisual());
    }

    /// <summary>The fraction drawn now (0 to 1).</summary>
    public double Fraction => fraction.Value;

    /// <summary>The fraction the ring is going to.</summary>
    public double Target { get; private set; }

    public void Set(double target, Animation? animation)
    {
        Target = Math.Clamp(target, 0, 1);
        fraction.Set(Target, animation);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var center = new Point(Size / 2, Size / 2);
        context.DrawEllipse(null, new Pen(track, Stroke), center, Radius, Radius);
        var value = Math.Clamp(fraction.Value, 0, 1);
        if (value <= 0) return;
        var pen = new Pen(arc, Stroke, lineCap: PenLineCap.Round);
        if (value >= 0.9999)
        {
            context.DrawEllipse(null, pen, center, Radius, Radius);
            return;
        }
        var angle = value * 2 * Math.PI;
        var start = new Point(center.X, center.Y - Radius);
        var end = new Point(center.X + Radius * Math.Sin(angle), center.Y - Radius * Math.Cos(angle));
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(start, false);
            path.ArcTo(end, new Size(Radius, Radius), 0, value > 0.5, SweepDirection.Clockwise);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>
/// A row's bar in the popover: a 3 pt capsule track under an accent fill. With a value the fill eases to it; with none (a CLI's own updater reports no progress and the design page says
/// no percentage is invented) a short fill sweeps across the track on the frame clock until it has a value or leaves the screen.
/// </summary>
internal sealed class UpdateProgressBar : Control
{
    public const double TrackHeight = 3;

    /// <summary>One sweep of the indeterminate fill across the track, seconds. UNVERIFIED: the design page's sweep speed is not recorded.</summary>
    public const double SweepSeconds = 1.4;

    private const double SweepShare = 0.3;
    private readonly IBrush wash;
    private readonly IBrush fill;
    private readonly AnimatedScalar value;
    private readonly Action<TimeSpan> frame;
    private IFrameSource? source;
    private bool waiting;
    private double phase;

    public UpdateProgressBar()
    {
        wash = Chrome.Overlay(0.12).Brush(this);
        fill = Chrome.Accent.Brush(this);
        IsHitTestVisible = false;
        value = new AnimatedScalar(this, 0, _ => InvalidateVisual());
        frame = OnFrame;
        AttachedToVisualTree += (_, _) => Start();
        DetachedFromVisualTree += (_, _) => Stop();
    }

    /// <summary>The fill's share of the track drawn now, or the sweep's position while it has no value.</summary>
    public double Shown => Target is null ? phase : value.Value;

    /// <summary>The value the bar is going to; null while the amount is not known.</summary>
    public double? Target { get; private set; }

    public void Set(double? target, Animation? animation)
    {
        Target = target is { } t ? Math.Clamp(t, 0, 1) : null;
        if (Target is { } known) value.Set(known, animation);
        else Request();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, TrackHeight);

    private void Start()
    {
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
        if (source is null || waiting || Target is not null) return;
        try
        {
            source.RequestFrame(frame);
            waiting = true;
        }
        catch (SwiftTrapException)
        {
            source = null; // no frame source bound (a headless test that did not ask for one): the fill stands still
        }
    }

    private void OnFrame(TimeSpan now)
    {
        waiting = false;
        phase = now.TotalSeconds % SweepSeconds / SweepSeconds;
        if (IsEffectivelyVisible) InvalidateVisual();
        Request();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        if (width <= 0) return;
        context.DrawRectangle(wash, null, new RoundedRect(new Rect(0, 0, width, TrackHeight), TrackHeight / 2));
        if (Target is { } known)
        {
            if (known > 0) context.DrawRectangle(fill, null, new RoundedRect(new Rect(0, 0, Math.Max(TrackHeight, width * value.Value), TrackHeight), TrackHeight / 2));
            return;
        }
        // The sweep: a fill SweepShare of the track long, entering from the left edge and leaving by the right.
        var length = width * SweepShare;
        var left = -length + (width + length) * phase;
        var from = Math.Max(0, left);
        var to = Math.Min(width, left + length);
        if (to > from) context.DrawRectangle(fill, null, new RoundedRect(new Rect(from, 0, to - from, TrackHeight), TrackHeight / 2));
    }
}
