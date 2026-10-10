// PORT-ONLY: OD-0136 amendment 2, step 4 "Progress ring" (ED-NEW-nth-motion). A thin ring with round ends on a --track
// circle, from 12 o'clock clockwise, on the step 3 spring. At 100 % it fills into a solid --strong disc (about 4 % overshoot)
// and a clear --on tick draws itself. Replaces ProgressRing, UpdateRing and VoicePreparationRing; its done disc is also the
// attachment file row's done mark (step 6) and the first-run sheet's done mark (step 15).
using Avalonia;
using SkiaSharp;

namespace Marrow.UI.Port.Nth;

/// <summary>The ring's state: its value spring and, once done, when (clock seconds).</summary>
public sealed record NthRingState(NthSpring Value, double? DoneAt, bool Puck);

/// <summary>Step 4. Size 14, 18 or 20 (24 in the image puck); set <see cref="Value"/> and call <see cref="Finish"/> at 100 %.</summary>
public sealed class NthRing : NthMotionControl
{
    private NthSpring value = NthSpring.At(0);
    private double? doneAt;
    private bool puck;

    /// <summary>The value the ring springs to (0..1).</summary>
    public double Value
    {
        get => value.Target;
        set
        {
            var target = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            if (target == this.value.Target) return;
            this.value = this.value.Retarget(target, Now, Calm);
            Send();
        }
    }

    /// <summary>Show <paramref name="target"/> at once.</summary>
    public void Jump(double target) { value = NthSpring.At(Math.Clamp(target, 0, 1)); Send(); }

    /// <summary>Whether the done disc shows (it animates in when this turns on; <see cref="ShowDone"/> shows it already in place).</summary>
    public bool IsDone
    {
        get => doneAt is not null;
        set { if (value == IsDone) return; doneAt = value ? Now : null; if (value) this.value = this.value.Retarget(1, Now, Calm); Send(); }
    }

    /// <summary>Done, already drawn in (a row that was done before it was shown).</summary>
    public void ShowDone() { value = NthSpring.At(1); doneAt = double.NegativeInfinity; Send(); }

    /// <summary>The white ring of the image puck (step 13): mark #fff on white 30 %.</summary>
    public bool Puck { get => puck; set { puck = value; Send(); } }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsNaN(Width) ? 14 : Width, double.IsNaN(Height) ? 14 : Height);

    protected override object? State => new NthRingState(value, doneAt, puck);

    protected override NthPainter CreatePainter() => new NthRingPainter();
}

/// <summary>Draws step 4: the ring, the done disc and its tick.</summary>
public sealed class NthRingPainter : NthSkiaPainter
{
    private readonly SKPaint paint = new() { IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPath path = new();

    public override bool Moving(NthFrame frame, double now) =>
        frame.State is NthRingState s && (!s.Value.Settled(now) || s.DoneAt is { } d && now - d < NthMotion.RingDoneSeconds);

    // the disc overshoots the ring by about 4 %
    public override Rect Bounds(NthFrame frame) => new Rect(frame.Size).Inflate(1);

    public override void Draw(SKCanvas canvas, NthFrame frame, double now)
    {
        if (frame.State is not NthRingState s) return;
        var size = Math.Min(frame.Size.Width, frame.Size.Height);
        canvas.Save();
        canvas.Translate((float)(frame.Size.Width / 2), (float)(frame.Size.Height / 2));
        DrawCentred(canvas, size, frame.Palette, frame.Calm, s.Value.Value(now), s.DoneAt is { } d ? now - d : null, 1, s.Puck);
        canvas.Restore();
    }

    /// <summary>The ring of <paramref name="size"/> at <paramref name="value"/>, <paramref name="doneSeconds"/> after it was done (null: not done).</summary>
    public void DrawCentred(SKCanvas canvas, double size, NthPalette palette, bool calm, double value, double? doneSeconds, double opacity, bool puck = false)
    {
        if (size <= 0 || opacity <= 0) return;
        var done = doneSeconds is { } seconds ? NthMotion.RingDone(seconds, calm) : new NthDone(1, 0, 0, 0);
        if (done.RingAlpha > 0)
        {
            var stroke = NthMotion.RingStroke(size);
            var radius = (float)NthMotion.RingRadius(size);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = (float)stroke;
            Paint(puck ? NthPalette.PuckTrack : palette.Track, opacity * done.RingAlpha);
            canvas.DrawCircle(0, 0, radius, paint);
            if (value > .002)
            {
                Paint(puck ? NthPalette.PuckMark : palette.Mark, opacity * done.RingAlpha);
                path.Rewind();
                var oval = new SKRect(-radius, -radius, radius, radius);
                if (value >= 1) path.AddCircle(0, 0, radius);
                else path.AddArc(oval, -90, (float)(360 * Math.Min(1, value)));
                canvas.DrawPath(path, paint);
            }
        }
        if (doneSeconds is null || done.DiscAlpha <= 0) return;
        var r = NthMotion.DiscRadius(size);
        paint.Style = SKPaintStyle.Fill;
        Paint(palette.Strong, opacity * done.DiscAlpha);
        canvas.DrawCircle(0, 0, (float)Math.Max(0, r * done.DiscScale), paint);
        if (done.Tick <= 0) return;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = (float)NthMotion.DiscTickStroke(r);
        Paint(palette.On, opacity * done.DiscAlpha);
        NthTicks.Partial(path, NthMotion.DiscTick, (float)r, 0, 0, done.Tick);
        canvas.DrawPath(path, paint);
    }

    private void Paint(SKColor color, double alpha) => paint.Color = color.WithAlpha((byte)Math.Round(color.Alpha * NthEase.Clamp01(alpha)));

    public override void Dispose()
    {
        paint.Dispose(); path.Dispose();
        base.Dispose();
    }
}

/// <summary>Builds a tick (a two-segment polyline) drawn up to a fraction of its length, into a reused path.</summary>
public static class NthTicks
{
    /// <summary><paramref name="points"/> scaled by <paramref name="scale"/> and moved by (dx, dy), drawn to <paramref name="drawn"/> (0..1) of its length.</summary>
    public static void Partial(SKPath path, SKPoint[] points, float scale, float dx, float dy, double drawn)
    {
        path.Rewind();
        if (drawn <= 0) return;
        float x0 = points[0].X * scale + dx, y0 = points[0].Y * scale + dy;
        float x1 = points[1].X * scale + dx, y1 = points[1].Y * scale + dy;
        float x2 = points[2].X * scale + dx, y2 = points[2].Y * scale + dy;
        double l1 = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)), l2 = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        var d = Math.Min(1, drawn) * (l1 + l2);
        path.MoveTo(x0, y0);
        if (d <= l1) { var f = (float)(d / l1); path.LineTo(x0 + (x1 - x0) * f, y0 + (y1 - y0) * f); return; }
        path.LineTo(x1, y1);
        var e = (float)((d - l1) / l2);
        path.LineTo(x1 + (x2 - x1) * e, y1 + (y2 - y1) * e);
    }
}
