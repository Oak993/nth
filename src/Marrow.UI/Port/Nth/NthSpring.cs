// PORT-ONLY: OD-0136 amendment 2 (ED-NEW-nth-motion). Step 3 "Value": a critically damped spring, stiffness 110, damping
// 2*sqrt(110) (about 21, no overshoot). ENGINEERING DECISION: solved in closed form from the moment its target last changed,
// so a value is a pure function of time: the render thread evaluates it at each frame without integrating, two frames at the
// same time agree, and tests read it at fixed clock times. Calm (reduced motion): step 4 "value jumps with a 0.1 s ease".
namespace Marrow.UI.Port.Nth;

/// <summary>A value easing toward <see cref="Target"/> from (<see cref="From"/>, <see cref="Velocity"/>) at <see cref="Start"/> seconds.</summary>
public readonly record struct NthSpring(double From, double Velocity, double Target, double Start, bool Calm = false)
{
    /// <summary>sqrt(stiffness): stiffness 110, critically damped.</summary>
    public static readonly double Omega = Math.Sqrt(110);

    /// <summary>The calm tween's length (step 4 "Calm").</summary>
    public const double CalmSeconds = .1;

    /// <summary>A value resting at <paramref name="value"/>.</summary>
    public static NthSpring At(double value) => new(value, 0, value, double.NegativeInfinity);

    public double Value(double now)
    {
        var dt = now - Start;
        if (double.IsNegativeInfinity(Start)) return Target;
        if (dt <= 0) return From;
        if (Calm) return From + (Target - From) * NthEase.Standard.Ease(dt / CalmSeconds);
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

    /// <summary>The same motion sent toward a new target at <paramref name="now"/>, keeping its value and speed.</summary>
    public NthSpring Retarget(double target, double now, bool calm = false) =>
        target == Target && calm == Calm ? this : new(Value(now), calm ? 0 : VelocityAt(now), target, now, calm);

    /// <summary>The page's rest test: within 0.0005 of the target and slower than 0.001 a second.</summary>
    public bool Settled(double now) =>
        double.IsNegativeInfinity(Start) || (Calm ? now - Start >= CalmSeconds : Math.Abs(Value(now) - Target) < .0005 && Math.Abs(VelocityAt(now)) < .001);
}
