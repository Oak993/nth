// PORT-ONLY: SK-13 review fix. An installed tree whose trust cannot be rebuilt is kept on EVERY launch (addons/orphans.json) until the item is removed or Reset runs; the startup GC runs off the
// launch path, once, on the pool.
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Host;

[Trait("Feature", "SK-13")]
[Trait("Spec", "ED-326")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class SkillsHostOrphanTests
{
    private static async Task<(PipelineRig Rig, string Folder)> ToolRig()
    {
        var rig = new PipelineRig(root: SkillsHostRig.FreshRoot());
        await rig.Create();
        return (rig, rig.Trusted(rig.Old.Pin!).Folder);
    }

    [Fact]
    public async Task An_orphaned_tree_survives_every_launch_and_every_collection_and_is_listed()
    {
        var (rig, folder) = await ToolRig();
        using (rig)
        {
            for (var launch = 1; launch <= 3; launch++)
            {
                using var host = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
                Assert.Equal(["fixture"], host.Pipeline.Orphans);
                await host.CollectInBackground();
                host.Pipeline.Collect();
                Assert.True(Directory.Exists(folder), "launch " + launch);
                Assert.Null(host.Pipeline.AcquireForViewBuild("fixture"));
            }
        }
    }

    [Fact]
    public async Task An_explicit_removal_of_the_item_releases_the_orphan_to_the_GC()
    {
        var (rig, folder) = await ToolRig();
        using (rig)
        {
            using (SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old))) { }
            using var host = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
            await host.Pipeline.RemoveAsync("fixture");
            Assert.Empty(host.Pipeline.Orphans);
            Assert.False(Directory.Exists(folder));
            Assert.False(File.Exists(Path.Combine(rig.Paths.Addons, "orphans.json")));
        }
    }

    [Fact]
    public async Task Reset_releases_every_orphan_and_the_marker_file_goes()
    {
        var (rig, folder) = await ToolRig();
        using (rig)
        {
            using var host = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
            await host.ResetAsync();
            Assert.Empty(host.Pipeline.Orphans);
            Assert.False(Directory.Exists(folder));
            Assert.False(File.Exists(Path.Combine(rig.Paths.Addons, "orphans.json")));
        }
    }

    [Fact]
    public async Task When_the_route_sidecar_is_gone_every_tool_tree_is_retained_not_just_one()
    {
        var (rig, folder) = await ToolRig();
        using (rig)
        {
            foreach (var route in Directory.GetFiles(Path.Combine(rig.Paths.Addons, "routes"))) File.Delete(route);
            for (var launch = 1; launch <= 2; launch++)
            {
                using var host = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
                await host.CollectInBackground();
                Assert.True(Directory.Exists(folder), "launch " + launch);
            }
        }
    }

    [Fact]
    public async Task A_corrupt_orphan_file_retains_every_tool_tree()
    {
        var (rig, folder) = await ToolRig();
        using (rig)
        {
            using (SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old))) { }
            File.WriteAllText(Path.Combine(rig.Paths.Addons, "orphans.json"), "not json");
            using var host = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
            await host.CollectInBackground();
            Assert.True(Directory.Exists(folder));
        }
    }

    [Fact]
    public async Task The_startup_collection_is_one_task_that_runs_off_the_caller_and_disposal_waits_for_it()
    {
        var (rig, _) = await ToolRig();
        using (rig)
        {
            var host = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
            var first = host.CollectInBackground();
            Assert.Same(first, host.CollectInBackground());
            host.Dispose();
            Assert.True(first.IsCompleted);
            Assert.ThrowsAny<ObjectDisposedException>(() => { _ = host.CollectInBackground(); });
        }
    }
}
