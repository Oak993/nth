// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-4, voice-SPEC V-11.4, DESIGN voice-composer.js:111-134).
namespace Marrow.Core.Port.Voice.Orb;

/// <summary>The easing helpers of the voice composer page, as written there.</summary>
internal static class OrbEasing
{
    /// <summary>SwiftUI <c>spring(response:dampingFraction:)</c> integrated in 4 ms substeps (<c>spring(o, target, dt, resp, zeta)</c>).</summary>
    internal static void Spring(ref double position, ref double velocity, double target, double dt, double response, double damping)
    {
        var w = 2 * Math.PI / response;
        var k = w * w;
        var c = 2 * damping * w;
        var n = Math.Max(1, Math.Ceiling(dt / 0.004));
        var h = dt / n;
        for (var i = 0; i < n; i++)
        {
            velocity += (-k * (position - target) - c * velocity) * h;
            position += velocity * h;
        }

        if (Math.Abs(position - target) < 1e-4 && Math.Abs(velocity) < 1e-3)
        {
            position = target;
            velocity = 0;
        }
    }

    /// <summary><c>follow(cur, target, dt, up, down)</c>: an exponential follower with a time constant for each direction.</summary>
    internal static double Follow(double current, double target, double dt, double up, double down) =>
        current + (target - current) * (1 - Math.Exp(-dt / Math.Max(1e-3, target > current ? up : down)));

    /// <summary><c>smooth(x)</c>: smoothstep, clamped.</summary>
    internal static double Smooth(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);

    /// <summary><c>easeOutCubic(x)</c>, clamped.</summary>
    internal static double EaseOutCubic(double x) => x <= 0 ? 0 : x >= 1 ? 1 : 1 - Math.Pow(1 - x, 3);
}
