// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): the WinRT reader (needs the app's 10.0.19041 TFM, voice-SPEC section 2.1).
using Windows.Devices.Enumeration;

namespace Marrow.App.Port.Voice;

/// <summary><c>DeviceAccessInformation.CreateFromDeviceClass(AudioCapture).CurrentStatus</c> (V-4.5, RESEARCH 3.3). Reading it opens no device and shows no prompt.</summary>
public sealed class WinRtMicrophonePrivacySource : IMicrophonePrivacySource
{
    public MicrophonePrivacy Current() => DeviceAccessInformation.CreateFromDeviceClass(DeviceClass.AudioCapture).CurrentStatus switch
    {
        DeviceAccessStatus.Allowed => MicrophonePrivacy.Allowed,
        DeviceAccessStatus.DeniedByUser => MicrophonePrivacy.DeniedByUser,
        DeviceAccessStatus.DeniedBySystem => MicrophonePrivacy.DeniedBySystem,
        _ => MicrophonePrivacy.Unspecified,
    };
}
