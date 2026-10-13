// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.2, 8.3, 8.4; unit AS-12; ED-NEW-dextra-as12-20). No Swift twin.
// What the Memory view shows and does, apart from how it draws: the notes by assistant and topic with search, the notes that wait for
// the owner's Keep, Edit and Delete and Source for each note, "forget everything about ⟨topic⟩" with the notes it will remove shown
// first, a note the owner writes himself, and the import of other agents' notes. Delete is real (the store removes the note from its
// file, the index, the search index and the built prompts); a used note asks once more before it goes, a waiting one does not.
// Everything that writes a file runs off the UI thread (IMemoryWork) and its outcome is applied back on it.
using System.Globalization;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant.Views;

/// <summary>The words of the Memory view, in one place.</summary>
public static class MemoryViewWords
{
    public const string Title = "Memory";
    public const string Subtitle = "Everything your assistants remember. Notes are used right away. You can edit or delete any of them, and a delete removes the note for real.";
    public const string SearchPrompt = "Search notes";
    public const string Empty = "Nothing is remembered yet. A note is written when you tell an assistant something worth keeping, or you can write one below.";
    public const string NoMatch = "No note matches that.";
    public const string WaitingTitle = "Waiting for you";
    public const string WaitingDetail = "These came from content nth does not trust, such as a web page, an email or a file outside your folders. They are not used until you keep them.";
    public const string KeepTitle = "Keep";
    public const string KeepAllTitle = "Keep all";
    public const string EditTitle = "Edit";
    public const string DeleteTitle = "Delete";
    public const string SourceTitle = "Source";
    public const string SaveTitle = "Save";
    public const string CancelTitle = "Cancel";
    public const string SharedTitle = "About you";
    public const string SharedDetail = "Every assistant reads these.";
    public const string WriteTitle = "Write a note yourself";
    public const string WritePrompt = "Something worth remembering";
    public const string AddTitle = "Add";
    public const string ForgetTitle = "Forget everything about…";
    public const string ForgetPrompt = "A topic, for example my diet";
    public const string ShowTitle = "Show the notes";
    public const string ImportTitle = "Bring in notes";
    public const string ImportDetail = "From Claude Code (CLAUDE.md and its memory folders) and Codex (AGENTS.md and its memory files), from each account.";
    public const string ImportNone = "No Claude or Codex account is set up, so there is nothing to bring in.";
    public const string Importing = "Reading the notes your other agents keep…";
    public const string WhyHeading = "Why do you know this?";

    public static string Count(MemoryCounts counts) =>
        (counts.Used == 1 ? "1 note" : $"{counts.Used} notes") + (counts.Waiting > 0 ? $" · {counts.Waiting} waiting for you" : "");

    public static string Detail(MemoryNote note, TimeZoneInfo zone)
    {
        var parts = new List<string> { MemoryRules.KindWord(note.Kind) };
        if (note.Kind == NoteKind.Fact && note.MayChange) parts.Add("may change");
        parts.Add(MemoryWords.Source(note));
        parts.Add(TimeZoneInfo.ConvertTime(note.Created, zone).ToString("d MMM", CultureInfo.InvariantCulture));
        return string.Join(" · ", parts);
    }

    public static string Importer(ImportReport report)
    {
        if (report.Kept + report.Quarantined + report.Merged + report.Refused + report.Failed == 0) return "Nothing new to bring in.";
        var parts = new List<string>();
        var written = report.Kept + report.Quarantined;
        parts.Add(written == 0 ? "No new notes" : written == 1 ? "1 new note" : $"{written} new notes");
        if (report.Merged > 0) parts.Add($"{report.Merged} already known");
        if (report.Quarantined > 0) parts.Add($"{report.Quarantined} waiting for you");
        if (report.Refused > 0) parts.Add($"{report.Refused} left out (secrets, other people, or deleted before)");
        if (report.Failed > 0) parts.Add($"{report.Failed} could not be saved");
        return string.Join(" · ", parts) + ".";
    }
}

