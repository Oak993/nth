// PORT-ONLY: integ-sk8-sk9. SK-9's per-chat generations bound to SK-8's pipeline, the one owner of the root (ED-325 "SK-9/SK-19 must bind this owner").
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Tests.Port.Skills.Pipelines;

[Trait("Spec", "DL-8")]
public sealed class PipelineGenerationBindingTests
{
    [Fact]
    [Trait("Feature", "SK-9")]
    [Trait("Rule", "R-TEST-4")]
    public async Task An_update_during_a_chat_keeps_its_generation_and_the_resumed_chat_gets_the_new_pin_at_the_same_path()
    {
        using var rig = new PipelineRig(skillset: true, disclosure: true);
        var pipeline = await rig.Create();
        using var generations = new SkillGenerations(rig.Paths, pipeline, rig.Protection, _ => []);
        var running = generations.Start("codex", "one", [Item(rig, rig.Old.Pin!)], Switches(), new GenerationBudget(8000));
        var path = running.Folder;
        Assert.Contains("OLD BODY", running.Invoke("skill"), StringComparison.Ordinal);
        pipeline.Collect();
        await pipeline.UpdateAsync("fixture", rig.Progress);
        pipeline.Collect();
        // The live chat's generation is frozen: same folder, same bytes, still verified, though its source pin was collected.
        Assert.True(running.Verify());
        Assert.Contains("OLD BODY", running.Invoke("skill"), StringComparison.Ordinal);
        Assert.False(Directory.Exists(rig.Trusted(rig.Old.Pin!).Folder));
        running.Dispose();
        using var resumed = generations.Start("codex", "one", [Item(rig, rig.Next.Pin!)], Switches(), new GenerationBudget(8000));
        Assert.Equal(path, resumed.Folder);
        Assert.Contains("NEW BODY", resumed.Invoke("skill"), StringComparison.Ordinal);
        pipeline.Collect();
        Assert.True(resumed.Verify());
    }

    [Fact]
    [Trait("Feature", "SK-9")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Removing_a_set_during_a_chat_keeps_that_chat_and_new_chats_no_longer_list_it()
    {
        using var rig = new PipelineRig(skillset: true, disclosure: true);
        var pipeline = await rig.Create();
        using var generations = new SkillGenerations(rig.Paths, pipeline, rig.Protection, _ => []);
        var item = Item(rig, rig.Old.Pin!);
        using var running = generations.Start("codex", "one", [item], Switches(), new GenerationBudget(8000));
        await pipeline.RemoveAsync("fixture");
        pipeline.Collect();
        Assert.Contains("OLD BODY", running.Invoke("skill"), StringComparison.Ordinal);
        using var other = generations.Start("codex", "two", [item], Switches(), new GenerationBudget(8000));
        Assert.Empty(other.Skills);
    }

    [Fact]
    [Trait("Feature", "SK-9")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Pipeline_GC_keeps_live_generations_and_spares_and_deletes_an_ended_chat()
    {
        using var rig = new PipelineRig(skillset: true, disclosure: true);
        var pipeline = await rig.Create();
        var generations = new SkillGenerations(rig.Paths, pipeline, rig.Protection, _ => []);
        var key = generations.Prepare("codex", [Item(rig, rig.Old.Pin!)], Switches(), new GenerationBudget(8000));
        var chat = generations.Start("codex", "one", [Item(rig, rig.Old.Pin!)], Switches(), new GenerationBudget(8000));
        Assert.Equal(key, chat.Key);
        var path = chat.Folder;
        pipeline.Collect();
        Assert.True(chat.Verify());
        var sk4Spare = pipeline.Snapshot().Single().SparePaths.Single();
        Assert.True(Directory.Exists(sk4Spare)); // SK-4's spare stays with its selected route.
        chat.Dispose();
        generations.Dispose(); // Waits for the background replacement spare, then releases every spare lease.
        pipeline.Collect();
        Assert.False(Directory.Exists(path));
        var codex = Path.Combine(rig.Paths.Views, "codex");
        Assert.True(!Directory.Exists(codex) || Directory.GetDirectories(codex).Length == 0);
        Assert.True(Directory.Exists(sk4Spare));
    }

    private static GenerationItem Item(PipelineRig rig, string pin) =>
        new(rig.Trusted(pin), [new GenerationSkill("fixture", "skill", "skill", "Fixture description", SkillGroup.Documents, 0, false)]);

    private static SkillSwitches Switches() => new("fixture", new Dictionary<string, bool>(), new Dictionary<string, bool>());
}
