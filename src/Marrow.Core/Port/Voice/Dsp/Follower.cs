// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-2): the design's exponential follower (voice-SPEC V-5.5).
namespace Marrow.Core.Port.Voice.Dsp;

/// <summary>The design's <c>follow(x, target, dt, up, down)</c>: <c>x + (target - x)(1 - exp(-dt / tau))</c>, tau = up when rising, down otherwise.</summary>
public static class Follower
{
    /// <summary>The smallest time constant, seconds (the design's <c>Math.max(1e-3, ...)</c>).</summary>
    public const double MinimumTau = 1e-3;

    public static double Follow(double current, double target, double dt, double up, double down) =>
        current + (target - current) * (1 - Math.Exp(-dt / Math.Max(MinimumTau, target > current ? up : down)));
}
