// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-10 and VT-15, voice-SPEC V-2.6): the feature flag.
using Marrow.Core.Port;

namespace Marrow.App.Port.Voice;

/// <summary>
/// V-2.6: <see cref="Enabled"/> hides the orb in the app's composers. It is false until <see cref="SwitchOn"/> is called, which the app's start-up composition does once the orb is in
/// every composer (VT-12, VT-15); it is listed in porting/STATE.md as incomplete while false. A test overrides <see cref="Switch"/> for its own flow (the tests of the orb's composer
/// do), or throws and resets the process switch (VT-15's). The attachment button's move to the composer's left (V-14.1, owner-ruled) and the Customization preview do not read it.
/// </summary>
public static class VoiceFeature
{
    private static volatile bool thrown;

    public static Ambient<VoiceFeatureSwitch> Switch { get; } = new(new VoiceFeatureSwitch(false));

    public static bool Enabled => thrown || Switch.Current.On;

    /// <summary>Turns the feature on for the process (V-2.6: "false until phase V7 merges").</summary>
    internal static void SwitchOn() => thrown = true;

    /// <summary>Tests only: back to off.</summary>
    internal static void Reset() => thrown = false;
}
