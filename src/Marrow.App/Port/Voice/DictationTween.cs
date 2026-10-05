// PORT-ONLY, owner-ruled addition (OD-0091, OD-0076; first-release voice typing, VT-11, voice-SPEC V-7.5): one CSS-like transition of a number.
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

/// <summary>
/// A number that moves from where it is to a target along a named curve over a duration, as a browser's CSS transition does: a target set while it moves starts again from the
/// value shown, and the same target set again changes nothing. It holds instants, not a clock: <see cref="Value"/> is a function of the time asked, so a frame, a test or a
/// replay reads the same number. A move with no curve, or one asked to be still (Reduce Motion), lands at once.
/// </summary>
internal struct DictationTween
{
    private double from;
    private double to;
    private long start;
    private Animation? curve;

    public DictationTween(double value)
    {
        from = to = value;
        start = 0;
        curve = null;
    }

    /// <summary>Where the number is going (or is).</summary>
    public readonly double Target => to;

    /// <summary>The number at <paramref name="now"/>: the start before the move begins, the target from its end on.</summary>
    public readonly double Value(ContinuousInstant now)
    {
        if (curve is null) return to;
        var elapsed = TimeSpan.FromTicks(now.Ticks - start).TotalSeconds;
        if (elapsed >= curve.Duration) return to;
        if (elapsed <= 0) return from;
        return from + (to - from) * curve.Progress(elapsed);
    }

    /// <summary>Whether the move is still under way at <paramref name="now"/>.</summary>
    public readonly bool Running(ContinuousInstant now) => curve is not null && TimeSpan.FromTicks(now.Ticks - start).TotalSeconds < curve.Duration;

    /// <summary>Goes to <paramref name="target"/> from the value shown at <paramref name="now"/>, on <paramref name="along"/>; at once when <paramref name="still"/> or there is no curve.</summary>
    public void Go(double target, ContinuousInstant now, Animation? along, bool still)
    {
        if (target == to) return; // the same target: a browser does not restart the move
        var shown = Value(now);
        if (still || along is null)
        {
            from = to = target;
            curve = null;
            return;
        }
        from = shown;
        to = target;
        start = now.Ticks;
        curve = along;
    }
}
