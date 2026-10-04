// PORT-ONLY, owner-ruled addition (OD-0091, OD-0099 Q-V-19 B; first-release voice typing, VT-5): the Windows audio endpoints behind a seam (voice-SPEC V-2.4, V-4.1, V-4.4).
namespace Marrow.Services.Port.Voice.Capture;

/// <summary>
/// The system's capture endpoints. Production: <c>NAudioCaptureHost</c> (MMDeviceEnumerator, WasapiRecorder, IMMNotificationClient); tests: <c>ScriptedCaptureHost</c>
/// (the Microphone row's "scripted enumerator"). Enumerating opens nothing; only <see cref="Open"/> opens a device.
/// </summary>
public interface ICaptureHost
{
    /// <summary>The active capture endpoints, in the system's order.</summary>
    IReadOnlyList<AudioEndpoint> CaptureEndpoints();

    /// <summary>The id of the default capture endpoint (the Console role), or null when there is none.</summary>
    string? DefaultEndpointId();

    /// <summary>Opens <paramref name="endpointId"/>, or the default endpoint when null, in its own mix format. Throws <see cref="CaptureDeviceException"/>.</summary>
    ICaptureDevice Open(string? endpointId);

    /// <summary>Subscribes to endpoint changes (any thread); disposing the result unsubscribes.</summary>
    IDisposable Watch(Action<EndpointNotice> handler);
}
