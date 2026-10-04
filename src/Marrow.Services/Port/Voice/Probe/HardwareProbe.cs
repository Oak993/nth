// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the hardware probe (voice-SPEC V-10.1, V-T45).
using System.Globalization;
using Marrow.Core.Port.Voice;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>
/// V-10.1. Four steps in the table's order, each reported to the caller the moment it completes and before the next one reads anything; the probe waits for nothing but the reads
/// themselves (no delay, no timer). A failing read, or a cancelled token, stops the run where it is: the rows already reported stay reported. The graphics adapter is the
/// non-software one with the most dedicated memory (the first listed on a tie).
/// </summary>
public sealed class HardwareProbe(IHardwareSource source, IGpuBackendCheck backendCheck) : IHardwareProbe
{
    private const long SharedMemoryBelow = 512L << 20;
    private const long Gigabyte = 1L << 30;

    public async Task<HardwareProfile> RunAsync(Action<ProbeRow>? report, CancellationToken cancellationToken)
    {
        report ??= _ => { };

        var cpu = await Step(() => (Name: source.ProcessorName().Trim(), Cores: source.PhysicalCores()), cancellationToken);
        report(new ProbeRow(ProbeRow.Processor, cpu.Name + ", " + cpu.Cores.ToString(CultureInfo.InvariantCulture) + " cores"));

        var memoryGB = await Step(() => (int)(source.TotalPhysicalBytes() / Gigabyte), cancellationToken);
        report(new ProbeRow(ProbeRow.Memory, memoryGB.ToString(CultureInfo.InvariantCulture) + " GB"));

        var gpu = await Step(() =>
        {
            var chosen = Choose(source.Adapters());
            return (Adapter: chosen, Driver: chosen is null ? "" : source.DriverVersion(chosen));
        }, cancellationToken);
        report(new ProbeRow(ProbeRow.Graphics, gpu.Adapter is null ? ProbeText.NoGraphicsAdapter : Describe(gpu.Adapter)));

        var backend = gpu.Adapter is null ? GpuBackend.None : await Step(() => backendCheck.Check(gpu.Adapter), cancellationToken);
        report(new ProbeRow(ProbeRow.GpuBackend, backend switch
        {
            GpuBackend.Vulkan => "Vulkan, working",
            GpuBackend.Cuda => "CUDA, working",
            GpuBackend.None => ProbeText.NoBackend,
        }));

        return new HardwareProfile(cpu.Name, cpu.Cores, memoryGB, gpu.Adapter?.Description ?? "", Vendor(gpu.Adapter), gpu.Adapter?.DedicatedBytes ?? 0,
            gpu.Adapter?.FreeBytes ?? 0, gpu.Driver, backend);
    }

    private static Task<T> Step<T>(Func<T> read, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(read, cancellationToken);
    }

    private static GpuAdapterInfo? Choose(IReadOnlyList<GpuAdapterInfo> adapters)
    {
        GpuAdapterInfo? best = null;
        foreach (var adapter in adapters)
            if (!adapter.IsSoftware && (best is null || adapter.DedicatedBytes > best.DedicatedBytes)) best = adapter;
        return best;
    }

    private static string Describe(GpuAdapterInfo adapter) => adapter.DedicatedBytes < SharedMemoryBelow
        ? adapter.Description + ", shared memory"
        : adapter.Description + ", " + Math.Round((double)adapter.DedicatedBytes / Gigabyte, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " GB";

    private static GpuVendor Vendor(GpuAdapterInfo? adapter) => adapter?.VendorId switch
    {
        0x10DE => GpuVendor.Nvidia,
        0x1002 => GpuVendor.Amd,
        0x8086 => GpuVendor.Intel,
        _ => GpuVendor.Other,
    };
}
