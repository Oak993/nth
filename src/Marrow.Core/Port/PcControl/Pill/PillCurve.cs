// PORT-ONLY: Tactus pill v2, the pill's other curves and the Reduce Motion policy (OD-0145, ED-NEW-pcctl-13; DESIGN.md 10.3,
// 10.4, 10.5). Every spring in the pill is critically damped and closed-form, like the travel: the width (response 0.28 s),
// the card's expand (0.30 s) and collapse (0.26 s), the lift while held (0.20 s), the size change across mixed DPI (0.12 s).
// With Reduce Motion every motion changes and none is left half-reduced: a travel of up to 200 DIP is a 150 ms ease
// (cubic-bezier 0.2, 0, 0, 1), a longer one a 120 ms fade through (one step at its middle); the morph is a 150 ms fade
// through; text, rows, the hint, the badge, the pause glyph and the lift are 150 ms opacity; nothing spins or breathes; the
// drag stays 1:1.
namespace Marrow.Core.Port.PcControl.Pill;

public enum PillCurveKind
{
    /// <summary>A critically damped spring in closed form.</summary>
    Spring,

    /// <summary>Reduce Motion's ease, cubic-bezier(0.2, 0, 0, 1).</summary>
    Ease,

    /// <summary>Reduce Motion's fade through: the value steps at the middle while the opacity dips to zero.</summary>
    FadeThrough,
}

/// <summary>One scalar motion: a spring from a value and velocity to a target, or one of Reduce Motion's timed changes. Seconds throughout.</summary>
public sealed record PillCurve(double From, double To, double Velocity0, double Omega, double Start, PillCurveKind Kind, double Duration)
{
    /// <summary>The springs' responses (2 pi / w, seconds).</summary>
    public const double WidthResponse = 0.28;
    public const double ExpandResponse = 0.30;
    public const double CollapseResponse = 0.26;
    public const double LiftResponse = 0.20;
    public const double DpiResponse = 0.12;

    /// <summary>Reduce Motion's durations (seconds).</summary>
    public const double ReducedEase = 0.150;
    public const double ReducedJump = 0.120;
    public const double ReducedFade = 0.150;

    /// <summary>Reduce Motion: travels up to this long ease; longer ones fade through.</summary>
    public const double ReducedEaseDistance = 200;

    /// <summary>A value held still.</summary>
    public static PillCurve Still(double value, double now) => new(value, value, 0, 20, now, PillCurveKind.Spring, 0);

    /// <summary>A spring of <paramref name="response"/> seconds, stiffened if its velocity would carry it more than <paramref name="cap"/> past.</summary>
    public static PillCurve Spring(double from, double to, double velocity, double response, double now, double cap = 0.5)
    {
        var omega = PillSpring.Stiffened(from - to, velocity, 2 * Math.PI / response, cap);
        return new PillCurve(from, to, velocity, omega, now, PillCurveKind.Spring, 0);
    }

    /// <summary>Reduce Motion's ease over <paramref name="duration"/> seconds, no velocity.</summary>
    public static PillCurve Ease(double from, double to, double now, double duration = ReducedEase) => new(from, to, 0, 0, now, PillCurveKind.Ease, duration);

    /// <summary>Reduce Motion's fade through over <paramref name="duration"/> seconds: the value steps at the middle.</summary>
    public static PillCurve FadeThrough(double from, double to, double now, double duration) => new(from, to, 0, 0, now, PillCurveKind.FadeThrough, duration);

    /// <summary>The spring a size change uses, or Reduce Motion's ease of the same change.</summary>
    public static PillCurve Size(double from, double to, double velocity, double response, double now, bool reduceMotion, double cap = 0.5) =>
        reduceMotion ? Ease(from, to, now) : Spring(from, to, velocity, response, now, cap);

    public double Value(double now)
    {
        var t = now - Start;
        switch (Kind)
        {
            case PillCurveKind.Ease:
                return From + ((To - From) * PillEase.Standard(Math.Clamp(t / Duration, 0, 1)));
            case PillCurveKind.FadeThrough:
                return t < Duration / 2 ? From : To;
            default:
                if (t <= 0) return From;
                return To + (Math.Exp(-Omega * t) * ((From - To) + ((Velocity0 + (Omega * (From - To))) * t)));
        }
    }

    public double Velocity(double now)
    {
        if (Kind != PillCurveKind.Spring) return 0;
        var t = now - Start;
        if (t <= 0) return Velocity0;
        var x0 = From - To;
        return Math.Exp(-Omega * t) * (Velocity0 - (Omega * (Velocity0 + (Omega * x0)) * t));
    }

    /// <summary>The opacity this curve asks for: below 1 only in a fade through (out over the first half, in over the second).</summary>
    public double Opacity(double now)
    {
        if (Kind != PillCurveKind.FadeThrough) return 1;
        var p = Math.Clamp((now - Start) / Duration, 0, 1);
        return Math.Abs(1 - (2 * p));
    }

    /// <summary>Within 1 (DIP) and under 20 per second, or a timed change that has run its time.</summary>
    public bool Settled(double now)
    {
        if (Kind != PillCurveKind.Spring) return now - Start >= Duration;
        if (From == To && Velocity0 == 0) return true;
        return now > Start && Math.Abs(Value(now) - To) < PillSpring.LandingDistance && Math.Abs(Velocity(now)) < PillSpring.SettledSpeed;
    }

