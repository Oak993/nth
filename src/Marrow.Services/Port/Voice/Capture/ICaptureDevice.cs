// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): one open capture stream (voice-SPEC V-4.1).
namespace Marrow.Services.Port.Voice.Capture;

/// <summary>
/// An opened capture endpoint in its own mix format. <see cref="DataAvailable"/> runs on the device's capture thread; <see cref="Stopped"/> runs on any thread,
/// with the reason when the device stopped by itself (null after a <see cref="Stop"/> asked for). Production: <c>NAudioCaptureDevice</c>; tests: <c>ScriptedCaptureDevice</c>.
/// </summary>
public interface ICaptureDevice : IDisposable
{
    string EndpointId { get; }

    DeviceFormat Format { get; }

    event CaptureBufferHandler? DataAvailable;

    event Action<CaptureDeviceFailure?>? Stopped;

    void Start();

    void Stop();
}
