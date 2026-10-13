using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-12 (DESIGN.md 8.3 "Delete is real"): the note is removed from its file, the index, the search index, and any cached
// summary that quoted it. What is left of it is a salted hash, so nothing automatic writes it back.
[Trait("Decision", "ED-NEW-dextra-as12-12")]
[Trait("Feature", "AS-12")]
public sealed class MemoryDeleteTests : IDisposable
{
    private const string Doomed = "The zebrafish tank needs its filter cleaned every second Sunday";
    private readonly MemoryRig rig = new();

    public void Dispose() => rig.Dispose();

    private string Add(string text, NoteKind kind = NoteKind.Fact, string scope = MemoryRig.Main, string? topic = null) =>
        rig.Memory.Propose(MemoryRig.Clean(text, kind, scope, topic)).Note!.Id;

    [Fact]
    public void Delete_removes_the_note_from_its_file_the_index_the_search_index_and_every_cached_summary()
    {
        var doomed = Add(Doomed, topic: "Aquarium");
        var other = Add("The owner prefers short answers", NoteKind.Preference);
        rig.Memory.Fold();                                                  // filed under its topic: aquarium.md
        Assert.Contains(Doomed, rig.Read(MemoryRig.Main, "aquarium.md"), StringComparison.Ordinal);
        Assert.Contains(doomed, rig.Read(MemoryRig.Main, "MEMORY.md"), StringComparison.Ordinal);
        Assert.Contains(Doomed, rig.Memory.PromptBlock(MemoryRig.Main), StringComparison.Ordinal);  // a summary that quotes it is built and cached
        Assert.Contains(Doomed, rig.Memory.PromptBlock(null), StringComparison.Ordinal);
        Assert.Equal(1, rig.Memory.SearchHits("zebrafish"));
        Assert.Equal(2, rig.Memory.CachedBlocksQuoting(doomed));

        var events = new List<MemoryChange>();
        rig.Memory.Changed += events.Add;
        Assert.Equal(new DeleteReport(1), rig.Memory.Delete([doomed]));

        // The file, the index, the search index, the caches and the lists.
        Assert.DoesNotContain("aquarium.md", rig.FileNames(MemoryRig.Main));
        Assert.DoesNotContain(Doomed, rig.AllText(), StringComparison.Ordinal);
        Assert.DoesNotContain("zebrafish", rig.AllText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(doomed, rig.Read(MemoryRig.Main, "MEMORY.md"), StringComparison.Ordinal);
        Assert.Equal(0, rig.Memory.SearchHits("zebrafish"));
        Assert.Equal(0, rig.Memory.CachedBlocksQuoting(doomed));
        Assert.DoesNotContain("zebrafish", rig.Memory.PromptBlock(MemoryRig.Main), StringComparison.Ordinal);   // rebuilt without it
        Assert.DoesNotContain("zebrafish", rig.Memory.PromptBlock(null), StringComparison.Ordinal);
        Assert.Empty(rig.Memory.Recall(null, "zebrafish filter"));
        Assert.Null(rig.Memory.Find(doomed));
        Assert.DoesNotContain(rig.Memory.Notes(null), n => n.Id == doomed);
        Assert.Equal([new(MemoryChangeKind.Deleted, [doomed])], events, new ChangeComparer());
        // Another note is untouched.
        Assert.Equal("The owner prefers short answers", rig.Memory.Find(other)!.Text);
        // And a restart does not bring it back.
        var reopened = rig.Reopen();
        Assert.Null(reopened.Find(doomed));
        Assert.Equal(0, reopened.SearchHits("zebrafish"));
    }

    [Fact]
    public void A_staged_note_and_the_last_note_of_a_scope_are_deleted_for_real_too()
    {
        var only = Add(Doomed);
        Assert.Contains("recent.md", rig.FileNames(MemoryRig.Main));
        rig.Memory.Delete([only]);
        Assert.Empty(rig.FileNames(MemoryRig.Main));
        Assert.DoesNotContain("zebrafish", rig.AllText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nothing_but_a_salted_hash_of_the_words_is_kept_and_no_temporary_file_is_left()
    {
        var id = Add(Doomed);
        rig.Memory.Delete([id]);
        var state = File.ReadAllText(Path.Combine(rig.MemoryRoot, "state.json"));
        Assert.Contains("\"deleted\"", state, StringComparison.Ordinal);
        Assert.DoesNotContain("zebrafish", state, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("filter", state, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(rig.MemoryRoot, "*.tmp", SearchOption.AllDirectories));
        // The same words with other case and punctuation hash the same, and a different salt would hash differently.
        using var other = new MemoryRig();
        other.Memory.Delete([other.Memory.Propose(MemoryRig.Clean(Doomed)).Note!.Id]);
        Assert.NotEqual(Hashes(state), Hashes(File.ReadAllText(Path.Combine(other.MemoryRoot, "state.json"))));
    }

    private static string Hashes(string state) =>
        string.Concat(System.Text.Json.Nodes.JsonNode.Parse(state)!["deleted"]!.AsArray().Select(i => i!["h"]!.GetValue<string>()));

    [Fact]
    public void Nothing_automatic_writes_a_deleted_note_back_but_the_owner_can_write_it_himself()
    {
        Add(Doomed);
        rig.Memory.Delete([rig.Memory.Notes(null).Single().Id]);
        var again = rig.Memory.Propose(MemoryRig.Clean("the Zebrafish tank needs its filter cleaned, every second Sunday!"));
        Assert.Equal(NoteRefusal.Deleted, again.Refusal);
        Assert.Contains("owner deleted this note", again.Message, StringComparison.Ordinal);
        Assert.Equal(NoteRefusal.Deleted, rig.Memory.Propose(MemoryRig.FromWeb(Doomed)).Refusal);
        Assert.Empty(rig.Memory.Notes(null));
        Assert.Equal(NoteResult.Kept, rig.Memory.Propose(MemoryRig.Typed(Doomed, scope: MemoryRig.Main)).Result);
        // The refusal survives a restart.
        rig.Memory.Delete([rig.Memory.Notes(null).Single().Id]);
        var reopened = rig.Reopen();
        Assert.Equal(NoteRefusal.Deleted, reopened.Propose(MemoryRig.Clean(Doomed)).Refusal);
    }

    [Fact]
    public void A_deleted_note_comes_back_after_the_hash_is_old_enough_to_forget()
    {
        Add(Doomed);
        rig.Memory.Delete([rig.Memory.Notes(null).Single().Id]);
        rig.Clock.Advance(MemoryState.DeletedKept + TimeSpan.FromDays(1));
        Assert.Equal(NoteResult.Kept, rig.Memory.Propose(MemoryRig.Clean(Doomed)).Result);
    }

    [Fact]
    public void A_reader_deletes_only_what_it_can_read_and_the_owner_deletes_anything()
    {
        var mine = Add("Astur one keeps the shop price list in the red binder", scope: MemoryRig.Astur);
        var theirs = Add("Astur two tracks the supplier invoices in a blue sheet", scope: MemoryRig.OtherAstur);
        var shared = Add("The owner is based in Pune", scope: MemoryScope.Shared);
        Assert.Equal(0, rig.Memory.Delete([theirs], reader: MemoryRig.Astur).Removed);
        Assert.NotNull(rig.Memory.Find(theirs));
        Assert.Equal(2, rig.Memory.Delete([mine, shared], reader: MemoryRig.Astur).Removed);
        Assert.Equal(1, rig.Memory.Delete([theirs], reader: MemoryRig.Main).Removed);   // Dextra reads all
        Assert.Empty(rig.Memory.Notes(null));
    }

    [Fact]
    public void Ids_that_name_no_note_and_repeated_ids_are_ignored()
    {
        var id = Add(Doomed);
        Assert.Equal(new DeleteReport(0), rig.Memory.Delete(["n000000000000", "../../x"]));
        Assert.Equal(new DeleteReport(1), rig.Memory.Delete([id, id, id]));
        Assert.Equal(new DeleteReport(0), rig.Memory.Delete([]));
    }

    [Fact]
    public void A_write_that_fails_deletes_nothing_and_says_so()
    {
        var id = Add(Doomed);
        var faults = new List<string>();
        rig.Memory.Faulted += faults.Add;
        rig.Store.Faults = fault => { if (fault == AssistantStoreFault.TempWritten) throw new IOException("the disk is full"); };
        var report = rig.Memory.Delete([id]);
        rig.Store.Faults = null;
        Assert.True(report.Failed);
        Assert.Equal(0, report.Removed);
        Assert.NotNull(rig.Memory.Find(id));
        Assert.Contains(faults, f => f.Contains("disk is full", StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateFiles(rig.MemoryRoot, ".*.tmp", SearchOption.AllDirectories));
        // The tombstone was never written either, so the words may be written again.
        Assert.Equal(NoteResult.Merged, rig.Memory.Propose(MemoryRig.Clean(Doomed)).Result);
    }

    [Fact]
    public void A_crash_after_the_file_is_rewritten_and_before_the_index_leaves_no_trace_once_the_store_opens()
    {
        var id = Add(Doomed);
        Add("The owner prefers short answers", NoteKind.Preference);
        rig.Memory.AfterWrite = point => { if (point == "index") throw new AssistantStoreCrash(); };
        Assert.Throws<AssistantStoreCrash>(() => rig.Memory.Delete([id]));
        // The file no longer holds it; the index still lists it until the store opens again and repairs it.
        Assert.DoesNotContain(Doomed, rig.Read(MemoryRig.Main, "recent.md"), StringComparison.Ordinal);
        Assert.Contains(id, rig.Read(MemoryRig.Main, "MEMORY.md"), StringComparison.Ordinal);
        rig.Store.AbandonForTests();
        var reopened = rig.Reopen();
        Assert.Null(reopened.Find(id));
        Assert.DoesNotContain(id, rig.Read(MemoryRig.Main, "MEMORY.md"), StringComparison.Ordinal);
        Assert.DoesNotContain("zebrafish", rig.AllText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Forget_everything_about_a_topic_shows_the_notes_first_and_deleting_them_is_real()
    {
        var a = Add("Dinner is at eight and the owner is vegetarian", topic: "Diet");
        var b = Add("The owner avoids peanuts", topic: "Diet");
        var c = Add("Peanuts are in the trail mix recipe", topic: "Recipes");
        var keep = Add("Releases go out on Fridays");
        var waiting = rig.Memory.Propose(MemoryRig.FromWeb("Page says the diet app wants a weekly weigh-in")).Note!.Id;

        var byTopic = rig.Memory.PreviewForget(null, "Forget everything about diet");
        Assert.Equal(new[] { a, b }.Order(StringComparer.Ordinal), byTopic.Where(n => n.Topic == "diet").Select(n => n.Id).Order(StringComparer.Ordinal));
        Assert.Contains(byTopic, n => n.Id == waiting);                 // a waiting note about diet is shown too
        var byWords = rig.Memory.PreviewForget(null, "peanuts");
        Assert.Equal(new[] { b, c }.Order(StringComparer.Ordinal), byWords.Select(n => n.Id).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(byWords, n => n.Id == keep);
        // Previewing removes nothing.
        Assert.Equal(5, rig.Memory.Notes(null).Count);
        Assert.Empty(rig.Memory.PreviewForget(null, "   "));
        Assert.Empty(rig.Memory.PreviewForget(null, "the and"));

        Assert.Equal(byTopic.Count, rig.Memory.Delete([.. byTopic.Select(n => n.Id)]).Removed);
        Assert.Equal(new[] { c, keep }.Order(StringComparer.Ordinal), rig.Memory.Notes(null).Select(n => n.Id).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("vegetarian", rig.AllText(), StringComparison.Ordinal);
    }

    private sealed class ChangeComparer : IEqualityComparer<MemoryChange>
    {
        public bool Equals(MemoryChange? x, MemoryChange? y) => x?.Kind == y?.Kind && x!.Ids.SequenceEqual(y!.Ids);

        public int GetHashCode(MemoryChange obj) => (int)obj.Kind;
    }
}
