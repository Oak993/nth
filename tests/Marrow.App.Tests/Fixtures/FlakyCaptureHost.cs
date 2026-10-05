using Marrow.Services.Port.Voice.Capture;

namespace Marrow.App.Tests.Fixtures;

// VT-13 review test fixture: a scripted endpoint host whose listing can be made to throw after the page was built (the Windows audio service stopping). It opens nothing.
public sealed class FlakyCaptureHost(ScriptedCaptureHost inner) : ICaptureHost
{
    public bool Failing { get; set; }

    public ScriptedCaptureHost Inner => inner;

    public IReadOnlyList<AudioEndpoint> CaptureEndpoints() => Failing ? throw new InvalidOperationException("the audio service stopped") : inner.CaptureEndpoints();

    public string? DefaultEndpointId() => inner.DefaultEndpointId();

    public ICaptureDevice Open(string? endpointId) => inner.Open(endpointId);

    public IDisposable Watch(Action<EndpointNotice> handler) => inner.Watch(handler);
}
