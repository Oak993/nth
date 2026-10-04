// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-4, voice-SPEC V-11.4, DESIGN voice-composer.js:725-773).
namespace Marrow.Core.Port.Voice.Orb;

/// <summary>The orb's state for motion (V-11.4 table): Hover is a flag of Rest and Landing, Pressed a flag of any state but Processing.</summary>
public enum OrbMotionMode { Rest, Listening, Processing, Blocked, Landing }

/// <summary>What the host tells <see cref="OrbMotion"/> each frame.</summary>
public readonly record struct OrbMotionInput(
    OrbMotionMode Mode,
    bool Hovered,
    bool Pressed,
    OrbBands Bands,
    double SlotDiameter,
    bool ReduceMotion = false);

/// <summary>The targets one state sets (the V-11.4 table), before any integrator moves toward them.</summary>
public readonly record struct OrbMotionTargets(
    double Speed,
    double Act,
    double Scale,
    double Glow,
    double Lift,
    double Desat,
    double ScaleResponse,
    double ScaleDamping);

/// <summary>The orb's integrators (V-11.4). One instance per orb slot.</summary>
public sealed class OrbMotion
{
    public OrbMotion(double slotDiameter) => throw new NotImplementedException();

    /// <summary>The Idle to Thinking progress shown (0 to 1).</summary>
    public double Act => throw new NotImplementedException();

    /// <summary>The speed shown, a multiple of the preset's resting speed.</summary>
    public double Speed => throw new NotImplementedException();

    /// <summary>The scale spring's position.</summary>
    public double Scale => throw new NotImplementedException();

    /// <summary>The scale spring's velocity.</summary>
    public double ScaleVelocity => throw new NotImplementedException();

    public double Glow => throw new NotImplementedException();

    public double Lift => throw new NotImplementedException();

    public double Desat => throw new NotImplementedException();

    /// <summary>The slot diameter shown (the slot springs to the setting).</summary>
    public double Diameter => throw new NotImplementedException();

    /// <summary>The slot spring's velocity.</summary>
    public double DiameterVelocity => throw new NotImplementedException();

    /// <summary>The voice follower, <c>S.v</c>: the smoothed <c>all</c> band, followed with attack 0.12 s and release 0.45 s.</summary>
    public double Voice => throw new NotImplementedException();

    /// <summary>The targets of a state (the V-11.4 table). <paramref name="voice"/> is the voice follower.</summary>
    public static OrbMotionTargets TargetsFor(OrbMotionMode mode, bool hovered, bool pressed, double voice) => throw new NotImplementedException();

    /// <summary>Moves every integrator by <paramref name="dt"/> seconds (at most 0.05, as the page); <paramref name="now"/> is the frame clock.</summary>
    public void Step(double now, double dt, in OrbMotionInput input) => throw new NotImplementedException();

    /// <summary>What the orb runtime takes this frame (bands only while Listening, V-11.4).</summary>
    public OrbFrameState FrameState(OrbMotionMode mode, OrbBands bands, bool reduceMotion) => throw new NotImplementedException();
}
