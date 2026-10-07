// PORT-ONLY: first-release feature unit SK-18 (OD-0118). The logic of the "Import skills" sheet: no Avalonia; the view draws what it holds and sends it the user's presses (the pattern of LibraryController).
// Changed is raised from whichever thread did the work; the view hands it to the UI thread.
using Marrow.Core.Port.Skills.Import;

namespace Marrow.Services.Port.Skills.Import;

public enum ImportPhase
{
    Closed,
    Scanning,
    Choosing,
    AskingDuplicate,
    Importing,
    Done,
}

public sealed record ImportRow(string Key, string Name, string Description, string Source, bool Ticked, bool AlreadyImported);

public sealed record ImportGroup(ForeignApp App, string Title, IReadOnlyList<ImportRow> Rows, bool AllTicked);

public sealed class ImportController
{
    private readonly object gate = new();
    private readonly Func<IReadOnlyList<string>, ForeignScan> scan;
    private readonly ImportedSkillsLibrary library;
    private readonly Func<Task<string?>>? pickFolder;
    private readonly List<string> folders = [];
    private readonly HashSet<string> ticks = new(StringComparer.Ordinal);
    private ForeignScan found = new([], []);
    private ImportPhase phase = ImportPhase.Closed;
    private ImportMode mode = ImportMode.Copy;
    private int revision;
    private DuplicateQuestion? question;
    private DuplicateChoice? decided;
    private SemaphoreSlim? answer;
    private ImportResult? result;
    private string summary = "";

    public ImportController(Func<IReadOnlyList<string>, ForeignScan> scan, ImportedSkillsLibrary library, Func<Task<string?>>? pickFolder = null)
    {
        this.scan = scan;
        this.library = library;
        this.pickFolder = pickFolder;
    }

    public event Action? Changed;

    public int Revision { get { lock (gate) return revision; } }

    public ImportPhase Phase { get { lock (gate) return phase; } }

    public ImportMode Mode { get { lock (gate) return mode; } }

    public int TickedCount { get { lock (gate) return ticks.Count; } }

    public int ProblemCount { get { lock (gate) return found.Problems.Count; } }

    public DuplicateQuestion? Question { get { lock (gate) return question; } }

    public ImportResult? Result { get { lock (gate) return result; } }

    public string Summary { get { lock (gate) return summary; } }

    public IReadOnlyList<ImportedSkill> Imported => library.List();

    public IReadOnlyList<ImportGroup> Groups
    {
        get
        {
            // the library is asked before the gate is taken: an import holds the library while it asks its question under the gate
            var already = library.List().Select(i => i.App + "|" + i.SourcePath).ToHashSet(StringComparer.Ordinal);
            lock (gate)
            {
                return [.. found.Skills.GroupBy(s => s.App).OrderBy(g => g.Key).Select(g =>
                {
                    var rows = g.Select(s => new ImportRow(s.Key, s.Name, s.Description, s.Source, ticks.Contains(s.Key), already.Contains(s.Key))).ToArray();
                    var open = rows.Where(r => !r.AlreadyImported).ToArray();
                    return new ImportGroup(g.Key, Title(g.Key), rows, open.Length > 0 && open.All(r => r.Ticked));
                })];
            }
        }
    }

    /// <summary>The imported skills were written by a newer Marrow: they are shown and never changed.</summary>
    public bool ReadOnly => library.ReadOnly;

    /// <summary>Opens the sheet. Only a closed sheet opens: while an import runs (even with the sheet hidden) the session it has is kept.</summary>
    public async Task OpenAsync()
    {
        lock (gate)
        {
            if (phase != ImportPhase.Closed) return;
            ticks.Clear();
            result = null;
            question = null;
            summary = "";
            phase = ImportPhase.Scanning;
        }
        Raise();
        await ScanAsync();
    }

