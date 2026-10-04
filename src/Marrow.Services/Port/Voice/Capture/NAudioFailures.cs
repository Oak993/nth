// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): what NAudio and COM throw, as the capture's failure kinds (voice-SPEC V-4.4).
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Marrow.Services.Port.Voice.Capture;

public static class NAudioFailures
{
    /// <summary>AUDCLNT_E_DEVICE_IN_USE.</summary>
    private const int DeviceInUse = unchecked((int)0x8889000A);

    /// <summary>AUDCLNT_E_DEVICE_INVALIDATED.</summary>
    private const int DeviceInvalidated = unchecked((int)0x88890004);

    /// <summary>ERROR_NOT_FOUND as an HRESULT (the enumerator's answer for an id that is not an endpoint).</summary>
    private const int NotFound = unchecked((int)0x80070490);

    /// <summary>The exception a session sees: a <see cref="CaptureDeviceException"/> passes through, anything else is wrapped with the kind <see cref="Map"/> gives it.</summary>
    public static CaptureDeviceException Wrap(Exception error) =>
        error as CaptureDeviceException ?? new CaptureDeviceException(Map(error), "The capture device failed: " + error.Message, error);

    public static CaptureDeviceFailure Map(Exception error) => error switch
    {
        AudioDeviceInUseException => CaptureDeviceFailure.InUse,
        AudioDeviceDisconnectedException => CaptureDeviceFailure.Removed,
        COMException { HResult: DeviceInUse } => CaptureDeviceFailure.InUse,
        COMException { HResult: DeviceInvalidated } => CaptureDeviceFailure.Removed,
        COMException { HResult: NotFound } => CaptureDeviceFailure.NotFound,
        _ => CaptureDeviceFailure.Other,
    };
}
