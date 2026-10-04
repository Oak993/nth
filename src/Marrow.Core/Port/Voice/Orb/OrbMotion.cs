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

/// <summary>
/// The orb's integrators (V-11.4): the activation ramps, the speed, glow, lift and desaturation followers, the scale spring and the slot
/// size spring, stepped in the order the page steps them. One instance per orb slot; deterministic for a given sequence of
/// (<c>now</c>, <c>dt</c>, input), so the same run always draws the same frames.
/// </summary>
public sealed class OrbMotion
{
    // The largest frame delta the page lets through (voice-composer.js:695: Math.min(0.05, ...)).
    private const double MaxDelta = 0.05;

    // The followers' time constants (V-11.4): speed 0.05 while Listening else 0.2 up and 0.5 down, glow 0.12 / 0.25, lift 0.12 / 0.3, desat 0.35/3 and 0.6/3.
    private const double ListeningSpeedTau = 0.05;
    private const double SpeedUpTau = 0.2;
    private const double SpeedDownTau = 0.5;
    private const double GlowUpTau = 0.12;
    private const double GlowDownTau = 0.25;
    private const double LiftUpTau = 0.12;
    private const double LiftDownTau = 0.3;
    private const double DesatUpTau = 0.35 / 3;
    private const double DesatDownTau = 0.6 / 3;

    private double _actFrom;
    private double _actTo;
    private double _actT0;
    private double _actDur = 1;
    private bool _actUp;
    private double _act;
    private double _speed = 1;
    private double _glow;
    private double _lift;
    private double _desat;
    private double _scale = 1;
    private double _scaleVelocity;
    private double _diameter;
    private double _diameterVelocity;
    private double _voice;

    /// <summary>A new orb at rest, its slot already at <paramref name="slotDiameter"/> (no spring yet).</summary>
    public OrbMotion(double slotDiameter)
    {
        _actFrom = _actTo = _act = OrbMotionK.RestAct;
        _diameter = slotDiameter;
    }

    /// <summary>The Idle to Thinking progress shown (0 to 1).</summary>
    public double Act => _act;

    /// <summary>The speed shown, a multiple of the preset's resting speed.</summary>
    public double Speed => _speed;

    /// <summary>The scale spring's position.</summary>
    public double Scale => _scale;

    /// <summary>The scale spring's velocity.</summary>
    public double ScaleVelocity => _scaleVelocity;

    public double Glow => _glow;

    public double Lift => _lift;

    public double Desat => _desat;

    /// <summary>The slot diameter shown (the slot springs to the setting).</summary>
    public double Diameter => _diameter;

    /// <summary>The slot spring's velocity.</summary>
    public double DiameterVelocity => _diameterVelocity;

    /// <summary>The voice follower, <c>S.v</c>: the smoothed <c>all</c> band, followed with attack 0.12 s and release 0.45 s.</summary>
    public double Voice => _voice;

    /// <summary>The targets of a state (the V-11.4 table). <paramref name="voice"/> is the voice follower.</summary>
    public static OrbMotionTargets TargetsFor(OrbMotionMode mode, bool hovered, bool pressed, double voice)
    {
        // hover only counts at Rest or Landing (voice-composer.js:722)
        var hover = hovered && mode is OrbMotionMode.Rest or OrbMotionMode.Landing;
        double speed = 1, act = OrbMotionK.RestAct, scale = 1, glow = 0, lift = 0, desat = 0;
        double response = OrbMotionK.SettleResp, damping = OrbMotionK.SettleDamp;
        switch (mode)
        {
            case OrbMotionMode.Listening:
                speed = 1 + OrbMotionK.VoiceSpeed * voice;
                act = 1;
                scale = OrbMotionK.ListenScale + OrbMotionK.VoiceScale * voice;
                glow = OrbMotionK.ListenGlow + OrbMotionK.VoiceGlow * voice;
                lift = OrbMotionK.ListenLift;
                response = OrbMotionK.PopResp;
                damping = OrbMotionK.PopDamp;
                break;
            case OrbMotionMode.Processing:
                speed = OrbMotionK.ProcSpeed;
                act = 1;
                scale = OrbMotionK.ProcScale;
                glow = OrbMotionK.ProcGlow;
                lift = OrbMotionK.ProcLift;
                response = OrbMotionK.PopResp;
                damping = OrbMotionK.ProcDamp;
                break;
            case OrbMotionMode.Blocked:
                // ED-NEW-f-vt-4 item 3: the table's "0.15 if hovered" is read from the pointer, not from the Rest/Landing hover state
                speed = OrbMotionK.ErrSpeed;
                desat = 1;
                glow = hovered ? OrbMotionK.BlockedHoverGlow : 0;
                break;
            default:
                if (hover)
                {
                    speed = OrbMotionK.HoverSpeed;
                    act = OrbMotionK.HoverAct;
                    scale = OrbMotionK.HoverScale;
                    glow = OrbMotionK.HoverGlow;
                    lift = OrbMotionK.HoverLift;
                    response = OrbMotionK.HoverResp;
                    damping = OrbMotionK.HoverDamp;
                }

                break;
        }

        // Pressed (any but Processing, V-11.4 table)
        if (pressed && mode != OrbMotionMode.Processing)
        {
            scale = OrbMotionK.PressScale * (mode == OrbMotionMode.Listening ? OrbMotionK.ListenScale : 1);
            response = OrbMotionK.PressResp;
            damping = OrbMotionK.PressDamp;
        }

        return new OrbMotionTargets(speed, act, scale, glow, lift, desat, response, damping);
    }

