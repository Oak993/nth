using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-12 (DESIGN.md 8.1 to 8.3): the memory store. Notes are written right away and used, in markdown topic files with an index,
// one set per assistant plus the shared "About you" set; each Astur reads its own and the shared set, Dextra reads all; the same note
// met again is joined, never doubled; the night's folding files and merges; the owner edits; a write that fails changes nothing.
[Trait("Decision", "ED-NEW-dextra-as12-9")]
[Trait("Feature", "AS-12")]
public sealed class MemoryStoreTests : IDisposable
{
    private readonly MemoryRig rig = new();

    public void Dispose() => rig.Dispose();

    private string Add(string text, NoteKind kind = NoteKind.Fact, string scope = MemoryRig.Main, string? topic = null) =>
        rig.Memory.Propose(MemoryRig.Clean(text, kind, scope, topic)).Note!.Id;

    [Fact]
    public void A_note_is_written_right_away_used_at_once_and_survives_a_restart_with_every_field()
    {
        var proposal = MemoryRig.Clean("The owner prefers short answers", NoteKind.Preference, topic: "Writing style");
        var outcome = rig.Memory.Propose(proposal);
        Assert.Equal(NoteResult.Kept, outcome.Result);
        var note = outcome.Note!;
        Assert.Equal((MemoryRig.Main, "writing-style", NoteKind.Preference, NoteAuthor.Assistant, MemoryRig.Start), (note.Scope, note.Topic, note.Kind, note.Author, note.Created));
        Assert.True(note.Used);
        Assert.Equal(MemoryRig.ChatSource, Assert.Single(note.Sources));
        Assert.Equal(["MEMORY.md", "recent.md"], rig.FileNames(MemoryRig.Main));
        Assert.Contains("The owner prefers short answers", rig.Memory.PromptBlock(MemoryRig.Main), StringComparison.Ordinal);
        Assert.Equal(note.Id, rig.Memory.Recall(MemoryRig.Main, "short answers").Single().Note.Id);

        var reopened = rig.Reopen();
        Assert.Equal(note with { LastUsed = reopened.Find(note.Id)!.LastUsed }, reopened.Find(note.Id));
    }

    [Fact]
    public void A_note_that_says_nothing_or_too_much_or_for_nobody_is_refused()
    {
        Assert.Equal(NoteRefusal.Empty, rig.Memory.Propose(MemoryRig.Clean("  \n ")).Refusal);
        Assert.Equal(NoteRefusal.Empty, rig.Memory.Propose(MemoryRig.Clean("ab")).Refusal);
        Assert.Equal(NoteRefusal.TooLong, rig.Memory.Propose(MemoryRig.Clean(new string('x', 3) + " " + string.Join(' ', Enumerable.Repeat("word", 120)))).Refusal);
        Assert.Equal(NoteResult.Kept, rig.Memory.Propose(MemoryRig.Clean(string.Join(' ', Enumerable.Repeat("word", 99)))).Result);
        Assert.Equal(NoteRefusal.Scope, rig.Memory.Propose(MemoryRig.Clean("Releases go out on Fridays", scope: "../escape")).Refusal);
        Assert.Equal(NoteRefusal.Scope, rig.Memory.Propose(MemoryRig.Clean("Releases go out on Fridays", scope: "Dextra")).Refusal);
        Assert.Equal(NoteRefusal.Empty, rig.Memory.Propose(MemoryRig.Clean("Releases go out on Fridays", (NoteKind)99)).Refusal);
        Assert.Single(rig.Memory.Notes(null));
    }

