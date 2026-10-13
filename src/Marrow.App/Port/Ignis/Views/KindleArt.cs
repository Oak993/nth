// PORT-ONLY: Ignis U4 (OD-0134, ED-NEW-ignis-views). The Kindle's colours and its drawn pieces, from the locked design
// (work/design/phoenix/phoenix-designs.html, ":root" tokens and ".kindle", ".kring", ".flame", ".ktrack"): the ember
// (core #ffd57e, outer #ff6a1f, its glow), the cold grey, the cool ring colours the gradient warms from, and the flame
// glyph's two paths (24 point box). The continuous motion (the breathing core, the tidy-up spin, the warm track's head) is a
// function of the paced ambient clock's time, drawn on the render thread (FrameClock.TimelineView, Ambient, onRenderThread),
// so it advances only at the 60 Hz slots of AmbientRefreshPacer and never wakes main.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Port.Ignis.Views;

/// <summary>The design's Ignis colours for one appearance (light or dark).</summary>
internal sealed record KindlePalette(
    Color EmberCore, Color EmberOuter, Color EmberGlow, Color EmberCold, Color CoolA, Color CoolB,
    Color Track, Color TrackRing, Color Good, Color Violet, Color Accent, Color Text, Color Text2, Color Text3, Color Control, Color ControlHover)
{
    public static KindlePalette Light(Color accent) => new(
        Rgb(0xffd57e), Rgb(0xff6a1f), Rgba(255, 106, 31, .55), Rgb(0xa3a9b4), Rgb(0xc3cbd8), Rgb(0x8f9bb0),
        Rgba(24, 34, 64, .1), Rgba(24, 34, 64, .16), Rgb(0x1c8f4e), Rgb(0x7b55e0), accent,
        Rgb(0x1b2030), Rgba(27, 32, 48, .62), Rgba(27, 32, 48, .4), Rgba(24, 34, 64, .065), Rgba(24, 34, 64, .11));

    public static KindlePalette Dark(Color accent) => new(
        Rgb(0xffd57e), Rgb(0xff6a1f), Rgba(255, 120, 50, .62), Rgb(0x6b7280), Rgb(0x8e9ab0), Rgb(0x5f6b82),
        Rgba(255, 255, 255, .12), Rgba(255, 255, 255, .2), Rgb(0x3ad26a), Rgb(0xa98bff), accent,
        Rgb(0xf2f4f8), Rgba(242, 244, 248, .64), Rgba(242, 244, 248, .4), Rgba(255, 255, 255, .075), Rgba(255, 255, 255, .14));

    public static KindlePalette For(Control control) =>
        PlainControls.IsDark(control.ActualThemeVariant)
            ? Dark(Marrow.UI.Chrome.Chrome.Accent.Resolve(control.ActualThemeVariant))
            : Light(Marrow.UI.Chrome.Chrome.Accent.Resolve(control.ActualThemeVariant));

    /// <summary>The ring's gradient stops (".kg-a", ".kg-b"): the cool colours mixed toward the ember by <paramref name="heat"/>.</summary>
    public (Color A, Color B) RingStops(double heat) => (Mix(CoolA, EmberCore, heat), Mix(CoolB, EmberOuter, heat));

    /// <summary>The quick memory fill's stops (".ktrack.cache .fill"): the core at 70 % of the heat, the outer at all of it.</summary>
    public (Color A, Color B) CacheStops(double heat) => (Mix(CoolA, EmberCore, heat * .7), Mix(CoolB, EmberOuter, heat));

    public static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);
        return Color.FromArgb(Lerp(from.A, to.A, amount), Lerp(from.R, to.R, amount), Lerp(from.G, to.G, amount), Lerp(from.B, to.B, amount));
    }

    public static Color Rgb(uint hex) => Color.FromRgb((byte)((hex >> 16) & 0xff), (byte)((hex >> 8) & 0xff), (byte)(hex & 0xff));

    public static Color Rgba(byte r, byte g, byte b, double a) => Color.FromArgb((byte)Math.Round(a * 255), r, g, b);

    public static SKColor Sk(Color color, double opacity = 1) =>
        new(color.R, color.G, color.B, (byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1)));
}

internal static class KindleMotion
{
    /// <summary>The design's ease-rise (cubic-bezier(.2,1.14,.32,1)).</summary>
    public static Animation Rise(double seconds) => Animation.TimingCurve(.2, 1.14, .32, 1, duration: seconds);

    /// <summary>The design's ease-smooth (cubic-bezier(.22,1,.36,1)).</summary>
    public static Animation Smooth(double seconds) => Animation.TimingCurve(.22, 1, .36, 1, duration: seconds);

