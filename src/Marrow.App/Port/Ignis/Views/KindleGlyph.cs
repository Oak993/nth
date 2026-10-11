// PORT-ONLY: Ignis U4 (OD-0134, ED-NEW-ignis-views). The Kindle capsule's lead: the ring (context fill, its gradient warming
// from grey to ember with the cache's heat, ".kring"), the hairline separator (".k-sep") and the breathing ember core
// (".k-core", the flame). Design: ring 16 pt (15 pt box, radius 6.2, 2.6 pt stroke over a 2.5 pt track), separator 1 x 12,
// flame 13 pt, 6 pt apart. Motion: the core breathes (scale 1 → 1.09, brightness 1.14, 3.6 s) while warm; the ring spins
// (1.1 s a turn) while tidying; a keep-warm throws a burst of nine sparks from the core (0.7 s); a tidy-up settles the ring
// (0.72 → 1.1 → 1 over 0.75 s). The heat eases over 0.6 s, the fill over 0.5 s. Reduce Motion: no breathing, no spin, no
// sparks, no settle; values change at once.
// ED-NEW-kindlehover-1 (owner: "Dull pill. Doesn't even do anything when I hover mouse over it?"): under the pointer (or while
// the popover is open) a cold or off capsule's ring, separator and drop lift from the disabled grey toward the design's
// secondary text tone (Lift), and a cold one's core kindles faintly (Kindle). Both are eased scalars that run only while they
// move: no timer and no ambient clock at rest.
using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Port.Ignis.Views;

internal sealed class KindleGlyph : AmbientCanvas
{
    public const double RingSize = 16, Gap = 6, SeparatorWidth = 1, SeparatorHeight = 12, FlameSize = 13;
    public const double GlyphWidth = RingSize + Gap + SeparatorWidth + Gap + FlameSize;

    private static readonly Animation SparkCurve = Animation.TimingCurve(.15, .7, .3, 1, duration: .7);
    private static readonly (double Angle, double Distance, double Delay)[] Sparks = MakeSparks(9, 11);

    private readonly AnimatedScalar heat, fill, spark, settle, lift, kindle;
    private bool warm, tidying;

    public KindleGlyph()
    {
        Width = GlyphWidth;
        Height = RingSize;
        ClipToBounds = false;
        heat = new AnimatedScalar(this, 0, _ => InvalidateVisual());
        fill = new AnimatedScalar(this, 0, _ => InvalidateVisual());
        spark = new AnimatedScalar(this, 1, _ => InvalidateVisual());
        settle = new AnimatedScalar(this, 1, _ => InvalidateVisual());
        lift = new AnimatedScalar(this, 0, _ => InvalidateVisual());
        kindle = new AnimatedScalar(this, 0, _ => InvalidateVisual());
    }

    public double Heat => heat.Value;
    public double Fill => fill.Value;
    public bool Breathing => warm;
    public bool Spinning => tidying;
    /// <summary>Sparks in flight (0 ... 1 while a burst runs, 1 at rest).</summary>
    public double SparkProgress => spark.Value;
    public double SettleProgress => settle.Value;
    /// <summary>The hover lift of the cold grey toward the secondary tone (0 at rest ... 1 under the pointer).</summary>
    public double Lift => lift.Value;
    /// <summary>The faint ember a cold core shows under the pointer (0 ... 1).</summary>
    public double Kindle => kindle.Value;

    protected override bool Moving => warm || tidying;

    /// <summary>Shows a state: <paramref name="toHeat"/> 0 ... 1, <paramref name="toFill"/> the context fill 0 ... 1.</summary>
    public void Show(double toHeat, double toFill, bool isWarm, bool isTidying, bool animate)
    {
        heat.Set(Math.Clamp(toHeat, 0, 1), animate ? KindleMotion.Ease(this, Animation.EaseOut(.6)) : null);
        fill.Set(Math.Clamp(toFill, 0, 1), animate ? KindleMotion.Ease(this, Animation.TimingCurve(.22, 1, .36, 1, duration: .5)) : null);
        if (isWarm != warm || isTidying != tidying)
        {
            warm = isWarm;
            tidying = isTidying;
            MovingChanged();
        }
    }

    /// <summary>The pointer's state: <paramref name="toLift"/> and <paramref name="toKindle"/> 0 ... 1, each along its curve (none: at once).</summary>
    public void Hover(double toLift, double toKindle, Animation? liftCurve, Animation? kindleCurve)
    {
        lift.Set(Math.Clamp(toLift, 0, 1), liftCurve);
        kindle.Set(Math.Clamp(toKindle, 0, 1), kindleCurve);
    }

    /// <summary>A keep-warm landed: nine sparks from the core.</summary>
    public void Spark()
    {
        if (KindleMotion.Reduce(this)) return;
        spark.Set(0, null);
        spark.Set(1, SparkCurve);
    }

    /// <summary>A tidy-up finished: the ring settles.</summary>
    public void Settle()
    {
        if (KindleMotion.Reduce(this)) return;
        settle.Set(0, null);
        settle.Set(1, Animation.Linear(.75));
    }

    private sealed record State(KindlePalette Palette, double Heat, double Fill, double Spark, double Settle, bool Warm, bool Tidying, double Lift, double Kindle);

    protected override object Snapshot() => new State(KindlePalette.For(this), heat.Value, fill.Value, spark.Value, settle.Value, warm, tidying, lift.Value, kindle.Value);

