// PORT-ONLY, owner-ruled addition (OD-0097, B1 to B3; first-release voice typing, VT-9, voice-SPEC V-13.4): what level the composer light is asked to run at while the voice is on.
namespace Marrow.Core.Port.Voice.Light;

/// <summary>The composer light's target level while you speak (V-13.4): the setting's level during Listening, Processing and Landing, otherwise 1.</summary>
public static class VoiceLightTarget
{
    /// <summary>The level of "Dims to 35 %" (B2).</summary>
    public const double DimLevel = 0.35;

    /// <summary>The level a setting asks for while the voice is on: <c>away</c> 0, <c>dim</c> 0.35, <c>stay</c> 1.</summary>
    public static double Level(VoiceLightVoice setting) => setting switch
    {
        VoiceLightVoice.Away => 0,
        VoiceLightVoice.Dim => DimLevel,
        VoiceLightVoice.Stay => 1,
    };

    /// <summary>True in the states in which the voice is on (the page's <c>voiceOn</c>): Listening, Processing and Landing.</summary>
    public static bool VoiceIsOn(Session.VoiceSessionState state) =>
        state is Session.VoiceSessionState.Listening or Session.VoiceSessionState.Processing or Session.VoiceSessionState.Landing;

    /// <summary>The target for a setting in a session state: <see cref="Level"/> while the voice is on, 1 otherwise.</summary>
    public static double For(VoiceLightVoice setting, Session.VoiceSessionState state) => VoiceIsOn(state) ? Level(setting) : 1;
}
