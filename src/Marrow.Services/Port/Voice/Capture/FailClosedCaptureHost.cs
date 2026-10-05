// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): the fail-closed default of the endpoint seam (voice-SPEC V-2.5).
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Port.Voice.Capture;

/// <summary><see cref="ICaptureHost"/> default: lists no device and opens none; every member throws <see cref="SeamNotBoundException"/>.</summary>
public sealed class FailClosedCaptureHost : ICaptureHost
{
    public IReadOnlyList<AudioEndpoint> CaptureEndpoints() => throw new SeamNotBoundException(nameof(ICaptureHost), nameof(CaptureEndpoints));

    public string? DefaultEndpointId() => throw new SeamNotBoundException(nameof(ICaptureHost), nameof(DefaultEndpointId));

    public ICaptureDevice Open(string? endpointId) => throw new SeamNotBoundException(nameof(ICaptureHost), nameof(Open));

    public IDisposable Watch(Action<EndpointNotice> handler) => throw new SeamNotBoundException(nameof(ICaptureHost), nameof(Watch));
}
