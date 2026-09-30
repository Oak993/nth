using Avalonia;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerWorkingDots.swift (B3 52bc792), :67-79,124-190.
// PORT-ONLY immutable transport for the source's layer configuration and one render-server sample.
internal sealed record DotFieldCompositionFrame(Size Size, IImmutableBrush Brush, bool Animated, bool Sweeps,
    TimeProvider Clock, ContinuousInstant Start, ContinuousInstant Now)
{
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
        && (!Sweeps || (Now - Start) < TimeSpan.FromSeconds(SweepPeriod + (Columns - 1) * SweepColumnDelay));

    public DotFieldCompositionFrame Sample()
    {
        // The compositor does not inherit the UI async flow. Retain this field's captured clock binding;
        // the shared timestamp/conversion remains SwiftClock's, including a ManualTimeProvider in headless.
        using var clock = SwiftClock.Provider.Override(Clock);
        return this with { Now = SwiftClock.Now };
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
