// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-10, voice-SPEC V-2.6): the value the feature flag holds.
namespace Marrow.App.Port.Voice;

/// <summary>Whether voice typing is on in the app's composers. A class, because an <c>Ambient</c> holds a reference type.</summary>
public sealed class VoiceFeatureSwitch(bool on)
{
    public bool On { get; } = on;
}
