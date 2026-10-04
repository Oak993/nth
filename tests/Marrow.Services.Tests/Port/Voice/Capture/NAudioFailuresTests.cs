using System.Runtime.InteropServices;
using Marrow.Services.Port.Voice.Capture;
using NAudio.CoreAudioApi;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-5 (voice-SPEC V-4.4): what NAudio and COM throw when a device is opened or started reaches the session as a CaptureDeviceException of the right kind.
public sealed class NAudioFailuresTests
{
    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")]
    public void NAudio_and_COM_failures_are_wrapped_as_CaptureDeviceException_of_their_kind()
    {
        Assert.Equal(CaptureDeviceFailure.InUse, NAudioFailures.Wrap(new AudioDeviceInUseException(unchecked((int)0x8889000A))).Failure);
        Assert.Equal(CaptureDeviceFailure.InUse, NAudioFailures.Wrap(new COMException("in use", unchecked((int)0x8889000A))).Failure);
        Assert.Equal(CaptureDeviceFailure.Removed, NAudioFailures.Wrap(new AudioDeviceDisconnectedException(unchecked((int)0x88890004))).Failure);
        Assert.Equal(CaptureDeviceFailure.Removed, NAudioFailures.Wrap(new COMException("invalidated", unchecked((int)0x88890004))).Failure);
        Assert.Equal(CaptureDeviceFailure.NotFound, NAudioFailures.Wrap(new COMException("not found", unchecked((int)0x80070490))).Failure);
        var other = new InvalidOperationException("anything else");
        var wrapped = NAudioFailures.Wrap(other);
        Assert.Equal(CaptureDeviceFailure.Other, wrapped.Failure);
        Assert.Same(other, wrapped.InnerException);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // an exception that already is a CaptureDeviceException is not wrapped twice
    public void A_CaptureDeviceException_passes_through_Wrap_unchanged()
    {
        var failure = new CaptureDeviceException(CaptureDeviceFailure.NotFound, "gone");
        Assert.Same(failure, NAudioFailures.Wrap(failure));
    }
}
