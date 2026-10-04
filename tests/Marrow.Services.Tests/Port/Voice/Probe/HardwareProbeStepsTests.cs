using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Voice.Probe;

// VT-6, V-10.1: each step is reported as it completes (OD-0096 "real progress"), no step waits on a clock, the adapter is chosen as the spec says, and a failure or a
// cancellation stops the run where it is.
public sealed class HardwareProbeStepsTests : IDisposable
{
    private readonly TestRuntime runtime = new();

    public void Dispose() => runtime.Dispose();

    private static GpuAdapterInfo Adapter(string description, long dedicated, uint vendor = 0x10DE, bool software = false, long free = 0) =>
        new(description, vendor, 0x2704, dedicated, free, software);

    private static ScriptedMachine Machine(params GpuAdapterInfo[] adapters) => ScriptedMachines.OwnersDesktop with { Adapters = adapters };

    private static Task<HardwareProfile> Run(ScriptedHardwareSource source, ScriptedGpuBackendCheck check, Action<ProbeRow>? report = null, CancellationToken ct = default) =>
        new HardwareProbe(source, check).RunAsync(report, ct).WaitAsync(TimeSpan.FromSeconds(10));

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.1")]
    public async Task Each_row_is_reported_before_the_next_step_reads_anything()
    {
        var sequence = new List<string>();
        var source = new ScriptedHardwareSource(ScriptedMachines.OwnersDesktop) { Called = name => sequence.Add("read:" + name) };
        var check = new ScriptedGpuBackendCheck(GpuBackend.Vulkan) { Called = () => sequence.Add("read:BackendCheck") };
        await Run(source, check, row => sequence.Add("row:" + row.Name));
        Assert.Equal(
            ["read:ProcessorName", "read:PhysicalCores", "row:Processor", "read:TotalPhysicalBytes", "row:Memory", "read:Adapters", "read:DriverVersion", "row:Graphics", "read:BackendCheck", "row:GPU backend"],
            sequence);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.1")]
    public async Task The_probe_needs_no_clock_it_finishes_with_the_manual_clock_never_advanced()
    {
        var rows = new List<ProbeRow>();
        await Run(new ScriptedHardwareSource(ScriptedMachines.OwnersDesktop), new ScriptedGpuBackendCheck(GpuBackend.Vulkan), rows.Add);
        Assert.Equal(4, rows.Count);
        Assert.Equal(TimeSpan.Zero, runtime.Time.Elapsed);
        Assert.Equal(0, runtime.Time.ActiveTimers);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.1")]
    public async Task Software_adapters_are_skipped_and_the_adapter_with_the_most_dedicated_memory_is_chosen()
    {
        var machine = Machine(
            Adapter("Microsoft Basic Render Driver", 0, 0x1414, software: true),
            Adapter("Intel(R) UHD Graphics 770", 128L << 20, 0x8086),
            Adapter("NVIDIA GeForce RTX 3060", 12L << 30),
            Adapter("AMD Radeon RX 6600", 8L << 30, 0x1002));
        var profile = await Run(new ScriptedHardwareSource(machine), new ScriptedGpuBackendCheck(GpuBackend.Vulkan));
        Assert.Equal("NVIDIA GeForce RTX 3060", profile.GpuDescription);
        Assert.Equal(GpuVendor.Nvidia, profile.GpuVendor);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.1")]
    public async Task Equal_dedicated_memory_keeps_the_adapter_the_system_listed_first()
    {
        var machine = Machine(Adapter("First", 4L << 30, 0x1002), Adapter("Second", 4L << 30));
        var profile = await Run(new ScriptedHardwareSource(machine), new ScriptedGpuBackendCheck(GpuBackend.Vulkan));
        Assert.Equal("First", profile.GpuDescription);
    }

    [Theory]
    [InlineData(0x10DE, GpuVendor.Nvidia)]
    [InlineData(0x1002, GpuVendor.Amd)]
    [InlineData(0x8086, GpuVendor.Intel)]
    [InlineData(0x1414, GpuVendor.Other)]
    [InlineData(0x5143, GpuVendor.Other)]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.2")]
    public async Task The_vendor_comes_from_the_PCI_vendor_id(int vendorId, GpuVendor expected)
    {
        var machine = Machine(Adapter("Some Adapter", 4L << 30, (uint)vendorId));
        var profile = await Run(new ScriptedHardwareSource(machine), new ScriptedGpuBackendCheck(GpuBackend.None));
        Assert.Equal(expected, profile.GpuVendor);
    }

    [Theory]
    [InlineData(536_870_911L, "X, shared memory")]
    [InlineData(536_870_912L, "X, 1 GB")]
    [InlineData(1_073_741_824L, "X, 1 GB")]
    [InlineData(2_684_354_560L, "X, 3 GB")]
    [InlineData(2_576_980_377L, "X, 2 GB")]
    [InlineData(17_179_869_184L, "X, 16 GB")]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.1")]
    public async Task The_graphics_row_reads_shared_memory_under_512_MB_and_otherwise_the_dedicated_GB_rounded(long dedicated, string expected)
    {
        var rows = new List<ProbeRow>();
        await Run(new ScriptedHardwareSource(Machine(Adapter("X", dedicated))), new ScriptedGpuBackendCheck(GpuBackend.None), rows.Add);
        Assert.Equal(expected, rows[2].Text);
    }

    [Theory]
    [InlineData(34_359_738_368L, 32)]
    [InlineData(34_359_738_367L, 31)]
    [InlineData(33_466_892_288L, 31)]
    [InlineData(1_073_741_823L, 0)]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.1")]
    public async Task Memory_is_the_total_physical_bytes_floored_to_whole_GB(long bytes, int gigabytes)
    {
        var rows = new List<ProbeRow>();
        var profile = await Run(new ScriptedHardwareSource(ScriptedMachines.OwnersDesktop with { MemoryBytes = bytes }), new ScriptedGpuBackendCheck(GpuBackend.None), rows.Add);
        Assert.Equal(gigabytes, profile.MemoryGB);
        Assert.Equal(gigabytes + " GB", rows[1].Text);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.1")]
    public async Task The_processor_name_is_trimmed_of_the_registrys_padding()
    {
        var rows = new List<ProbeRow>();
        var profile = await Run(new ScriptedHardwareSource(ScriptedMachines.FourCoreMachine with { Processor = "  Intel(R) Core(TM) i5-6500 CPU @ 3.20GHz   " }), new ScriptedGpuBackendCheck(GpuBackend.None), rows.Add);
        Assert.Equal("Intel(R) Core(TM) i5-6500 CPU @ 3.20GHz", profile.CpuName);
        Assert.Equal("Intel(R) Core(TM) i5-6500 CPU @ 3.20GHz, 4 cores", rows[0].Text);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.1")]
    public async Task With_no_usable_adapter_the_backend_is_not_tested_and_none_works()
    {
        var machine = Machine(Adapter("Microsoft Basic Render Driver", 0, 0x1414, software: true));
        var check = new ScriptedGpuBackendCheck(GpuBackend.Vulkan);
        var rows = new List<ProbeRow>();
        var profile = await Run(new ScriptedHardwareSource(machine), check, rows.Add);
        Assert.Equal(0, check.Checks.Count);
        Assert.Equal(GpuBackend.None, profile.Backend);
        Assert.Equal(ProbeText.NoGraphicsAdapter, rows[2].Text);
        Assert.Equal("None working, runs on the processor", rows[3].Text);
        Assert.Equal("", profile.GpuDescription);
        Assert.Equal(GpuVendor.Other, profile.GpuVendor);
        Assert.Equal(0, profile.DedicatedVramBytes);
        Assert.Equal("", profile.DriverVersion);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.1")]
    public async Task The_backend_check_is_given_the_chosen_adapter()
    {
        var check = new ScriptedGpuBackendCheck(GpuBackend.Cuda);
        await Run(new ScriptedHardwareSource(ScriptedMachines.OwnersDesktop), check);
        var adapter = Assert.Single(check.Checks);
        Assert.Equal("NVIDIA GeForce RTX 4080", adapter!.Description);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.1")]
    public async Task The_driver_version_is_read_for_the_chosen_adapter_only()
    {
        var source = new ScriptedHardwareSource(Machine(Adapter("Small", 1L << 30, 0x8086), Adapter("Big", 8L << 30)));
        await Run(source, new ScriptedGpuBackendCheck(GpuBackend.None));
        Assert.Equal(["Big"], source.DriverVersionsAskedFor);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.1")]
    public async Task A_cancellation_between_steps_stops_the_run_and_later_steps_are_not_read()
    {
        using var cts = new CancellationTokenSource();
        var source = new ScriptedHardwareSource(ScriptedMachines.OwnersDesktop);
        var check = new ScriptedGpuBackendCheck(GpuBackend.Vulkan);
        var rows = new List<ProbeRow>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(source, check, row =>
        {
            rows.Add(row);
            if (row.Name == ProbeRow.Memory) cts.Cancel();
        }, cts.Token));
        Assert.Equal(["Processor", "Memory"], rows.Select(r => r.Name));
        Assert.DoesNotContain("Adapters", source.Calls);
        Assert.Empty(check.Checks);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.1")]
    public async Task A_token_cancelled_before_the_run_reads_nothing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var source = new ScriptedHardwareSource(ScriptedMachines.OwnersDesktop);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(source, new ScriptedGpuBackendCheck(GpuBackend.None), ct: cts.Token));
        Assert.Empty(source.Calls);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.1")]
    public async Task A_failing_step_reaches_the_caller_with_the_rows_before_it_already_reported()
    {
        var source = new ScriptedHardwareSource(ScriptedMachines.OwnersDesktop);
        source.Failures["Adapters"] = new InvalidOperationException("DXGI is not available");
        var rows = new List<ProbeRow>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(source, new ScriptedGpuBackendCheck(GpuBackend.Vulkan), rows.Add));
        Assert.Equal("DXGI is not available", error.Message);
        Assert.Equal(["Processor", "Memory"], rows.Select(r => r.Name));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-10.1")]
    public async Task Reporting_is_optional()
    {
        var profile = await Run(new ScriptedHardwareSource(ScriptedMachines.OwnersDesktop), new ScriptedGpuBackendCheck(GpuBackend.Vulkan));
        Assert.Equal(GpuBackend.Vulkan, profile.Backend);
    }
}
