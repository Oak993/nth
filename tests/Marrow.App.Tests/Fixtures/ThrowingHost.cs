using Marrow.Services.Port.Voice.Capture;

namespace Marrow.App.Tests.Fixtures;

// VT-5 test fixture: an endpoint host whose listing throws what it is given (a Windows audio service that is not running). It opens nothing.
public sealed class ThrowingHost(Exception failure) : ICaptureHost
{
    public IReadOnlyList<AudioEndpoint> CaptureEndpoints() => throw failure;

    public string? DefaultEndpointId() => throw failure;

    public ICaptureDevice Open(string? endpointId) => throw failure;

    public IDisposable Watch(Action<EndpointNotice> handler) => throw failure;
}
