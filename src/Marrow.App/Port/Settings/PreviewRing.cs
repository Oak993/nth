// PORT-ONLY CZ-3 (customization-SPEC CZ-4.3; ComposerView.swift:692-773 ContextMeter ring; playground logic.js:309-312): the preview composer's context ring. Drawn as the real
// ring is (a 15 point circle, the track in the quaternary colour, the arc in the accent, both 2.5 wide, the arc from the top, inside the button's 6 and 5 point padding).
// While a turn runs the arc spins at 330 degrees a second, eased in and out with a time constant of 0.35 s; it asks for frames only while it moves.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class PreviewRing : Decorator
{
    internal const double DegreesPerSecond = 330;
    internal const double TimeConstant = 0.35;
    /// <summary>The arc, as the playground draws it: 31 of the circle's 44 at rest and typing, 10.5 while running (logic.js:618).</summary>
    internal const double RestFraction = 31.0 / 44, RunningFraction = 10.5 / 44;

    private readonly Avalonia.Controls.Shapes.Path arc;
    private readonly RotateTransform spin = new();
    private readonly CzNumber fraction;
    private readonly Action<TimeSpan> frame;
    private bool running;
    private double speed;      // 0..1, eased toward the run flag
    private double angle;
    private ContinuousInstant last;
    private IFrameSource? frames;
    private bool waiting;

    /// <summary>The spin so far, in degrees, 0 up to 360.</summary>
    internal double Angle => angle;

    /// <summary>The arc's share of the circle now drawn.</summary>
    internal double Fraction => fraction.Value;

    internal PreviewRing()
    {
        IsHitTestVisible = false;
        var ring = new Grid { Width = 15, Height = 15 }; // :717,725
        var track = new Avalonia.Controls.Shapes.Path { StrokeThickness = 2.5, Data = new EllipseGeometry(new Rect(0, 0, 15, 15)) };
        track.Stroke = SwiftColor.Quaternary.Brush(track);
        arc = new Avalonia.Controls.Shapes.Path { StrokeThickness = 2.5, StrokeLineCap = PenLineCap.Round, RenderTransformOrigin = RelativePoint.Center };
        arc.RenderTransform = new TransformGroup { Children = { new RotateTransform(-90), spin } };
        arc.Stroke = (Popovers.GetTint(this) ?? SwiftColor.AccentColor).Brush(arc);
        ring.Children.Add(track);
        ring.Children.Add(arc);
        Child = new Border { Padding = new Thickness(6, 5), Child = ring }; // :726-728 the padded hit area
        fraction = new CzNumber(new CzMotion(this), RestFraction, f => arc.Data = Arc(f));
        arc.Data = Arc(RestFraction);
        frame = Tick;
        DetachedFromVisualTree += (_, _) => Stop();
        AttachedToVisualTree += (_, _) => Wake();
    }

    /// <summary>CZ-4.3: Running spins the ring; anything else lets it run down.</summary>
    internal void Run(bool on)
    {
        if (running == on) return;
        running = on;
        fraction.To(on ? RunningFraction : RestFraction, CzMotion.Ease(.6)); // the playground's stroke-dasharray transition: .6s var(--ez2)
        last = SwiftClock.Now;
        Wake();
    }

    private void Wake()
    {
        if (waiting || !IsAttachedToVisualTree() || (!running && speed <= 0)) return;
        frames = FrameClock.Source.Current;
        waiting = true;
        frames.RequestFrame(frame);
    }

    private void Stop()
    {
        if (waiting) frames?.CancelFrame(frame);
        waiting = false;
    }

    private void Tick(TimeSpan _)
    {
        waiting = false;
        var now = SwiftClock.Now;
        var dt = Math.Max(0, (now - last).TotalSeconds);
        last = now;
        speed += ((running ? 1 : 0) - speed) * (1 - Math.Exp(-dt / TimeConstant));
        if (!running && speed < 0.001) speed = 0;
        angle = (angle + dt * DegreesPerSecond * speed) % 360;
        spin.Angle = angle;
        Wake();
    }

    // :720-723 A circle trimmed from zero, before its -90 degree rotation.
    private static Geometry Arc(double share)
    {
        if (share >= 1) return new EllipseGeometry(new Rect(0, 0, 15, 15));
        var geometry = new StreamGeometry();
        if (share <= 0) return geometry;
        using var context = geometry.Open();
        context.BeginFigure(new Point(15, 7.5), false);
        var turn = share * 2 * Math.PI;
        context.ArcTo(new Point(7.5 + 7.5 * Math.Cos(turn), 7.5 + 7.5 * Math.Sin(turn)), new Size(7.5, 7.5), 0, share > 0.5, SweepDirection.Clockwise);
        context.EndFigure(false);
        return geometry;
    }

    private bool IsAttachedToVisualTree() => TopLevel.GetTopLevel(this) is not null;
}
