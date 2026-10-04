// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the fail-closed default of the probe seam (V-2.5).
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary><see cref="IHardwareProbe"/> default: reads no hardware.</summary>
public sealed class FailClosedHardwareProbe : IHardwareProbe
{
    public Task<HardwareProfile> RunAsync(Action<ProbeRow>? report, CancellationToken cancellationToken) =>
        throw new SeamNotBoundException(nameof(IHardwareProbe), nameof(RunAsync));
}
