using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Port.Skills.Store;
namespace Marrow.Services.Tests.Port.Skills.Pipelines;
[Trait("Spec", "DL-8")]
public sealed class PipelineGenerationTests
{
    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Skillset_retry_replaces_unselected_routing_and_publishes_new_spares()
    {
        using var rig = new PipelineRig(skillset: true);
        var fail = true;
        var pipeline = await rig.Create(step => { if (fail && step == PipelineStep.BeforeCommit) throw new IOException("Injected crash"); });
        var old = pipeline.Snapshot().Single();
        await Assert.ThrowsAsync<IOException>(() => pipeline.UpdateAsync("fixture", rig.Progress));
        Assert.Equal(old.SparePaths, pipeline.Snapshot().Single().SparePaths);
        fail = false;
        await pipeline.UpdateAsync("fixture", rig.Progress);
        var current = pipeline.Snapshot().Single();
        Assert.NotEqual(old.Folder, current.Folder);
        Assert.NotEqual(old.SparePaths.Single(), current.SparePaths.Single());
        Assert.Equal("Fixture skill instructions", File.ReadAllText(Path.Combine(current.SparePaths.Single(), "skills", "skill", "SKILL.md")));
        Assert.Equal(rig.Next.Pin, rig.Restart().State.Get("fixture").InstalledPin);
    }

    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_leased_generation_holds_old_sources_and_GC_removes_unleased_siblings()
    {
        using var rig = new PipelineRig(skillset: true);
        var pipeline = await rig.Create();
        var old = pipeline.Snapshot().Single();
        var descriptor = new StoreManifest(rig.Old.Files.Where(f => f.Path == "skill/SKILL.md").Select(f => f with { Path = "skills/" + f.Path }));
        using var lease = pipeline.AcquireGeneration(old.SparePaths.Single(), descriptor, [("fixture", old.Pin)]);
        Assert.NotNull(lease);
        await pipeline.UpdateAsync("fixture", rig.Progress);
        pipeline.Collect();
        Assert.True(Directory.Exists(old.Folder));
        Assert.True(Directory.Exists(lease.Folder));
        lease.Dispose();
        pipeline.Collect();
        Assert.False(Directory.Exists(old.Folder));
        Assert.False(Directory.Exists(lease.Folder));
        Assert.True(Directory.Exists(pipeline.Snapshot().Single().SparePaths.Single()));
    }

    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Multi_pin_set_installs_under_a_safe_key_and_preserves_the_composed_state_pin()
    {
        using var rig = new PipelineRig(skillset: true, multiPin: true);
        var pipeline = await rig.Create();
        await pipeline.UpdateAsync("fixture", rig.Progress);
        Assert.Equal(UpdateCheck.CataloguePin(rig.Next), pipeline.State.Get("fixture").InstalledPin);
        Assert.Equal(UpdateCheck.CataloguePin(rig.Next), rig.Restart().State.Get("fixture").InstalledPin);
    }

    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Trusted_empty_directories_survive_update_and_extra_directories_fail_verification()
    {
        using var rig = new PipelineRig(emptyDirectory: true);
        var pipeline = await rig.Create();
        using var old = pipeline.Acquire("fixture");
        Assert.NotNull(old);
        await pipeline.UpdateAsync("fixture", rig.Progress);
        var current = pipeline.Snapshot().Single();
        Assert.True(Directory.Exists(Path.Combine(current.Folder, "empty")));
        Directory.CreateDirectory(Path.Combine(current.Folder, "extra"));
        Assert.Null(pipeline.Acquire("fixture"));
        Assert.Equal(ItemState.Damaged, pipeline.State.Get("fixture").State);
    }
    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_snapshot_selected_before_an_update_cannot_lease_different_source_pins_after_commit()
    {
        using var rig = new PipelineRig(skillset: true);
        var pipeline = await rig.Create();
        var old = pipeline.Snapshot().Single();
        var manifest = new StoreManifest(rig.Old.Files.Where(f => f.Path == "skill/SKILL.md").Select(f => f with { Path = "skills/" + f.Path }));
        await pipeline.UpdateAsync("fixture", rig.Progress);
        Assert.Null(pipeline.AcquireGeneration(old.SparePaths.Single(), manifest, [("fixture", old.Pin)]));
    }
}
