using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.Services.Tests.Port.Voice.Probe;

// VT-6, V-2.4, V-2.5: IHardwareProbe is a seam with a fail-closed default and ScriptedProbe as its test double; the hardware source and the backend check fail closed too.
public sealed class HardwareProbeSeamTests
{
    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_ambient_probe_default_throws_when_touched_and_reads_no_hardware()
    {
        var error = Assert.Throws<SeamNotBoundException>(() =>
        {
            _ = HardwareProbeSeams.Probe.Current.RunAsync(null, CancellationToken.None);
        });
        Assert.Equal(nameof(IHardwareProbe), error.Seam);
        Assert.IsType<FailClosedHardwareProbe>(HardwareProbeSeams.Probe.Current);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_hardware_source_and_the_backend_check_defaults_throw_on_every_member()
    {
        var source = new FailClosedHardwareSource();
        Assert.Throws<SeamNotBoundException>(() => source.ProcessorName());
        Assert.Throws<SeamNotBoundException>(() => source.PhysicalCores());
        Assert.Throws<SeamNotBoundException>(() => source.TotalPhysicalBytes());
        Assert.Throws<SeamNotBoundException>(() => source.Adapters());
        Assert.Throws<SeamNotBoundException>(() => source.DriverVersion(new GpuAdapterInfo("x", 0, 0, 0, 0, false)));
        Assert.Throws<SeamNotBoundException>(() => new FailClosedGpuBackendCheck().Check(null));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public async Task A_test_binds_a_scripted_probe_for_its_own_flow_only()
    {
        var scripted = new ScriptedProbe(ScriptedMachines.TwoGbVramCard);
        using (HardwareProbeSeams.Probe.Override(scripted))
        {
            var profile = await HardwareProbeSeams.Probe.Current.RunAsync(null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("NVIDIA GeForce GTX 1050", profile.GpuDescription);
        }
        Assert.IsType<FailClosedHardwareProbe>(HardwareProbeSeams.Probe.Current);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public async Task The_four_scripted_machines_differ_in_the_way_the_seam_table_names()
    {
        var profiles = new List<HardwareProfile>();
        foreach (var machine in new[] { ScriptedMachines.OwnersDesktop, ScriptedMachines.CpuOnlyLaptop, ScriptedMachines.FourCoreMachine, ScriptedMachines.TwoGbVramCard })
            profiles.Add(await new ScriptedProbe(machine).RunAsync(null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal([GpuBackend.Vulkan, GpuBackend.None, GpuBackend.None, GpuBackend.Vulkan], profiles.Select(p => p.Backend));
        Assert.Equal([8, 10, 4, 6], profiles.Select(p => p.PhysicalCores));
        Assert.Equal(2L << 30, profiles[3].DedicatedVramBytes);
        // the Core recommender can read what the probe gives (V-10.2)
        Assert.Equal("large-v3-turbo-q8_0", Recommender.Recommend(profiles[0], "auto").ModelId);
        Assert.Equal("large-v3-turbo-q5_0", Recommender.Recommend(profiles[3], "auto").ModelId);
        Assert.Equal("base.en", Recommender.Recommend(profiles[2], "en").ModelId);
    }
}
