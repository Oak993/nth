// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-4, voice-SPEC V-11.4, DESIGN voice-composer.js:111-134).
namespace Marrow.Core.Port.Voice.Orb;

/// <summary>The easing helpers of the voice composer page, as written there.</summary>
internal static class OrbEasing
{
    /// <summary>SwiftUI <c>spring(response:dampingFraction:)</c> integrated in 4 ms substeps (<c>spring(o, target, dt, resp, zeta)</c>).</summary>
    internal static void Spring(ref double position, ref double velocity, double target, double dt, double response, double damping) => throw new NotImplementedException();

    /// <summary><c>follow(cur, target, dt, up, down)</c>: an exponential follower with a time constant for each direction.</summary>
    internal static double Follow(double current, double target, double dt, double up, double down) => throw new NotImplementedException();

    /// <summary><c>smooth(x)</c>: smoothstep, clamped.</summary>
    internal static double Smooth(double x) => throw new NotImplementedException();

    /// <summary><c>easeOutCubic(x)</c>, clamped.</summary>
    internal static double EaseOutCubic(double x) => throw new NotImplementedException();
}
