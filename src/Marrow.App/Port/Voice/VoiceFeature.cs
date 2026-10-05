// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-15, voice-SPEC V-2.6): the feature flag.
namespace Marrow.App.Port.Voice;

/// <summary>Whether the orb shows in the composers (V-2.6).</summary>
internal static class VoiceFeature
{
    internal static bool Enabled => throw new NotImplementedException();

    internal static void SwitchOn() => throw new NotImplementedException();

    internal static void Reset() => throw new NotImplementedException();
}
