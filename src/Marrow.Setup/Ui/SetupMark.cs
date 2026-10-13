using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Marrow.Setup.Motion;
using Marrow.Setup.Theme;
using SkiaSharp;

namespace Marrow.Setup.Ui;

/// <summary>One pose of the installer's mark (the canvas's <c>.mark</c> and its keyframes).</summary>
/// <param name="TileOpacity">The tile's opacity (<c>mFade</c>).</param>
/// <param name="TileScale">The tile's scale about its centre (<c>mPop</c>, 0.86 to 1).</param>
/// <param name="LiftOpacity">The lift shadow's opacity (<c>mLift</c>, 0.15 to 1).</param>
/// <param name="LiftY">The lift shadow's offset in DIP (−7 to 0).</param>
/// <param name="LiftScale">The lift shadow's scale (0.86 to 1).</param>
/// <param name="Draw">The drawn share of the n (stroke-dashoffset 100 to 0).</param>
/// <param name="BeadOpacity">The bead's opacity.</param>
/// <param name="BeadScale">The bead's scale about its centre.</param>
/// <param name="DriftX">The silver's drift as a share of the drift layer's size (−0.07 to 0.08).</param>
/// <param name="DriftY">(−0.06 to 0.07).</param>
public readonly record struct MarkPose(double TileOpacity, double TileScale, double LiftOpacity, double LiftY, double LiftScale, double Draw, double BeadOpacity, double BeadScale, double DriftX, double DriftY)
{
    /// <summary>The finished mark, its silver at rest.</summary>
    public static MarkPose Shown => new(1, 1, 1, 0, 1, 1, 1, 1, 0, 0);

    /// <summary>Before the pop-in: nothing drawn.</summary>
    public static MarkPose Hidden => new(0, .86, 0, -7, .86, 0, 0, 0, 0, 0);
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, "Motion", and 2, override 9; OD-0136's locked mark B as the canvas draws it; unit U2,
/// ED-NEW-inst-u2-1). The nth tile at a screen's size (<c>--ms</c>): the dark tile with its silver drift layer, the pool
/// and the highlight, a 1 DIP inner rim, the n and its bead in their own gradients under one soft drop shadow, and the
/// lift shadow under the tile. The boot pop-in without the shine sweep, once per window on its first screen and again on
/// Ready: the tile fades in (0.26 s) and grows from 0.86 on a spring (response 0.62 s, damping 0.72) while its shadow
/// lifts 7 DIP with it; the n draws from 0.30 s to 1.04 s on <c>cubic-bezier(.65,0,.35,1)</c>; the bead pops at 0.98 s
/// (fade 0.12 s, spring 0.42 s, 0.55). The silver drifts ±7 % over 16 s (ease-in-out, alternating) on Welcome and Ready
/// only; every other mark is still. Calm: everything fades in 0.2 s, nothing grows or draws, no drift. A dimmed mark
/// (Problem) is desaturated and darkened. A contrast theme drops the lift shadow. To Narrator it is an image named "nth".
/// </summary>
public sealed class SetupMark : Control, IMotion
{
    public const double PopDuration = 1.4, DriftPeriod = 16;

    private static readonly CubicBezier DrawCurve = new(.65, 0, .35, 1);
    private static readonly CubicBezier DriftCurve = new(.42, 0, .58, 1);
    private readonly Func<bool> calm;
    private ScreenTokens tokens = ScreenTokens.Dark;
    private Animator? animator;
    private double popAt = -1;
    private double driftAt = -1;
    private double driftStart;

    public SetupMark(double size, Func<bool> calm, bool drifts = false)
    {
        this.calm = calm ?? throw new ArgumentNullException(nameof(calm));
        Side = size;
        Width = size;
        Height = size;
        Drifts = drifts;
        IsHitTestVisible = false;
        AutomationProperties.SetName(this, "nth");
        AutomationProperties.SetControlTypeOverride(this, Avalonia.Automation.Peers.AutomationControlType.Image);
        ScreenTokens.Watch(this, value =>
        {
            tokens = value;
            InvalidateVisual();
        });
    }

    public double Side { get; }

    /// <summary>The silver drifts (Welcome and Ready).</summary>
    public bool Drifts { get; }

    /// <summary>Problem's mark: desaturated (0.6) and darker (0.86).</summary>
    public bool Dim { get; set; }

    /// <summary>The pose drawn now.</summary>
    public MarkPose Pose { get; private set; } = MarkPose.Shown;

    /// <summary>Seconds since the pop-in began, or −1 when it is not playing.</summary>
    public double PopElapsed => popAt;

    /// <summary>True while the silver drifts.</summary>
    public bool IsDrifting => driftAt >= 0;

    public void Attach(Animator frames) => animator = frames;

    private bool held;

    /// <summary>Stops the mark's motion where it is (its screen left); <see cref="Resume"/> carries on from there.</summary>
    public void Pause() => held = true;

    /// <summary>Carries on after <see cref="Pause"/> (Welcome again after Options: no replay, the drift goes on).</summary>
    public void Resume()
    {
        if (!held) return;
        held = false;
        if (popAt >= 0 || driftAt >= 0 || pendingDrift > 0) animator?.Start(this);
    }

    /// <summary>Shows the finished mark at once, still (or drifting from the start of its path, <paramref name="drift"/>).</summary>
    public void Show(bool drift = false)
    {
        held = false;
        popAt = -1;
        driftAt = drift && Drifts && !calm() ? 0 : -1;
        driftStart = 0;
        Draw();
        if (driftAt >= 0) animator?.Start(this);
    }

    /// <summary>
    /// Plays the pop-in. With <paramref name="driftAfter"/> null the silver drifts from the start of its path at once (the
    /// canvas's Welcome); with a delay it holds still until then and starts mid-path, where its resting place lies, so it
    /// never jumps (Ready's moment: 3.1 s).
    /// </summary>
    public void Pop(double? driftAfter = null)
    {
        held = false;
        popAt = 0;
        driftAt = -1;
        driftStart = 0;
        pendingDrift = Drifts && !calm() ? driftAfter ?? 0 : -1;
        if (pendingDrift == 0)
        {
            driftAt = 0;
            pendingDrift = -1;
        }
        Draw();
        if (animator is null)
        {
            popAt = -1;
            Draw();
            return;
        }
        animator.Start(this);
    }

    private double pendingDrift = -1;

    /// <summary>Starts the drift now from the place where the still silver lies (mid-path), so it never jumps.</summary>
    public void StartDriftMidPath()
    {
        if (!Drifts || calm()) return;
        driftAt = 0;
        driftStart = MidPath;
        animator?.Start(this);
    }

    /// <summary>The share of the drift's 16 s path where the silver is at rest (translate 0): the canvas's −7.65 s.</summary>
    public static double MidPath => 7.65;

    bool IMotion.Advance(double seconds)
    {
        if (held) return false;
        var moving = false;
        if (popAt >= 0)
        {
            popAt += seconds;
            if (pendingDrift > 0 && popAt >= pendingDrift)
            {
                pendingDrift = -1;
                driftAt = 0;
                driftStart = MidPath;
            }
            if (popAt >= (calm() ? 0.2 : PopDuration) && pendingDrift < 0) popAt = -1;
            else moving = true;
        }
        if (driftAt >= 0)
        {
            driftAt += seconds;
            moving = true;
        }
        Draw();
        return moving;
    }

    /// <summary>The pose at <paramref name="t"/> seconds into the pop-in.</summary>
    public static MarkPose PopPose(double t, bool calm)
    {
        if (calm)
        {
            var fade = Math.Clamp(t / 0.2, 0, 1);
            return new MarkPose(fade, 1, fade, 0, 1, 1, fade, 1, 0, 0);
        }
        var tile = SpringStep(t, 0.62, 0.72);
        var lift = SpringStep(t / 1.15, 0.62, 0.72);
        var draw = DrawCurve.Evaluate(Math.Clamp((t - 0.30) / 0.74, 0, 1));
        var beadT = t - 0.98;
        var beadOpacity = Math.Clamp(beadT / 0.12, 0, 1);
        var beadScale = beadT <= 0 ? 0 : SpringStep(beadT, 0.42, 0.55);
        return new MarkPose(
            Math.Clamp(t / 0.26, 0, 1), .86 + .14 * tile,
            Math.Clamp(.15 + .85 * lift, 0, 1), -7 * (1 - lift), .86 + .14 * lift,
            draw, beadOpacity, beadScale, 0, 0);
    }

    /// <summary>The silver's offset (share of the drift layer) <paramref name="seconds"/> into its alternating 16 s path.</summary>
    public static (double X, double Y) DriftAt(double seconds)
    {
        var cycle = seconds % (2 * DriftPeriod);
        var p = cycle <= DriftPeriod ? cycle / DriftPeriod : 2 - cycle / DriftPeriod;
        var e = DriftCurve.Evaluate(p);
        return (-.07 + .15 * e, -.06 + .13 * e);
    }

    /// <summary>A spring's step response from rest (0 to 1), the canvas's <c>linear()</c> curves.</summary>
    public static double SpringStep(double t, double response, double damping)
    {
        if (t <= 0) return 0;
        var w0 = 2 * Math.PI / response;
        if (damping >= 1) return 1 - Math.Exp(-w0 * t) * (1 + w0 * t);
        var wd = w0 * Math.Sqrt(1 - damping * damping);
        var a = damping * w0;
        return 1 - Math.Exp(-a * t) * (Math.Cos(wd * t) + a / wd * Math.Sin(wd * t));
    }

    private void Draw()
    {
        var pose = popAt >= 0 ? PopPose(popAt, calm()) : MarkPose.Shown;
        if (driftAt >= 0)
        {
            var (x, y) = DriftAt(driftStart + driftAt);
            pose = pose with { DriftX = x, DriftY = y };
        }
        Pose = pose;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context) =>
        context.Custom(new MarkOperation(new Rect(-Side * .5, -Side * .5, Side * 2, Side * 2), Side, Pose, Dim, tokens.Contrast ? [] : tokens.MarkLift));

    private sealed class MarkOperation(Rect bounds, double side, MarkPose pose, bool dim, ShadowLayer[] lift) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point point) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var opacity = Math.Clamp(lease.CurrentOpacity, 0, 1);
            if (opacity <= 0) return;
            var canvas = lease.SkCanvas;
            var saved = canvas.Save();
            try
            {
                if (opacity < 1)
                {
                    using var fade = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * opacity)) };
                    canvas.SaveLayer(fade);
                }
                MarkPainter.Paint(canvas, (float)side, pose, dim, lift);
            }
            finally
            {
                canvas.RestoreToCount(saved);
            }
        }
    }
}

