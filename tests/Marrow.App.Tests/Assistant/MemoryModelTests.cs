using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-12 (DESIGN.md 8.2, 8.3, 8.4): what the Memory view shows and does, apart from how it draws: notes by assistant and topic with
// search; the notes that wait with Keep and Delete; Edit, Delete (asked once more for a used note) and Source; a note the owner writes;
// "forget everything about" with the notes shown first; and the import.
[Trait("Decision", "ED-NEW-dextra-as12-20")]
[Trait("Feature", "AS-12")]
public sealed class MemoryModelTests : IDisposable
{
    private readonly MemoryRig rig = new();
    private readonly List<MemoryModel> models = [];

    public void Dispose()
    {
        foreach (var model in models) model.Dispose();
        rig.Dispose();
    }

    private MemoryModel Model(MemoryImport? import = null, Func<IReadOnlyList<MemoryImportSource>>? sources = null)
    {
        var model = new MemoryModel(rig.Memory, id => id switch { MemoryRig.Main => "Dextra", MemoryRig.Astur => "Shop", _ => id }, () => MemoryRig.Main, import, sources, TimeZoneInfo.Utc, new InlineMemoryWork());
        models.Add(model);
        return model;
    }

    private string Add(string text, NoteKind kind = NoteKind.Fact, string scope = MemoryRig.Main, string? topic = null) =>
        rig.Memory.Propose(MemoryRig.Clean(text, kind, scope, topic)).Note!.Id;

    private void Fill()
    {
        Add("The owner is based in Pune", NoteKind.Fact, MemoryScope.Shared, "About you");
        Add("The owner prefers short answers", NoteKind.Preference, MemoryScope.Shared, "Writing style");
        Add("Releases go out on Fridays", NoteKind.Procedure, MemoryRig.Main, "Releases");
        Add("Invoices are checked on Mondays", NoteKind.Procedure, MemoryRig.Main, "Accounts");
        Add("The shop price list is in the red binder", NoteKind.Fact, MemoryRig.Astur, "Prices");
        rig.Memory.Propose(MemoryRig.FromWeb("Page says the vault is in the red folder"));
    }

    [Fact]
    public void Notes_are_grouped_by_assistant_then_topic_the_shared_set_first_and_waiting_notes_apart()
    {
        Fill();
        var model = Model();
        Assert.Equal(["About you", "Dextra", "Shop"], model.Groups.Select(g => g.Title));
        Assert.Equal([2, 2, 1], model.Groups.Select(g => g.Count));
        Assert.Equal(["About you", "Writing style"], model.Groups[0].Topics.Select(t => t.Title));
        Assert.Equal(["Accounts", "Releases"], model.Groups[1].Topics.Select(t => t.Title));
        Assert.Equal("Every assistant reads these.", model.Groups[0].Detail);
        Assert.Equal(new MemoryCounts(5, 1), model.Counts);
        Assert.Equal("5 notes · 1 waiting for you", MemoryViewWords.Count(model.Counts));
        Assert.Equal("1 note", MemoryViewWords.Count(new MemoryCounts(1, 0)));
        var waiting = Assert.Single(model.Waiting);
        Assert.True(waiting.Quarantined);
        Assert.DoesNotContain(model.Groups.SelectMany(g => g.Topics).SelectMany(t => t.Notes), n => n.Quarantined);
        Assert.False(model.IsEmpty);
        Assert.False(model.NoMatch);
    }

    [Fact]
    public void An_empty_memory_says_so_and_a_search_that_matches_nothing_says_that()
    {
        var model = Model();
        Assert.True(model.IsEmpty);
        Assert.Empty(model.Groups);
        Add("Releases go out on Fridays");
        Assert.False(model.IsEmpty);                                       // the model follows the store
        model.Query = "zebrafish";
        Assert.True(model.NoMatch);
        Assert.Empty(model.Groups);
        model.Query = "";
        Assert.False(model.NoMatch);
    }

    [Fact]
    public void Search_matches_words_of_the_note_its_topic_or_its_assistant_and_waiting_notes_too()
    {
        Fill();
        var model = Model();
        model.Query = "red";
        Assert.Equal(["Shop"], model.Groups.Select(g => g.Title));
        Assert.Single(model.Waiting);                                      // "red folder"
        model.Query = "releases";                                          // the topic
        Assert.Equal(["Releases go out on Fridays"], model.Groups.SelectMany(g => g.Topics).SelectMany(t => t.Notes).Select(n => n.Text));
        Assert.Empty(model.Waiting);
        model.Query = "shop";                                              // the assistant's name
        Assert.Equal(["Shop"], model.Groups.Select(g => g.Title));
        model.Query = "owner pune";
        Assert.Equal(["The owner is based in Pune"], model.Groups.SelectMany(g => g.Topics).SelectMany(t => t.Notes).Select(n => n.Text));
    }