/// <summary>Where the model's file work runs: away from the UI thread, with its outcome applied back on it.</summary>
public interface IMemoryWork
{
    /// <summary>Runs store work (it reads and writes files) away from the UI thread.</summary>
    Task<T> Run<T>(Func<T> work);

    /// <summary>Applies an outcome on the UI thread.</summary>
    void Post(Action ui);
}

/// <summary>The product's work: the thread pool, and back on the thread the model was made on (the UI's).</summary>
public sealed class PoolMemoryWork : IMemoryWork
{
    private readonly SynchronizationContext? context = SynchronizationContext.Current;

    public Task<T> Run<T>(Func<T> work) => Task.Run(work);

    public void Post(Action ui)
    {
        if (context is null) ui();
        else context.Post(_ => ui(), null);
    }
}

/// <summary>The notes of one topic.</summary>
public sealed record MemoryTopicGroup(string Topic, string Title, IReadOnlyList<MemoryNote> Notes);

/// <summary>The notes of one assistant (or the shared set), by topic.</summary>
public sealed record MemoryScopeGroup(string Scope, string Title, string Detail, IReadOnlyList<MemoryTopicGroup> Topics)
{
    public int Count => Topics.Sum(t => t.Notes.Count);
}

public sealed class MemoryModel : IDisposable
{
    private readonly MemoryStore store;
    private readonly MemoryImport? import;
    private readonly Func<IReadOnlyList<MemoryImportSource>> sources;
    private readonly Func<string, string> scopeName;
    private readonly Func<string?> mainAssistant;
    private readonly IMemoryWork work;
    private readonly Action<string, string> audit;
    private readonly Action imported;
    private readonly TimeZoneInfo zone;
    private string query = "";
    private string status = "";
    private int busy;
    private readonly string? fixedScope;

    /// <param name="store">The memory.</param>
    /// <param name="scopeName">The name of an assistant by its id (Dextra, an Astur); the shared set is named here.</param>
    /// <param name="mainAssistant">Dextra's id: where a procedure or project note the owner writes goes.</param>
    /// <param name="import">The import of other agents' notes; null: the view offers none.</param>
    /// <param name="sources">The accounts the import reads now.</param>
    /// <param name="zone">The zone dates are shown in (the PC's).</param>
    /// <param name="work">Where file work runs; the thread pool by default.</param>
    /// <param name="audit">Writes one activity line for what the owner did to the memory (action, result: counts and ids, never a note's words).</param>
    /// <param name="imported">Called after an import the owner ran (starts the watch over the folders, 8.4).</param>
    public MemoryModel(MemoryStore store, Func<string, string>? scopeName = null, Func<string?>? mainAssistant = null, MemoryImport? import = null,
        Func<IReadOnlyList<MemoryImportSource>>? sources = null, TimeZoneInfo? zone = null, IMemoryWork? work = null, Action<string, string>? audit = null,
        Action? imported = null, string? fixedScope = null)
    {
        this.audit = audit ?? ((_, _) => { });
        this.imported = imported ?? (() => { });
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.fixedScope = fixedScope;
        this.scopeName = scopeName ?? (id => id);
        this.mainAssistant = mainAssistant ?? (() => null);
        this.import = import;
        this.sources = sources ?? (() => []);
        this.zone = zone ?? TimeZoneInfo.Local;
        this.work = work ?? new PoolMemoryWork();
        store.Changed += OnStoreChanged;
        store.Faulted += OnFaulted;
        Rebuild();
    }

    /// <summary>Raised when anything the view shows changed (from any thread; the view marshals it).</summary>
    public event Action? Changed;

