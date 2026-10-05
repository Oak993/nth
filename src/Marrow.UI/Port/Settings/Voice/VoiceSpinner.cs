// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): the thin spinning ring of the hardware card (DESIGN gen.py:410-414 .czspin).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;

namespace Marrow.UI.Port.Settings;

/// <summary>
/// The design's `.czspin`: a ring of 1.6 px in the text colour at 25 %, with a quarter of it (the top border) in the full colour, turning once in 0.8 s, linear. Driven by the
/// frame clock (ED-117) so a test steps it; with no frame source bound it stands still. Like the system spinner it keeps turning under Reduce Motion: it says that work is going on.
/// </summary>
public sealed class VoiceSpinner : Control
{
    /// <summary>One turn (seconds), DESIGN gen.py:411 `animation: czspin .8s linear infinite`.</summary>
    public const double Period = 0.8;

    private const double Stroke = 1.6;
    private readonly Action<TimeSpan> frame;
    private readonly double size;
    private IFrameSource? source;
    private bool waiting;

    /// <summary>The ring's turn now, in degrees, as last drawn.</summary>
    public double Angle { get; private set; }

    /// <param name="size">The ring's diameter in points (the design's 11 in a button, 12 in a probe row).</param>
    public VoiceSpinner(double size = 11)
    {
        this.size = size;
        frame = OnFrame;
        IsHitTestVisible = false;
        AttachedToVisualTree += (_, _) => Start();
        DetachedFromVisualTree += (_, _) => Stop();
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(size, size);

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
        if (source is null || waiting) return;
        try
        {
            source.RequestFrame(frame);
            waiting = true;
        }
        catch (SwiftTrapException)
        {
            source = null; // no frame source bound (a headless test that did not ask for one): the ring stands still
        }
    }

    private void OnFrame(TimeSpan now)
    {
        waiting = false;
        Angle = now.TotalSeconds % Period / Period * 360;
        if (IsEffectivelyVisible && Opacity > 0) InvalidateVisual(); // a hidden ring keeps its phase without repainting
        Request();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var colour = ((SolidColorBrush)CzTheme.Text(this)).Color;
        var radius = (size - Stroke) / 2;
        var centre = new Point(size / 2, size / 2);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(colour.A * .25), colour.R, colour.G, colour.B)), Stroke), centre, radius, radius);
        // The top border of the box is the quarter of the ring from -45 to +45 degrees round 12 o'clock.
        var start = Math.PI * (-135 + Angle) / 180;
        var end = Math.PI * (-45 + Angle) / 180;
        var arc = new StreamGeometry();
        using (var path = arc.Open())
        {
            path.BeginFigure(new Point(centre.X + radius * Math.Cos(start), centre.Y + radius * Math.Sin(start)), false);
            path.ArcTo(new Point(centre.X + radius * Math.Cos(end), centre.Y + radius * Math.Sin(end)), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(colour), Stroke, lineCap: PenLineCap.Flat), arc);
    }
}