/// <summary>Draws the installer's mark with SkiaSharp, in DIP, the tile's top left at the origin (the canvas's <c>MARK</c> markup).</summary>
public static class MarkPainter
{
    private static readonly SKColor G800 = new(0x14, 0x16, 0x1c), G600 = new(0x3b, 0x41, 0x4f), S400 = new(0x9a, 0xa3, 0xb5), S300 = new(0xb9, 0xc0, 0xcd);
    private static readonly SKColor S200 = new(0xd6, 0xda, 0xe2), S100 = new(0xe8, 0xeb, 0xf0), White = new(0xff, 0xff, 0xff);

    public static void Paint(SKCanvas canvas, float s, MarkPose pose, bool dim, IReadOnlyList<ShadowLayer> lift)
    {
        using var paint = new SKPaint { IsAntialias = true };
        // the lift: inset 6 % 8 % 0, radius 24 %, its shadows outside it only
        if (pose.LiftOpacity > 0.002 && lift.Count > 0)
        {
            var box = new SKRect(.08f * s, .06f * s, .92f * s, s);
            var saved = canvas.Save();
            canvas.Translate(0, (float)pose.LiftY);
            canvas.Scale((float)pose.LiftScale, (float)pose.LiftScale, box.MidX, box.MidY);
            using var shape = new SKRoundRect(box, .24f * box.Width, .24f * box.Height);
            canvas.ClipRoundRect(shape, SKClipOperation.Difference, true);
            foreach (var layer in lift)
            {
                var c = layer.Color;
                paint.Color = new SKColor(c.R, c.G, c.B, (byte)Math.Round(c.A * Math.Clamp(pose.LiftOpacity, 0, 1)));
                paint.MaskFilter = layer.Blur > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(layer.Blur / 2)) : null;
                var r = box;
                r.Inflate((float)layer.Spread, (float)layer.Spread);
                r.Offset((float)layer.X, (float)layer.Y);
                using var drop = new SKRoundRect(r, Math.Max(0, .24f * box.Width + (float)layer.Spread), Math.Max(0, .24f * box.Height + (float)layer.Spread));
                canvas.DrawRoundRect(drop, paint);
                paint.MaskFilter?.Dispose();
                paint.MaskFilter = null;
            }
            canvas.RestoreToCount(saved);
        }
        if (pose.TileOpacity <= 0.002) return;
        var tileSaved = canvas.Save();
        var tile = new SKRect(0, 0, s, s);
        canvas.Scale((float)pose.TileScale, (float)pose.TileScale, s / 2, s / 2);
        using (var layerPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * Math.Clamp(pose.TileOpacity, 0, 1))) })
        {
            if (dim) layerPaint.ColorFilter = Dim();
            canvas.SaveLayer(layerPaint);
        }
        var corner = .217f * s;
        using var round = new SKRoundRect(tile, corner, corner);
        canvas.ClipRoundRect(round, SKClipOperation.Intersect, true);
        paint.Color = G800;
        canvas.DrawRect(tile, paint);
        // the drift layer: 220 % of the tile at −60 %, linear-gradient(145deg, g800 22 %, g600 47 %, s400 72 %, s300 84 %)
        var d = 2.2f * s;
        var dx = -.6f * s + (float)pose.DriftX * d;
        var dy = -.6f * s + (float)pose.DriftY * d;
        using (var drift = Css(145, new SKRect(dx, dy, dx + d, dy + d), [G800, G800, G600, S400, S300, S300], [0, .22f, .47f, .72f, .84f, 1]))
        {
            paint.Shader = drift;
            canvas.DrawRect(tile, paint);
        }
        // the pool: radial-gradient(60% 60% at 82% 95%, rgba(214,218,226,.55), clear 70%)
        using (var pool = SKShader.CreateRadialGradient(new SKPoint(.82f * s, .95f * s), .6f * s, [new SKColor(214, 218, 226, 140), new SKColor(214, 218, 226, 0)], [0, .7f], SKShaderTileMode.Clamp))
        {
            paint.Shader = pool;
            canvas.DrawRect(tile, paint);
        }
        // the highlight: linear-gradient(160deg, white .5, white .07 42 %, clear 60 %)
        using (var hi = Css(160, tile, [new SKColor(255, 255, 255, 128), new SKColor(255, 255, 255, 18), new SKColor(255, 255, 255, 0)], [0, .42f, .6f]))
        {
            paint.Shader = hi;
            canvas.DrawRect(tile, paint);
        }
        paint.Shader = null;
        Glyph(canvas, s, pose);
        // the tile's inner rim: inset 0 0 0 1px rgba(255,255,255,.34), above everything in the tile
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        paint.Color = new SKColor(255, 255, 255, 87);
        using (var rim = new SKRoundRect(new SKRect(.5f, .5f, s - .5f, s - .5f), corner - .5f, corner - .5f)) canvas.DrawRoundRect(rim, paint);
        canvas.RestoreToCount(tileSaved);
    }

    private static void Glyph(SKCanvas canvas, float s, MarkPose pose)
    {
        var draw = Math.Clamp(pose.Draw, 0, 1);
        var bead = Math.Clamp(pose.BeadOpacity, 0, 1);
        if (draw <= 0 && bead <= 0) return;
        var saved = canvas.Save();
        // the svg's viewBox 8 8 240 240 over the tile
        var k = s / 240f;
        canvas.Scale(k, k);
        canvas.Translate(-8, -8);
        // fG: feDropShadow dx 0, dy 3, stdDeviation 5, #06080c at .38, over the stroke and the bead together
        using (var shadow = new SKPaint { ImageFilter = SKImageFilter.CreateDropShadow(0, 3, 5, 5, new SKColor(6, 8, 12, 97)) }) canvas.SaveLayer(shadow);
        using var paint = new SKPaint { IsAntialias = true };
        if (draw > 0)
        {
            using var path = new SKPath();
            path.MoveTo(70, 196);
            path.LineTo(70, 124);
            path.ArcTo(new SKPoint(44, 44), 0, SKPathArcSize.Small, SKPathDirection.Clockwise, new SKPoint(158, 124));
            path.LineTo(158, 196);
            using var stroke = SKShader.CreateLinearGradient(new SKPoint(0, 80), new SKPoint(0, 196), [White, S100, S300], [0, .55f, 1], SKShaderTileMode.Clamp);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 26;
            paint.StrokeCap = SKStrokeCap.Butt;
            paint.Shader = stroke;
            if (draw >= 1) canvas.DrawPath(path, paint);
            else
            {
                using var measure = new SKPathMeasure(path);
                using var part = new SKPath();
                if (measure.GetSegment(0, (float)(measure.Length * draw), part, true)) canvas.DrawPath(part, paint);
            }
            paint.Shader = null;
            paint.Style = SKPaintStyle.Fill;
        }
        if (bead > 0 && pose.BeadScale > 0)
        {
            var beadSaved = canvas.Save();
            canvas.Scale((float)pose.BeadScale, (float)pose.BeadScale, 186, 74);
            using var fill = SKShader.CreateRadialGradient(new SKPoint(186 - 14 + .36f * 28, 74 - 14 + .3f * 28), .8f * 28, [White, S200, S400], [0, .5f, 1], SKShaderTileMode.Clamp);
            paint.Shader = fill;
            paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * bead));
            canvas.DrawCircle(186, 74, 14, paint);
            paint.Shader = null;
            canvas.RestoreToCount(beadSaved);
        }
        canvas.Restore();
        canvas.RestoreToCount(saved);
    }

    /// <summary>A CSS <c>linear-gradient(angle)</c> over <paramref name="box"/>: its line through the centre, as long as the box's projection.</summary>
    private static SKShader Css(double degrees, SKRect box, SKColor[] colors, float[] stops)
    {
        var a = degrees * Math.PI / 180;
        var (dx, dy) = (Math.Sin(a), -Math.Cos(a));
        var half = (box.Width * Math.Abs(dx) + box.Height * Math.Abs(dy)) / 2;
        var (cx, cy) = (box.MidX, box.MidY);
        return SKShader.CreateLinearGradient(new SKPoint((float)(cx - dx * half), (float)(cy - dy * half)), new SKPoint((float)(cx + dx * half), (float)(cy + dy * half)), colors, stops, SKShaderTileMode.Clamp);
    }

    /// <summary>CSS <c>saturate(.6) brightness(.86)</c> as one colour matrix.</summary>
    private static SKColorFilter Dim()
    {
        const float s = .6f, b = .86f;
        float[] m =
        [
            (.213f + .787f * s) * b, (.715f - .715f * s) * b, (.072f - .072f * s) * b, 0, 0,
            (.213f - .213f * s) * b, (.715f + .285f * s) * b, (.072f - .072f * s) * b, 0, 0,
            (.213f - .213f * s) * b, (.715f - .715f * s) * b, (.072f + .928f * s) * b, 0, 0,
            0, 0, 0, 1, 0,
        ];
        return SKColorFilter.CreateColorMatrix(m);
    }
}
