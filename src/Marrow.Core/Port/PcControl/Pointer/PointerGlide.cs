// PORT-ONLY: Tactus pointer, its glide (OD-0144, ED-NEW-pcctl-21; DESIGN.md 4.5 "The Tactus pointer", canvas 01 and 02:
// "glide 260 ms"). The pointer moves between targets on one critically damped spring in closed form, the same kind as the
// pill's travel (PillSpring): x(t) = e^(-w t) (x0 + (v0 + w x0) t). From rest it is within 0.5 DIP of its target 180 ms
// after it starts for a move of 40 DIP or less, 260 ms for 800 DIP or more, and in between in proportion, so it is never
// linear, short hops are quick and long ones are not rushed. A new target mid-glide keeps the pointer's place and velocity
// (no jump); when that velocity would carry it more than 2 DIP past the target the spring is made stiffer instead.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Port.PcControl.Pointer;

public sealed class PointerGlide
{
    /// <summary>A glide of <see cref="ShortDistance"/> or less lands in this long (seconds).</summary>
    public const double ShortSeconds = 0.18;

    /// <summary>A glide of <see cref="LongDistance"/> or more lands in this long (seconds).</summary>
    public const double LongSeconds = 0.26;

    public const double ShortDistance = 40;
    public const double LongDistance = 800;

    /// <summary>Landed: within this distance of the target (DIP).</summary>
    public const double Landing = 0.5;

    /// <summary>The most a glide may pass its target when retargeted with speed (DIP).</summary>
    public const double MaxExcursion = 2;

    private PointerGlide(double start, PillVector from, PillVector target, PillVector velocity, double omega)
    {
        StartTime = start;
        From = from;
        Target = target;
        StartVelocity = velocity;
        Omega = omega;
    }

    public double StartTime { get; }

    public PillVector From { get; }

    public PillVector Target { get; }

    public PillVector StartVelocity { get; }

    /// <summary>The spring's natural frequency (1/s).</summary>
    public double Omega { get; }

    /// <summary>A pointer resting at <paramref name="at"/>.</summary>
    public static PointerGlide Still(double now, PillVector at) => new(now, at, at, PillVector.Zero, OmegaFor(0));

    /// <summary>A glide from <paramref name="from"/> moving at <paramref name="velocity"/> (DIP/s) to <paramref name="target"/>.</summary>
    public static PointerGlide Start(double now, PillVector from, PillVector target, PillVector velocity)
    {
        var offset = from - target;
        var omega = OmegaFor(offset.Length);
        omega = Math.Max(PillSpring.Stiffened(offset.X, velocity.X, omega, MaxExcursion), PillSpring.Stiffened(offset.Y, velocity.Y, omega, MaxExcursion));
        return new PointerGlide(now, from, target, velocity, omega);
    }

    /// <summary>A new glide to <paramref name="target"/> from where this one is at <paramref name="now"/>, with its velocity.</summary>
    public PointerGlide Retarget(double now, PillVector target) => Start(now, Value(now), target, Velocity(now));

    /// <summary>How long a glide of <paramref name="distance"/> takes from rest to land (seconds).</summary>
    public static double LandingSeconds(double distance) =>
        ShortSeconds + ((LongSeconds - ShortSeconds) * Math.Clamp((distance - ShortDistance) / (LongDistance - ShortDistance), 0, 1));

    /// <summary>The w that lands a glide of <paramref name="distance"/> from rest within 0.5 DIP in its landing time.</summary>
    public static double OmegaFor(double distance)
    {
        // d e^(-u) (1 + u) = 0.5 with u = w T; the left side falls from d > 0.5 toward 0, so bisect for u.
        var d = Math.Max(ShortDistance, distance);
        var low = 0.0;
        var high = 64.0;
        for (var i = 0; i < 200; i++)
        {
            var mid = (low + high) / 2;
            if (Math.Log(d / Landing) - mid + Math.Log(1 + mid) > 0) low = mid;
            else high = mid;
        }
        return (low + high) / 2 / LandingSeconds(distance);
    }

    public PillVector Value(double now)
    {
        var t = Math.Max(0, now - StartTime);
        var offset = From - Target;
        return Target + new PillVector(Offset(offset.X, StartVelocity.X, t), Offset(offset.Y, StartVelocity.Y, t));
    }

    public PillVector Velocity(double now)
    {
        var t = Math.Max(0, now - StartTime);
        var offset = From - Target;
        return new PillVector(Speed(offset.X, StartVelocity.X, t), Speed(offset.Y, StartVelocity.Y, t));
    }

    /// <summary>Within 0.5 DIP of the target and slower than 20 DIP/s.</summary>
    public bool Landed(double now) => (Value(now) - Target).Length <= Landing && Velocity(now).Length < PillSpring.SettledSpeed;

    /// <summary>The curve's end: within 0.05 DIP and slower than 5 DIP/s; from here the pointer is drawn on its target.</summary>
    public bool Ended(double now) => (Value(now) - Target).Length <= 0.05 && Velocity(now).Length < PillSpring.EndSpeed;

    /// <summary>Where to draw it: the curve until it ends, then exactly the target (an invisible step).</summary>
    public PillVector Presented(double now) => Ended(now) ? Target : Value(now);

    /// <summary>When it lands (seconds): the first millisecond from which it stays landed; its start when it is still.</summary>
    public double LandsAt()
    {
        if (Landed(StartTime)) return StartTime;
        for (var ms = 1; ms <= 2000; ms++)
        {
            var at = StartTime + (ms / 1000.0);
            if (Landed(at)) return at;
        }
        return StartTime + 2;
    }

    private double Offset(double x0, double v0, double t) => Math.Exp(-Omega * t) * (x0 + ((v0 + (Omega * x0)) * t));

    private double Speed(double x0, double v0, double t) => Math.Exp(-Omega * t) * (v0 - (Omega * (v0 + (Omega * x0)) * t));
}
