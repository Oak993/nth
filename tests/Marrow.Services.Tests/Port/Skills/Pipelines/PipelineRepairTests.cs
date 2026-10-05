// PORT-ONLY: integ-sk8-sk9, cross-unit gap ED-323 (5)(b): Repair of archive tools through SK-4's installer, in SK-8's pipeline.
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Pipelines;

[Trait("Spec", "DL-8")]
public sealed class PipelineRepairTests
{
    [Theory]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repair_reinstalls_the_installed_pin_at_its_path_and_collects_the_damaged_copy(bool msi)
    {
        using var rig = new PipelineRig(msi: msi);
        var pipeline = await rig.Create();
        var folder = pipeline.Snapshot().Single().Folder;
        rig.Damage(folder);
        Assert.Null(pipeline.Acquire("fixture"));
        Assert.Equal(ItemState.Damaged, pipeline.State.Get("fixture").State);
        Assert.True(await pipeline.RepairAsync("fixture", rig.Progress));
        // The installed pin, not the catalogue's newer one, is downloaded and reinstalled by SK-4's installer with the orchestrator's parameters.
        Assert.Equal(rig.Old.Pin, rig.LastDownloadPin);
        Assert.Equal(msi ? 2 : 0, rig.ProcessRuns);
        var record = pipeline.State.Get("fixture");
        Assert.Equal(rig.Old.Pin, record.InstalledPin);
        Assert.Equal(ItemState.UpdateAvailable, record.State);
        using (var lease = pipeline.Acquire("fixture"))
        {
            Assert.NotNull(lease);
            Assert.Equal(folder, lease.Folder);
            Assert.Equal("offline fixture", File.ReadAllText(Path.Combine(folder, "payload.txt")));
        }
        pipeline.Collect();
        Assert.Equal(new[] { folder }, Directory.GetDirectories(rig.Paths.Tools));
        Assert.Equal(folder, PipelineRoutes.Read(rig.Paths.StateFile).Single().Folder);
        Assert.Contains(1, rig.Reports);
    }

    [Theory]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData(PipelineStep.Downloaded)]
    [InlineData(PipelineStep.SetAside)]
    [InlineData(PipelineStep.Installed)]
    [InlineData(PipelineStep.BeforeCommit)]
    public async Task A_failed_repair_keeps_the_item_damaged_and_a_retry_succeeds(PipelineStep step)
    {
        using var rig = new PipelineRig();
        var fail = true;
        var pipeline = await rig.Create(at => { if (fail && at == step) throw new IOException("Injected failure"); });
        var folder = pipeline.Snapshot().Single().Folder;
        rig.Damage(folder);
        Assert.Null(pipeline.Acquire("fixture"));
        await Assert.ThrowsAsync<IOException>(() => pipeline.RepairAsync("fixture", rig.Progress));
        Assert.Equal(ItemState.Damaged, pipeline.State.Get("fixture").State);
        Assert.Null(pipeline.Acquire("fixture"));
        Assert.Null(pipeline.AcquireForViewBuild("fixture"));
        fail = false;
        Assert.True(await pipeline.RepairAsync("fixture", rig.Progress));
        using var lease = pipeline.Acquire("fixture");
        Assert.NotNull(lease);
        Assert.Equal("offline fixture", File.ReadAllText(Path.Combine(folder, "payload.txt")));
    }

    [Theory]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData(InstallStep.Created)]
    [InlineData(InstallStep.Built)]
    [InlineData(InstallStep.Renamed)]
    [InlineData(InstallStep.Locked)]
    [InlineData(InstallStep.Verified)]
    [InlineData(InstallStep.Published)]
    public async Task An_installer_failure_during_repair_keeps_the_item_damaged_and_repairable_after_restart(InstallStep step)
    {
        using var rig = new PipelineRig();
        var pipeline = await rig.Create(installCrash: step);
        var folder = pipeline.Snapshot().Single().Folder;
        rig.Damage(folder);
        Assert.Null(pipeline.Acquire("fixture"));
        await Assert.ThrowsAsync<IOException>(() => pipeline.RepairAsync("fixture", rig.Progress));
        Assert.Null(pipeline.Acquire("fixture"));
        var restarted = rig.Restart();
        Assert.Equal(ItemState.Damaged, restarted.State.Get("fixture").State);
        Assert.True(await restarted.RepairAsync("fixture", rig.Progress));
        using var lease = restarted.Acquire("fixture");
        Assert.NotNull(lease);
        restarted.Collect();
        Assert.Equal(new[] { folder }, Directory.GetDirectories(rig.Paths.Tools));
    }

    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Repair_waits_while_a_running_chat_leases_the_damaged_folder()
    {
        using var rig = new PipelineRig();
        var pipeline = await rig.Create();
        var lease = pipeline.Acquire("fixture")!;
        rig.Damage(lease.Folder);
        Assert.Null(pipeline.Acquire("fixture"));
        Assert.False(await pipeline.RepairAsync("fixture", rig.Progress));
        Assert.Equal(0, rig.Downloads);
        Assert.Equal("tampered", File.ReadAllText(Path.Combine(lease.Folder, "payload.txt")));
        lease.Dispose();
        Assert.True(await pipeline.RepairAsync("fixture", rig.Progress));
        using var again = pipeline.Acquire("fixture");
        Assert.NotNull(again);
    }

    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Repair_refuses_an_item_that_is_not_damaged_and_unbound_seams()
    {
        using var rig = new PipelineRig();
        var pipeline = await rig.Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.RepairAsync("fixture", rig.Progress));
        rig.Damage(pipeline.Snapshot().Single().Folder);
        var unbound = rig.Unbound();
        Assert.Null(unbound.Acquire("fixture"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => unbound.RepairAsync("fixture", rig.Progress));
        Assert.Equal(0, rig.Downloads);
    }
}
