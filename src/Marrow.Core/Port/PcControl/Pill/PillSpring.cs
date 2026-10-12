// PORT-ONLY: Tactus pill v2, the travel spring (OD-0145, ED-NEW-pcctl-13 item 6; DESIGN.md 10.3). One critically damped
// spring for both axes, in closed form: x(t) = e^(-w t) (x0 + (v0 + w x0) t), with x0 the start offset from the target and
// v0 the start velocity. Its value at any time is computed, never integrated, so rendering, hit testing and a grab read
// the same curve. Its stiffness depends on the distance so that, from rest, every travel is within 1 DIP of its anchor
// 400 ms after it starts: short moves are gentle, long ones brisk, all land together. It keeps the release velocity
// whole; when that would carry it more than 3 DIP past the anchor (or, thrown outward, past the release point) it is made
// stiffer instead, so a hard throw lands sooner rather than slinging past.
namespace Marrow.Core.Port.PcControl.Pill;

/// <summary>A point or velocity in DIP (or DIP per second).</summary>
public readonly record struct PillVector(double X, double Y)
{
    public static readonly PillVector Zero = new(0, 0);

    public double Length => Math.Sqrt((X * X) + (Y * Y));

    public static PillVector operator +(PillVector a, PillVector b) => new(a.X + b.X, a.Y + b.Y);

    public static PillVector operator -(PillVector a, PillVector b) => new(a.X - b.X, a.Y - b.Y);

    public static PillVector operator *(PillVector a, double k) => new(a.X * k, a.Y * k);
}

/// <summary>One travel of the pill: from where it was, with the velocity it had, to its target, on one closed-form curve.</summary>
public sealed class PillSpring
{
    /// <summary>From rest, every travel is within <see cref="LandingDistance"/> of its target this long after it starts.</summary>
    public const double LandingSeconds = 0.4;

    /// <summary>The landing tolerance and the visual settle distance (DIP).</summary>
    public const double LandingDistance = 1;

    /// <summary>Distances under this travel as if they were this long, so a tiny move is never twitchy.</summary>
    public const double MinimumDistance = 60;

    /// <summary>The most a travel may pass its anchor, or its release point when thrown outward (DIP).</summary>
    public const double MaxExcursion = 3;

    /// <summary>Visually settled: within 1 DIP and slower than this (DIP/s). Every policy waits for it.</summary>
    public const double SettledSpeed = 20;

    /// <summary>The curve's end: within 0.1 physical pixel and slower than this (DIP/s); the value is then the target.</summary>
    public const double EndSpeed = 5;

    public const double EndPixels = 0.1;

    private PillSpring(double start, PillVector from, PillVector target, PillVector velocity, double omega, double distanceOmega)
    {
        StartTime = start;
        From = from;
        Target = target;
        StartVelocity = velocity;
        Omega = omega;
        DistanceOmega = distanceOmega;
    }

    /// <summary>When the travel started (seconds, on the caller's clock).</summary>
    public double StartTime { get; }

    public PillVector From { get; }

    public PillVector Target { get; }

    /// <summary>The velocity it started with, kept whole (DIP/s).</summary>
    public PillVector StartVelocity { get; }

    /// <summary>The spring's natural frequency w (1/s), the larger of the distance's and the one that caps the excursion.</summary>
    public double Omega { get; }

    /// <summary>The distance's own w, before any stiffening.</summary>
    public double DistanceOmega { get; }

    /// <summary>The response (2 pi / w, seconds).</summary>
    public double Response => 2 * Math.PI / Omega;

    /// <summary>A travel from <paramref name="from"/> moving at <paramref name="velocity"/> to <paramref name="target"/>, starting at <paramref name="now"/>.</summary>
    public static PillSpring Travel(double now, PillVector from, PillVector target, PillVector velocity)
    {
        var offset = from - target;
        var distanceOmega = OmegaForDistance(offset.Length);
        var omega = Math.Max(Stiffened(offset.X, velocity.X, distanceOmega), Stiffened(offset.Y, velocity.Y, distanceOmega));
        return new PillSpring(now, from, target, velocity, omega, distanceOmega);
    }

    /// <summary>A new travel to <paramref name="target"/> from where this one is at <paramref name="now"/>, keeping its velocity (no jump).</summary>
    public PillSpring Retarget(double now, PillVector target) => Travel(now, Value(now), target, Velocity(now));

