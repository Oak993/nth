using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.Services.Tests.Port.Voice.Probe;

// VT-6, V-T45 (V-10.1): the probe formats its four rows from scripted DXGI, Win32 and registry values: the owner's desktop, a CPU-only laptop, a 4-core machine and a
// 2 GB VRAM card. The expected strings are written out here, not computed from the scripted values.
public sealed class HardwareProbeRowsTests
{
    private static async Task<(IReadOnlyList<ProbeRow> Rows, HardwareProfile Profile)> Run(ScriptedMachine machine)
    {
        var rows = new List<ProbeRow>();
        var profile = await new ScriptedProbe(machine).RunAsync(rows.Add, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        return (rows, profile);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T45")]
    public async Task The_owners_desktop_gives_the_four_rows_and_a_Vulkan_profile()
    {
        var (rows, profile) = await Run(ScriptedMachines.OwnersDesktop);
        Assert.Equal(
            [
                new ProbeRow("Processor", "AMD Ryzen 7 9800X3D 8-Core Processor, 8 cores"),
                new ProbeRow("Memory", "31 GB"),
                new ProbeRow("Graphics", "NVIDIA GeForce RTX 4080, 16 GB"),
                new ProbeRow("GPU backend", "Vulkan, working"),
            ],
            rows);
        Assert.Equal("AMD Ryzen 7 9800X3D 8-Core Processor", profile.CpuName);
        Assert.Equal(8, profile.PhysicalCores);
        Assert.Equal(31, profile.MemoryGB);
        Assert.Equal("NVIDIA GeForce RTX 4080", profile.GpuDescription);
        Assert.Equal(GpuVendor.Nvidia, profile.GpuVendor);
        Assert.Equal(17_171_480_576L, profile.DedicatedVramBytes);
        Assert.Equal(15_032_385_536L, profile.FreeVramBytes);
        Assert.Equal("32.0.15.6094", profile.DriverVersion);
        Assert.Equal(GpuBackend.Vulkan, profile.Backend);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T45")]
    public async Task A_CPU_only_laptop_shows_shared_memory_and_no_working_GPU_backend()
    {
        var (rows, profile) = await Run(ScriptedMachines.CpuOnlyLaptop);
        Assert.Equal(
            [
                new ProbeRow("Processor", "12th Gen Intel(R) Core(TM) i5-1235U, 10 cores"),
                new ProbeRow("Memory", "15 GB"),
                new ProbeRow("Graphics", "Intel(R) Iris(R) Xe Graphics, shared memory"),
                new ProbeRow("GPU backend", "None working, runs on the processor"),
            ],
            rows);
        Assert.Equal(GpuBackend.None, profile.Backend);
        Assert.Equal(GpuVendor.Intel, profile.GpuVendor);
        Assert.Equal(134_217_728L, profile.DedicatedVramBytes);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T45")]
    public async Task A_four_core_machine_reports_four_cores()
    {
        var (rows, profile) = await Run(ScriptedMachines.FourCoreMachine);
        Assert.Equal(
            [
                new ProbeRow("Processor", "Intel(R) Core(TM) i5-6500 CPU @ 3.20GHz, 4 cores"),
                new ProbeRow("Memory", "7 GB"),
                new ProbeRow("Graphics", "Intel(R) HD Graphics 530, shared memory"),
                new ProbeRow("GPU backend", "None working, runs on the processor"),
            ],
            rows);
        Assert.Equal(4, profile.PhysicalCores);
        Assert.Equal(7, profile.MemoryGB);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T45")]
    public async Task A_two_GB_VRAM_card_reads_two_GB_and_its_free_memory_is_kept()
    {
        var (rows, profile) = await Run(ScriptedMachines.TwoGbVramCard);
        Assert.Equal(
            [
                new ProbeRow("Processor", "Intel(R) Core(TM) i5-9400F CPU @ 2.90GHz, 6 cores"),
                new ProbeRow("Memory", "15 GB"),
                new ProbeRow("Graphics", "NVIDIA GeForce GTX 1050, 2 GB"),
                new ProbeRow("GPU backend", "Vulkan, working"),
            ],
            rows);
        Assert.Equal(2_147_483_648L, profile.DedicatedVramBytes);
        Assert.Equal(1_717_986_918L, profile.FreeVramBytes);
        Assert.Equal(GpuVendor.Nvidia, profile.GpuVendor);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public async Task A_working_CUDA_runtime_reads_CUDA_working()
    {
        var (rows, profile) = await Run(ScriptedMachines.OwnersDesktop with { Backend = GpuBackend.Cuda });
        Assert.Equal(new ProbeRow("GPU backend", "CUDA, working"), rows[3]);
        Assert.Equal(GpuBackend.Cuda, profile.Backend);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public async Task The_row_names_are_the_four_the_spec_names()
    {
        var (rows, _) = await Run(ScriptedMachines.OwnersDesktop);
        Assert.Equal(["Processor", "Memory", "Graphics", "GPU backend"], rows.Select(r => r.Name));
        Assert.Equal([ProbeRow.Processor, ProbeRow.Memory, ProbeRow.Graphics, ProbeRow.GpuBackend], rows.Select(r => r.Name));
    }
}