    [Fact]
    public async Task Changed_is_raised_when_the_store_changes_even_from_another_thread()
    {
        var model = Model();
        var raised = new ManualResetEventSlim();
        model.Changed += raised.Set;
        await Task.Run(() => rig.Memory.Propose(MemoryRig.Clean("Releases go out on Fridays")));
        Assert.True(raised.IsSet);
        Assert.Equal(1, model.Counts.Used);
        Assert.Single(model.Groups);
    }

    [Fact]
    public async Task Editing_a_note_saves_it_and_a_refusal_keeps_the_edit_open_and_says_why()
    {
        var id = Add("The owner lives in Pune");
        var model = Model();
        model.BeginEdit(id);
        Assert.Equal((id, "The owner lives in Pune"), (model.EditingId, model.EditText));
        model.EditText = "The owner password is hunter2horse";
        Assert.False(await model.SaveEdit());
        Assert.Equal(id, model.EditingId);
        Assert.Contains("Secrets are never stored", model.EditProblem, StringComparison.Ordinal);
        Assert.Equal("The owner lives in Pune", rig.Memory.Find(id)!.Text);
        model.EditText = "The owner lives in Mumbai";
        Assert.True(await model.SaveEdit());
        Assert.Null(model.EditingId);
        Assert.Equal("Saved.", model.Status);
        Assert.Equal("The owner lives in Mumbai", rig.Memory.Find(id)!.Text);
        model.BeginEdit(id);
        model.CancelEdit();
        Assert.Null(model.EditingId);
        model.BeginEdit("n000000000000");
        Assert.Null(model.EditingId);
    }

    [Fact]
    public async Task Delete_asks_once_more_for_a_used_note_and_removes_it_for_real_and_a_waiting_note_goes_at_once()
    {
        var used = Add("The zebrafish tank needs its filter cleaned");
        var waiting = rig.Memory.Propose(MemoryRig.FromWeb("Page says the vault is in the red folder")).Note!.Id;
        var model = Model();
        await model.Delete(used);
        Assert.Equal(used, model.ConfirmDeleteId);
        Assert.NotNull(rig.Memory.Find(used));
        model.CancelDelete();
        Assert.Null(model.ConfirmDeleteId);
        await model.Delete(used);
        await model.Delete(used);
        Assert.Null(rig.Memory.Find(used));
        Assert.Equal("Deleted. The note is gone from the files, the index and the search.", model.Status);
        Assert.DoesNotContain("zebrafish", rig.AllText(), StringComparison.OrdinalIgnoreCase);
        await model.Delete(waiting);
        Assert.Null(rig.Memory.Find(waiting));
        Assert.True(model.IsEmpty);
    }

    [Fact]
    public async Task Keep_releases_one_waiting_note_or_all_of_them()
    {
        var a = rig.Memory.Propose(MemoryRig.FromWeb("Page one says the vault is in the red folder")).Note!.Id;
        rig.Memory.Propose(MemoryRig.FromWeb("Page two says the backup runs at midnight"));
        rig.Memory.Propose(MemoryRig.FromWeb("Page three says the printer is on floor two"));
        var model = Model();
        await model.Keep(a);
        Assert.Equal("Kept. It is used from now on.", model.Status);
        Assert.Equal(new MemoryCounts(1, 2), model.Counts);
        await model.Keep(a);
        Assert.Equal("That note is not waiting.", model.Status);
        await model.KeepAll();
        Assert.Equal("Kept 2 notes. They are used from now on.", model.Status);
        Assert.Equal(new MemoryCounts(3, 0), model.Counts);
        await model.KeepAll();
        Assert.Equal("Nothing was kept.", model.Status);
    }

    [Fact]
    public async Task Source_opens_for_one_note_at_a_time_and_closes_with_a_second_press()
    {
        var a = Add("Releases go out on Fridays");
        var b = Add("Builds use dotnet 10");
        var model = Model();
        model.ToggleSource(a);
        model.ToggleSource(b);
        Assert.Equal(b, model.SourceOpenId);
        model.ToggleSource(b);
        Assert.Null(model.SourceOpenId);
        model.ToggleSource(a);
        await model.Delete(a);
        await model.Delete(a);
        Assert.Null(model.SourceOpenId);                                   // a note that went away takes its open source with it
    }

