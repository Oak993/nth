using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerWorkingDots.swift (B3 52bc792), :67-79,124-190.
// PORT-ONLY immutable transport for the source's layer configuration and one render-server sample.
// OD-0088 (ED-253, round 4), SPEC 6.4 "Every use of the effective accent eases .32 s": a new accent is sent with the colour
// the field showed (From) and when the change began (BlendStart); each sample blends From to Target on AppearanceTurn's curve
// (0.32 s, CSS ease, per channel premultiplied, as SwiftColor's accent brushes ease), on the render server, so the dots ease
// with every other use of the accent and still never ask the UI thread for a frame. Brush is the colour at Now.
internal sealed record DotFieldCompositionFrame(Size Size, IImmutableBrush Brush, bool Animated, bool Sweeps,
    TimeProvider Clock, ContinuousInstant Start, ContinuousInstant Now)
{
    /// <summary>The colour the field is drawn in once a blend is done.</summary>
    public Color Target { get; init; }

    /// <summary>The colour shown when the accent last changed, while the blend from it runs; null when there is none.</summary>
    public Color? From { get; init; }

    /// <summary>When the blend from <see cref="From"/> began.</summary>
    public ContinuousInstant BlendStart { get; init; }

    private static readonly TimeSpan BlendSpan = TimeSpan.FromSeconds(AppearanceTurn.Curve.Duration);

    // ED-117 (2), as FrameClock.IsComplete: a span is complete 1 ms early at most.
    private static bool Complete(ContinuousInstant since, ContinuousInstant now) =>
        (now - since).Ticks + TimeSpan.TicksPerMillisecond >= BlendSpan.Ticks;

    /// <summary>The colour shown at <paramref name="now"/>: <paramref name="target"/>, or on its way there from <paramref name="from"/>.</summary>
    public static Color Shown(Color target, Color? from, ContinuousInstant blendStart, ContinuousInstant now) =>
        from is { } start && !Complete(blendStart, now)
            ? SwiftColor.PremultipliedMix(start, target, AppearanceTurn.Curve.Progress(Math.Max(0, (now - blendStart).TotalSeconds)))
            : target;

    /// <summary>A frame drawn in <paramref name="target"/>, blending from <paramref name="from"/> since <paramref name="blendStart"/>.</summary>
    public static DotFieldCompositionFrame Create(Size size, Color target, Color? from, ContinuousInstant blendStart, bool animated,
        bool sweeps, TimeProvider clock, ContinuousInstant start, ContinuousInstant now)
    {
        var blending = from is not null && !Complete(blendStart, now);
        return new DotFieldCompositionFrame(size, new ImmutableSolidColorBrush(Shown(target, from, blendStart, now)), animated, sweeps, clock, start, now)
        {
            Target = target,
            From = blending ? from : null,
            BlendStart = blendStart,
        };
    }

    /// <summary>Whether the blend to <see cref="Target"/> still runs at <see cref="Now"/>.</summary>
    public bool Blending => BlendingAt(Now);

    /// <summary>Whether the blend to <see cref="Target"/> still runs at <paramref name="now"/>.</summary>
    internal bool BlendingAt(ContinuousInstant now) => From is not null && !Complete(BlendStart, now);

    /// <summary>The colour this field shows at <paramref name="now"/>.</summary>
    internal Color ShownAt(ContinuousInstant now) => Shown(Target, From, BlendStart, now);
    private const double DotSize = 2;
    private const double Pitch = 8;
    private const int MaxRows = 3;
    private const double Period = 2.2;
    private const double ColumnDelay = 0.045;
    private const double RowDelay = 0.09;
    private const double SweepPeriod = 0.8;
    private const double SweepColumnDelay = 0.022;

    public int Columns => (int)Math.Ceiling(Size.Width / Pitch) + 1;
    public int Rows => Sweeps ? Math.Max(1, (int)Math.Ceiling(Size.Height / Pitch))
        : Math.Min(MaxRows, Math.Max(1, (int)Math.Ceiling(Size.Height / Pitch)));
    public Rect RenderBounds => Size.Width > 0 && Size.Height > 0
        ? new Rect(Pitch / 2 - DotSize / 2, Size.Height - Pitch / 2 - (Rows - 1) * Pitch - DotSize / 2,
            (Columns - 1) * Pitch + DotSize, (Rows - 1) * Pitch + DotSize) : default;
    public double Elapsed => ElapsedAt(Now);
    private double ElapsedAt(ContinuousInstant now) => Sweeps ? (now - Start).TotalSeconds : now.Ticks / (double)TimeSpan.TicksPerSecond - 1;
    public bool NeedsFrame => NeedsFrameAt(Now);

    /// <summary>Whether the field still moves at <paramref name="now"/> (a frame sampled then would need another).</summary>
    internal bool NeedsFrameAt(ContinuousInstant now) => Animated && Size.Width > 0 && Size.Height > 0
        && (!Sweeps || (now - Start) < TimeSpan.FromSeconds(SweepPeriod + (Columns - 1) * SweepColumnDelay) || BlendingAt(now));

    /// <summary>
    /// Now on the clock this field captured. The compositor does not inherit the UI async flow. Retain this field's captured clock
    /// binding; the shared timestamp/conversion remains SwiftClock's, including a ManualTimeProvider in headless. ED-NEW-lean-9: a
    /// flow that already reads that clock (the render thread reads the process default, which production's fields capture) reads it
    /// as it is, without installing the same provider again (an AsyncLocal change, a closure and a restorer at every sample).
    /// </summary>
    internal ContinuousInstant ClockNow()
    {
        if (ReferenceEquals(SwiftClock.Provider.Current, Clock)) return SwiftClock.Now;
        using var clock = SwiftClock.Provider.Override(Clock);
        return SwiftClock.Now;
    }

    public DotFieldCompositionFrame Sample() => At(ClockNow());

    /// <summary>This field sampled at <paramref name="now"/>; while it blends, drawn in <paramref name="brush"/> when given.</summary>
    internal DotFieldCompositionFrame At(ContinuousInstant now, IImmutableBrush? brush = null)
    {
        if (From is null) return this with { Now = now };
        // ED-253: the accent's blend, sampled here; once done the frame is the target's and blends no more.
        return this with
        {
            Now = now,
            Brush = brush ?? new ImmutableSolidColorBrush(Shown(Target, From, BlendStart, now)),
            From = BlendingAt(now) ? From : null,
        };
    }

    public Point PositionAt(int row, int column) => new(Pitch / 2 + column * Pitch, Size.Height - (Pitch / 2 + row * Pitch));
    public double OpacityAt(int row, int column) => OpacityAt(row, column, Elapsed, Rows);

    private double OpacityAt(int row, int column, double elapsed, int rows)
    {
        var opacity = Sweeps ? 0 : 0.28;
        if (Animated)
        {
            var seconds = elapsed - column * (Sweeps ? SweepColumnDelay : ColumnDelay) - row * (Sweeps ? 0 : RowDelay);
            var period = Sweeps ? SweepPeriod : Period;
            if (!Sweeps && seconds >= 0) seconds %= period;
            opacity = KeyframeOpacity(seconds, period, Sweeps ? SweepPeak : LoopPeak, Sweeps ? 0 : 0.08, Sweeps ? 0.7 : 0.75,
                Sweeps ? SweepRise : LoopRise, Sweeps ? SweepFall : LoopFall);
        }
        return opacity * (Sweeps ? 1 : 1.0 - (double)row / rows);
    }

    // ED-NEW-lean-9: the four curves of the keyframes, made once (each dot of each draw made its own before, 56 B a dot); the same
    // durations, computed as before (split = duration * peakTime; the fall lasts duration - split).
    private const double LoopPeak = 0.35;
    private const double SweepPeak = 0.4;
    private static readonly Animation LoopRise = Animation.EaseOut(Period * LoopPeak);
    private static readonly Animation LoopFall = Animation.EaseIn(Period - Period * LoopPeak);
    private static readonly Animation SweepRise = Animation.EaseOut(SweepPeriod * SweepPeak);
    private static readonly Animation SweepFall = Animation.EaseIn(SweepPeriod - SweepPeriod * SweepPeak);

    private static double KeyframeOpacity(double seconds, double duration, double peakTime, double start, double peak, Animation rise, Animation fall)
    {
        if (seconds <= 0 || seconds >= duration) return start;
        var split = duration * peakTime;
        return seconds <= split
            ? start + (peak - start) * rise.Progress(seconds)
            : peak + (start - peak) * fall.Progress(seconds - split);
    }

    public void Draw(ImmediateDrawingContext context) => Draw(context, Now, Brush);

    /// <summary>The field as sampled at <paramref name="now"/>, in <paramref name="brush"/> (ED-NEW-lean-9: no sampled frame made).</summary>
    internal void Draw(ImmediateDrawingContext context, ContinuousInstant now, IImmutableBrush brush)
    {
        if (Size.Width <= 0 || Size.Height <= 0) return;
        var elapsed = ElapsedAt(now);
        var rows = Rows;
        var columns = Columns;
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var opacity = OpacityAt(row, column, elapsed, rows);
                if (opacity <= 0) continue;
                var center = PositionAt(row, column);
                var bounds = new Rect(center.X - DotSize / 2, center.Y - DotSize / 2, DotSize, DotSize);
                using var alpha = context.PushOpacity(opacity, bounds);
                context.DrawEllipse(brush, null, center, DotSize / 2, DotSize / 2);
            }
    }
}
