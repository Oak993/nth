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
    public bool Blending => From is not null && !Complete(BlendStart, Now);
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
    public double Elapsed => Sweeps ? (Now - Start).TotalSeconds : Now.Ticks / (double)TimeSpan.TicksPerSecond - 1;
    public bool NeedsFrame => Animated && Size.Width > 0 && Size.Height > 0
        && (!Sweeps || (Now - Start) < TimeSpan.FromSeconds(SweepPeriod + (Columns - 1) * SweepColumnDelay) || Blending);

    public DotFieldCompositionFrame Sample()
    {
        // The compositor does not inherit the UI async flow. Retain this field's captured clock binding;
        // the shared timestamp/conversion remains SwiftClock's, including a ManualTimeProvider in headless.
        using var clock = SwiftClock.Provider.Override(Clock);
        var now = SwiftClock.Now;
        if (From is null) return this with { Now = now };
        // ED-253: the accent's blend, sampled here; once done the frame is the target's and blends no more.
        var blending = !Complete(BlendStart, now);
        return this with
        {
            Now = now,
            Brush = new ImmutableSolidColorBrush(Shown(Target, From, BlendStart, now)),
            From = blending ? From : null,
        };
    }

    public Point PositionAt(int row, int column) => new(Pitch / 2 + column * Pitch, Size.Height - (Pitch / 2 + row * Pitch));
    public double OpacityAt(int row, int column)
    {
        var opacity = Sweeps ? 0 : 0.28;
        if (Animated)
        {
            var seconds = Elapsed - column * (Sweeps ? SweepColumnDelay : ColumnDelay) - row * (Sweeps ? 0 : RowDelay);
            var period = Sweeps ? SweepPeriod : Period;
            if (!Sweeps && seconds >= 0) seconds %= period;
            opacity = KeyframeOpacity(seconds, period, Sweeps ? 0.4 : 0.35, Sweeps ? 0 : 0.08, Sweeps ? 0.7 : 0.75);
        }
        return opacity * (Sweeps ? 1 : 1.0 - (double)row / Rows);
    }

    private static double KeyframeOpacity(double seconds, double duration, double peakTime, double start, double peak)
    {
        if (seconds <= 0 || seconds >= duration) return start;
        var split = duration * peakTime;
        return seconds <= split
            ? start + (peak - start) * Animation.EaseOut(split).Progress(seconds)
            : peak + (start - peak) * Animation.EaseIn(duration - split).Progress(seconds - split);
    }

    public void Draw(ImmediateDrawingContext context)
    {
        if (Size.Width <= 0 || Size.Height <= 0) return;
        for (var row = 0; row < Rows; row++)
            for (var column = 0; column < Columns; column++)
            {
                var opacity = OpacityAt(row, column);
                if (opacity <= 0) continue;
                var center = PositionAt(row, column);
                var bounds = new Rect(center.X - DotSize / 2, center.Y - DotSize / 2, DotSize, DotSize);
                using var alpha = context.PushOpacity(opacity, bounds);
                context.DrawEllipse(Brush, null, center, DotSize / 2, DotSize / 2);
            }
    }
}
