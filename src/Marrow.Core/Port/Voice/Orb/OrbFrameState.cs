// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-4, voice-SPEC V-11.2).
namespace Marrow.Core.Port.Voice.Orb;

/// <summary>
/// What the state layer hands the orb for one frame (voice-orbs.js:24-34, the <c>s</c> argument of <c>frame(t, s)</c>). A null
/// <see cref="Speed"/>, <see cref="Scale"/> or <see cref="Glow"/> means the export's own value; <see cref="Bands"/> null means no audio.
/// </summary>
public readonly record struct OrbFrameState(
    double Act = 0,
    OrbBands? Bands = null,
    double? Speed = null,
    double? Scale = null,
    double? Glow = null,
    double Lift = 0,
    double Desat = 0,
    bool ReduceMotion = false);

/// <summary>The extra uniform <c>mrw</c> the state layer sets (V-11.2): the lift shadow, the error desaturation, and 1 when dark.</summary>
public readonly record struct OrbMrw(double Lift, double Desat, double Dark);