    [Fact]
    public async Task A_note_the_owner_writes_goes_where_its_kind_belongs_in_his_own_words()
    {
        var model = Model();
        model.AddText = "My sister Asha is vegetarian";
        Assert.True(await model.Add());                                          // his own words: the third-party rule does not hold him
        model.AddKind = NoteKind.Procedure;
        model.AddText = "Back up the laptop on Sundays";
        Assert.True(await model.Add());
        Assert.Equal("", model.AddText);
        var notes = rig.Memory.Notes(null);
        var asha = Assert.Single(notes, n => n.Text.Contains("Asha", StringComparison.Ordinal));
        Assert.Equal((MemoryScope.Shared, NoteAuthor.Owner, NoteKind.Fact), (asha.Scope, asha.Author, asha.Kind));
        Assert.Equal(MemoryRig.Main, Assert.Single(notes, n => n.Kind == NoteKind.Procedure).Scope);
        Assert.Equal(NoteSourceKind.Owner, asha.Sources.Single().Kind);
        // A refusal says why and keeps the words.
        model.AddText = "The key is sk-proj-AbCdEfGhIjKlMnOpQrStUv1234";
        Assert.False(await model.Add());
        Assert.Contains("Secrets are never stored", model.AddProblem, StringComparison.Ordinal);
        Assert.Equal("The key is sk-proj-AbCdEfGhIjKlMnOpQrStUv1234", model.AddText);
        // The same words again are one note.
        model.AddText = "Back up the laptop on Sundays";
        Assert.True(await model.Add());
        Assert.Equal("Already kept.", model.Status);
        Assert.Equal(2, rig.Memory.Notes(null).Count);
    }

    [Fact]
    public async Task Forget_everything_about_a_topic_shows_the_notes_first_and_removes_only_when_confirmed()
    {
        Add("Dinner is at eight and the owner is vegetarian", topic: "Diet");
        Add("The owner avoids peanuts", topic: "Diet");
        Add("Releases go out on Fridays");
        var model = Model();
        model.ForgetText = "Forget everything about diet";
        model.PreviewForget();
        Assert.Equal(2, model.ForgetPreview!.Count);
        Assert.Equal(3, rig.Memory.Notes(null).Count);
        model.CancelForget();
        Assert.Null(model.ForgetPreview);
        model.PreviewForget();
        await model.ConfirmForget();
        Assert.Equal("Removed 2 notes, for real.", model.Status);
        Assert.Equal(["Releases go out on Fridays"], rig.Memory.Notes(null).Select(n => n.Text));
        Assert.Null(model.ForgetPreview);
        Assert.Equal("", model.ForgetText);
        model.ForgetText = "unicorns";
        model.PreviewForget();
        Assert.Equal("No note is about that.", model.Status);
        await model.ConfirmForget();                                             // nothing is previewed, so nothing goes
        Assert.Single(rig.Memory.Notes(null));
    }

    [Fact]
    public async Task File_work_runs_off_the_calling_thread_and_its_outcome_is_applied_where_the_owner_is()
    {
        var spy = new SpyMemoryWork();
        var model = new MemoryModel(rig.Memory, id => id, () => MemoryRig.Main, new MemoryImport(rig.Memory), () => [], TimeZoneInfo.Utc, spy);
        var id = Add("Releases go out on Fridays");
        spy.Drain();
        var caller = Environment.CurrentManagedThreadId;
        await model.Delete(id);
        await model.Delete(id);
        var waitingBefore = model.Status;
        Assert.Null(rig.Memory.Find(id));                                   // the write is done ...
        Assert.Equal("", waitingBefore);                                    // ... and nothing the owner reads has changed until his thread applies it
        spy.Drain();
        Assert.Equal("Deleted. The note is gone from the files, the index and the search.", model.Status);
        Assert.NotEmpty(spy.RunThreads);
        Assert.DoesNotContain(caller, spy.RunThreads);
        model.Dispose();
    }

    [Fact]
    public async Task A_press_while_a_write_is_running_is_dropped_not_queued()
    {
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var work = new GatedMemoryWork(gate.Task, started);
        var model = new MemoryModel(rig.Memory, id => id, () => MemoryRig.Main, null, null, TimeZoneInfo.Utc, work);
        var a = rig.Memory.Propose(MemoryRig.FromWeb("Page one says the vault is in the red folder")).Note!.Id;
        var first = model.Keep(a);
        await started.Task;
        Assert.True(model.Busy);
        await model.Keep(a);                                                // dropped at once
        Assert.True(rig.Memory.Find(a)!.Quarantined);
        gate.SetResult();
        await first;
        Assert.False(model.Busy);
        Assert.True(rig.Memory.Find(a)!.Used);
        model.Dispose();
    }

