// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): what ICaptureHost.Open throws.
namespace Marrow.Services.Port.Voice.Capture;

/// <summary>A capture device could not be opened. <see cref="Failure"/> says why; a session shows the capture-failed note for any of them (V-4.4).</summary>
public sealed class CaptureDeviceException(CaptureDeviceFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public CaptureDeviceFailure Failure { get; } = failure;
}