    [Fact]
    public void The_same_note_met_again_is_one_note_with_both_sources()
    {
        var first = rig.Memory.Propose(MemoryRig.Clean("Invoices go to the accounts folder every Friday afternoon")).Note!;
        var other = new NoteSource(NoteSourceKind.Chat, "chat:ffffffffffffffffffffffffffffffff#7", Detail: "Billing");
        var again = rig.Memory.Propose(MemoryRig.Clean("invoices go to the Accounts folder every friday afternoon!") with { Source = other });
        Assert.Equal(NoteResult.Merged, again.Result);
        Assert.Equal(first.Id, again.Note!.Id);
        Assert.Equal([MemoryRig.ChatSource, other], again.Note.Sources);
        Assert.Equal("Invoices go to the accounts folder every Friday afternoon", again.Note.Text);
        Assert.Single(rig.Memory.Notes(null));
        // A third meeting with the first source adds nothing.
        Assert.Equal(2, rig.Memory.Propose(MemoryRig.Clean("Invoices go to the accounts folder every Friday afternoon")).Note!.Sources.Count);
    }

    [Fact]
    public void A_near_copy_of_four_or_more_words_of_the_same_kind_is_joined_and_a_different_kind_is_not()
    {
        var first = rig.Memory.Propose(MemoryRig.Clean("Invoices go to the accounts folder every Friday afternoon", NoteKind.Procedure)).Note!;
        Assert.Equal(first.Id, rig.Memory.Propose(MemoryRig.Clean("Invoices go to the account folders every Friday afternoons", NoteKind.Procedure)).Note!.Id);
        Assert.NotEqual(first.Id, rig.Memory.Propose(MemoryRig.Clean("Invoices go to the accounts folder every Friday afternoon, then to archive", NoteKind.Procedure)).Note!.Id);
        Assert.Equal(2, rig.Memory.Notes(null).Count);
        // Short notes are compared whole: a few shared words do not make the same note.
        var a = Add("Pune office");
        Assert.NotEqual(a, Add("Pune office hours"));
    }

    [Fact]
    public void Each_Astur_reads_its_own_notes_and_the_shared_set_and_Dextra_and_the_owner_read_all()
    {
        var mine = Add("Astur one keeps the shop price list in the red binder", scope: MemoryRig.Astur);
        var theirs = Add("Astur two tracks the supplier invoices in a blue sheet", scope: MemoryRig.OtherAstur);
        var dextra = Add("Dextra reviews the weekly budget on Mondays", scope: MemoryRig.Main);
        var shared = Add("The owner is based in Pune", scope: MemoryScope.Shared);
        Assert.Equal(new[] { mine, shared }.Order(StringComparer.Ordinal), rig.Memory.Notes(MemoryRig.Astur).Select(n => n.Id).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { theirs, shared }.Order(StringComparer.Ordinal), rig.Memory.Notes(MemoryRig.OtherAstur).Select(n => n.Id).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { mine, theirs, dextra, shared }.Order(StringComparer.Ordinal), rig.Memory.Notes(MemoryRig.Main).Select(n => n.Id).Order(StringComparer.Ordinal));
        Assert.Equal(4, rig.Memory.Notes(null).Count);
        // Recall and the prompt block follow the same scope.
        Assert.Empty(rig.Memory.Recall(MemoryRig.Astur, "supplier invoices"));
        Assert.Single(rig.Memory.Recall(MemoryRig.Astur, "price list"));
        Assert.Single(rig.Memory.Recall(MemoryRig.Astur, "Pune"));
        Assert.Single(rig.Memory.Recall(MemoryRig.Main, "supplier invoices"));
        Assert.DoesNotContain("supplier", rig.Memory.PromptBlock(MemoryRig.Astur), StringComparison.Ordinal);
        Assert.Contains("Pune", rig.Memory.PromptBlock(MemoryRig.Astur), StringComparison.Ordinal);
        Assert.Contains("supplier", rig.Memory.PromptBlock(MemoryRig.Main), StringComparison.Ordinal);
        // A reader that is not an assistant reads only the shared set.
        Assert.Equal([shared], rig.Memory.Notes("../x").Select(n => n.Id));
    }