    public TimeZoneInfo Zone => zone;
    public string Subtitle => fixedScope is null ? MemoryViewWords.Subtitle : $"Notes kept by {ScopeTitle(fixedScope)}. Edit or delete any of its notes here.";
    private MemoryNote? Find(string id) => store.Find(id) is { } note && (fixedScope is null || note.Scope == fixedScope) ? note : null;

    /// <summary>A write is under way: further commands wait for it (a double press changes nothing twice).</summary>
    public bool Busy => Volatile.Read(ref busy) != 0;

    // ---- what is shown ------------------------------------------------------------------------------------------------------

    public string Query
    {
        get => query;
        set
        {
            var next = value ?? "";
            if (next == query) return;
            query = next;
            Rebuild();
            Raise();
        }
    }

    /// <summary>The notes that match the search, by assistant then topic. Empty when nothing is remembered or nothing matches.</summary>
    public IReadOnlyList<MemoryScopeGroup> Groups { get; private set; } = [];

    /// <summary>The notes that wait for the owner (matching the search), oldest first.</summary>
    public IReadOnlyList<MemoryNote> Waiting { get; private set; } = [];

    public MemoryCounts Counts { get; private set; } = new(0, 0);

    /// <summary>Files that could not be read or a newer nth wrote, and the import's last problems.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = [];

    /// <summary>What the last action did, in a line the view reads out ("Deleted. The note is gone from the files.").</summary>
    public string Status => status;

    public bool IsEmpty => Counts.Used == 0 && Counts.Waiting == 0;

    public bool NoMatch => !IsEmpty && Groups.Count == 0 && Waiting.Count == 0;

    private void Rebuild()
    {
        var words = MemoryRules.Tokens(query);
        var all = store.Notes(null).Where(n => fixedScope is null || n.Scope == fixedScope).ToArray();
        Counts = new MemoryCounts(all.Count(n => n.Used), all.Count(n => n.Quarantined));
        bool Matches(MemoryNote n)
        {
            if (words.Count == 0) return true;
            var tokens = MemoryRules.Tokens(n.Text + " " + n.Topic.Replace('-', ' ') + " " + ScopeTitle(n.Scope));
            return words.All(tokens.Contains);
        }
        var shown = all.Where(Matches).ToList();
        Waiting = [.. shown.Where(n => n.Quarantined).OrderBy(n => n.Created)];
        var main = mainAssistant();
        Groups = [.. shown.Where(n => n.Used).GroupBy(n => n.Scope)
            .OrderBy(g => g.Key == MemoryScope.Shared ? 0 : g.Key == main ? 1 : 2).ThenBy(g => ScopeTitle(g.Key), StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new MemoryScopeGroup(g.Key, ScopeTitle(g.Key), g.Key == MemoryScope.Shared ? MemoryViewWords.SharedDetail : "",
                [.. g.GroupBy(n => n.Topic).OrderBy(t => MemoryRules.TopicTitle(t.Key), StringComparer.CurrentCultureIgnoreCase)
                    .Select(t => new MemoryTopicGroup(t.Key, MemoryRules.TopicTitle(t.Key), [.. t.OrderBy(n => n.Created)]))]))];
        Problems = [.. store.Problems, .. importProblems];
        // A note that went away takes its open edit, source and question with it.
        if (EditingId is { } editing && !all.Any(n => n.Id == editing)) EditingId = null;
        if (SourceOpenId is { } source && !all.Any(n => n.Id == source)) SourceOpenId = null;
        if (ConfirmDeleteId is { } confirm && !all.Any(n => n.Id == confirm)) ConfirmDeleteId = null;
    }

    public string ScopeTitle(string scope) => scope == MemoryScope.Shared ? MemoryViewWords.SharedTitle : scopeName(scope);

    private void Raise() => Changed?.Invoke();

    // The store raises these on whichever thread wrote; the model changes only on the UI thread.
    private void OnStoreChanged(MemoryChange change) => work.Post(() =>
    {
        Rebuild();
        Raise();
    });

