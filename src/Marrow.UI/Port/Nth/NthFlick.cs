// PORT-ONLY: OD-0136 amendment 3 (ED-NEW-nth-motion), step "Thread history loading", option B1 "Flick", round 6 of the spec page
// (nth-motion-review.html, STEPS thread-history build notes and its canvas engine SETUP.nline / DRAW.nline). The icon's n drawn
// as one line in the --ftop to --fbot material over a faint --track n: it draws (0 to 1.08 s), its tip flicks off at the top of
// the arch (0.54 s) along a quadratic curve and lands as the bead with one soft settle, a slanted light crosses the n and ends in
// the bead (1.3 to 1.95 s, the bead keeps it 0.4 s), the n empties from where it began (2.1 to 3.18 s) while the bead fades on
// the same ease from the moment the emptying passes the top of the arch, then only the faint n until 3.6 s; loop. The control is
// hidden for its first 0.4 s, then enters (opacity 0.4 s, scale 0.86 to 1 in 0.6 s, .2,.8,.2,1) and its loop starts 0.3 s
// later; on Finish it leaves (opacity 0.22 s .4,0,1,1, scale to 0.9 in 0.3 s .4,0,.2,1). Calm: the full n and the bead, still,
// opacity easing 0.55 to 1 every 2.4 s; enter and leave by opacity only.
using Avalonia;
using SkiaSharp;

namespace Marrow.UI.Port.Nth;

public sealed record NthFlickState(double? FinishedAt);

/// <summary>The thread history loader (B1 "Flick"), 48 px (the spec: 40 in narrow windows, 56 in very large ones).</summary>
public sealed class NthFlick : NthMotionControl
{
    /// <summary>The blank wait before anything shows (a history that arrives sooner shows nothing).</summary>
    public const double Hidden = .4;

    /// <summary>The leave: opacity 0.22 s, scale 0.3 s.</summary>
    public const double LeaveSeconds = .3;

    private double? finishedAt;

    public NthFlick(double size = 48) { Width = size; Height = size; }

    /// <summary>The history is ready: the loader leaves (0.3 s) while the messages rise in.</summary>
    public void Finish() { finishedAt ??= Now; Send(); }

    /// <summary>Whether the loader has been asked to leave.</summary>
    public bool IsFinished => finishedAt is not null;

    /// <summary>Whether anything of the loader shows at <paramref name="now"/> (hidden at first; gone once it has left).</summary>
    public static bool Shows(double started, double? finishedAt, double now) =>
        now - started >= Hidden && (finishedAt is not { } f || f - started >= Hidden && now - f < LeaveSeconds);

    protected override object? State => new NthFlickState(finishedAt);

    protected override NthPainter CreatePainter() => new NthFlickPainter();
}

