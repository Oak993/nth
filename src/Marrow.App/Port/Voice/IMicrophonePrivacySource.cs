// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): the WinRT privacy reader behind a seam.
namespace Marrow.App.Port.Voice;

/// <summary>The Windows microphone privacy setting. Production: <see cref="WinRtMicrophonePrivacySource"/>; tests: a scripted answer. Reading it opens no device.</summary>
public interface IMicrophonePrivacySource
{
    MicrophonePrivacy Current();
}
