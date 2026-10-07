// PORT-ONLY: SK-13 / ED-326 (6)(a). The composition the app owes: ONE SkillsPipeline per data root, the only owner of that root, with SK-9's generations bound to it through IGenerationStore
// (a second owner on the root would delete the other's trees: ED-326 (1)). Offline fixtures only: fake protection, no seam to a download, a process or a network.
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Host;

[Trait("Feature", "SK-13")]
[Trait("Decision", "ED-326")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class SkillsHostCompositionTests
{
    [Fact]
    public async Task One_data_root_has_one_owner_and_a_second_open_is_refused_until_the_first_is_disposed()
    {
        using var rig = new PipelineRig(skillset: true, disclosure: true, root: SkillsHostRig.FreshRoot());
        await rig.Create();
        var options = SkillsHostRig.Options(rig, rig.Old);
        var first = SkillsHost.Open(rig.Paths.Root, options);
        Assert.Same(first, SkillsHost.Find(rig.Paths.Root));
        Assert.Throws<InvalidOperationException>(() => SkillsHost.Open(rig.Paths.Root, options));
        Assert.Throws<InvalidOperationException>(() => SkillsHost.Open(rig.Paths.Root + Path.DirectorySeparatorChar, options));
        first.Dispose();
        Assert.Null(SkillsHost.Find(rig.Paths.Root));
        using var again = SkillsHost.Open(rig.Paths.Root, options);
        Assert.NotSame(first, again);
        Assert.NotSame(first.Pipeline, again.Pipeline);
    }

    [Fact]
    public async Task Two_data_roots_have_two_independent_pipelines()
    {
        using var a = new PipelineRig(skillset: true, disclosure: true, root: SkillsHostRig.FreshRoot());
        using var b = new PipelineRig(skillset: true, disclosure: true, root: SkillsHostRig.FreshRoot());
        await a.Create();
        await b.Create();
        using var hostA = SkillsHost.Open(a.Paths.Root, SkillsHostRig.Options(a, a.Old));
        using var hostB = SkillsHost.Open(b.Paths.Root, SkillsHostRig.Options(b, b.Old));
        Assert.NotSame(hostA.Pipeline, hostB.Pipeline);
        Assert.Equal(a.Paths.Root, hostA.Paths.Root);
        Assert.Equal(b.Paths.Root, hostB.Paths.Root);
    }

    [Fact]
    public async Task The_generations_are_bound_to_the_roots_pipeline_so_its_GC_keeps_a_live_chat_and_collects_it_when_the_chat_ends()
    {
        using var rig = new PipelineRig(skillset: true, disclosure: true, root: SkillsHostRig.FreshRoot());
        await rig.Create();
        using var host = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
        var item = SkillsHostRig.Item(rig, rig.Old.Pin!);
        var chat = host.Generations.Start("codex", "one", [item], SkillsHostRig.Switches(), new GenerationBudget(8000));
        var path = chat.Folder;
        host.Pipeline.Collect(); // the pipeline owns the root's GC: it must know this generation is leased
        Assert.True(chat.Verify());
        Assert.True(Directory.Exists(path));
        var sk4Spare = host.Pipeline.Snapshot().Single().SparePaths.Single();
        Assert.True(Directory.Exists(sk4Spare));
        chat.Dispose();
        host.Generations.Dispose();
        host.Pipeline.Collect();
        Assert.False(Directory.Exists(path)); // an ended chat is the pipeline's to delete
        Assert.True(Directory.Exists(sk4Spare));
    }

    [Fact]
    public async Task A_removal_during_a_chat_keeps_that_chat_through_the_hosts_pipeline_and_new_chats_no_longer_list_the_set()
    {
        using var rig = new PipelineRig(skillset: true, disclosure: true, root: SkillsHostRig.FreshRoot());
        await rig.Create();
        using var host = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
        var item = SkillsHostRig.Item(rig, rig.Old.Pin!);
        using var running = host.Generations.Start("codex", "one", [item], SkillsHostRig.Switches(), new GenerationBudget(8000));
        await host.Pipeline.RemoveAsync("fixture");
        host.Pipeline.Collect();
        Assert.Contains("OLD BODY", running.Invoke("skill"), StringComparison.Ordinal);
        using var other = host.Generations.Start("codex", "two", [item], SkillsHostRig.Switches(), new GenerationBudget(8000));
        Assert.Empty(other.Skills);
    }

    [Fact]
    public async Task An_installed_skill_set_is_restored_from_the_catalogue_after_a_restart_and_is_usable()
    {
        using var rig = new PipelineRig(skillset: true, disclosure: true, root: SkillsHostRig.FreshRoot());
        await rig.Create();
        using var host = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
        Assert.Empty(host.Unrestored);
        Assert.Equal(ItemState.Installed, host.Pipeline.State.Get("fixture").State);
        Assert.Single(host.Pipeline.Snapshot());
        using var lease = host.Pipeline.AcquireForViewBuild("fixture");
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task An_installed_record_whose_trust_cannot_be_rebuilt_is_dropped_and_reported_its_tree_is_left_and_never_used()
    {
        using var rig = new PipelineRig(root: SkillsHostRig.FreshRoot()); // a tool: its installed tree has no per-file pins in the catalogue to rebuild a descriptor from
        await rig.Create();
        var folder = rig.Trusted(rig.Old.Pin!).Folder;
        using var host = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
        Assert.Equal(["fixture"], host.Unrestored);
        Assert.Null(host.Pipeline.State.Items.FirstOrDefault(r => r.Id == "fixture")?.InstalledPin);
        Assert.Null(host.Pipeline.AcquireForViewBuild("fixture"));
        Assert.Null(host.Pipeline.Acquire("fixture"));
        Assert.Empty(host.Pipeline.Snapshot());
        Assert.True(Directory.Exists(folder), "Nothing is deleted behind the user's back when trust cannot be rebuilt.");
    }

    [Fact]
    public async Task The_startup_collection_removes_the_residue_of_an_install_that_stopped()
    {
        using var rig = new PipelineRig(skillset: true, disclosure: true, root: SkillsHostRig.FreshRoot());
        await rig.Create();
        var residue = Path.Combine(rig.Paths.Tools, "tmp-abandoned");
        Directory.CreateDirectory(residue);
        File.WriteAllText(Path.Combine(residue, "half.txt"), "an install that stopped");
        using var host = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
        Assert.True(Directory.Exists(residue), "Open must not collect: the GC is off the launch path");
        await host.CollectInBackground();
        Assert.False(Directory.Exists(residue));
        Assert.Equal(ItemState.Installed, host.Pipeline.State.Get("fixture").State);
    }

    [Fact]
    public async Task The_startup_collection_finishes_a_reset_that_was_cut_short()
    {
        using var rig = new PipelineRig(skillset: true, disclosure: true, root: SkillsHostRig.FreshRoot());
        await rig.Create();
        using (var first = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old)))
            await first.ResetAsync();
        // The marker is still there and a shim a held handle kept remains in addons; nothing is installed.
        var bin = Path.Combine(rig.Paths.Addons, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "python.exe"), "a shim a held handle kept");
        File.WriteAllText(Path.Combine(rig.Paths.Skills, "reset-pending"), "");
        using var reopened = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
        await reopened.CollectInBackground();
        Assert.False(Directory.Exists(bin));
        Assert.False(File.Exists(Path.Combine(rig.Paths.Skills, "reset-pending")));
    }

    [Fact]
    [Trait("Spec", "DL-16")]
    public async Task Reset_through_the_host_removes_what_is_installed_and_keeps_enabled_json_and_the_accepted_terms()
    {
        using var rig = new PipelineRig(nonOpen: true, skillset: true, disclosure: true, root: SkillsHostRig.FreshRoot());
        await rig.Create();
        Directory.CreateDirectory(rig.Paths.Skills);
        File.WriteAllText(rig.Paths.EnabledFile, "fixture enabled switches");
        var folder = rig.Trusted(rig.Old.Pin!).Folder;
        var hash = ItemTerms.LicenceHash(rig.Old)!;
        using var host = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
        Assert.True(host.Pipeline.State.HasAccepted(hash));
        Assert.Null(host.Terms.Offer("fixture"));
        await host.ResetAsync();
        Assert.Empty(host.Pipeline.Snapshot());
        Assert.False(Directory.Exists(folder));
        Assert.Equal("fixture enabled switches", File.ReadAllText(rig.Paths.EnabledFile));
        Assert.True(host.Pipeline.State.HasAccepted(hash));
        host.Dispose();
        // and across a restart: state.json still holds the acceptance
        using var again = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
        Assert.True(again.Pipeline.State.HasAccepted(hash));
        Assert.Null(again.Terms.Offer("fixture"));
    }
}