public sealed class NthFlickPainter : NthSkiaPainter
{
    private static readonly NthCurve Flick = new(.3, .55, .25, 1);
    private readonly SKPaint paint = new() { IsAntialias = true, StrokeCap = SKStrokeCap.Butt, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint light = new() { IsAntialias = true, BlendMode = SKBlendMode.SrcATop };
    private readonly SKPaint layer = new();
    private readonly SKPath n = new(), line = new(), bead = new();
    private SKPathMeasure? measure;
    private SKShader? lineFill, beadFill, band;
    private (double Size, bool Dark) builtFor = (-1, false);
    private double length, stroke, beadRadius, q;
    private SKPoint beadAt, flickFrom, flickVia;

    /// <summary>The cycle, the draw, the hold and the un-draw (spec "Cycle 3.6 s").</summary>
    public const double Cycle = 3.6, DrawSeconds = 1.08, UndrawFrom = 2.1, LoopDelay = .3;

    public override bool Moving(NthFrame frame, double now)
    {
        if (frame.State is not NthFlickState s) return false;
        var finished = s.FinishedAt;
        // before it shows it waits (the first frame after 0.4 s must come), after it has left nothing moves
        return finished is not { } f || now - f < NthFlick.LeaveSeconds && f - frame.Started >= NthFlick.Hidden;
    }

    // the spec's 64 px canvas for 48: 8 px spare on each side so nothing is cut
    public override Rect Bounds(NthFrame frame) => new Rect(frame.Size).Inflate(Math.Min(frame.Size.Width, frame.Size.Height) / 6);

    public override void Draw(SKCanvas canvas, NthFrame frame, double now)
    {
        if (frame.State is not NthFlickState s) return;
        var size = Math.Min(frame.Size.Width, frame.Size.Height);
        if (size <= 0 || !NthFlick.Shows(frame.Started, s.FinishedAt, now)) return;
        var shown = now - frame.Started - NthFlick.Hidden;
        double opacity, scale;
        if (frame.Calm)
        {
            opacity = NthEase.Clamp01(shown / .4);
            scale = 1;
            if (s.FinishedAt is { } f) opacity *= 1 - NthEase.Accelerate.Ease((now - f) / .22);
        }
        else
        {
            opacity = NthEase.Out.Ease(shown / .4);
            scale = .86 + .14 * NthEase.Out.Ease(shown / .6);
            if (s.FinishedAt is { } f)
            {
                opacity *= 1 - NthEase.Accelerate.Ease((now - f) / .22);
                scale *= 1 - .1 * NthEase.Standard.Ease((now - f) / .3);
            }
        }
        if (opacity <= 0) return;
        canvas.Save();
        canvas.Translate((float)(frame.Size.Width / 2), (float)(frame.Size.Height / 2));
        canvas.Scale((float)scale);
        layer.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * NthEase.Clamp01(opacity)));
        // one layer: the light's SrcATop lights only what the loader drew, and the enter/leave opacity applies to all of it
        var r = size / 2 + size / 6;
        canvas.SaveLayer(new SKRect((float)-r, (float)-r, (float)r, (float)r), layer);
        DrawCentred(canvas, size, frame.Palette, frame.Dark, frame.Calm, shown - LoopDelay);
        canvas.Restore();
        canvas.Restore();
    }

    private void Build(double size, NthPalette palette, bool dark)
    {
        if (builtFor == (size, dark)) return;
        builtFor = (size, dark);
        // the canvas engine: S = 48, q = 0.006 S (the icon's 256 units scaled 0.288 at 48), stroke 0.092 S, centred on (128, 128)
        q = size * .006; stroke = size * .092;
        float X(double u) => (float)((u - 128) * q);
        n.Rewind();
        n.MoveTo(X(70), X(196));
        n.LineTo(X(70), X(124));
        n.ArcTo(new SKRect(X(70), X(80), X(158), X(168)), 180, 180, false);
        n.LineTo(X(158), X(196));
        measure?.Dispose();
        measure = new SKPathMeasure(n, false);
        length = measure.Length;
        beadRadius = .6 * stroke;
        beadAt = new SKPoint(X(186), X(74));
        flickFrom = new SKPoint(X(114), X(80));
        flickVia = new SKPoint(X(150), X(66));
        lineFill?.Dispose(); beadFill?.Dispose(); band?.Dispose();
        lineFill = SKShader.CreateLinearGradient(new SKPoint(0, (float)(X(80) - stroke / 2)), new SKPoint(0, X(196)), [palette.FillTop, palette.FillBottom], null, SKShaderTileMode.Clamp);
        beadFill = SKShader.CreateLinearGradient(new SKPoint(0, (float)-beadRadius), new SKPoint(0, (float)beadRadius), [palette.FillTop, palette.FillBottom], null, SKShaderTileMode.Clamp);
        var shine = palette.FillLight;
        band = SKShader.CreateLinearGradient(new SKPoint(-14, 0), new SKPoint(14, 0), [shine.WithAlpha(0), shine, shine.WithAlpha(0)], [0, .5f, 1], SKShaderTileMode.Clamp);
    }

    /// <summary>The n and its bead centred on the canvas origin, <paramref name="seconds"/> into the loop (negative: before it starts).</summary>
    public void DrawCentred(SKCanvas canvas, double size, NthPalette palette, bool dark, bool calm, double seconds)
    {
        Build(size, palette, dark);
        paint.Shader = null; paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = (float)stroke;
        paint.Color = palette.Track;
        canvas.DrawPath(n, paint);
        if (calm)
        {
            var pulse = .55 + .45 * (.5 + .5 * Math.Cos(2 * Math.PI * seconds / 2.4));
            paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * pulse));
            paint.Shader = lineFill;
            canvas.DrawPath(n, paint);
            DrawBead(canvas, beadAt, beadRadius, pulse);
            paint.Shader = null;
            return;
        }
        if (seconds < 0) return;
        var s = seconds % Cycle;
        var head = length * NthEase.InOut.Ease(s / DrawSeconds);
        var tail = length * NthEase.InOut.Ease((s - UndrawFrom) / DrawSeconds);
        if (head - tail > .25)
        {
            line.Rewind();
            measure!.GetSegment((float)tail, (float)head, line, true);
            paint.Color = SKColors.White; paint.Shader = lineFill;
            canvas.DrawPath(line, paint);
            paint.Shader = null;
        }
        // B1: the tip flicks off at the arch top and lands as the bead; the bead fades as the emptying passes the arch top
        var at = beadAt; double r = 0, alpha = 1;
        if (s >= .54 && s < .96)
        {
            var f = Flick.Ease((s - .54) / .42); var w = 1 - f;
            at = new SKPoint((float)(w * w * flickFrom.X + 2 * w * f * flickVia.X + f * f * beadAt.X), (float)(w * w * flickFrom.Y + 2 * w * f * flickVia.Y + f * f * beadAt.Y));
            r = stroke / 2 + (beadRadius - stroke / 2) * f;
        }
        else if (s >= .96 && s < UndrawFrom + DrawSeconds)
        {
            var g = (s - .96) / .3;
            r = beadRadius * (g < 1 ? 1 + .12 * Math.Sin(Math.PI * g) : 1);
            var e = NthEase.Clamp01((tail / length - .5) / .5);
            if (e > 0) { r *= 1 - .2 * e; alpha = 1 - e; }
        }
        if (r > 0 && alpha > 0)
        {
            DrawBead(canvas, at, r, alpha);
            // the bead keeps the light: 55 % of it inside the disc, fading over 0.4 s (.4,0,.2,1)
            var kept = s - 1.59;
            if (kept > 0 && kept < .4)
            {
                paint.Style = SKPaintStyle.Fill; paint.Shader = null;
                paint.Color = palette.FillLight.WithAlpha((byte)Math.Round(palette.FillLight.Alpha * alpha * .55 * (1 - NthEase.Standard.Ease(kept / .4))));
                canvas.DrawCircle(at, (float)r, paint);
            }
        }
        // the light: one band, half width 14, slanted 15 degrees, left to right across the 64 px canvas, SrcATop
        var a = (s - 1.3) / .65;
        if (a > 0 && a < 1)
        {
            var canvasSize = size + size / 3;
            const double slant = .268;
            var x = -14 + (canvasSize + 28 + slant * canvasSize) * NthEase.Glide.Ease(a) - canvasSize / 2;
            canvas.Save();
            canvas.Translate((float)x, (float)(-canvasSize / 2));
            var skew = new SKMatrix(1, (float)-slant, 0, 0, 1, 0, 0, 0, 1);
            canvas.Concat(in skew);
            light.Shader = band;
            canvas.DrawRect(new SKRect(-14, 0, 14, (float)canvasSize), light);
            canvas.Restore();
        }
    }

    private void DrawBead(SKCanvas canvas, SKPoint at, double radius, double alpha)
    {
        paint.Style = SKPaintStyle.Fill;
        paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * NthEase.Clamp01(alpha)));
        bead.Rewind(); bead.AddCircle(0, 0, (float)radius);
        canvas.Save();
        canvas.Translate(at.X, at.Y);
        paint.Shader = beadFill; // the bead's own material, moved by the canvas
        canvas.DrawPath(bead, paint);
        canvas.Restore();
        paint.Shader = null; paint.Style = SKPaintStyle.Stroke;
    }

    public override void Dispose()
    {
        paint.Dispose(); light.Dispose(); layer.Dispose(); n.Dispose(); line.Dispose(); bead.Dispose(); measure?.Dispose();
        lineFill?.Dispose(); beadFill?.Dispose(); band?.Dispose();
        base.Dispose();
    }
}