    private void OnFaulted(string line) => work.Post(() =>
    {
        status = line;
        Rebuild();
        Raise();
    });

    // Runs one write: the store work away from the UI thread, then its outcome applied on it. A command that arrives while one is
    // running is dropped (the press is not queued behind a write the owner has not seen finish).
    private async Task<bool> Write<T>(Func<T> storeWork, Action<T> apply)
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return false;
        try
        {
            var result = await work.Run(storeWork).ConfigureAwait(true);
            work.Post(() =>
            {
                apply(result);
                Rebuild();
                Raise();
            });
            return true;
        }
        finally
        {
            Volatile.Write(ref busy, 0);
        }
    }

    // ---- one note: edit, delete, keep, source ---------------------------------------------------------------------------------

    public string? EditingId { get; private set; }

    public string EditText { get; set; } = "";

    public string? EditProblem { get; private set; }

    public string? ConfirmDeleteId { get; private set; }

    public string? SourceOpenId { get; private set; }

    public void BeginEdit(string id)
    {
        if (Find(id) is not { } note) return;
        EditingId = id;
        EditText = note.Text;
        EditProblem = null;
        ConfirmDeleteId = null;
        Raise();
    }

    public void CancelEdit()
    {
        if (EditingId is null) return;
        EditingId = null;
        EditProblem = null;
        Raise();
    }

    /// <summary>Saves the edit. A refusal (a secret, too long, empty) keeps the edit open and says why. True when the note was saved.</summary>
    public async Task<bool> SaveEdit()
    {
        if (EditingId is not { } id || Find(id) is null) return false;
        var text = EditText;
        var saved = false;
        await Write(() => store.Edit(id, text), outcome =>
        {
            if (!outcome.Written)
            {
                EditProblem = outcome.Message;
                return;
            }
            saved = true;
            EditingId = null;
            EditProblem = null;
            status = "Saved.";
        }).ConfigureAwait(true);
        return saved;
    }

    /// <summary>Delete on a note: a used note asks once more first; a waiting one goes at once (it was never used).</summary>
    public async Task Delete(string id)
    {
        if (Find(id) is not { } note) return;
        if (note.Used && ConfirmDeleteId != id)
        {
            ConfirmDeleteId = id;
            EditingId = null;
            Raise();
            return;
        }
        ConfirmDeleteId = null;
        await Write(() => store.Delete([id]), report =>
        {
            status = report.Failed ? "The note could not be deleted: the memory files could not be written. Nothing changed." : "Deleted. The note is gone from the files, the index and the search.";
            audit("delete", report.Failed ? "failed" : "deleted note " + id);
        }).ConfigureAwait(true);
    }

    public void CancelDelete()
    {
        if (ConfirmDeleteId is null) return;
        ConfirmDeleteId = null;
        Raise();
    }

    public Task Keep(string id) => Find(id) is null ? Task.CompletedTask : Write(() => store.Keep(id), kept =>
    {
        status = kept ? "Kept. It is used from now on." : "That note is not waiting.";
        if (kept) audit("keep", "kept note " + id);
    });

    public Task KeepAll()
    {
        var ids = Waiting.Select(n => n.Id).ToList();
        return Write(() => store.Keep(ids), kept =>
        {
            status = kept.Count == 0 ? "Nothing was kept." : kept.Count == 1 ? "Kept 1 note. It is used from now on." : $"Kept {kept.Count} notes. They are used from now on.";
            if (kept.Count > 0) audit("keep", $"kept {kept.Count} notes");
        });
    }

    public void ToggleSource(string id)
    {
        if (Find(id) is null) return;
        SourceOpenId = SourceOpenId == id ? null : id;
        Raise();
    }

    // ---- writing a note --------------------------------------------------------------------------------------------------------

    public string AddText { get; set; } = "";

    public NoteKind AddKind { get; set; } = NoteKind.Fact;

    public string? AddProblem { get; private set; }

    /// <summary>The owner writes a note himself: his own words, so the third-party rule does not apply and a note he deleted may be written again.</summary>
    public async Task<bool> Add()
    {
        var main = mainAssistant();
        var scope = fixedScope ?? (AddKind is NoteKind.Preference or NoteKind.Fact || main is null ? MemoryScope.Shared : main);
        var proposal = new NoteProposal(AddText, AddKind, scope, new NoteSource(NoteSourceKind.Owner, "memory view"))
        {
            Author = NoteAuthor.Owner,
            OwnerTyped = true,
        };
        var added = false;
        await Write(() => store.Propose(proposal), outcome =>
        {
            if (!outcome.Written)
            {
                AddProblem = outcome.Message;
                return;
            }
            added = true;
            AddProblem = null;
            AddText = "";
            status = outcome.Result == NoteResult.Merged ? outcome.Message : "Kept.";
        }).ConfigureAwait(true);
        return added;
    }

    // ---- forget everything about --------------------------------------------------------------------------------------------------

    public string ForgetText { get; set; } = "";

    /// <summary>The notes the request would remove, shown before anything is: null until the owner asks to see them.</summary>
    public IReadOnlyList<MemoryNote>? ForgetPreview { get; private set; }

    public void PreviewForget()
    {
        ForgetPreview = store.PreviewForget(null, ForgetText).Where(n => fixedScope is null || n.Scope == fixedScope).ToArray();
        status = ForgetPreview.Count == 0 ? "No note is about that." : "";
        Raise();
    }

    public async Task ConfirmForget()
    {
        if (ForgetPreview is not { Count: > 0 } preview) return;
        var ids = preview.Where(n => Find(n.Id) is not null).Select(n => n.Id).ToList();
        await Write(() => store.Delete(ids), report =>
        {
            ForgetPreview = null;
            ForgetText = "";
            status = report.Failed ? "The notes could not be deleted: the memory files could not be written. Nothing changed."
                : report.Removed == 1 ? "Removed 1 note, for real." : $"Removed {report.Removed} notes, for real.";
            audit("forget", report.Failed ? "failed" : $"removed {report.Removed} notes");
        }).ConfigureAwait(true);
    }

    public void CancelForget()
    {
        if (ForgetPreview is null) return;
        ForgetPreview = null;
        Raise();
    }

    // ---- import ---------------------------------------------------------------------------------------------------------------------

    private IReadOnlyList<string> importProblems = [];

    public bool CanImport => import is not null;

    public bool Importing { get; private set; }

    /// <summary>What the last import did, in a line; empty before one.</summary>
    public string ImportSummary { get; private set; } = "";

    public bool HasAccounts => sources().Count > 0;

    /// <summary>Brings in the notes other agents keep, off the UI thread. Returns when the notes are in.</summary>
    public async Task ImportAsync()
    {
        if (import is null || Importing) return;
        var accounts = sources();
        if (accounts.Count == 0)
        {
            ImportSummary = MemoryViewWords.ImportNone;
            Raise();
            return;
        }
        Importing = true;
        ImportSummary = MemoryViewWords.Importing;
        Raise();
        ImportReport report;
        try
        {
            report = await work.Run(() => import.Import(accounts)).ConfigureAwait(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException)
        {
            report = new ImportReport(0, 0, 0, 0, 0, 0, 0, ["The import stopped: " + error.Message]);
        }
        work.Post(() =>
        {
            importProblems = report.Problems;
            Importing = false;
            ImportSummary = MemoryViewWords.Importer(report);
            audit("import", $"read {report.FilesRead} files: kept {report.Kept}, waiting {report.Quarantined}, joined {report.Merged}, refused {report.Refused}");
            imported();
            Rebuild();
            Raise();
        });
    }

    public void Dispose()
    {
        store.Changed -= OnStoreChanged;
        store.Faulted -= OnFaulted;
    }
}
