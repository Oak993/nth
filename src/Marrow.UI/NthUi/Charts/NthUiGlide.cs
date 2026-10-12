// PORT-ONLY: web surfaces W16b (DESIGN.md 2.6 interruptible springs, 10 Reduce Motion; ED-NEW-web-w16b-1). No Swift counterpart.
// The spring of a thing that follows the pointer: a chart's readout and cursor. The nth value spring (stiffness 110) takes over
// a second to settle across a chart, which is right for a number changing and too slow for a bubble chasing the pointer, so
// this one is the same critically damped, closed-form, retargetable spring, three times as quick (omega 22: it covers
// half the way in 0.08 s and is within a pixel after about 0.35 s). Reduce Motion takes the same 0.1 s ease as the value spring.
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi.Charts;

/// <summary>A critically damped spring on a position in DIPs, evaluated in closed form.</summary>
internal readonly record struct NthUiGlide(double From, double Velocity, double Target, double Start, bool Calm = false)
{
    /// <summary>The spring's angular frequency.</summary>
    public const double Omega = 22;

    /// <summary>A position at rest at <paramref name="value"/>.</summary>
    public static NthUiGlide At(double value) => new(value, 0, value, double.NegativeInfinity);

    public double Value(double now)
    {
        var dt = now - Start;
        if (double.IsNegativeInfinity(Start)) return Target;
        if (dt <= 0) return From;
        if (Calm) return From + (Target - From) * NthEase.Standard.Ease(dt / NthSpring.CalmSeconds);
        var x0 = From - Target;
        var b = Velocity + Omega * x0;
        return Target + (x0 + b * dt) * Math.Exp(-Omega * dt);
    }

    public double VelocityAt(double now)
    {
        var dt = now - Start;
        if (double.IsNegativeInfinity(Start) || Calm || dt < 0) return 0;
        var x0 = From - Target;
        var b = Velocity + Omega * x0;
        return (b - Omega * (x0 + b * dt)) * Math.Exp(-Omega * dt);
    }

    /// <summary>The same motion sent toward <paramref name="target"/> from the position and speed it has at <paramref name="now"/>.</summary>
    public NthUiGlide Retarget(double target, double now, bool calm = false) =>
        target == Target && calm == Calm ? this : new NthUiGlide(Value(now), calm ? 0 : VelocityAt(now), target, now, calm);

    /// <summary>Within a fiftieth of a DIP of the target and slower than a quarter of a DIP a second.</summary>
    public bool Settled(double now) =>
        double.IsNegativeInfinity(Start) || (Calm ? now - Start >= NthSpring.CalmSeconds : Math.Abs(Value(now) - Target) < .02 && Math.Abs(VelocityAt(now)) < .25);

    /// <summary>The position at <paramref name="now"/>, exactly the target once settled.</summary>
    public double Rest(double now) => Settled(now) ? Target : Value(now);
}
