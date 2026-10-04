// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the fail-closed default of the hardware source seam (V-2.5).
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary><see cref="IHardwareSource"/> default: reads no registry key, memory status or adapter.</summary>
public sealed class FailClosedHardwareSource : IHardwareSource
{
    public string ProcessorName() => throw new SeamNotBoundException(nameof(IHardwareSource), nameof(ProcessorName));

    public int PhysicalCores() => throw new SeamNotBoundException(nameof(IHardwareSource), nameof(PhysicalCores));

    public long TotalPhysicalBytes() => throw new SeamNotBoundException(nameof(IHardwareSource), nameof(TotalPhysicalBytes));

    public IReadOnlyList<GpuAdapterInfo> Adapters() => throw new SeamNotBoundException(nameof(IHardwareSource), nameof(Adapters));

    public string DriverVersion(GpuAdapterInfo adapter) => throw new SeamNotBoundException(nameof(IHardwareSource), nameof(DriverVersion));
}
