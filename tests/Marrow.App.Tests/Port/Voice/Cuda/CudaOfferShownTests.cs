using Marrow.App.Port.Voice.Cuda;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Port.Voice.Cuda;

// VT-17 (OD-0106 CUDA A; OD-0099 Q-V-9 C): the offer is shown only on an NVIDIA PC whose hardware probe worked, and only while nothing stops the download from finishing. The
// probe, the backend check and the companions are scripted; no real hardware, driver or network is read.
public sealed class CudaOfferShownTests
{
    private static ScriptedMachine Machine(uint vendor, GpuBackend backend, string name = "Some Graphics") =>
        new("Test CPU", 8, 16_000_000_000L, [new GpuAdapterInfo(name, vendor, 0x1111, 8_000_000_000L, 6_000_000_000L, false)], "31.0.0.1", backend);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C; Oracle OD-0106
    public async Task The_owners_NVIDIA_desktop_with_a_working_probe_is_offered_CUDA()
    {
        await using var rig = new CudaOfferRig(ScriptedMachines.OwnersDesktop);
        Assert.False(rig.Offer.Row.Shown); // nothing is offered until the probe has answered

        await rig.Offer.EvaluateAsync(CancellationToken.None);

        Assert.True(rig.Offer.Row.Shown);
        Assert.Equal(CudaOfferState.Offer, rig.Offer.Row.State);
        Assert.Equal("NVIDIA CUDA", rig.Offer.Row.Title);
        Assert.Contains(rig.Rows, r => r.State == CudaOfferState.Offer); // announced
    }

    [Theory]
    [InlineData(0x10DE, GpuBackend.Vulkan)]
    [InlineData(0x10DE, GpuBackend.None)]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    [Trait("Decision", "ED-267")] // ED-351, numbered by the lead at merge
    public async Task Any_NVIDIA_adapter_whose_probe_finished_is_offered_CUDA_whatever_Vulkan_said(uint vendor, GpuBackend backend)
    {
        await using var rig = new CudaOfferRig(Machine(vendor, backend, "NVIDIA GeForce GTX 1050"));

        await rig.Offer.EvaluateAsync(CancellationToken.None);

        Assert.Equal(CudaOfferState.Offer, rig.Offer.Row.State);
    }

    [Theory]
    [InlineData(0x1002, GpuBackend.Vulkan)] // AMD
    [InlineData(0x8086, GpuBackend.None)] // Intel
    [InlineData(0x1414, GpuBackend.None)] // Microsoft Basic Render
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    public async Task A_PC_without_an_NVIDIA_graphics_card_is_offered_nothing(uint vendor, GpuBackend backend)
    {
        await using var rig = new CudaOfferRig(Machine(vendor, backend));

        await rig.Offer.EvaluateAsync(CancellationToken.None);

        Assert.Equal(CudaOfferRow.Hidden, rig.Offer.Row);
        Assert.Empty(rig.Rows);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    public async Task The_CPU_only_laptop_is_offered_nothing()
    {
        await using var rig = new CudaOfferRig(ScriptedMachines.CpuOnlyLaptop);

        await rig.Offer.EvaluateAsync(CancellationToken.None);

        Assert.False(rig.Offer.Row.Shown);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    [Trait("Decision", "ED-267")] // ED-351, numbered by the lead at merge
    public async Task A_probe_that_fails_or_is_unbound_offers_nothing_and_the_error_does_not_escape()
    {
        await using var unbound = new CudaOfferRig(probe: new FailClosedHardwareProbe());
        await unbound.Offer.EvaluateAsync(CancellationToken.None);
        await using var broken = new CudaOfferRig(probe: new ThrowingProbe());
        await broken.Offer.EvaluateAsync(CancellationToken.None);

        Assert.False(unbound.Offer.Row.Shown);
        Assert.False(broken.Offer.Row.Shown);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    [Trait("Decision", "ED-267")] // ED-351, numbered by the lead at merge
    public async Task A_cancelled_probe_leaves_the_row_hidden()
    {
        await using var rig = new CudaOfferRig(ScriptedMachines.OwnersDesktop);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await rig.Offer.EvaluateAsync(cancel.Token);

        Assert.False(rig.Offer.Row.Shown);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    [Trait("Decision", "ED-267")] // ED-351, numbered by the lead at merge
    public async Task While_CUDA_is_already_the_working_backend_there_is_nothing_to_offer()
    {
        await using var rig = new CudaOfferRig(ScriptedMachines.OwnersDesktop with { Backend = GpuBackend.Cuda });

        await rig.Offer.EvaluateAsync(CancellationToken.None);

        Assert.False(rig.Offer.Row.Shown); // the GPU backend row already reads "CUDA, working"
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    [Trait("Decision", "ED-267")] // ED-351, numbered by the lead at merge
    public async Task Nothing_is_offered_while_no_installer_for_NVIDIAs_libraries_is_bound()
    {
        await using var rig = new CudaOfferRig(ScriptedMachines.OwnersDesktop, new FailClosedCudaCompanions());

        await rig.Offer.EvaluateAsync(CancellationToken.None);

        Assert.False(rig.Offer.Row.Shown); // an offer that could only fail is never shown
        Assert.Empty(rig.Server.Requests);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    [Trait("Decision", "ED-267")] // ED-351, numbered by the lead at merge
    public async Task A_pack_and_libraries_already_on_disk_show_the_installed_state_until_the_restart()
    {
        await using var rig = new CudaOfferRig(ScriptedMachines.OwnersDesktop);
        Directory.CreateDirectory(rig.Installer.FolderFor(rig.Pin));
        foreach (var f in rig.Pin.Files) await File.WriteAllBytesAsync(Path.Combine(rig.Installer.FolderFor(rig.Pin), f.Name), new byte[f.Bytes]);
        rig.Companions.Place(rig.Installer.FolderFor(rig.Pin));

        await rig.Offer.EvaluateAsync(CancellationToken.None);

        Assert.Equal(CudaOfferState.Installed, rig.Offer.Row.State);
        Assert.Empty(rig.Server.Requests);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    [Trait("Decision", "ED-267")] // ED-351, numbered by the lead at merge
    public async Task A_pack_on_disk_without_the_libraries_is_still_an_offer()
    {
        await using var rig = new CudaOfferRig(ScriptedMachines.OwnersDesktop);
        Directory.CreateDirectory(rig.Installer.FolderFor(rig.Pin));
        foreach (var f in rig.Pin.Files) await File.WriteAllBytesAsync(Path.Combine(rig.Installer.FolderFor(rig.Pin), f.Name), new byte[f.Bytes]);

        await rig.Offer.EvaluateAsync(CancellationToken.None);

        Assert.Equal(CudaOfferState.Offer, rig.Offer.Row.State);
    }
}
