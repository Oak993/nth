using System.Text.Json.Nodes;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: Dextra's store under crashes (DESIGN.md 15.3, unit AS-3): temp-then-rename leaves the old file or the new
// one at every cut, the mission journal resumes what a crash left unfolded, a torn or damaged journal never loses the
// lines before it, a delete cut part way still ends deleted, and an interrupted mission comes back through the mission
// model's own rule. A crash is a fault hook that throws where the process would die, then the store is abandoned (its
// lock released, nothing folded) and opened again.
[Trait("Decision", "ED-NEW-dextra-as3-2")]
[Trait("Feature", "AS-3")]
public sealed class AssistantStoreCrashTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private string MissionsFolder => Path.Combine(temp.Store, "missions");

    private static JsonObject Mission(string state, int step) => new() { ["state"] = state, ["step"] = step };

    private static Action<AssistantStoreFault> CrashAt(AssistantStoreFault point) => fault =>
    {
        if (fault == point) throw new AssistantStoreCrash();
    };

    private static void Crash(AssistantStore store, Action write)
    {
        Assert.Throws<AssistantStoreCrash>(write);
        store.AbandonForTests();
    }

    [Theory]
    [InlineData("TempPartlyWritten")]
    [InlineData("TempWritten")]
    public void A_crash_before_the_rename_leaves_the_old_file_whole_and_the_temporary_file_is_removed_at_open(string pointName)
    {
        var point = Enum.Parse<AssistantStoreFault>(pointName);
        using (var store = AssistantStore.Open(temp.Roots)) store.SaveProfile("dextra", new JsonObject { ["name"] = "Old", ["pad"] = new string('x', 4096) });
        var profile = Path.Combine(temp.Store, "assistants", "dextra", "profile.json");
        var before = File.ReadAllBytes(profile);
        var crashing = AssistantStore.Open(temp.Roots);
        crashing.Faults = CrashAt(point);
        Crash(crashing, () => crashing.SaveProfile("dextra", new JsonObject { ["name"] = "New", ["pad"] = new string('y', 4096) }));
        Assert.Equal(before, File.ReadAllBytes(profile));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(profile)!, ".profile.json.*.tmp"));
        using var reopened = AssistantStore.Open(temp.Roots);
        Assert.Equal(1, reopened.OpenReport.TempFilesRemoved);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(profile)!, "*.tmp"));
        Assert.Equal("Old", reopened.Profile("dextra")!["name"]!.GetValue<string>());
    }

    [Fact]
    public void A_completed_write_replaces_the_file_whole_and_leaves_no_temporary_file()
    {
        using var store = AssistantStore.Open(temp.Roots);
        store.SaveProfile("dextra", new JsonObject { ["name"] = "Old" });
        store.SaveProfile("dextra", new JsonObject { ["name"] = "New" });
        Assert.Equal("New", store.Profile("dextra")!["name"]!.GetValue<string>());
        Assert.Empty(Directory.EnumerateFiles(temp.Store, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_crash_after_the_journal_line_resumes_the_saved_state_from_the_journal()
    {
        using (var store = AssistantStore.Open(temp.Roots)) store.SaveMission("m-1", Mission("working", 1));
        var crashing = AssistantStore.Open(temp.Roots);
        crashing.Faults = CrashAt(AssistantStoreFault.JournalAppended);
        Crash(crashing, () => crashing.SaveMission("m-1", Mission("working", 2)));
        Assert.Equal(1, JsonNode.Parse(File.ReadAllText(Path.Combine(MissionsFolder, "m-1.json")))!["seq"]!.GetValue<long>());
        using var reopened = AssistantStore.Open(temp.Roots);
        Assert.Equal(["m-1"], reopened.OpenReport.ReplayedFromJournal);
        var mission = reopened.Mission("m-1")!;
        Assert.Equal(2, mission.Sequence);
        Assert.Equal(2, mission.Body["step"]!.GetValue<int>());
        Assert.False(File.Exists(Path.Combine(MissionsFolder, "m-1.journal"))); // folded at open
        Assert.Equal(2, JsonNode.Parse(File.ReadAllText(Path.Combine(MissionsFolder, "m-1.json")))!["seq"]!.GetValue<long>());
    }

    [Fact]
    public void A_mission_that_only_ever_reached_its_journal_comes_back()
    {
        var crashing = AssistantStore.Open(temp.Roots);
        crashing.Faults = CrashAt(AssistantStoreFault.JournalAppended);
        Crash(crashing, () => crashing.SaveMission("m-new", Mission("planning", 0)));
        Assert.False(File.Exists(Path.Combine(MissionsFolder, "m-new.json")));
        using var reopened = AssistantStore.Open(temp.Roots);
        Assert.Equal("planning", reopened.Mission("m-new")!.Body["state"]!.GetValue<string>());
    }

    [Fact]
    public void A_crash_after_the_fold_wrote_the_snapshot_keeps_one_copy_of_each_save()
    {
        var crashing = AssistantStore.Open(temp.Roots, new AssistantStoreOptions { CompactEvery = 1 });
        crashing.SaveMission("m-1", Mission("working", 1));
        crashing.Faults = CrashAt(AssistantStoreFault.SnapshotWritten);
        Crash(crashing, () => crashing.SaveMission("m-1", Mission("working", 2)));
        Assert.True(File.Exists(Path.Combine(MissionsFolder, "m-1.journal"))); // the cut came before it was emptied
        using var reopened = AssistantStore.Open(temp.Roots);
        Assert.Empty(reopened.OpenReport.ReplayedFromJournal);
        Assert.Equal(2, reopened.Mission("m-1")!.Sequence);
        Assert.False(File.Exists(Path.Combine(MissionsFolder, "m-1.journal")));
    }

    [Fact]
    public void A_journal_line_cut_by_a_crash_is_dropped_and_every_line_before_it_is_kept()
    {
        var crashing = AssistantStore.Open(temp.Roots);
        crashing.SaveMission("m-1", Mission("working", 1));
        crashing.SaveMission("m-1", Mission("working", 2));
        crashing.AbandonForTests();
        File.AppendAllText(Path.Combine(MissionsFolder, "m-1.journal"), "{\"seq\":3,\"mission\":{\"state\":\"wor");
        using var reopened = AssistantStore.Open(temp.Roots);
        Assert.Equal(2, reopened.Mission("m-1")!.Body["step"]!.GetValue<int>());
        Assert.Empty(reopened.OpenReport.Damaged);
        Assert.Equal(3, reopened.SaveMission("m-1", Mission("working", 3))); // numbering goes on from the kept line
    }

    [Fact]
    public void A_damaged_line_inside_the_journal_is_reported_and_the_journal_is_kept_aside()
    {
        var crashing = AssistantStore.Open(temp.Roots);
        crashing.SaveMission("m-1", Mission("working", 1));
        crashing.AbandonForTests();
        var journal = Path.Combine(MissionsFolder, "m-1.journal");
        File.AppendAllText(journal, "not json at all\n{\"seq\":3,\"mission\":{\"state\":\"working\",\"step\":3}}\n");
        using var reopened = AssistantStore.Open(temp.Roots);
        Assert.Equal(["m-1.journal:2"], reopened.OpenReport.Damaged);
        Assert.Equal(1, reopened.Mission("m-1")!.Body["step"]!.GetValue<int>());
        Assert.False(File.Exists(journal));
        Assert.Single(Directory.EnumerateFiles(MissionsFolder, "m-1.journal.damaged-*"));
    }

    [Fact]
    public void A_snapshot_that_does_not_parse_is_kept_aside_and_never_overwritten()
    {
        using (var store = AssistantStore.Open(temp.Roots)) store.SaveMission("m-1", Mission("working", 1));
        var snapshot = Path.Combine(MissionsFolder, "m-1.json");
        File.WriteAllText(snapshot, "{\"seq\":1,\"mission\":");
        using var reopened = AssistantStore.Open(temp.Roots);
        Assert.Null(reopened.Mission("m-1"));
        var aside = Assert.Single(reopened.OpenReport.Damaged);
        Assert.StartsWith("m-1.json.damaged-", aside, StringComparison.Ordinal);
        Assert.Equal("{\"seq\":1,\"mission\":", File.ReadAllText(Path.Combine(MissionsFolder, aside)));
    }

    [Fact]
    public void A_delete_cut_after_its_tombstone_still_ends_deleted()
    {
        using (var store = AssistantStore.Open(temp.Roots)) store.SaveMission("m-1", Mission("working", 1));
        // The tombstone reached the journal; the crash came before the files were removed.
        File.AppendAllText(Path.Combine(MissionsFolder, "m-1.journal"), "{\"seq\":2,\"deleted\":true}\n");
        using var reopened = AssistantStore.Open(temp.Roots);
        Assert.Null(reopened.Mission("m-1"));
        Assert.Empty(Directory.EnumerateFiles(MissionsFolder, "m-1*"));
    }

    [Fact]
    public void An_interrupted_mission_comes_back_through_the_mission_models_rule_and_stays_so()
    {
        static JsonObject? PauseLive(JsonObject body) =>
            body["state"]!.GetValue<string>() is "working" or "pausing" or "stopping"
                ? new JsonObject { ["state"] = "paused", ["step"] = body["step"]!.DeepClone(), ["resumePrompt"] = true }
                : null;
        var options = new AssistantStoreOptions { InterruptedMission = PauseLive };
        var crashing = AssistantStore.Open(temp.Roots, options);
        crashing.SaveMission("m-a", Mission("working", 4));
        crashing.SaveMission("m-b", Mission("done", 9));
        crashing.SaveMission("m-c", Mission("pausing", 2));
        crashing.AbandonForTests();
        using (var reopened = AssistantStore.Open(temp.Roots, options))
        {
            Assert.Equal(["m-a", "m-c"], reopened.OpenReport.Interrupted);
            var a = reopened.Mission("m-a")!;
            Assert.Equal("paused", a.Body["state"]!.GetValue<string>());
            Assert.True(a.Body["resumePrompt"]!.GetValue<bool>());
            Assert.Equal(4, a.Body["step"]!.GetValue<int>());
            Assert.Equal(2, a.Sequence);
            Assert.Equal("done", reopened.Mission("m-b")!.Body["state"]!.GetValue<string>());
        }
        using var again = AssistantStore.Open(temp.Roots, options);
        Assert.Empty(again.OpenReport.Interrupted);
        Assert.Equal("paused", again.Mission("m-c")!.Body["state"]!.GetValue<string>());
    }
}
