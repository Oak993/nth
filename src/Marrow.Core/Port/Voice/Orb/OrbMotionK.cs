// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-4, voice-SPEC V-11.4, DESIGN voice-composer.js:15-25).
namespace Marrow.Core.Port.Voice.Orb;

/// <summary>The motion constants <c>K</c> of the voice composer page, exactly (V-11.4). Speeds are multiples of the preset's resting speed.</summary>
public static class OrbMotionK
{
    public const double RestBase = 1.0;
    public const double RestAct = 0;
    public const double HoverSpeed = 2.0;
    public const double VoiceSpeed = 2.2;
    public const double ProcSpeed = 4.6;
    public const double ErrSpeed = 0.6;
    public const double HoverAct = 0.3;
    public const double HoverScale = 1.04;
    public const double ListenScale = 1.16;
    public const double VoiceScale = 0.04;
    public const double ProcScale = 1.1;
    public const double PressScale = 0.94;
    public const double HoverGlow = 0.55;
    public const double ListenGlow = 0.45;
    public const double VoiceGlow = 0.4;
    public const double ProcGlow = 0.75;

    /// <summary>The source's activation ramp (seconds, ease-out cubic), toward a higher activation.</summary>
    public const double ActUp = 0.22;

    /// <summary>The source's settle ramp (seconds, smoothstep), toward a lower activation.</summary>
    public const double ActDown = 0.65;

    /// <summary>The speed follower on the voice: attack.</summary>
    public const double VAttack = 0.12;

    /// <summary>The speed follower on the voice: release.</summary>
    public const double VRelease = 0.45;

    public const double PopResp = 0.3;
    public const double PopDamp = 0.62;
    public const double SettleResp = 0.32;
    public const double SettleDamp = 0.78;
    public const double HoverResp = 0.22;
    public const double HoverDamp = 0.8;

    /// <summary>Stagger, send hold and final minimum belong to the session and the words (VT-3, VT-11); carried here as the table lists them.</summary>
    public const double Stagger = 0.026;

    public const double SendHold = 3.0;
    public const double FinalMin = 0.75;

    /// <summary>The press spring (voice-composer.js:735): response 0.18, damping 0.8.</summary>
    public const double PressResp = 0.18;

    public const double PressDamp = 0.8;

    /// <summary>The Processing scale spring's damping (voice-composer.js:731): 0.75, response <see cref="PopResp"/>.</summary>
    public const double ProcDamp = 0.75;

    /// <summary>The slot size spring (V-11.4): response 0.34, damping 0.86.</summary>
    public const double SlotResp = 0.34;

    public const double SlotDamp = 0.86;
}
