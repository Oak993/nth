// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): Windows' microphone privacy answer (voice-SPEC V-4.5, RESEARCH 3.3).
namespace Marrow.App.Port.Voice;

/// <summary>What <c>DeviceAccessInformation.CurrentStatus</c> says for the audio capture class: Unspecified (a desktop app has no declared capability), Allowed, DeniedByUser, DeniedBySystem.</summary>
public enum MicrophonePrivacy { Unspecified, Allowed, DeniedByUser, DeniedBySystem }