    /// <summary>The system's or the window's Reduce Motion, or no frame clock: nothing loops, nothing flies.</summary>
    public static bool Reduce(Control owner)
    {
        if (!FrameClock.IsBound || EnvironmentValues.GetAccessibilityReduceMotion(owner)) return true;
        try { return Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion; }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return true; }
    }

    /// <summary>An animation, or none when motion is reduced.</summary>
    public static Animation? Ease(Control owner, Animation curve) => Reduce(owner) ? null : curve;

    /// <summary>
    /// ED-NEW-perf-idle-1: sets <paramref name="scalar"/> to <paramref name="to"/> along <paramref name="ease"/> for a change of the
    /// shown state, and not at all for the slow drift of the clock. The cache's heat falls 0.0002 a second, so the once-a-second
    /// re-read must not start an animation run (each run asks the frame clock for a frame at every display refresh for 0.6 s):
    /// a target already set is left alone (a run in flight keeps going, not restarted), a change under <paramref name="steady"/>
    /// is not a movement, so it lands at once, and only once it has grown to <paramref name="step"/> (nothing is running).
    /// </summary>
    public static void Follow(AnimatedScalar scalar, double to, Animation? ease, double steady, double step)
    {
        var gap = Math.Abs(scalar.Target - to);
        if (gap == 0) return;
        if (gap >= steady) { scalar.Set(to, ease); return; }
        if (scalar.IsRunning || gap < step) return;
        scalar.Set(to, null);
    }

    /// <summary>The breathing scale at <paramref name="seconds"/> (keyframes breathe: 1 → 1.09 at half of 3.6 s → 1, ease-in-out).</summary>
    public static double Breath(double seconds)
    {
        var p = seconds / 3.6 % 1;
        var half = p < .5 ? p * 2 : (1 - p) * 2;
        var eased = half * half * (3 - 2 * half);
        return eased;
    }
}

/// <summary>The flame glyph (".flame"): a cold grey base, the warm outer at the heat's opacity, the core over it.</summary>
internal static class KindleFlame
{
    public const string Outer = "M12.6 2.2c.5 2.9-1.1 4.7-2.8 6.5C8.2 10.4 6.5 12.3 6.5 15.2c0 3.5 2.5 6.3 5.6 6.3s5.9-2.6 5.9-6.2c0-2.4-1.1-4.2-2.3-5.5.1 1.4-.4 2.6-1.4 3.1.4-3.5-.4-7.7-1.7-10.7z";
    public const string Core = "M12.3 12.2c.1 1.5-.8 2.3-1.5 3.1-.5.6-.9 1.3-.9 2.2 0 1.6 1.1 2.7 2.3 2.7s2.4-1.1 2.4-2.6c0-1.1-.6-2-1.2-2.6 0 .7-.3 1.2-.8 1.4.1-1.6-.1-3-.3-4.2z";

    private static readonly Lazy<SKPath> OuterPath = new(() => SKPath.ParseSvgPathData(Outer));
    private static readonly Lazy<SKPath> CorePath = new(() => SKPath.ParseSvgPathData(Core));

