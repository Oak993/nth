using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Port.Skills.Shims;

namespace Marrow.Services.Tests.Port.Skills.Pipelines;

[Trait("Feature", "SK-8")]
[Trait("Rule", "DL-8")]
public sealed class PipelineAcceptanceTests
{
    [Fact]
    public async Task New_pin_switches_shims_and_generations_together_and_old_lease_survives_GC()
    {
        using var rig = new PipelineRig();
        var pipeline = await rig.Create();
        using var lease = pipeline.Acquire("fixture")!;
        var old = lease.Folder;
        await pipeline.UpdateAsync("fixture", rig.Progress);
        var snapshot = PipelineRoutes.Read(rig.Paths.StateFile);
        Assert.Equal(rig.Next.Pin, snapshot.Single().Pin);
        Assert.Equal(snapshot.Single().Folder, Path.GetDirectoryName(PipelineRoutes.ResolveShim(rig.Paths.StateFile, "fixture.exe").Target));
        Assert.Equal(snapshot.Single().SparePaths, pipeline.Snapshot().Single().SparePaths);
        pipeline.Collect();
        Assert.True(File.Exists(Path.Combine(old, "payload.txt")));
        lease.Dispose();
        pipeline.Collect();
        Assert.False(Directory.Exists(old));
        Assert.NotNull(pipeline.Acquire("fixture"));
    }

    [Theory]
    [InlineData(PipelineStep.Downloaded)]
    [InlineData(PipelineStep.Installed)]
    [InlineData(PipelineStep.RoutesPrepared)]
    [InlineData(PipelineStep.BeforeCommit)]
    public async Task Failure_at_each_pipeline_step_keeps_previous_pin_usable_after_restart(PipelineStep step)
    {
        using var rig = new PipelineRig();
        var pipeline = await rig.Create((at) => { if (at == step) throw new IOException("Injected crash"); });
        await Assert.ThrowsAsync<IOException>(() => pipeline.UpdateAsync("fixture", rig.Progress));
        var restarted = rig.Restart();
        using var lease = restarted.Acquire("fixture");
        Assert.NotNull(lease);
        Assert.Equal(rig.Old.Pin, restarted.State.Get("fixture").InstalledPin);
        Assert.Equal("offline fixture", File.ReadAllText(Path.Combine(lease.Folder, "payload.txt")));
    }

    [Theory]
    [InlineData(Marrow.Services.Port.Skills.Install.InstallStep.Created)]
    [InlineData(Marrow.Services.Port.Skills.Install.InstallStep.Built)]
    [InlineData(Marrow.Services.Port.Skills.Install.InstallStep.Renamed)]
    [InlineData(Marrow.Services.Port.Skills.Install.InstallStep.Locked)]
    [InlineData(Marrow.Services.Port.Skills.Install.InstallStep.Verified)]
    [InlineData(Marrow.Services.Port.Skills.Install.InstallStep.Published)]
    public async Task Installer_crash_at_each_step_preserves_previous_version(Marrow.Services.Port.Skills.Install.InstallStep step)
    {
        using var rig = new PipelineRig();
        var pipeline = await rig.Create(installCrash: step);
        await Assert.ThrowsAsync<IOException>(() => pipeline.UpdateAsync("fixture", rig.Progress));
        using var lease = rig.Restart().Acquire("fixture");
        Assert.NotNull(lease);
        Assert.Equal("offline fixture", File.ReadAllText(Path.Combine(lease.Folder, "payload.txt")));
    }

    [Fact]
    public async Task Changed_non_open_terms_block_before_download_and_acceptance_allows_update()
    {
        using var rig = new PipelineRig(nonOpen: true);
        var pipeline = await rig.Create();
        await Assert.ThrowsAsync<PipelineTermsRequiredException>(() => pipeline.UpdateAsync("fixture", rig.Progress));
        Assert.Equal(0, rig.Downloads);
        pipeline.AcceptTerms("fixture", DateTimeOffset.UtcNow);
        await pipeline.UpdateAsync("fixture", rig.Progress);
        Assert.Equal(1, rig.Downloads);
    }

    [Fact]
    public async Task Update_everything_lists_installed_latest_progress_and_result()
    {
        using var rig = new PipelineRig();
        var pipeline = await rig.Create();
        IUpdateItem item = Assert.Single(pipeline.UpdateItems());
        Assert.Equal(UpdateItemKind.SkillsAndTools, item.Kind);
        Assert.Equal(rig.Old.Pin, await item.InstalledVersion(default));
        Assert.Equal(rig.Next.Pin, await item.LatestVersion(default));
        var outcome = await item.Update(rig.Progress, default);
        Assert.True(outcome.Succeeded);
        Assert.True(outcome.Changed);
        Assert.False(outcome.RestartRequired);
        Assert.Equal(rig.Next.Pin, outcome.NewVersion);
        Assert.Contains(0.5, rig.Reports);
        Assert.Contains(1, rig.Reports);
    }

    [Fact]
    public async Task Remove_while_leased_removes_route_now_and_deletes_after_lease_ends()
    {
        using var rig = new PipelineRig();
        var pipeline = await rig.Create();
        using var lease = pipeline.Acquire("fixture")!;
        await pipeline.RemoveAsync("fixture");
        Assert.Empty(pipeline.Snapshot());
        Assert.Throws<InvalidOperationException>(() => PipelineRoutes.ResolveShim(rig.Paths.StateFile, "fixture.exe"));
        Assert.True(Directory.Exists(lease.Folder));
        lease.Dispose();
        pipeline.Collect();
        Assert.False(Directory.Exists(lease.Folder));
    }

    [Fact]
    public async Task Removing_Python_removes_package_sets_and_keeps_skills_with_missing_needs()
    {
        using var rig = new PipelineRig();
        var pipeline = await rig.CreateDependencies();
        await pipeline.RemoveAsync("T1");
        Assert.DoesNotContain(pipeline.Snapshot(), r => r.Id == "T1" || r.Id.StartsWith("P", StringComparison.Ordinal));
        Assert.Contains(pipeline.Snapshot(), r => r.Id == "A1");
        Assert.Contains("T1", pipeline.MissingNeeds("A1"));
    }

    [Fact]
    public async Task Reset_preserves_enabled_and_accepted_terms_and_defers_live_tree()
    {
        using var rig = new PipelineRig(nonOpen: true);
        var pipeline = await rig.Create();
        pipeline.AcceptTerms("fixture", DateTimeOffset.UtcNow);
        Directory.CreateDirectory(rig.Paths.Skills);
        File.WriteAllText(rig.Paths.EnabledFile, "fixture enabled switches");
        using var lease = pipeline.Acquire("fixture")!;
        await pipeline.ResetAsync();
        Assert.Empty(pipeline.Snapshot());
        Assert.Single(pipeline.State.AcceptedTerms);
        Assert.Equal("fixture enabled switches", File.ReadAllText(rig.Paths.EnabledFile));
        Assert.True(Directory.Exists(lease.Folder));
        lease.Dispose();
        pipeline.Collect();
        Assert.False(Directory.Exists(rig.Paths.Tools));
        Assert.False(Directory.Exists(rig.Paths.Store));
    }
}
