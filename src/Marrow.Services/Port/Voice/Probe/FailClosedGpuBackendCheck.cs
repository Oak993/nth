// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the fail-closed default of the backend check seam (V-2.5).
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary><see cref="IGpuBackendCheck"/> default: loads no native library.</summary>
public sealed class FailClosedGpuBackendCheck : IGpuBackendCheck
{
    public GpuBackend Check(GpuAdapterInfo? adapter) => throw new SeamNotBoundException(nameof(IGpuBackendCheck), nameof(Check));
}