    /// <summary>Draws the flame into <paramref name="box"/> at <paramref name="heat"/> (0 cold ... 1 hot), with its drop-shadow glow.
    /// ED-NEW-kindlehover-1: <paramref name="lift"/> lifts the cold base toward the secondary text tone, <paramref name="kindle"/>
    /// lights a faint core over it (a cold capsule under the pointer).</summary>
    public static void Draw(SKCanvas canvas, SKRect box, KindlePalette palette, double heat, double brightness = 1, double lift = 0, double kindle = 0)
    {
        var saved = canvas.Save();
        canvas.Translate(box.Left, box.Top);
        canvas.Scale(box.Width / 24f, box.Height / 24f);
        var scale = box.Width / 24f;
        using (var paint = new SKPaint { IsAntialias = true, Color = KindlePalette.Sk(ColdBase(palette, lift)) })
            canvas.DrawPath(OuterPath.Value, paint);
        var faint = Math.Clamp(kindle, 0, 1) * (1 - Math.Clamp(heat, 0, 1));
        if (faint > 0.001)
        {
            // the kindling: a faint warm wash on the drop, its core lit, a small glow (never the warm flame's full strength)
            using (var glow = new SKPaint { IsAntialias = true, Color = KindlePalette.Sk(palette.EmberGlow, .35 * faint), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(1.2 / scale)) })
                canvas.DrawPath(CorePath.Value, glow);
            using (var warm = new SKPaint { IsAntialias = true, Color = KindlePalette.Sk(palette.EmberOuter, KindleOuter * faint) })
                canvas.DrawPath(OuterPath.Value, warm);
            using (var core = new SKPaint { IsAntialias = true, Color = KindlePalette.Sk(palette.EmberCore, KindleCore * faint) })
                canvas.DrawPath(CorePath.Value, core);
        }
        if (heat > 0.001)
        {
            using (var glow = new SKPaint { IsAntialias = true, Color = KindlePalette.Sk(palette.EmberGlow, heat), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(4 * heat / 2 / scale)) })
                canvas.DrawPath(OuterPath.Value, glow);
            using (var warm = new SKPaint { IsAntialias = true, Color = KindlePalette.Sk(Brighten(palette.EmberOuter, brightness), heat) })
                canvas.DrawPath(OuterPath.Value, warm);
            using (var core = new SKPaint { IsAntialias = true, Color = KindlePalette.Sk(Brighten(palette.EmberCore, brightness), heat) })
                canvas.DrawPath(CorePath.Value, core);
        }
        canvas.RestoreToCount(saved);
    }

    /// <summary>The kindled core's and outer wash's opacity at a full kindle (faint: well under the warm flame's).</summary>
    public const double KindleCore = .7, KindleOuter = .16;

    /// <summary>The cold drop's base at <paramref name="lift"/>: the design's cold grey, lifted toward its secondary text tone.</summary>
    public static Color ColdBase(KindlePalette palette, double lift) => KindlePalette.Mix(palette.EmberCold, palette.Text2, lift);

    public static Color Brighten(Color color, double by)
    {
        if (by <= 1) return color;
        static byte Up(byte c, double k) => (byte)Math.Min(255, Math.Round(c * k));
        return Color.FromArgb(color.A, Up(color.R, by), Up(color.G, by), Up(color.B, by));
    }
}

/// <summary>
/// A control whose drawing moves with the paced ambient clock: a render-thread timeline (Ambient) hands it the time, and its
/// draw operation reads that time when the compositor draws. It runs only while <see cref="Moving"/> and Reduce Motion is off.
/// </summary>
internal abstract class AmbientCanvas : Control
{
    private readonly DateTimeOffset startedAt = Marrow.Core.Port.SwiftDate.Now;
    private readonly AnimationTimeline timeline;
    private double time;

    protected AmbientCanvas()
    {
        IsHitTestVisible = false;
        timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(1.0 / 60, !Moving || KindleMotion.Reduce(this), Ambient: true), date =>
        {
            var seconds = RenderFrameSource.DecorationTimeFor(this) is { } phase ? phase.TotalSeconds : (date - startedAt).TotalSeconds;
            Volatile.Write(ref time, seconds);
            if (!RenderFrameSource.InvalidateTicking()) InvalidateVisual();
        }, this);
        AttachedToVisualTree += (_, _) => { if (FrameClock.IsBound) timeline.Appear(); };
        DetachedFromVisualTree += (_, _) => timeline.Disappear();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == EnvironmentValues.AccessibilityReduceMotionProperty) { timeline.Reschedule(); InvalidateVisual(); }
        };
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The ambient clock's seconds (0 while still).</summary>
    public double Time => Volatile.Read(ref time);

    /// <summary>Whether the ambient timeline runs (tests).</summary>
    internal bool IsAmbientRunning => Moving && !KindleMotion.Reduce(this);

    /// <summary>Whether anything here moves on its own now; call <see cref="MovingChanged"/> when it may have changed.</summary>
    protected abstract bool Moving { get; }

    /// <summary>Re-reads <see cref="Moving"/> (starts or pauses the ambient timeline) and redraws.</summary>
    protected void MovingChanged()
    {
        timeline.Reschedule();
        InvalidateVisual();
    }

    /// <summary>Everything the drawing needs from main (read on main when the drawing is recorded; the render thread must not touch the control).</summary>
    protected abstract object Snapshot();

    /// <summary>Draws with the time of the frame being drawn (render thread). <paramref name="still"/>: Reduce Motion, draw the rest pose.</summary>
    protected abstract void Draw(SKCanvas canvas, Size size, double seconds, bool still, object snapshot);

    public sealed override void Render(DrawingContext context) =>
        context.Custom(new Operation(this, new Rect(Bounds.Size), KindleMotion.Reduce(this), Snapshot()));

    private sealed class Operation(AmbientCanvas owner, Rect bounds, bool still, object snapshot) : ICustomDrawOperation
    {
        public Rect Bounds => bounds.Inflate(24);
        public bool HitTest(Point point) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            owner.Draw(lease.SkCanvas, bounds.Size, still ? 0 : owner.Time, still, snapshot);
        }
    }
}
