using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-4")]
public sealed class AsturOpenItemsTests
{
    [Theory]
    [InlineData("2")]
    [InlineData("\"wrong-type\"")]
    public async Task An_unknown_deletion_record_is_preserved_without_deleting_files(string version)
    {
        using var rig = new AsturTeamRig();
        await rig.RootAsync();
        var astur = (await rig.Team.CreateAsync(AsturTeamRig.Draft("Shop", AsturShape.Squircle))).Profile!;
        var own = Path.Combine(rig.Store.AssistantFolder(astur.Id), "keep.txt");
        File.WriteAllText(own, "fake");
        await rig.Team.RetireAsync(astur.Id);
        var path = Path.Combine(rig.Store.Root, "astur-delete-" + astur.Id + ".json");
        var original = "{\"version\":" + version + ",\"assistant\":\"" + astur.Id + "\",\"keep\":true}";
        File.WriteAllText(path, original);
        Assert.False((await rig.Team.DeleteFilesAsync(astur.Id)).Ok);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.True(File.Exists(own));
    }

    [Fact]
    public async Task Pill_order_survives_edit_and_reopening_without_changing_a_siblings_order()
    {
        using var rig = new AsturTeamRig();
        await rig.RootAsync();
        var draft = AsturTeamRig.Draft("Shop", AsturShape.Squircle) with { PillOrder = new ValueList<string>(["codex", "claude"]) };
        var made = (await rig.Team.CreateAsync(draft)).Profile!;
        var sibling = (await rig.Team.CreateAsync(AsturTeamRig.Draft("Desk", AsturShape.Hexagon))).Profile!;
        Assert.Equal(["codex", "claude"], rig.Open().Get(made.Id)!.PillOrder);
        Assert.Empty(rig.Team.Get(sibling.Id)!.PillOrder);
        Assert.True((await rig.Team.UpdateAsync(made.Id, AsturTeamRules.ToDraft(made) with { PillOrder = new ValueList<string>(["claude", "codex"]) })).Ok);
        Assert.Equal(["claude", "codex"], rig.Open().Get(made.Id)!.PillOrder);
    }

    [Fact]
    public async Task File_delete_needs_retirement_and_keeps_other_assistants_and_shared_notes()
    {
        using var rig = new AsturTeamRig();
        var root = await rig.RootAsync();
        var astur = (await rig.Team.CreateAsync(AsturTeamRig.Draft("Shop", AsturShape.Squircle))).Profile!;
        var own = Path.Combine(rig.Store.AssistantFolder(astur.Id), "own.txt");
        var shared = Path.Combine(rig.Store.SharedMemoryFolder(), "owner.md");
        File.WriteAllText(own, "fake own"); File.WriteAllText(shared, "fake shared");
        Assert.False((await rig.Team.DeleteFilesAsync(astur.Id)).Ok);
        Assert.True(File.Exists(own));
        Assert.True((await rig.Team.RetireAsync(astur.Id)).Ok);
        Assert.True(File.Exists(own));
        Assert.True((await rig.Team.DeleteFilesAsync(astur.Id)).Ok);
        Assert.False(File.Exists(own));
        Assert.True(File.Exists(shared));
        Assert.NotNull(rig.Open().Root);
        Assert.Equal(root.Id, rig.Team.Root!.Id);
        Assert.True(rig.Team.Get(astur.Id)!.Retired);
        Assert.True((await rig.Team.DeleteFilesAsync(astur.Id)).Ok);
    }

    [Fact]
    public async Task New_root_uses_the_hosts_id_while_later_asturs_get_new_ids()
    {
        using var temp = new AssistantTempRoot();
        using var store = AssistantStore.Open(temp.Roots);
        const string rootId = "dextra-host-id";
        using var team = AssistantTeam.Open(store, new AssistantTeamOptions { RootId = rootId, NewId = () => "new-astur" });
        Assert.Equal(rootId, (await team.EnsureRootAsync()).Profile!.Id);
        Assert.Equal("new-astur", (await team.CreateAsync(AsturTeamRig.Draft("Shop", AsturShape.Squircle))).Profile!.Id);
    }

    [Fact]
    public async Task A_crash_after_one_delete_leaves_an_intent_and_an_explicit_retry_finishes_only_owned_files()
    {
        using var rig = new AsturTeamRig();
        await rig.RootAsync();
        var astur = (await rig.Team.CreateAsync(AsturTeamRig.Draft("Shop", AsturShape.Squircle))).Profile!;
        File.WriteAllText(Path.Combine(rig.Store.AssistantFolder(astur.Id), "a.txt"), "fake");
        File.WriteAllText(Path.Combine(rig.Store.AssistantFolder(astur.Id), "b.txt"), "fake");
        await rig.Team.RetireAsync(astur.Id);
        using (AssistantStore.AsturDeleteCheckpoint.Override(_ => throw new IOException("fake crash")))
            Assert.False((await rig.Team.DeleteFilesAsync(astur.Id)).Ok);
        var intent = Path.Combine(rig.Store.Root, "astur-delete-" + astur.Id + ".json");
        Assert.Contains("pending", File.ReadAllText(intent), StringComparison.Ordinal);
        Assert.True((await rig.Team.DeleteFilesAsync(astur.Id)).Ok);
        Assert.Contains("done", File.ReadAllText(intent), StringComparison.Ordinal);
        Assert.True(rig.Open().Get(astur.Id)!.Retired);
    }
}
