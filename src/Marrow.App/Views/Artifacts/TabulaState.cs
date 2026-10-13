using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Marrow.Services.Store;
using Marrow.Core.Port;

// PORT-ONLY: web surfaces W14, what a thread's Tabula pane shows (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.5 "Its state
// (tabs, scroll, version) is kept for the thread and comes back when reopened", 3.11 "artifact selection and version. Stored
// with the thread", 6.1 the gallery, 6.3 the version strip, view switch and Ask to change; ED-NEW-web-w14-1). No Swift
// counterpart. One state per thread, outside the pane's controls, so closing and reopening the pane, or switching threads
// and back, shows the same artifact, version and view. The choice is saved beside the thread's artifacts (tabula.json).

namespace Marrow.App.Views.Artifacts;

/// <summary>Preview, Code or Diff (6.3).</summary>
public enum TabulaViewMode
{
    Preview,
    Code,
    Diff,
}

/// <summary>How Diff lays out the two versions (6.3 "side by side or inline").</summary>
public enum TabulaDiffLayout
{
    Inline,
    SideBySide,
}

/// <summary>The gallery's filter (6.1 "This project / All threads").</summary>
public enum TabulaGalleryScope
{
    ThisProject,
    AllThreads,
}

/// <summary>An artifact by its chat and id (the chat may be another one: the gallery opens those read-only).</summary>
public sealed record TabulaRef(Guid Thread, string Id);

/// <summary>A change asked from the pane that the model has not answered yet (the strip's pulsing dot).</summary>
public sealed record TabulaPendingAsk(string Id, int FromVersion, string Prompt, DateTimeOffset At)
{
    public Guid RequestId { get; init; } = Guid.NewGuid();
    public int Number { get; init; }
    public bool IsQueued { get; init; }
}

/// <summary>What one thread's Tabula pane shows.</summary>
public sealed class TabulaState
{
    public const string FileName = "tabula.json";

    private readonly string? file;
    private readonly List<TabulaPendingAsk> asks = [];
    private JsonObject? savedFields;
    private bool preserveFile;
    private int savedRevision;
    private TabulaState? storedState;
    internal static Ambient<Func<string, byte[]>> ReadBytes { get; } = new((Func<string, byte[]>)File.ReadAllBytes);
    internal static Ambient<Action<string, byte[]>> WriteBytes { get; } = new((Action<string, byte[]>)FileOps.WriteAtomic);
    internal Task Persistence { get; private set; } = Task.CompletedTask;

    public TabulaState(Guid thread, string? file = null)
    {
        Thread = thread;
        this.file = file;
    }

    /// <summary>The thread whose pane this is.</summary>
    public Guid Thread { get; }

    /// <summary>The artifact shown, or null for the empty pane.</summary>
    public TabulaRef? Selected { get; private set; }

    /// <summary>The version shown; null follows the latest (a new version shows as it lands).</summary>
    public int? Version { get; private set; }

    public TabulaViewMode View { get; private set; } = TabulaViewMode.Preview;

    /// <summary>The version Diff compares the shown one with; null is the one before it.</summary>
    public int? Compare { get; private set; }

    public TabulaDiffLayout DiffLayout { get; private set; } = TabulaDiffLayout.Inline;

    /// <summary>The project file the Code view shows; null is the entry.</summary>
    public string? CodeFile { get; private set; }

    public bool IsGalleryOpen { get; private set; }

    public TabulaGalleryScope GalleryScope { get; private set; } = TabulaGalleryScope.ThisProject;

    public string GallerySearch { get; private set; } = "";

    public bool IsAskOpen { get; private set; }

    /// <summary>What the Ask to change composer holds (kept while the pane is closed).</summary>
    public string AskText { get; set; } = "";

    /// <summary>The text chosen in Markdown or code when Ask to change opened ("Change this part").</summary>
    public string? AskSelection { get; private set; }

    public TabulaPendingAsk? PendingAsk => asks.FirstOrDefault(a => !a.IsQueued);

    public IReadOnlyList<TabulaPendingAsk> QueuedAsks => asks.Where(a => a.IsQueued).ToArray();

    /// <summary>Accepted requests, in thread order; kept outside the pane's controls (ED-NEW-web-nc6-1).</summary>
    public IReadOnlyList<TabulaPendingAsk> Asks => asks.ToArray();