    private async Task ScanAsync()
    {
        string[] chosen;
        lock (gate) chosen = [.. folders];
        ForeignScan scanned;
        try { scanned = await Task.Run(() => scan(chosen)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { scanned = new ForeignScan([], [new ScanProblem("", e.Message)]); }
        lock (gate)
        {
            found = scanned;
            ticks.IntersectWith(found.Skills.Select(s => s.Key));
            if (phase == ImportPhase.Scanning) phase = ImportPhase.Choosing;
        }
        Raise();
    }

    public void Close()
    {
        lock (gate)
        {
            // A question still open is answered Skip so the import that waits on it can end.
            if (phase == ImportPhase.AskingDuplicate) { decided = DuplicateChoice.Skip; answer?.Release(); }
            phase = ImportPhase.Closed;
            ticks.Clear();
            found = new ForeignScan([], []);
            question = null;
            result = null;
            summary = "";
            folders.Clear();
        }
        Raise();
    }

    public void Toggle(string key)
    {
        lock (gate)
        {
            if (phase != ImportPhase.Choosing || found.Skills.All(s => s.Key != key)) return;
            if (!ticks.Remove(key)) ticks.Add(key);
        }
        Raise();
    }

    public void ToggleGroup(ForeignApp app)
    {
        var already = library.List().Select(i => i.App + "|" + i.SourcePath).ToHashSet(StringComparer.Ordinal);
        lock (gate)
        {
            if (phase != ImportPhase.Choosing) return;
            var keys = found.Skills.Where(s => s.App == app && !already.Contains(s.Key)).Select(s => s.Key).ToArray();
            if (keys.Length == 0) return;
            if (keys.All(ticks.Contains)) foreach (var key in keys) ticks.Remove(key);
            else foreach (var key in keys) ticks.Add(key);
        }
        Raise();
    }

    public void SetMode(ImportMode next)
    {
        lock (gate) mode = next;
        Raise();
    }

    public async Task AddFolderAsync()
    {
        if (pickFolder is null) return;
        var path = await pickFolder();
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (gate)
        {
            if (phase != ImportPhase.Choosing) return;
            if (!folders.Contains(path, StringComparer.Ordinal)) folders.Add(path);
            phase = ImportPhase.Scanning;
        }
        Raise();
        await ScanAsync();
    }

    public Task ImportSelectedAsync()
    {
        ForeignSkill[] chosen;
        lock (gate) chosen = [.. found.Skills.Where(s => ticks.Contains(s.Key))];
        return RunAsync(chosen);
    }

    /// <summary>Everything found that is not already imported from the same original.</summary>
    public Task ImportAllAsync()
    {
        ForeignSkill[] chosen;
        var already = library.List().Select(i => i.App + "|" + i.SourcePath).ToHashSet(StringComparer.Ordinal);
        lock (gate)
        {
            chosen = [.. found.Skills.Where(s => !already.Contains(s.Key))];
        }
        return RunAsync(chosen);
    }

    private async Task RunAsync(ForeignSkill[] chosen)
    {
        ImportMode how;
        lock (gate)
        {
            if (phase != ImportPhase.Choosing) return;
            phase = ImportPhase.Importing;
            how = mode;
            decided = null;
            applyRest = false;
        }
        Raise();
        DuplicateChoice? rest = null;
        ImportResult? done = null;
        string? failure = null;
        await Task.Run(() =>
        {
            try
            {
                done = library.Import(chosen, how, q =>
                {
                    if (rest is { } remembered) return remembered;
                    var wait = new SemaphoreSlim(0);
                    lock (gate)
                    {
                        question = q;
                        decided = null;
                        answer = wait;
                        phase = ImportPhase.AskingDuplicate;
                    }
                    Raise();
                    wait.Wait();
                    DuplicateChoice choice;
                    lock (gate)
                    {
                        choice = decided ?? DuplicateChoice.Skip;
                        if (applyRest) rest = choice;
                        question = null;
                        answer = null;
                        phase = ImportPhase.Importing;
                    }
                    Raise();
                    return choice;
                });
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException) { failure = ImportWords.CouldNotSave; }
        });
        lock (gate)
        {
            result = done;
            summary = done is null ? failure ?? ImportWords.CouldNotSave : ImportWords.Done(done.Imported.Count - done.Replaced.Count, done.Replaced.Count, done.Skipped.Count, done.Failed.Count);
            ticks.Clear();
            phase = ImportPhase.Done;
        }
        Raise();
    }

    private bool applyRest;

    public void AnswerDuplicate(DuplicateChoice choice, bool applyToRest)
    {
        lock (gate)
        {
            if (phase != ImportPhase.AskingDuplicate) return;
            decided = choice;
            applyRest = applyToRest;
            answer?.Release();
        }
    }

    public bool Remove(string id)
    {
        if (library.ReadOnly) return false;
        var removed = library.Remove(id);
        Raise();
        return removed;
    }

    public void SetEnabled(string id, bool on)
    {
        if (library.ReadOnly) return;
        library.SetEnabled(id, on);
        Raise();
    }

    private static string Title(ForeignApp app) => app switch
    {
        ForeignApp.ClaudeCode => ImportWords.ClaudeCode,
        ForeignApp.Codex => ImportWords.Codex,
        ForeignApp.Antigravity => ImportWords.Antigravity,
        ForeignApp.Cursor => ImportWords.Cursor,
        _ => ImportWords.Folders,
    };

    /// <summary>Tells the views to draw again (something outside the controller changed the library, or the controller itself did).</summary>
    public void Raise()
    {
        lock (gate) revision++;
        Changed?.Invoke();
    }
}
