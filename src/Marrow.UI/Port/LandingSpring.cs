// PORT-ONLY (ED-NEW-perf-motion-10): a critically damped spring that keeps its speed through every new target and lands on
// its target exactly, with no speed, at its rest time. The zoom glide and the sidebar's selection glide stepped such a spring
// frame by frame and, once what was left fell under a rest bound, put the value on the target: the last frame jumped what was
// left, up to five times the step before it at 144 Hz. Here each target starts a segment from the value and speed reached, solved
// in closed form, x(t) = target + (c1 + c2 t) e^(-rate t); the rest time is when the segment's envelope falls under the bound, and
// what is left there is folded into the segment's second half (MotionLanding), so the steps shrink to nothing.

namespace Marrow.UI.Port;

/// <summary>A critically damped spring on one axis that lands on its target (ED-NEW-perf-motion-10).</summary>
public struct LandingSpring
{
    private double c1;
    private double c2;
    private double rate;
    private double elapsed;
    private double restAt;
    private double endOffset;
    private double endSpeed;

    public LandingSpring(double position)
    {
        Position = position;
        Target = position;
    }

    /// <summary>The value now.</summary>
    public double Position { get; private set; }

    /// <summary>Its speed now, per second.</summary>
    public double Velocity { get; private set; }

    /// <summary>The target of the segment under way.</summary>
    public double Target { get; private set; }

    /// <summary>Whether the value has landed on its target.</summary>
    public readonly bool Landed => elapsed >= restAt;

    /// <summary>
    /// Starts a segment from the value and speed reached (the speed given a <paramref name="push"/> first) towards
    /// <paramref name="target"/>, at <paramref name="rate"/> per second, resting once what is left of it (and of its speed over
    /// the spring's time) is under <paramref name="rest"/>.
    /// </summary>
    public void Aim(double target, double rate, double rest, double push = 0)
    {
        Velocity += push;
        Target = target;
        this.rate = rate;
        c1 = Position - target;
        c2 = Velocity + rate * c1;
        elapsed = 0;
        restAt = RestTime(rest);
        endOffset = Offset(restAt);
        endSpeed = Speed(restAt);
    }

    /// <summary>Moves <paramref name="seconds"/> along the segment; true once it has landed (ED-117 (2): within 1 ms of its rest).</summary>
    public bool Advance(double seconds)
    {
        if (Landed)
        {
            Position = Target;
            Velocity = 0;
            return true;
        }
        elapsed += Math.Max(0, seconds);
        if (elapsed + 0.001 >= restAt)
        {
            elapsed = restAt;
            Position = Target;
            Velocity = 0;
            return true;
        }
        Position = Target + MotionLanding.Land(Offset(elapsed), elapsed, restAt, -endOffset, endSpeed);
        Velocity = MotionLanding.LandSpeed(Speed(elapsed), elapsed, restAt, -endOffset, endSpeed);
        return false;
    }

    /// <summary>Puts the value on its target at once, at rest.</summary>
    public void Settle()
    {
        Position = Target;
        Velocity = 0;
        elapsed = restAt;
    }

    private readonly double Offset(double t) => (c1 + c2 * t) * Math.Exp(-rate * t);

    private readonly double Speed(double t) => (c2 - rate * (c1 + c2 * t)) * Math.Exp(-rate * t);

    // The first time after which (|c1| + |c2| / rate + |c2| t) e^(-rate t), a bound on both the offset and the speed over the
    // spring's time, stays under rest; from rest that bound is the offset itself (a push of rate times the change), so the rest
    // time is the free spring's.
    private readonly double RestTime(double rest)
    {
        var k = rate;
        if (k <= 0) return 0;
        var a = Math.Abs(c1) + Math.Abs(c2) / k;
        var b = Math.Abs(c2);
        double Bound(double t) => (a + b * t) * Math.Exp(-k * t);
        var peak = b > 0 ? Math.Max(0, 1 / k - a / b) : 0;
        if (Bound(peak) <= rest) return 0;
        var low = peak;
        var high = peak + 1 / k;
        for (var i = 0; i < 64 && Bound(high) > rest; i++) high += high - peak;
        for (var i = 0; i < 64; i++)
        {
            var mid = (low + high) / 2;
            if (Bound(mid) > rest) low = mid;
            else high = mid;
        }
        return high;
    }
}
