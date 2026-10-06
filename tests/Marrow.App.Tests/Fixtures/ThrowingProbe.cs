using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Fixtures;

// VT-17 test fixture: a hardware probe that throws as DXGI would on a machine with no usable adapter list.
internal sealed class ThrowingProbe : IHardwareProbe
{
    public Task<HardwareProfile> RunAsync(Action<ProbeRow>? report, CancellationToken cancellationToken) => throw new InvalidOperationException("dxgi unavailable");
}