    [Fact]
    public async Task The_import_brings_in_notes_off_the_calling_thread_and_says_what_it_did()
    {
        var folder = Path.Combine(rig.Temp.Folder, "claude-a");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "CLAUDE.md"), "- Always reply in English.\n- The staging password: hunter2horse\n- Run the tests before every commit.\n");
        var source = new MemoryImportSource("acca", "Claude A", ProviderKind.Claude, folder);
        var model = Model(new MemoryImport(rig.Memory, () => MemoryRig.Main), () => [source]);
        Assert.True(model.CanImport);
        Assert.True(model.HasAccounts);
        await model.ImportAsync();
        Assert.False(model.Importing);
        Assert.Equal("2 new notes · 1 left out (secrets, other people, or deleted before).", model.ImportSummary);
        Assert.Equal(2, model.Counts.Used);
        Assert.DoesNotContain("hunter2horse", rig.AllText(), StringComparison.Ordinal);
        await model.ImportAsync();
        Assert.Equal("Nothing new to bring in.", model.ImportSummary);
    }

    [Fact]
    public async Task What_the_owner_does_is_written_to_the_activity_log_as_counts_and_ids_never_as_a_notes_words()
    {
        var lines = new List<(string Action, string Result)>();
        var imported = 0;
        var folder = Path.Combine(rig.Temp.Folder, "claude-a");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "CLAUDE.md"), "- Always reply in English.\n");
        var source = new MemoryImportSource("acca", "Claude A", ProviderKind.Claude, folder);
        var model = new MemoryModel(rig.Memory, id => id, () => MemoryRig.Main, new MemoryImport(rig.Memory), () => [source], TimeZoneInfo.Utc, new InlineMemoryWork(),
            (action, result) => lines.Add((action, result)), () => imported++);
        models.Add(model);
        var used = Add("The zebrafish tank needs its filter cleaned", topic: "Aquarium");
        var waiting = rig.Memory.Propose(MemoryRig.FromWeb("Page says the vault is in the red folder")).Note!.Id;
        await model.Keep(waiting);
        await model.Delete(used);
        await model.Delete(used);
        Add("Dinner is at eight", topic: "Diet");
        model.ForgetText = "diet";
        model.PreviewForget();
        await model.ConfirmForget();
        await model.ImportAsync();
        Assert.Equal(1, imported);
        Assert.Equal(["keep", "delete", "forget", "import"], lines.Select(l => l.Action));
        Assert.Equal($"kept note {waiting}", lines[0].Result);
        Assert.Equal($"deleted note {used}", lines[1].Result);
        Assert.Equal("removed 1 notes", lines[2].Result);
        Assert.Equal("read 1 files: kept 1, waiting 0, joined 0, refused 0", lines[3].Result);
        var all = string.Join('\n', lines.Select(l => l.Action + "=" + l.Result));
        Assert.DoesNotContain("zebrafish", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Dinner", all, StringComparison.Ordinal);
        Assert.DoesNotContain("vault", all, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_import_with_no_account_says_there_is_nothing_to_bring_in_and_without_an_import_it_is_not_offered()
    {
        var none = Model(new MemoryImport(rig.Memory), () => []);
        await none.ImportAsync();
        Assert.Equal("No Claude or Codex account is set up, so there is nothing to bring in.", none.ImportSummary);
        Assert.False(Model().CanImport);
    }

    [Fact]
    public void Files_the_store_could_not_read_or_a_newer_nth_wrote_are_listed_as_problems()
    {
        rig.Store.Dispose();
        Directory.CreateDirectory(rig.Folder(MemoryRig.Main));
        File.WriteAllText(Path.Combine(rig.Folder(MemoryRig.Main), "recent.md"), "<!-- nth memory 9 -->\n# Recent\n\n- from the future <!--n {\"id\":\"nfuture00001\"}-->\n");
        rig.Reopen();
        var model = Model();
        Assert.Equal(["recent.md was written by a newer version of nth. It is left as it is and its notes are not used."], model.Problems);
    }

    [Fact]
    public void The_detail_line_says_the_kind_whether_it_may_change_where_it_came_from_and_when()
    {
        var note = rig.Memory.Propose(MemoryRig.Clean("The owner lives in Pune", NoteKind.Fact) with { MayChange = true }).Note!;
        Assert.Equal("Fact · may change · from your chat “Planning” · 12 Oct", MemoryViewWords.Detail(note, TimeZoneInfo.Utc));
        var report = new ImportReport(2, 1, 3, 1, 4, 2, 1, []);
        Assert.Equal("4 new notes · 4 already known · 1 waiting for you · 2 left out (secrets, other people, or deleted before) · 1 could not be saved.", MemoryViewWords.Importer(report));
    }
}
