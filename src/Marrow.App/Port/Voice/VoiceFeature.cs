// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-15, voice-SPEC V-2.6): the feature flag.
namespace Marrow.App.Port.Voice;

/// <summary>
/// Whether the orb shows in the composers (V-2.6). Off until <see cref="SwitchOn"/> is called, which the app's start-up composition does once the orb is in every composer (VT-12).
/// Tracked in porting/STATE.md as incomplete while it is off. The Customization preview does not read it.
/// </summary>
internal static class VoiceFeature
{
    private static volatile bool enabled;

    internal static bool Enabled => enabled;

    /// <summary>Turns the feature on (V-2.6: "false until phase V7 merges").</summary>
    internal static void SwitchOn() => enabled = true;

    /// <summary>Tests only: back to off.</summary>
    internal static void Reset() => enabled = false;
}
