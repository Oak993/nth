// PORT-ONLY (ED-NEW-perf-motion-10; ED-333's UNRESOLVED run-out, ruled by the lead 2026-10-13: "no motion may end with a step
// larger than its previous frame's step"). A spring is cut at its settling time with a little of its travel left (a critically
// damped one 0.79 %, ln 1000 of its time constant), and the last frame jumped that remainder: four to seventeen times the step
// before it. The remainder is folded into the second half of the run instead: a correction that is nothing, with no speed and
// no acceleration, where it starts, and that brings the value exactly onto the target with no speed at the settling time. The
// duration and the first half of every curve are unchanged.

namespace Marrow.UI.Port;

/// <summary>The folding of a spring's remainder into the end of its run (ED-NEW-perf-motion-10).</summary>
public static class MotionLanding
{
    /// <summary>The share of the run, counted back from its end, over which the remainder is folded in.</summary>
    public const double Window = 0.5;

    /// <summary>
    /// A curve <paramref name="free"/> that is <paramref name="left"/> short of its end at <paramref name="end"/>, still moving at
    /// <paramref name="speed"/> there, made to land: its value at <paramref name="seconds"/>, equal to the free curve over the
    /// first half, exactly the end with no speed at <paramref name="end"/>.
    /// </summary>
    public static double Land(double free, double seconds, double end, double left, double speed)
    {
        var window = Window * end;
        if (window <= 0) return free;
        var u = (seconds - (end - window)) / window;
        if (u <= 0) return free;
        if (u >= 1) return free + left;
        return free + left * Arrive(u) - speed * window * Settle(u);
    }

    /// <summary>The speed of <see cref="Land"/> at <paramref name="seconds"/>: the free curve's speed, plus the correction's.</summary>
    public static double LandSpeed(double freeSpeed, double seconds, double end, double left, double speed)
    {
        var window = Window * end;
        if (window <= 0) return freeSpeed;
        var u = (seconds - (end - window)) / window;
        if (u <= 0) return freeSpeed;
        if (u >= 1) return 0;
        return freeSpeed + left * ArriveSpeed(u) / window - speed * SettleSpeed(u);
    }

    /// <summary>
    /// A curve <paramref name="free"/> that last crosses its end 1 at <paramref name="from"/> (late in the run, an underdamped spring
    /// past 1 there) made to land: equal to the free curve up to <paramref name="from"/>, then its distance from 1 drawn in to nothing
    /// at <paramref name="end"/>, (free - 1)(1 - s(u)), so it keeps the free curve's overshoot and crossings and arrives with no speed.
    /// </summary>
    public static double Narrow(double free, double seconds, double from, double end)
    {
        if (seconds <= from || end <= from) return free;
        var u = (seconds - from) / (end - from);
        if (u >= 1) return 1;
        return 1 + (free - 1) * (1 - Arrive(u));
    }

    /// <summary>The speed of <see cref="Narrow"/> at <paramref name="seconds"/>.</summary>
    public static double NarrowSpeed(double free, double freeSpeed, double seconds, double from, double end)
    {
        if (seconds <= from || end <= from) return freeSpeed;
        var u = (seconds - from) / (end - from);
        if (u >= 1) return 0;
        return freeSpeed * (1 - Arrive(u)) - (free - 1) * ArriveSpeed(u) / (end - from);
    }

    // 0 at 0 and 1 at 1, with no speed and no acceleration at either end (the smootherstep).
    private static double Arrive(double u) => u * u * u * (10 - 15 * u + 6 * u * u);
    private static double ArriveSpeed(double u) => 30 * u * u * (1 - u) * (1 - u);

    // 0 at both ends with no acceleration there, no speed at 0 and a unit speed at 1: it takes the free curve's last speed away.
    private static double Settle(double u) => u * u * u * (-4 + 7 * u - 3 * u * u);
    private static double SettleSpeed(double u) => u * u * (-12 + 28 * u - 15 * u * u);
}