    public void AddAsk(TabulaPendingAsk pending)
    {
        if (asks.Any(a => a.RequestId == pending.RequestId)) return;
        var number = Math.Max(pending.FromVersion + 1, asks.Where(a => a.Id == pending.Id).Select(a => a.Number + 1).DefaultIfEmpty(1).Max());
        asks.Add(pending with { Number = number });
        Commit(save: false);
    }

    public void StartAsk(Guid request, int currentVersion)
    {
        var at = asks.FindIndex(a => a.RequestId == request);
        if (at < 0) return;
        var active = asks[at] with { IsQueued = false, Number = currentVersion + 1 };
        asks[at] = active;
        var next = active.Number;
        for (var i = at + 1; i < asks.Count; i++)
            if (asks[i].Id == active.Id) asks[i] = asks[i] with { Number = ++next };
        Commit(save: false);
    }

    public void FinishAsk(Guid request)
    {
        if (asks.RemoveAll(a => a.RequestId == request) == 0) return;
        Commit(save: false);
    }

    /// <summary>One turn can write several versions: every waiting node stays after all written versions.</summary>
    public void RebaseAsks(string artifact, int currentVersion)
    {
        var number = Math.Max(currentVersion, asks.Where(a => a.Id == artifact && !a.IsQueued).Select(a => a.Number).DefaultIfEmpty(0).Max());
        var changed = false;
        for (var i = 0; i < asks.Count; i++)
        {
            if (asks[i].Id != artifact || !asks[i].IsQueued) continue;
            number++;
            if (asks[i].Number == number) continue;
            asks[i] = asks[i] with { Number = number };
            changed = true;
        }
        if (changed) Commit(save: false);
    }

    /// <summary>The artifact is another chat's: it opens read-only, with "Copy into this thread" (6.1).</summary>
    public bool IsReadOnly => Selected is { } selected && selected.Thread != Thread;

    /// <summary>Raised after any change.</summary>
    public event Action? Changed;

    public void Select(TabulaRef? artifact, int? version = null)
    {
        var same = Selected == artifact;
        Selected = artifact;
        Version = version;
        IsGalleryOpen = false;
        if (!same)
        {
            Compare = null;
            CodeFile = null;
            AskSelection = null;
            if (View == TabulaViewMode.Diff) View = TabulaViewMode.Preview;
        }
        Commit(save: true);
    }

    /// <summary>Shows a version (null follows the latest). A compare version at or after it moves before it.</summary>
    public void ShowVersion(int? version)
    {
        if (Version == version) return;
        Version = version;
        if (Compare is { } compare && version is { } shown && compare >= shown) Compare = null;
        Commit(save: true);
    }

    public void SetView(TabulaViewMode view)
    {
        if (View == view) return;
        View = view;
        Commit(save: true);
    }

    public void SetCompare(int? version)
    {
        if (Compare == version) return;
        Compare = version;
        Commit(save: true);
    }

    public void SetDiffLayout(TabulaDiffLayout layout)
    {
        if (DiffLayout == layout) return;
        DiffLayout = layout;
        Commit(save: true);
    }

    public void SetCodeFile(string? path)
    {
        if (CodeFile == path) return;
        CodeFile = path;
        Commit(save: true);
    }

    public void OpenGallery(bool open)
    {
        if (IsGalleryOpen == open) return;
        IsGalleryOpen = open;
        Commit(save: false);
    }

    public void SetGalleryScope(TabulaGalleryScope scope)
    {
        if (GalleryScope == scope) return;
        GalleryScope = scope;
        Commit(save: false);
    }

    public void SetGallerySearch(string text)
    {
        text ??= "";
        if (GallerySearch == text) return;
        GallerySearch = text;
        Commit(save: false);
    }

    public void OpenAsk(bool open, string? selection = null)
    {
        if (IsAskOpen == open && AskSelection == selection) return;
        IsAskOpen = open;
        AskSelection = open ? selection : null;
        Commit(save: false);
    }

    public void ClearAskSelection()
    {
        if (AskSelection is null) return;
        AskSelection = null;
        Commit(save: false);
    }

    public void SetPendingAsk(TabulaPendingAsk? pending)
    {
        if (PendingAsk == pending) return;
        if (PendingAsk is { } previous) FinishAsk(previous.RequestId);
        if (pending is not null) AddAsk(pending);
    }

