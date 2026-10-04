using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Engine;
using Marrow.Services.Tests.Port.Voice.Engine;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.Services.Tests.Port.Voice.Probe;

// VT-6, WINDOWS-ONLY: the real DXGI, Win32 and registry reads. They cannot run anywhere else; on other systems they are reported as skipped with that reason.
public sealed class HardwareSourceWindowsTests
{
    [WindowsOnlyFact("reads the registry, GlobalMemoryStatusEx and GetLogicalProcessorInformationEx")]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_real_processor_memory_and_core_reads_are_plausible()
    {
        var source = new WindowsHardwareSource();
        Assert.False(string.IsNullOrWhiteSpace(source.ProcessorName()));
        Assert.InRange(source.PhysicalCores(), 1, Environment.ProcessorCount);
        Assert.True(source.TotalPhysicalBytes() >= 1L << 30);
    }

    [WindowsOnlyFact("enumerates DXGI adapters")]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_real_DXGI_enumeration_gives_adapters_with_names_and_sane_memory()
    {
        var source = new WindowsHardwareSource();
        var adapters = source.Adapters();
        Assert.All(adapters, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Description));
            Assert.True(a.DedicatedBytes >= 0);
            Assert.True(a.FreeBytes >= 0);
        });
        foreach (var adapter in adapters.Where(a => !a.IsSoftware)) _ = source.DriverVersion(adapter);
    }

    [WindowsOnlyFact("runs the whole probe on the real machine and loads the VAD model")]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public async Task The_real_probe_reports_its_four_rows_in_order()
    {
        var vad = Path.Combine(AppContext.BaseDirectory, "Port", "Voice", "Assets", "ggml-silero-v6.2.0.bin");
        var inventory = new FileRuntimeInventory(AppContext.BaseDirectory, Path.Combine(Path.GetTempPath(), "MarrowVt6NoCuda"), () => false);
        var probe = new HardwareProbe(new WindowsHardwareSource(), new GpuBackendCheck(inventory.Current, new WhisperNetVadLoadTest(vad)));
        var rows = new List<ProbeRow>();
        var profile = await probe.RunAsync(rows.Add, CancellationToken.None);
        Assert.Equal(["Processor", "Memory", "Graphics", "GPU backend"], rows.Select(r => r.Name));
        Assert.True(profile.PhysicalCores >= 1);
        Assert.True(profile.Backend is GpuBackend.None or GpuBackend.Vulkan or GpuBackend.Cuda);
    }
}
