// PORT-ONLY, owner-ruled addition (OD-0097, B1 to B3; first-release voice typing, VT-9, voice-SPEC V-13.4): the follower that carries the composer light to its target level.
namespace Marrow.Core.Port.Voice.Light;

/// <summary>
/// <c>vis = follow(vis, target, dt, 0.2, 0.17)</c> (voice-options.js): the light's level follows its target with a time constant of 0.2 s going up (95 % in 0.6 s) and
/// 0.17 s going down (95 % in 0.51 s); it snaps to 0 below 0.003 and to 1 above 0.997; under reduce motion it jumps to the target. Starts at 1: the light as it is without voice.
/// </summary>
public sealed class VoiceLightFollower
{
    /// <summary>Time constants, seconds.</summary>
    public const double RiseSeconds = 0.2;

    public const double FallSeconds = 0.17;

    public const double SnapLow = 0.003;

    public const double SnapHigh = 0.997;

    /// <summary>The level now, 0 to 1.</summary>
    public double Value { get; private set; } = 1;

    /// <summary>The target of the last step.</summary>
    public double Target { get; private set; } = 1;

    /// <summary>True while the value is not at its target (another frame is wanted).</summary>
    public bool Moving => Value != Target;

    /// <summary>Takes <paramref name="value"/> at once (a first frame, or a still).</summary>
    public void Jump(double value)
    {
        Value = Math.Clamp(value, 0, 1);
        Target = Value;
    }

    /// <summary>Advances by <paramref name="dt"/> seconds toward <paramref name="target"/> (0 to 1) and returns the value.</summary>
    public double Step(double target, double dt, bool reduceMotion)
    {
        Target = Math.Clamp(double.IsNaN(target) ? 1 : target, 0, 1);
        if (reduceMotion)
        {
            Value = Target;
            return Value;
        }
        if (dt <= 0) return Value;
        var v = Dsp.Follower.Follow(Value, Target, dt, RiseSeconds, FallSeconds);
        Value = v < SnapLow ? 0 : v > SnapHigh ? 1 : v;
        return Value;
    }
}
