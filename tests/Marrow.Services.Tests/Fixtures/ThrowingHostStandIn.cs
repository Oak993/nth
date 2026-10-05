using Marrow.Services.Port.Voice.Capture;

namespace Marrow.Services.Tests.Fixtures;

// VT-13 review test fixture: an endpoint host whose listing throws (the audio service is not running). It opens nothing.
public sealed class ThrowingHostStandIn : ICaptureHost
{
    public IReadOnlyList<AudioEndpoint> CaptureEndpoints() => throw new InvalidOperationException("the audio service is not running");

    public string? DefaultEndpointId() => throw new InvalidOperationException("the audio service is not running");

    public ICaptureDevice Open(string? endpointId) => throw new InvalidOperationException("the audio service is not running");

    public IDisposable Watch(Action<EndpointNotice> handler) => throw new InvalidOperationException("the audio service is not running");
}