    [Fact]
    public void Shared_notes_live_in_about_you_and_the_files_are_laid_out_as_the_design_says()
    {
        Add("The owner is based in Pune", NoteKind.Fact, MemoryScope.Shared);
        Add("The owner prefers short answers", NoteKind.Preference, MemoryScope.Shared);
        Add("Releases go out on Fridays", NoteKind.Procedure);
        rig.Memory.Fold();
        Assert.Equal(["MEMORY.md", "about-you.md"], rig.FileNames(MemoryScope.Shared));
        Assert.Equal(["MEMORY.md", "procedures.md"], rig.FileNames(MemoryRig.Main));
        Assert.Contains("## About you", rig.Read(MemoryScope.Shared, "MEMORY.md"), StringComparison.Ordinal);
        Assert.Contains("- The owner is based in Pune ", rig.Read(MemoryScope.Shared, "about-you.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void Recall_ranks_by_the_share_of_the_query_a_note_holds_and_limits_the_answer()
    {
        var best = Add("The staging server runs nightly backups to the blue drive");
        Add("Backups of the laptop go to the cloud");
        Add("The owner likes dark mode");
        Assert.Equal(best, rig.Memory.Recall(MemoryRig.Main, "nightly backups staging").First().Note.Id);
        Assert.Equal(2, rig.Memory.Recall(MemoryRig.Main, "backups").Count);
        Assert.Single(rig.Memory.Recall(MemoryRig.Main, "backups", 1));
        Assert.Empty(rig.Memory.Recall(MemoryRig.Main, "zebrafish"));
        Assert.Equal(3, rig.Memory.Recall(MemoryRig.Main, null, 50).Count);
        Assert.All(rig.Memory.Recall(MemoryRig.Main, "backups"), r => Assert.InRange(r.Score, 0.99, 1.01));
    }

    [Fact]
    public void Using_a_note_marks_it_used_to_the_hour_and_writes_its_file_at_most_once_an_hour()
    {
        var id = Add("The staging server runs nightly backups to the blue drive");
        var writes = 0;
        rig.Store.Faults = fault => { if (fault == AssistantStoreFault.TempWritten) writes++; };
        Assert.Null(rig.Memory.Find(id)!.LastUsed);
        rig.Memory.Recall(MemoryRig.Main, "backups");
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero), rig.Memory.Find(id)!.LastUsed);
        var first = writes;
        Assert.True(first > 0);
        rig.Clock.Advance(TimeSpan.FromMinutes(20));
        rig.Memory.Recall(MemoryRig.Main, "backups");
        rig.Memory.PromptBlock(MemoryRig.Main);
        Assert.Equal(first, writes);
        rig.Clock.Advance(TimeSpan.FromMinutes(45));
        rig.Memory.Recall(MemoryRig.Main, "backups");
        Assert.True(writes > first);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero), rig.Memory.Find(id)!.LastUsed);
        // It is on disk too.
        rig.Store.Faults = null;
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero), rig.Reopen().Find(id)!.LastUsed);
    }

    [Fact]
    public void The_prompt_block_lists_used_notes_preferences_first_marks_a_fact_that_may_change_and_is_built_once()
    {
        Assert.Equal("", rig.Memory.PromptBlock(MemoryRig.Main));
        rig.Memory.Propose(MemoryRig.Clean("The owner lives in Pune", NoteKind.Fact) with { MayChange = true });
        rig.Memory.Propose(MemoryRig.Clean("Releases go out on Fridays", NoteKind.Procedure));
        rig.Memory.Propose(MemoryRig.Clean("The owner prefers short answers", NoteKind.Preference));
        var block = rig.Memory.PromptBlock(MemoryRig.Main);
        Assert.StartsWith("What you remember about the owner and his work.", block, StringComparison.Ordinal);
        var lines = block.Split('\n').Skip(1).ToList();
        Assert.Equal([
            "- The owner prefers short answers",
            "- Releases go out on Fridays",
            "- The owner lives in Pune (may have changed: check before relying on it)",
        ], lines);
        Assert.Same(block, rig.Memory.PromptBlock(MemoryRig.Main));                     // built once for the notes as they stand
        Assert.Equal(1, rig.Memory.CachedBlockCount);
        rig.Memory.Propose(MemoryRig.Clean("Builds use dotnet 10", NoteKind.Procedure));
        Assert.Equal(0, rig.Memory.CachedBlockCount);                                   // a change drops what was built
        Assert.Contains("dotnet 10", rig.Memory.PromptBlock(MemoryRig.Main), StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_block_is_cut_to_its_limit_and_says_how_many_it_left_out()
    {
        using var small = new MemoryRig(o => o with { MaxPromptChars = 400 });
        for (var i = 0; i < 20; i++) small.Memory.Propose(MemoryRig.Clean($"Customer folder number {i} lives on drive {(char)('a' + i)} in the archive room"));
        var block = small.Memory.PromptBlock(MemoryRig.Main);
        Assert.True(block.Length <= 400 + 40);
        Assert.Matches(@"\n\(\d+ more: use recall\.\)\z", block);
    }

    [Fact]
    public void Only_a_few_prompt_blocks_are_kept_built()
    {
        using var small = new MemoryRig(o => o with { MaxCachedBlocks = 2 });
        small.Memory.Propose(MemoryRig.Clean("The owner prefers short answers", NoteKind.Preference, MemoryScope.Shared));
        foreach (var reader in new[] { "astur1", "astur2", "astur3", "astur4" }) small.Memory.PromptBlock(reader);
        Assert.Equal(2, small.Memory.CachedBlockCount);
    }

    [Fact]
    public void Folding_files_staged_notes_under_their_topics_and_joins_duplicates_into_the_older_note()
    {
        var a = Add("Dinner is at eight", topic: "Diet");
        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        var b = Add("The owner avoids peanuts", topic: "Diet");
        var c = Add("Releases go out on Fridays", NoteKind.Procedure);
        Assert.Equal(["MEMORY.md", "recent.md"], rig.FileNames(MemoryRig.Main));

        var report = rig.Memory.Fold();
        Assert.Equal(new FoldReport(3, 0), report);
        Assert.Equal(["MEMORY.md", "diet.md", "procedures.md"], rig.FileNames(MemoryRig.Main));
        Assert.Contains("# Diet", rig.Read(MemoryRig.Main, "diet.md"), StringComparison.Ordinal);
        Assert.Contains("Dinner is at eight", rig.Read(MemoryRig.Main, "diet.md"), StringComparison.Ordinal);
        Assert.Contains("The owner avoids peanuts", rig.Read(MemoryRig.Main, "diet.md"), StringComparison.Ordinal);
        Assert.DoesNotContain(c, rig.Read(MemoryRig.Main, "diet.md"), StringComparison.Ordinal);
        // Folding twice changes nothing: the files are byte for byte the same.
        var before = rig.AllText();
        Assert.False(rig.Memory.Fold().Changed);
        Assert.Equal(before, rig.AllText());
        Assert.Equal(new[] { a, b, c }.Order(StringComparer.Ordinal), rig.Reopen().Notes(null).Select(n => n.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Folding_joins_notes_that_say_the_same_thing_and_keeps_the_older_notes_words_and_every_source()
    {
        var older = rig.Memory.Propose(MemoryRig.Clean("Invoices go to the accounts folder every Friday afternoon")).Note!.Id;
        rig.Memory.Fold();
        rig.Clock.Advance(TimeSpan.FromHours(30));
        // A second copy arrives after the first was filed (a hand edit of a topic file, or a restart that lost a merge): it is found by the fold.
        rig.Store.Dispose();
        var topicFile = Path.Combine(rig.Folder(MemoryRig.Main), "facts.md");
        File.AppendAllText(topicFile, "- Invoices go to the account folders every Friday afternoons <!--n {\"id\":\"nsecondcopy01\",\"kind\":\"fact\",\"created\":\"2026-10-12T10:00:00Z\",\"sources\":[{\"kind\":\"chat\",\"target\":\"chat:bbbb#2\"}]}-->\n");
        var reopened = rig.Reopen();
        Assert.Equal(2, reopened.Notes(null).Count);
        var report = reopened.Fold();
        Assert.Equal(1, report.Merged);
        var left = Assert.Single(reopened.Notes(null));
        Assert.Equal(older, left.Id);
        Assert.Equal("Invoices go to the accounts folder every Friday afternoon", left.Text);
        Assert.Equal(2, left.Sources.Count);
    }

    [Fact]
    public void Folding_never_joins_a_used_note_with_one_that_waits()
    {
        Add("Invoices go to the accounts folder every Friday afternoon");
        rig.Memory.Propose(MemoryRig.FromWeb("Page: invoices go to the accounts folder, every Friday afternoon, then to the public share"));
        Assert.Equal(new MemoryCounts(1, 1), rig.Memory.Counts(null));
        Assert.Equal(new FoldReport(1, 0), rig.Memory.Fold());               // the used note is filed; the waiting one is neither filed nor joined
        Assert.Equal(new MemoryCounts(1, 1), rig.Memory.Counts(null));
    }

    [Fact]
    public void Folding_runs_once_a_local_day()
    {
        Add("Releases go out on Fridays", NoteKind.Procedure);
        Assert.NotNull(rig.Memory.FoldIfDue());
        Assert.Null(rig.Memory.FoldIfDue());
        rig.Clock.Advance(TimeSpan.FromHours(10));                          // 19:30, the same day
        Assert.Null(rig.Memory.FoldIfDue());
        rig.Clock.Advance(TimeSpan.FromHours(15));                          // 10:30 the next day
        Assert.NotNull(rig.Memory.FoldIfDue());
        // The day it last ran is remembered across a restart.
        Assert.Null(rig.Reopen().FoldIfDue());
    }

    [Fact]
    public void A_fold_the_process_died_in_the_middle_of_leaves_each_note_once()
    {
        var a = Add("Dinner is at eight", topic: "Diet");
        var b = Add("The owner avoids peanuts", topic: "Diet");
        rig.Memory.AfterWrite = point => { if (point == "file:diet") throw new AssistantStoreCrash(); };
        Assert.Throws<AssistantStoreCrash>(() => rig.Memory.Fold());
        // The topic file is written; the staging file still holds the same notes.
        Assert.Contains(a, rig.Read(MemoryRig.Main, "diet.md"), StringComparison.Ordinal);
        Assert.Contains(a, rig.Read(MemoryRig.Main, "recent.md"), StringComparison.Ordinal);
        rig.Store.AbandonForTests();
        var reopened = rig.Reopen();
        Assert.Equal(new[] { a, b }.Order(StringComparer.Ordinal), reopened.Notes(null).Select(n => n.Id).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("recent.md", rig.FileNames(MemoryRig.Main));
        Assert.Equal(2, rig.Read(MemoryRig.Main, "diet.md").Split('\n').Count(l => l.StartsWith("- ", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_owner_edits_a_note_and_it_keeps_its_id_and_sources_and_becomes_his_own()
    {
        var note = rig.Memory.Propose(MemoryRig.Clean("The owner lives in Pune", NoteKind.Fact) with { MayChange = true }).Note!;
        var events = new List<MemoryChange>();
        rig.Memory.Changed += events.Add;
        var edit = rig.Memory.Edit(note.Id, "  The owner lives in\nMumbai  ", kind: NoteKind.Fact, mayChange: false, topic: "Places");
        Assert.Equal(NoteResult.Kept, edit.Result);
        var edited = rig.Memory.Find(note.Id)!;
        Assert.Equal(("The owner lives in Mumbai", "places", false, NoteAuthor.Owner), (edited.Text, edited.Topic, edited.MayChange, edited.Author));
        Assert.Equal([MemoryRig.ChatSource, new NoteSource(NoteSourceKind.Owner, "memory view", Detail: "edited by you")], edited.Sources);
        Assert.Contains("MEMORY.md", rig.FileNames(MemoryRig.Main));
        Assert.DoesNotContain("Pune", rig.AllText(), StringComparison.Ordinal);
        Assert.Equal(1, events.Count(e => e.Kind == MemoryChangeKind.Edited));
        Assert.Equal(0, rig.Memory.SearchHits("pune"));
        Assert.Equal(1, rig.Memory.SearchHits("mumbai"));
        Assert.Equal(note.Id, rig.Memory.Recall(MemoryRig.Main, "mumbai").Single().Note.Id);
        // Refusals leave the note as it was.
        Assert.Equal(NoteRefusal.NotFound, rig.Memory.Edit("n000000000000", "text here").Refusal);
        Assert.Equal(NoteRefusal.Empty, rig.Memory.Edit(note.Id, "  ").Refusal);
        Assert.Equal(NoteRefusal.TooLong, rig.Memory.Edit(note.Id, new string('x', 501)).Refusal);
        Assert.Equal("The owner lives in Mumbai", rig.Memory.Find(note.Id)!.Text);
    }

    [Fact]
    public void Editing_a_waiting_note_does_not_release_it_and_a_may_change_flag_is_only_for_facts()
    {
        var waiting = rig.Memory.Propose(MemoryRig.FromWeb("Invoices go to the accounts folder every Friday")).Note!;
        Assert.True(rig.Memory.Edit(waiting.Id, "Invoices go to the accounts folder on Fridays").Written);
        Assert.True(rig.Memory.Find(waiting.Id)!.Quarantined);
        Assert.Empty(rig.Memory.Recall(null, "invoices"));
        var preference = Add("The owner prefers short answers", NoteKind.Preference);
        Assert.False(rig.Memory.Edit(preference, "The owner prefers very short answers", mayChange: true).Note!.MayChange);
    }

    [Fact]
    public void Keep_releases_waiting_notes_and_ignores_ids_that_wait_for_nothing()
    {
        var a = rig.Memory.Propose(MemoryRig.FromWeb("Page one says the vault is in the red folder")).Note!.Id;
        var b = rig.Memory.Propose(MemoryRig.FromWeb("Page two says the backup runs at midnight")).Note!.Id;
        var used = Add("Releases go out on Fridays");
        var released = rig.Memory.Keep([a, b, used, "n000000000000", a]);
        Assert.Equal(new[] { a, b }.Order(StringComparer.Ordinal), released.Order(StringComparer.Ordinal));
        Assert.Equal(new MemoryCounts(3, 0), rig.Memory.Counts(null));
        Assert.False(rig.Memory.Keep(used));                               // nothing waits: nothing to keep
        Assert.True(rig.Reopen().Find(a)!.Used);
    }

    [Fact]
    public void A_full_memory_refuses_more_and_Keep_does_not_go_past_the_limit()
    {
        using var small = new MemoryRig(o => o with { MaxNotesPerScope = 2 });
        small.Memory.Propose(MemoryRig.Clean("Releases go out on Fridays"));
        small.Memory.Propose(MemoryRig.Clean("Builds use dotnet 10 on this machine"));
        var full = small.Memory.Propose(MemoryRig.Clean("The owner uses Windows 11"));
        Assert.Equal(NoteRefusal.Full, full.Refusal);
        Assert.Contains("memory is full", full.Message, StringComparison.Ordinal);
        var waiting = small.Memory.Propose(MemoryRig.FromWeb("Page says the vault is in the red folder")).Note!.Id;
        Assert.Empty(small.Memory.Keep([waiting]));
        Assert.True(small.Memory.Find(waiting)!.Quarantined);
        small.Memory.Delete([small.Memory.Notes(null).First(n => n.Used).Id]);
        Assert.Single(small.Memory.Keep([waiting]));
    }

    [Fact]
    public void A_write_that_fails_changes_nothing_reports_it_and_the_next_try_works()
    {
        var faults = new List<string>();
        rig.Memory.Faulted += faults.Add;
        rig.Store.Faults = fault => { if (fault == AssistantStoreFault.TempWritten) throw new IOException("the disk is full"); };
        var outcome = rig.Memory.Propose(MemoryRig.Clean("Releases go out on Fridays"));
        rig.Store.Faults = null;
        Assert.Equal(NoteResult.Failed, outcome.Result);
        Assert.Contains("Nothing changed", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(rig.Memory.Notes(null));
        Assert.Equal(0, rig.Memory.SearchHits("releases"));
        Assert.Single(faults);
        Assert.Empty(rig.FileNames(MemoryRig.Main));
        Assert.Equal(NoteResult.Kept, rig.Memory.Propose(MemoryRig.Clean("Releases go out on Fridays")).Result);
    }

    [Fact]
    public void A_batch_writes_each_scope_once_and_announces_one_change_per_scope()
    {
        var writes = 0;
        rig.Store.Faults = fault => { if (fault == AssistantStoreFault.TempWritten) writes++; };
        var events = new List<MemoryChange>();
        rig.Memory.Changed += events.Add;
        var outcomes = rig.Memory.ProposeMany([.. Enumerable.Range(0, 10).Select(i => MemoryRig.Clean($"Customer {i} folder is on shelf {i * 7} in the archive room"))]);
        Assert.All(outcomes, o => Assert.Equal(NoteResult.Kept, o.Result));
        Assert.Equal(2, writes);                                           // recent.md and MEMORY.md
        var change = Assert.Single(events);
        Assert.Equal(MemoryChangeKind.Added, change.Kind);
        Assert.Equal(10, change.Ids.Count);
    }

    [Fact]
    public void Notes_compared_inside_one_batch_see_the_notes_before_them()
    {
        var outcomes = rig.Memory.ProposeMany([
            MemoryRig.Clean("Releases go out on Fridays"),
            MemoryRig.Clean("releases go out on fridays."),
            MemoryRig.Clean("Releases go out on Fridays") with { Scope = MemoryScope.Shared },
        ]);
        Assert.Equal([NoteResult.Kept, NoteResult.Merged, NoteResult.Kept], outcomes.Select(o => o.Result));
        Assert.Equal(outcomes[0].Note!.Id, outcomes[1].Note!.Id);
        Assert.NotEqual(outcomes[0].Note!.Id, outcomes[2].Note!.Id);
    }

    [Fact]
    public void Many_threads_writing_reading_and_deleting_at_once_leave_a_consistent_memory()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, worker =>
        {
            for (var i = 0; i < 12; i++)
            {
                var outcome = rig.Memory.Propose(MemoryRig.Clean($"Worker {worker} wrote note {i} about shelf {worker * 100 + i}"));
                Assert.Equal(NoteResult.Kept, outcome.Result);
                ids.Add(outcome.Note!.Id);
                rig.Memory.Recall(MemoryRig.Main, $"worker {worker}");
                rig.Memory.PromptBlock(MemoryRig.Main);
                if (i % 3 == 0) rig.Memory.Delete([outcome.Note.Id]);
            }
        });
        var expected = ids.Where(id => rig.Memory.Find(id) is not null).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(96 - 32, expected.Count);
        Assert.Equal(expected, rig.Memory.Notes(null).Select(n => n.Id).Order(StringComparer.Ordinal));
        var reopened = rig.Reopen();
        Assert.Equal(expected, reopened.Notes(null).Select(n => n.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void An_invalid_store_option_is_refused_and_a_store_that_is_closed_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryStore(rig.Store, new MemoryStoreOptions { MaxNotesPerScope = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryStore(rig.Store, new MemoryStoreOptions { MaxPromptChars = 10 }));
        Assert.Throws<ArgumentNullException>(() => new MemoryStore(null!));
        rig.Store.Dispose();
        Assert.Throws<ObjectDisposedException>(() => rig.Memory.Propose(MemoryRig.Clean("Releases go out on Fridays")));
    }
}