    private void Commit(bool save)
    {
        if (save)
        {
            savedRevision++;
            Save();
        }
        Changed?.Invoke();
    }

    // ---- tabula.json: the artifact, version, view, compare, layout and file; nothing else is kept ----

    /// <summary>Reads a thread's saved choice; a missing or broken file is the empty pane, never an error.</summary>
    public static TabulaState Load(Guid thread, string? file)
    {
        if (file is not null && Dispatcher.UIThread.CheckAccess())
        {
            var pending = new TabulaState(thread, file);
            pending.Persistence = pending.LoadForPane();
            return pending;
        }
        return ReadStored(thread, file);
    }

    // ED-NEW-web-review-27: disk reads and atomic-write retries never occupy the UI thread.
    // A selection made while loading wins; stored extension fields and a future version still apply.
    private async Task LoadForPane()
    {
        TabulaState loaded = this;
        await DiskWriter.Shared.RunQueued(() =>
        {
            loaded = ReadStored(Thread, file);
            storedState = loaded;
        }).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            savedFields = loaded.savedFields;
            preserveFile = loaded.preserveFile;
            if (savedRevision == 0)
            {
                Selected = loaded.Selected;
                Version = loaded.Version;
                View = loaded.View;
                Compare = loaded.Compare;
                DiffLayout = loaded.DiffLayout;
                CodeFile = loaded.CodeFile;
                Changed?.Invoke();
            }
        });
    }

    private static TabulaState ReadStored(Guid thread, string? file)
    {
        var state = new TabulaState(thread, file);
        if (file is null || !File.Exists(file)) return state;
        try
        {
            if (JsonNode.Parse(ReadBytes.Current(file)) is not JsonObject root) return state;
            if (root["format"]?.GetValue<int>() is > 1)
            {
                state.preserveFile = true;
                return state;
            }
            state.savedFields = (JsonObject)root.DeepClone();
            if (root["artifact"]?.GetValue<string>() is { Length: > 0 } id)
            {
                var owner = root["thread"]?.GetValue<string>() is { } text && Guid.TryParse(text, out var other) ? other : thread;
                state.Selected = new TabulaRef(owner, id);
            }
            state.Version = root["version"]?.GetValue<int>() is > 0 and var version ? version : null;
            state.Compare = root["compare"]?.GetValue<int>() is > 0 and var compare ? compare : null;
            state.View = Enum.TryParse<TabulaViewMode>(root["view"]?.GetValue<string>(), ignoreCase: true, out var view) ? view : TabulaViewMode.Preview;
            state.DiffLayout = root["diff"]?.GetValue<string>() == "side" ? TabulaDiffLayout.SideBySide : TabulaDiffLayout.Inline;
            state.CodeFile = root["file"]?.GetValue<string>() is { Length: > 0 } path ? path : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
        {
            return new TabulaState(thread, file);
        }
        return state;
    }

    private void Save()
    {
        if (file is null || preserveFile) return;
        var root = new JsonObject();
        root["format"] = 1;
        root["artifact"] = Selected?.Id;
        root["version"] = Version;
        root["view"] = View.ToString().ToLowerInvariant();
        root["compare"] = Compare;
        root["diff"] = DiffLayout == TabulaDiffLayout.SideBySide ? "side" : "inline";
        root["file"] = CodeFile;
        root["thread"] = Selected is { } selected && selected.Thread != Thread ? selected.Thread.ToString("D") : null;
        if (Dispatcher.UIThread.CheckAccess())
        {
            // Read and write operations are already ordered by the shutdown-drained queue.
            // They never wait on the UI continuation that restores the visible choice.
            Persistence = DiskWriter.Shared.RunQueued(() => WriteStored(root));
            return;
        }
        WriteStored(root);
    }

    private void WriteStored(JsonObject choice)
    {
        var stored = storedState ?? this;
        if (file is null || stored.preserveFile) return;
        var root = stored.savedFields is null ? new JsonObject() : (JsonObject)stored.savedFields.DeepClone();
        foreach (var field in choice) root[field.Key] = field.Value?.DeepClone();
        if (root["thread"] is null) root.Remove("thread");
        try
        {
            if (Path.GetDirectoryName(file) is { } folder && Directory.Exists(folder))
                WriteBytes.Current(file, JsonSerializer.SerializeToUtf8Bytes(root));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The choice is a convenience: a full disk keeps the pane working with what it shows.
        }
    }
}