    /// <summary>Moves every integrator by <paramref name="dt"/> seconds (at most 0.05, as the page); <paramref name="now"/> is the frame clock.</summary>
    public void Step(double now, double dt, in OrbMotionInput input)
    {
        dt = dt > 0 ? Math.Min(MaxDelta, dt) : 0;
        var reduce = input.ReduceMotion;
        var listening = input.Mode == OrbMotionMode.Listening;

        // the voice follower (voice-composer.js:722: S.v = follow(S.v, S.bands.all, dt, vAttack, vRelease))
        _voice = reduce ? input.Bands.All : OrbEasing.Follow(_voice, input.Bands.All, dt, OrbMotionK.VAttack, OrbMotionK.VRelease);
        var t = TargetsFor(input.Mode, input.Hovered, input.Pressed, _voice);

        if (reduce)
        {
            // V-11.4: reduce motion lands every integrator at once on its target
            _actFrom = _actTo = _act = t.Act;
            _actUp = false;
            _speed = t.Speed;
            _glow = t.Glow;
            _lift = t.Lift;
            _desat = t.Desat;
            _scale = t.Scale;
            _scaleVelocity = 0;
            _diameter = input.SlotDiameter;
            _diameterVelocity = 0;
            return;
        }

        RampTo(t.Act, now);
        _act = RampAt(now);
        var speedUp = listening ? ListeningSpeedTau : SpeedUpTau;
        var speedDown = listening ? ListeningSpeedTau : SpeedDownTau;
        _speed = OrbEasing.Follow(_speed, t.Speed, dt, speedUp, speedDown);
        _glow = OrbEasing.Follow(_glow, t.Glow, dt, GlowUpTau, GlowDownTau);
        _lift = OrbEasing.Follow(_lift, t.Lift, dt, LiftUpTau, LiftDownTau);
        _desat = OrbEasing.Follow(_desat, t.Desat, dt, DesatUpTau, DesatDownTau);
        OrbEasing.Spring(ref _scale, ref _scaleVelocity, t.Scale, dt, t.ScaleResponse, t.ScaleDamping);

        // the slot springs to the setting (voice-composer.js:741-747)
        if (_diameter != input.SlotDiameter || _diameterVelocity != 0)
            OrbEasing.Spring(ref _diameter, ref _diameterVelocity, input.SlotDiameter, dt, OrbMotionK.SlotResp, OrbMotionK.SlotDamp);
    }

    /// <summary>What the orb runtime takes this frame (bands only while Listening, V-11.4).</summary>
    public OrbFrameState FrameState(OrbMotionMode mode, OrbBands bands, bool reduceMotion) =>
        new(Act: _act, Bands: mode == OrbMotionMode.Listening ? bands : null, Speed: _speed * OrbMotionK.RestBase, Scale: _scale, Glow: _glow,
            Lift: _lift, Desat: _desat, ReduceMotion: reduceMotion);

    // voice-composer.js:115-117: a ramp restarts from the value shown whenever its target moves by 1e-6 or more
    private void RampTo(double to, double now)
    {
        if (Math.Abs(_actTo - to) < 1e-6) return;
        _actFrom = _act;
        _actTo = to;
        _actT0 = now;
        _actUp = to > _actFrom;
        _actDur = _actUp ? OrbMotionK.ActUp : OrbMotionK.ActDown;
    }

    // toward a higher value an ease-out cubic over 0.22 s, toward a lower one a smoothstep over 0.65 s (the export's transitionProgress)
    private double RampAt(double now)
    {
        var x = (now - _actT0) / _actDur;
        return _actFrom + (_actTo - _actFrom) * (_actUp ? OrbEasing.EaseOutCubic(x) : OrbEasing.Smooth(x));
    }
}