    /// <summary>The position at <paramref name="now"/>.</summary>
    public PillVector Value(double now)
    {
        var t = Math.Max(0, now - StartTime);
        var offset = From - Target;
        return Target + new PillVector(Offset(offset.X, StartVelocity.X, Omega, t), Offset(offset.Y, StartVelocity.Y, Omega, t));
    }

    /// <summary>The velocity at <paramref name="now"/> (DIP/s).</summary>
    public PillVector Velocity(double now)
    {
        var t = Math.Max(0, now - StartTime);
        var offset = From - Target;
        return new PillVector(Speed(offset.X, StartVelocity.X, Omega, t), Speed(offset.Y, StartVelocity.Y, Omega, t));
    }

    /// <summary>Within 1 DIP and under 20 DIP/s: what auto-expand, saving and the dodge guard wait for.</summary>
    public bool VisuallySettled(double now) => (Value(now) - Target).Length <= LandingDistance && Velocity(now).Length < SettledSpeed;

    /// <summary>The curve's end: within 0.1 physical pixel and under 5 DIP/s; from here the pill sits on the target.</summary>
    public bool Ended(double now, double pixelsPerDip) =>
        (Value(now) - Target).Length * pixelsPerDip <= EndPixels && Velocity(now).Length < EndSpeed;

    /// <summary>The value to present: the curve until it ends, then exactly the target (an invisible step).</summary>
    public PillVector Presented(double now, double pixelsPerDip) => Ended(now, pixelsPerDip) ? Target : Value(now);

    /// <summary>
    /// The w that brings a travel of <paramref name="distance"/> from rest within 1 DIP in 400 ms: d e^(-u) (1 + u) = 1, w = u / 0.4.
    /// Distances under 60 DIP use 60.
    /// </summary>
    public static double OmegaForDistance(double distance)
    {
        var d = Math.Max(MinimumDistance, distance);
        // f(u) = ln d - u + ln(1 + u) falls from ln d > 0 at u = 0 toward minus infinity; bisect for its root.
        var low = 0.0;
        var high = 64.0;
        for (var i = 0; i < 200; i++)
        {
            var mid = (low + high) / 2;
            if (Math.Log(d) - mid + Math.Log(1 + mid) > 0) low = mid;
            else high = mid;
        }
        return (low + high) / 2 / LandingSeconds;
    }

    /// <summary>
    /// The exact excursion of one axis: how far past the target it swings (thrown inward), or how far past its start it
    /// goes (thrown outward, or from the target itself). The curve has at most one extremum, so this is exact.
    /// </summary>
    public static double Excursion(double x0, double v0, double omega)
    {
        if (v0 == 0) return 0;
        var b = v0 + (omega * x0);
        // v(t) = e^(-w t) (v0 - w b t) is zero once, at t = v0 / (w b), when that is ahead.
        if (b == 0) return 0;
        var t = v0 / (omega * b);
        if (t <= 0) return 0;
        var extreme = Offset(x0, v0, omega, t);
        var outward = x0 == 0 || Math.Sign(v0) == Math.Sign(x0);
        if (outward) return Math.Max(0, Math.Abs(extreme) - Math.Abs(x0));
        return Math.Sign(extreme) != Math.Sign(x0) ? Math.Abs(extreme) : 0;
    }

    /// <summary>
    /// The smallest w at least <paramref name="omega"/> whose excursion is at most <paramref name="cap"/> (3 DIP for a
    /// travel), by bisection; the velocity is kept.
    /// </summary>
    public static double Stiffened(double x0, double v0, double omega, double cap = MaxExcursion)
    {
        if (Excursion(x0, v0, omega) <= cap) return omega;
        var low = omega;
        var high = omega * 2;
        while (Excursion(x0, v0, high) > cap && high < 1e7) high *= 2;
        for (var i = 0; i < 100; i++)
        {
            var mid = (low + high) / 2;
            if (Excursion(x0, v0, mid) > cap) low = mid;
            else high = mid;
        }
        return high;
    }

    private static double Offset(double x0, double v0, double w, double t) => Math.Exp(-w * t) * (x0 + ((v0 + (w * x0)) * t));

    private static double Speed(double x0, double v0, double w, double t) => Math.Exp(-w * t) * (v0 - (w * (v0 + (w * x0)) * t));
}