    /// <summary>The curve's end: within 0.1 and under 5 per second (the value then shows the target), or its time run.</summary>
    public bool Done(double now)
    {
        if (Kind != PillCurveKind.Spring) return now - Start >= Duration;
        if (From == To && Velocity0 == 0) return true;
        return now > Start && Math.Abs(Value(now) - To) < 0.1 && Math.Abs(Velocity(now)) < PillSpring.EndSpeed;
    }
}

/// <summary>cubic-bezier(0.2, 0, 0, 1), Reduce Motion's ease, solved for x by bisection.</summary>
public static class PillEase
{
    public static double Standard(double progress) => CubicBezier(0.2, 0, 0, 1, progress);

    public static double CubicBezier(double x1, double y1, double x2, double y2, double progress)
    {
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;
        static double B(double a, double b, double t) => (3 * a * t * (1 - t) * (1 - t)) + (3 * b * t * t * (1 - t)) + (t * t * t);
        double low = 0, high = 1, mid = progress;
        for (var i = 0; i < 40; i++)
        {
            mid = (low + high) / 2;
            if (B(x1, x2, mid) < progress) low = mid;
            else high = mid;
        }
        return B(y1, y2, mid);
    }
}

/// <summary>A travel of the pill in both axes: the shared spring, or with Reduce Motion an ease (up to 200 DIP) or a fade through.</summary>
public sealed class PillTravel
{
    private PillTravel(PillSpring? spring, PillCurve? x, PillCurve? y, PillVector target)
    {
        Spring = spring;
        X = x;
        Y = y;
        Target = target;
    }

    /// <summary>The spring, when motion is allowed.</summary>
    public PillSpring? Spring { get; }

    /// <summary>Reduce Motion's two timed changes.</summary>
    public PillCurve? X { get; }

    public PillCurve? Y { get; }

    public PillVector Target { get; }

    public static PillTravel Start(double now, PillVector from, PillVector target, PillVector velocity, bool reduceMotion)
    {
        if (!reduceMotion) return new PillTravel(PillSpring.Travel(now, from, target, velocity), null, null, target);
        var distance = (target - from).Length;
        if (distance <= PillCurve.ReducedEaseDistance)
            return new PillTravel(null, PillCurve.Ease(from.X, target.X, now), PillCurve.Ease(from.Y, target.Y, now), target);
        return new PillTravel(null, PillCurve.FadeThrough(from.X, target.X, now, PillCurve.ReducedJump), PillCurve.FadeThrough(from.Y, target.Y, now, PillCurve.ReducedJump), target);
    }

    public PillVector Value(double now) => Spring?.Value(now) ?? new PillVector(X!.Value(now), Y!.Value(now));

    public PillVector Velocity(double now) => Spring?.Velocity(now) ?? PillVector.Zero;

    /// <summary>The pill's opacity during the travel (a fade through only).</summary>
    public double Opacity(double now) => X?.Opacity(now) ?? 1;

    public bool VisuallySettled(double now) => Spring?.VisuallySettled(now) ?? X!.Settled(now);

    /// <summary>The value to draw: the curve, then exactly the target once it ends.</summary>
    public PillVector Presented(double now, double pixelsPerDip) => Spring?.Presented(now, pixelsPerDip) ?? Value(now);
}

/// <summary>The card's morph out of the pill and back (DESIGN.md 10.4).</summary>
public static class PillMorph
{
    /// <summary>The card's width: the pill's widest, so a full pill never narrows on its way to a card.</summary>
    public const double CardWidth = 440;

    /// <summary>The card's height cap.</summary>
    public const double MaxCardHeight = 420;

    /// <summary>The body fades in over this span of an expand (seconds), rising this far (DIP).</summary>
    public const double BodyInFrom = 0.120;
    public const double BodyInTo = 0.300;
    public const double BodyRise = 6;

    /// <summary>The body fades out over the first 100 ms of a collapse; the rows under the pill fold in 100 ms.</summary>
    public const double BodyOut = 0.100;
    public const double RowsFold = 0.100;

    /// <summary>The card's height: header plus body, at most min(420, work height - 16 - the pill's top within the work area).</summary>
    public static double CardHeight(double content, double workHeight, double pillTop) =>
        Math.Max(PillLayout.Height, Math.Min(content, Math.Min(MaxCardHeight, workHeight - 16 - pillTop)));

    /// <summary>The card's left edge: the pill's left kept for Left, its right for Right, its centre for Centre.</summary>
    public static double CardLeft(PillAnchor anchor, double pillLeft, double pillWidth, double cardWidth = CardWidth) => anchor switch
    {
        PillAnchor.Left => pillLeft,
        PillAnchor.Right => pillLeft + pillWidth - cardWidth,
        _ => pillLeft + (pillWidth / 2) - (cardWidth / 2),
    };

    /// <summary>The body's opacity <paramref name="elapsed"/> seconds into an expand or a collapse.</summary>
    public static double BodyOpacity(bool expanding, double elapsed)
    {
        if (expanding) return Math.Clamp((elapsed - BodyInFrom) / (BodyInTo - BodyInFrom), 0, 1);
        return Math.Clamp(1 - (elapsed / BodyOut), 0, 1);
    }

    /// <summary>How far below its place the body is during an expand (DIP): 6 at the start of its fade, 0 at the end.</summary>
    public static double BodyOffset(double elapsed) => BodyRise * (1 - BodyOpacity(true, elapsed));
}
