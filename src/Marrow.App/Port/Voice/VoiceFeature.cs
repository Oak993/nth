// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-10, voice-SPEC V-2.6): the feature flag.
using Marrow.Core.Port;

namespace Marrow.App.Port.Voice;

/// <summary>
/// V-2.6: <see cref="Enabled"/> hides the orb in the app's composers. It is false until phase V7 switches voice typing on (listed in
/// porting/STATE.md as incomplete while false); a test overrides <see cref="Switch"/> for its own flow, as the tests of the orb's composer do.
/// The attachment button's move to the composer's left (V-14.1, owner-ruled) does not read it.
/// </summary>
public static class VoiceFeature
{
    public static Ambient<VoiceFeatureSwitch> Switch { get; } = new(new VoiceFeatureSwitch(false));

    public static bool Enabled => Switch.Current.On;
}