    /// <summary>The track and separator colour at <paramref name="lifted"/>: the design's track ring, lifted toward its tertiary text tone.</summary>
    internal static Avalonia.Media.Color TrackColor(KindlePalette palette, double lifted) => KindlePalette.Mix(palette.TrackRing, palette.Text3, lifted);

    protected override void Draw(SKCanvas canvas, Size size, double seconds, bool still, object snapshot)
    {
        var state = (State)snapshot;
        var palette = state.Palette;
        var h = state.Heat;
        var warm = state.Warm;
        var tidying = state.Tidying;
        var lifted = state.Lift;
        var trackColor = TrackColor(palette, lifted);
        var cy = (float)(size.Height / 2);

        // ---- the ring: track, then the fill's arc in the warming gradient, with its glow
        var ringBox = new SKRect(0, cy - 8, 16, cy + 8);
        var saved = canvas.Save();
        var cx = ringBox.MidX;
        var ringScale = SettleScale(state.Settle);
        var spin = tidying && !still ? seconds / 1.1 % 1 * 360 : 0;
        canvas.Translate(cx, cy);
        canvas.Scale((float)ringScale);
        canvas.RotateDegrees((float)spin);
        canvas.Scale(16f / 15f);
        canvas.Translate(-7.5f, -7.5f);
        using (var track = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, Color = KindlePalette.Sk(trackColor) })
            canvas.DrawCircle(7.5f, 7.5f, 6.2f, track);
        var f = state.Fill;
        if (f > 0.001)
        {
            var (a, b) = palette.RingStops(h);
            if (lifted > 0) (a, b) = (KindlePalette.Mix(a, palette.Text2, lifted), KindlePalette.Mix(b, palette.Text2, lifted));
            using var arcPath = new SKPath();
            var oval = new SKRect(7.5f - 6.2f, 7.5f - 6.2f, 7.5f + 6.2f, 7.5f + 6.2f);
            if (f >= .999) arcPath.AddOval(oval);
            else arcPath.AddArc(oval, -90, (float)(f * 360));
            if (h > .01)
            {
                using var glow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.6f, StrokeCap = SKStrokeCap.Round, Color = KindlePalette.Sk(palette.EmberGlow, h), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(1.5 * h)) };
                canvas.DrawPath(arcPath, glow);
            }
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(15, 15), [KindlePalette.Sk(a), KindlePalette.Sk(b)], SKShaderTileMode.Clamp);
            using var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.6f, StrokeCap = SKStrokeCap.Round, Shader = shader };
            canvas.DrawPath(arcPath, arc);
        }
        canvas.RestoreToCount(saved);

        // ---- the separator
        var sepX = (float)(RingSize + Gap);
        using (var sep = new SKPaint { IsAntialias = true, Color = KindlePalette.Sk(trackColor) })
            canvas.DrawRect(new SKRect(sepX, cy - 6, sepX + 1, cy + 6), sep);

        // ---- the core: the flame, breathing while warm
        var flameX = (float)(sepX + SeparatorWidth + Gap);
        var breath = warm && !still ? KindleMotion.Breath(seconds) : 0;
        var scale = (float)(1 + .09 * breath);
        var half = (float)(FlameSize / 2) * scale;
        var center = new SKPoint(flameX + (float)FlameSize / 2, cy);
        KindleFlame.Draw(canvas, new SKRect(center.X - half, center.Y - half, center.X + half, center.Y + half), palette, h, 1 + .14 * breath, lifted, state.Kindle);

        // ---- sparks: a burst from the core while one is in flight
        var p = state.Spark;
        if (p < 1)
        {
            using var dot = new SKPaint { IsAntialias = true };
            using var glow = new SKPaint { IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f) };
            foreach (var (angle, distance, delay) in Sparks)
            {
                var t = Math.Clamp((p * .7 - delay) / (.7 - delay), 0, 1);
                if (t <= 0 || t >= 1) continue;
                var x = center.X + (float)(Math.Cos(angle) * distance * t);
                var y = center.Y + (float)((Math.Sin(angle) * distance * .8 - 4) * t);
                var r = (float)(1.5 * (1.2 - t));
                glow.Color = KindlePalette.Sk(palette.EmberGlow, 1 - t);
                canvas.DrawCircle(x, y, r + 1.5f, glow);
                dot.Color = KindlePalette.Sk(palette.EmberCore, 1 - t);
                canvas.DrawCircle(x, y, r, dot);
            }
        }
    }

    // keyframes settle: 0 % 1, 35 % 0.72, 72 % 1.1, 100 % 1 (each leg eased by the design's rise curve, approximated smooth)
    private static double SettleScale(double p)
    {
        if (p >= 1 || p <= 0) return 1;
        static double Smooth(double t) => t * t * (3 - 2 * t);
        if (p < .35) return 1 - .28 * Smooth(p / .35);
        if (p < .72) return .72 + .38 * Smooth((p - .35) / .37);
        return 1.1 - .1 * Smooth((p - .72) / .28);
    }

    // The design's burst: n sparks round the circle from straight up, a little jitter, 9 to 18 points out, up to 60 ms late.
    // A fixed seed: the same burst every time (nothing random on screen between runs).
    private static (double, double, double)[] MakeSparks(int n, int seed)
    {
        var random = new Random(seed);
        var sparks = new (double, double, double)[n];
        for (var i = 0; i < n; i++)
            sparks[i] = (Math.PI * 2 * i / n + random.NextDouble() * .5 - .25 - Math.PI / 2, 9 + random.NextDouble() * 9, random.NextDouble() * .06);
        return sparks;
    }
}
