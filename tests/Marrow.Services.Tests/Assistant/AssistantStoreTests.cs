using System.Text.Json.Nodes;
using Marrow.Services.Assistant;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: Dextra's store (DESIGN.md 15.3, unit AS-3): the round trip of every document kind, the layout, ids that
// cannot leave their folder, one store per folder, the feed's 30 days and the default-path seam of 16.2.
[Trait("Decision", "ED-NEW-dextra-as3-2")]
[Trait("Feature", "AS-3")]
public sealed class AssistantStoreTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private static JsonObject Mission(string state, int step) => new()
    {
        ["state"] = state,
        ["brief"] = "Tidy the release notes",
        ["plan"] = new JsonArray("read", "edit", "review"),
        ["step"] = step,
        ["lineage"] = new JsonObject { ["assistant"] = "dextra", ["parent"] = null },
    };

    [Fact]
    public void Missions_profiles_and_duties_round_trip_through_a_reopen()
    {
        using (var store = AssistantStore.Open(temp.Roots))
        {
            Assert.Equal(1, store.SaveMission("m-1", Mission("working", 1)));
            Assert.Equal(2, store.SaveMission("m-1", Mission("working", 2)));
            store.SaveMission("m-2", Mission("done", 3));
            store.SaveProfile("dextra", new JsonObject { ["name"] = "Dextra", ["tint"] = "#7aa2ff" });
            store.SaveDuties("dextra", new JsonArray(new JsonObject { ["id"] = "d1", ["every"] = "weekday 08:00" }));
        }
        using var reopened = AssistantStore.Open(temp.Roots);
        var missions = reopened.Missions();
        Assert.Equal(["m-1", "m-2"], missions.Select(m => m.Id));
        Assert.Equal(2, missions[0].Sequence);
        Assert.True(JsonNode.DeepEquals(Mission("working", 2), missions[0].Body));
        Assert.True(JsonNode.DeepEquals(Mission("done", 3), reopened.Mission("m-2")!.Body));
        Assert.Equal("Dextra", reopened.Profile("dextra")!["name"]!.GetValue<string>());
        Assert.Equal("d1", reopened.Duties("dextra")![0]!["id"]!.GetValue<string>());
        Assert.Equal(["dextra"], reopened.AssistantIds());
        Assert.Empty(reopened.OpenReport.ReplayedFromJournal);
        Assert.Empty(reopened.OpenReport.Damaged);
    }

    [Fact]
    public void A_returned_mission_is_a_copy_the_caller_cannot_change_the_store_through()
    {
        using var store = AssistantStore.Open(temp.Roots);
        var body = Mission("working", 1);
        store.SaveMission("m-1", body);
        body["state"] = "changed after the save";
        var read = store.Mission("m-1")!;
        read.Body["state"] = "changed after the read";
        Assert.Equal("working", store.Mission("m-1")!.Body["state"]!.GetValue<string>());
    }

    [Fact]
    public void The_layout_is_the_one_section_15_3_names()
    {
        using (var store = AssistantStore.Open(temp.Roots))
        {
            store.SaveMission("m-1", Mission("working", 1));
            store.SaveProfile("dextra", new JsonObject { ["name"] = "Dextra" });
            store.SaveDuties("dextra", new JsonArray());
            store.AppendFeed(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero), [new JsonObject { ["kind"] = "turnFinished" }]);
            Assert.Equal(Path.Combine(temp.Store, "assistants", "dextra", "folder"), store.AssistantFolder("dextra"));
            Assert.Equal(Path.Combine(temp.Store, "memory", "dextra"), store.MemoryFolder("dextra"));
            Assert.Equal(Path.Combine(temp.Store, "memory", "shared"), store.SharedMemoryFolder());
            Assert.Equal(Path.Combine(temp.Store, "teach", "dextra"), store.TeachFolder("dextra"));
            var worktree = store.MissionWorktreeFolder("m-1");
            Assert.Equal(Path.Combine(temp.Store, "worktrees", "m-1"), worktree);
            Assert.False(Directory.Exists(worktree)); // git worktree add wants a folder that does not exist yet
            Assert.True(Directory.Exists(Path.GetDirectoryName(worktree)));
        }
        Assert.True(File.Exists(Path.Combine(temp.Store, "missions", "m-1.json")));
        Assert.False(File.Exists(Path.Combine(temp.Store, "missions", "m-1.journal"))); // folded at close
        Assert.True(File.Exists(Path.Combine(temp.Store, "assistants", "dextra", "profile.json")));
        Assert.True(File.Exists(Path.Combine(temp.Store, "assistants", "dextra", "duties.json")));
        Assert.True(File.Exists(Path.Combine(temp.Store, "feed", "2026-10-12.jsonl")));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:")]
    [InlineData("")]
    [InlineData("-leading")]
    [InlineData("Dextra")]
    [InlineData("con")]
    [InlineData("LPT1")]
    [InlineData("shared")]
    [InlineData("a.json")]
    [InlineData("an-id-of-sixty-five-characters-is-one-more-than-the-store-allows-x")]
    public void An_id_that_could_leave_its_folder_or_collide_is_refused_everywhere(string id)
    {
        using var store = AssistantStore.Open(temp.Roots);
        Assert.False(AssistantStore.IsId(id));
        Assert.Throws<ArgumentException>(() => store.SaveMission(id, Mission("working", 1)));
        Assert.Throws<ArgumentException>(() => store.Mission(id));
        Assert.Throws<ArgumentException>(() => store.SaveProfile(id, new JsonObject()));
        Assert.Throws<ArgumentException>(() => store.AssistantFolder(id));
        Assert.Throws<ArgumentException>(() => store.MemoryFolder(id));
        Assert.Throws<ArgumentException>(() => store.TeachFolder(id));
        Assert.Throws<ArgumentException>(() => store.MissionWorktreeFolder(id));
        Assert.False(Directory.Exists(Path.Combine(temp.Folder, "escape")));
    }

    [Fact]
    public void A_second_store_on_the_same_folder_is_refused_until_the_first_closes()
    {
        var first = AssistantStore.Open(temp.Roots);
        Assert.Throws<AssistantStoreBusyException>(() => AssistantStore.Open(temp.Roots));
        first.Dispose();
        using var second = AssistantStore.Open(temp.Roots);
        Assert.Throws<ObjectDisposedException>(() => first.Missions());
    }

    [Fact]
    public void Delete_removes_the_mission_for_real_and_it_stays_gone_after_a_reopen()
    {
        using (var store = AssistantStore.Open(temp.Roots))
        {
            store.SaveMission("m-1", Mission("working", 1));
            store.SaveMission("m-2", Mission("working", 1));
            Assert.True(store.DeleteMission("m-1"));
            Assert.False(store.DeleteMission("m-1"));
            Assert.Null(store.Mission("m-1"));
        }
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(temp.Store, "missions"), "m-1*"));
        using var reopened = AssistantStore.Open(temp.Roots);
        Assert.Equal(["m-2"], reopened.Missions().Select(m => m.Id));
    }

    [Fact]
    public void The_journal_is_folded_into_the_snapshot_every_compact_every_saves()
    {
        using var store = AssistantStore.Open(temp.Roots, new AssistantStoreOptions { CompactEvery = 3 });
        var journal = Path.Combine(temp.Store, "missions", "m-1.journal");
        var snapshot = Path.Combine(temp.Store, "missions", "m-1.json");
        store.SaveMission("m-1", Mission("working", 1));
        store.SaveMission("m-1", Mission("working", 2));
        Assert.Equal(2, File.ReadAllLines(journal).Length);
        Assert.False(File.Exists(snapshot));
        store.SaveMission("m-1", Mission("working", 3));
        Assert.False(File.Exists(journal));
        Assert.Equal(3, JsonNode.Parse(File.ReadAllText(snapshot))!["seq"]!.GetValue<long>());
    }

    [Fact]
    public void The_feed_keeps_thirty_days_counting_today_and_reads_one_day_in_order()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));
        using var store = AssistantStore.Open(temp.Roots, new AssistantStoreOptions { Time = clock });
        var start = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        for (var day = 0; day < 42; day++)
            store.AppendFeed(start.AddDays(day), [new JsonObject { ["n"] = day }, new JsonObject { ["n"] = day * 100 }]);
        Assert.Equal([0, 0], store.ReadFeed(new DateOnly(2026, 9, 1)).Select(e => e["n"]!.GetValue<int>()));
        Assert.Equal(12, store.PruneFeed()); // Sept 1 to 12 go; Sept 13 to Oct 12 are the 30 days kept
        var kept = Directory.EnumerateFiles(Path.Combine(temp.Store, "feed")).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(30, kept.Count);
        Assert.Equal("2026-09-13.jsonl", kept[0]);
        Assert.Equal([41, 4100], store.ReadFeed(new DateOnly(2026, 10, 12)).Select(e => e["n"]!.GetValue<int>()));
        Assert.Empty(store.ReadFeed(new DateOnly(2026, 9, 12)));
    }

    [Fact]
    public void A_feed_line_cut_by_a_crash_is_skipped_and_the_rest_of_the_day_reads()
    {
        using var store = AssistantStore.Open(temp.Roots);
        var at = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        store.AppendFeed(at, [new JsonObject { ["n"] = 1 }]);
        File.AppendAllText(Path.Combine(temp.Store, "feed", "2026-10-12.jsonl"), "{\"n\":2,\"cut");
        Assert.Equal([1], store.ReadFeed(new DateOnly(2026, 10, 12)).Select(e => e["n"]!.GetValue<int>()));
    }

    [Fact]
    public void The_default_seam_puts_the_store_under_the_storage_root_and_the_audit_log_where_12_5_names()
    {
        var root = Path.Combine(temp.Folder, "nth");
        using var bound = Storage.Locations.Override(StorageLocations.Create(root, Path.Combine(temp.Folder, "home")));
        var roots = new DefaultPathAssistantStorageRoots();
        Assert.Equal(Path.Combine(root, "Assistant"), roots.AssistantRoot);
        Assert.Equal(Path.Combine(root, "assistant", "audit"), roots.AuditDirectory);
        Assert.IsType<DefaultPathAssistantStorageRoots>(AssistantStorage.Roots.Current);
        using (AssistantStorage.Roots.Override(temp.Roots)) Assert.Same(temp.Roots, AssistantStorage.Roots.Current);
    }
}
