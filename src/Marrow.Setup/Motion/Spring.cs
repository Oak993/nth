namespace Marrow.Setup.Motion;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, "Springs are interruptible"; 3b; unit U1, ED-NEW-inst-u1-1). A spring with a value, a
/// velocity and a target, described as the canvas describes its springs: a response (the period of the undamped motion,
/// in seconds) and a damping ratio (1 is critically damped: no overshoot). Retargeting changes only the target, so the
/// motion turns around from where it is with the speed it has; nothing restarts from an end state. Stepped with a fixed
/// small step, so its path does not depend on the frame rate.
/// </summary>
public sealed class Spring
{
    private const double Step = 1.0 / 480;

    public Spring(double response, double damping, double value = 0, double precision = 0.001)
    {
        Response = response;
        Damping = damping;
        Value = value;
        Target = value;
        Precision = precision;
    }

    /// <summary>The period of the undamped motion in seconds; a critically damped spring settles in about this long.</summary>
    public double Response
    {
        get;
        set => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>The damping ratio; 1 is critically damped.</summary>
    public double Damping
    {
        get;
        set => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>How close to the target, and how slow, counts as at rest.</summary>
    public double Precision { get; set; }

    public double Value { get; private set; }

    public double Velocity { get; private set; }

    public double Target { get; private set; }

    public bool IsAtRest => Math.Abs(Value - Target) <= Precision && Math.Abs(Velocity) <= Precision * 10;

    /// <summary>Changes only the target: the value and its speed carry on.</summary>
    public void Retarget(double target) => Target = target;

    /// <summary>Adds to the value without touching the target or the speed (a layout difference added to an offset, 3b's FLIP).</summary>
    public void Shift(double amount) => Value += amount;

    /// <summary>Sets value and target at once, at rest (setting a screen up before it shows).</summary>
    public void Snap(double value)
    {
        Value = value;
        Target = value;
        Velocity = 0;
    }

    /// <summary>Advances by <paramref name="seconds"/>. True while it still moves.</summary>
    public bool Advance(double seconds)
    {
        if (IsAtRest)
        {
            Value = Target;
            Velocity = 0;
            return false;
        }
        var omega = 2 * Math.PI / Response;
        var stiffness = omega * omega;
        var friction = 2 * Damping * omega;
        var left = Math.Max(0, seconds);
        while (left > 1e-9)
        {
            var h = Math.Min(Step, left);
            var acceleration = -stiffness * (Value - Target) - friction * Velocity;
            Velocity += acceleration * h;
            Value += Velocity * h;
            left -= h;
        }
        if (!IsAtRest) return true;
        Value = Target;
        Velocity = 0;
        return false;
    }
}

/// <summary>A CSS cubic-bezier timing curve: progress 0..1 in, eased progress out.</summary>
public sealed class CubicBezier
{
    private readonly double x1, y1, x2, y2;

    public CubicBezier(double x1, double y1, double x2, double y2)
    {
        this.x1 = x1;
        this.y1 = y1;
        this.x2 = x2;
        this.y2 = y2;
    }

    /// <summary>The canvas's screen change: <c>cubic-bezier(.2,.8,.2,1)</c>.</summary>
    public static CubicBezier ScreenChange { get; } = new(.2, .8, .2, 1);

    /// <summary>CSS <c>ease</c>.</summary>
    public static CubicBezier Ease { get; } = new(.25, .1, .25, 1);

    public static CubicBezier Linear { get; } = new(0, 0, 1, 1);

    public double Evaluate(double progress)
    {
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;
        // Newton's method on x(t) = progress, then bisection if it stalls.
        var t = progress;
        for (var i = 0; i < 8; i++)
        {
            var x = Curve(x1, x2, t) - progress;
            if (Math.Abs(x) < 1e-6) return Curve(y1, y2, t);
            var slope = Slope(x1, x2, t);
            if (Math.Abs(slope) < 1e-6) break;
            t = Math.Clamp(t - x / slope, 0, 1);
        }
        double low = 0, high = 1;
        t = progress;
        for (var i = 0; i < 30; i++)
        {
            var x = Curve(x1, x2, t);
            if (Math.Abs(x - progress) < 1e-7) break;
            if (x < progress) low = t;
            else high = t;
            t = (low + high) / 2;
        }
        return Curve(y1, y2, t);
    }

    private static double Curve(double a, double b, double t) => 3 * a * t * (1 - t) * (1 - t) + 3 * b * t * t * (1 - t) + t * t * t;

    private static double Slope(double a, double b, double t) => 3 * a * (1 - t) * (1 - t) + 6 * (b - a) * t * (1 - t) + 3 * (1 - b) * t * t;
}

/// <summary>
/// A timed move from wherever the value is to a target along a curve, after a delay. Retargeting starts the next move from
/// the current value, so a reversal mid-flight never jumps. Used where the canvas gives a duration and a curve instead of a
/// spring (the screen change).
/// </summary>
public sealed class Tween
{
    private double from;
    private double elapsed;
    private double duration;
    private CubicBezier curve = CubicBezier.Linear;

    public Tween(double value)
    {
        Value = value;
        Target = value;
        from = value;
    }

    public double Value { get; private set; }

    public double Target { get; private set; }

    public bool IsAtRest => Value == Target;

    public void Retarget(double target, double seconds, CubicBezier ease, double delay = 0)
    {
        ArgumentNullException.ThrowIfNull(ease);
        from = Value;
        Target = target;
        duration = Math.Max(seconds, 1e-6);
        elapsed = -Math.Max(0, delay);
        curve = ease;
        if (seconds <= 0 && delay <= 0) Value = target;
    }

    public void Snap(double value)
    {
        Value = value;
        Target = value;
        from = value;
        elapsed = 0;
    }

    public bool Advance(double seconds)
    {
        if (IsAtRest) return false;
        elapsed += seconds;
        if (elapsed <= 0) return true;
        var progress = elapsed / duration;
        if (progress >= 1)
        {
            Value = Target;
            return false;
        }
        Value = from + (Target - from) * curve.Evaluate(progress);
        return true;
    }
}
