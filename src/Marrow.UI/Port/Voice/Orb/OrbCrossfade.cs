// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-8, voice-SPEC V-11): the orb swap crossfade (V-11.5).
using Marrow.Core.Port.Voice;

namespace Marrow.UI.Port.Voice.Orb;

/// <summary>
/// "Both orbs stay ready; a change crossfades the two views (follow tau 0.12 s, snapped within 0.004)" (V-11.5). Each orb has a mix that follows 1 for the chosen orb and
/// 0 for the other with the time constant <see cref="Tau"/> and lands on the target within <see cref="Snap"/>; a view is drawn while its mix is above 0 and with that
/// mix as its opacity. Reduce motion lands both at once.
/// </summary>
public sealed class OrbCrossfade
{
    /// <summary>The follow's time constant, seconds (V-11.5).</summary>
    public const double Tau = 0.12;

    /// <summary>A mix within this of its target lands on it (V-11.5).</summary>
    public const double Snap = 0.004;

    private double siri;
    private double membrane;

    public OrbCrossfade(VoiceOrbKind chosen)
    {
        Chosen = chosen;
        siri = chosen == VoiceOrbKind.Siri ? 1 : 0;
        membrane = 1 - siri;
    }

    public VoiceOrbKind Chosen { get; private set; }

    public double Mix(VoiceOrbKind kind) => kind == VoiceOrbKind.Siri ? siri : membrane;

    /// <summary>True while the orb is drawn (its mix is above 0).</summary>
    public bool Visible(VoiceOrbKind kind) => Mix(kind) > 0;

    /// <summary>True when both mixes sit on their targets.</summary>
    public bool Settled => siri is 0 or 1 && membrane is 0 or 1;

    /// <summary>Moves both mixes toward the chosen orb by <paramref name="dt"/> seconds; returns true when the choice changed on this call.</summary>
    public bool Step(VoiceOrbKind chosen, double dt, bool reduceMotion)
    {
        var changed = chosen != Chosen;
        Chosen = chosen;
        siri = Follow(siri, chosen == VoiceOrbKind.Siri ? 1 : 0, dt, reduceMotion);
        membrane = Follow(membrane, chosen == VoiceOrbKind.Membrane ? 1 : 0, dt, reduceMotion);
        return changed;
    }

    private static double Follow(double current, double target, double dt, bool reduceMotion)
    {
        if (reduceMotion) return target;
        var next = current + (target - current) * (1 - Math.Exp(-dt / Tau));
        return Math.Abs(target - next) < Snap ? target : next;
    }
}
