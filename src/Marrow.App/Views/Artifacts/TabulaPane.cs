using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Marrow.App.Runtime;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;

// PORT-ONLY: web surfaces W14, the Tabula pane (E:/Droppy/work/design/web-surfaces/DESIGN.md 6.1 the gallery, 6.3 the pane,
// 6.4 streaming, 6.5 the bridge's cards; the approved canvas index.html section 4; ED-NEW-web-w14-1). No Swift counterpart.
// The pane's registration with the workspace, the per-thread state, and the places it reaches out to: the web surface its
// previews run in, the save and open dialogs, the clipboard, Visus (W8a binds "Open in Visus"), and the chat a change is
// asked in. The store's events open the pane: a new artifact opens it on itself, artifact_create opens it the moment it
// starts streaming, and a chip in the chat opens it on its artifact.

namespace Marrow.App.Views.Artifacts;

/// <summary>A save dialog's request: the title, where it starts, the suggested name, and the one type it offers.</summary>
/// <param name="IsFolder">The chosen path is a folder to make (a project saves as its folder).</param>
public sealed record TabulaSaveRequest(string Title, string? Folder, string FileName, string TypeName, string Extension, bool IsFolder = false);

/// <summary>The system's save and open dialogs (a seam: tests answer for the user).</summary>
public interface ITabulaFiles
{
    /// <summary>The chosen path, or null when the user cancels.</summary>
    Task<string?> SaveAsync(Control owner, TabulaSaveRequest request);

    /// <summary>The chosen files (empty when the user cancels).</summary>
    Task<IReadOnlyList<string>> OpenAsync(Control owner, string title, bool multiple);
}

/// <summary>The clipboard (a seam: tests read what was copied).</summary>
public interface ITabulaClipboard
{
    Task<bool> SetTextAsync(Control owner, string text);

    Task<bool> SetImageAsync(Control owner, byte[] png);
}

/// <summary>The app's dialogs and clipboard, through the window's storage provider and clipboard.</summary>
public sealed class TabulaSystem : ITabulaFiles, ITabulaClipboard
{
    public static TabulaSystem Instance { get; } = new();

    public async Task<string?> SaveAsync(Control owner, TabulaSaveRequest request)
    {
        if (TopLevel.GetTopLevel(owner)?.StorageProvider is not { CanSave: true } storage) return null;
        IStorageFolder? start = null;
        if (request.Folder is { } folder && Directory.Exists(folder)) start = await storage.TryGetFolderFromPathAsync(folder);
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = request.Title,
            SuggestedFileName = request.FileName,
            SuggestedStartLocation = start,
            DefaultExtension = request.IsFolder ? null : request.Extension,
            ShowOverwritePrompt = !request.IsFolder,
            FileTypeChoices = request.IsFolder ? null : [new FilePickerFileType(request.TypeName) { Patterns = ["*." + request.Extension] }],
        });
        return file?.TryGetLocalPath();
    }

    public async Task<IReadOnlyList<string>> OpenAsync(Control owner, string title, bool multiple)
    {
        if (TopLevel.GetTopLevel(owner)?.StorageProvider is not { CanOpen: true } storage) return [];
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = multiple });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<bool> SetTextAsync(Control owner, string text)
    {
        if (TopLevel.GetTopLevel(owner)?.Clipboard is not { } clipboard) return false;
        await clipboard.SetTextAsync(text);
        return true;
    }

    public async Task<bool> SetImageAsync(Control owner, byte[] png)
    {
        if (TopLevel.GetTopLevel(owner)?.Clipboard is not { } clipboard) return false;
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        await clipboard.SetBitmapAsync(bitmap);
        return true;
    }
}

/// <summary>Where a thread's Tabula pane is, for the store's events: its state and how to open it.</summary>
public sealed record TabulaTarget(TabulaState State, Action OpenPane);

/// <summary>Everything one Tabula pane reaches: its thread, the store, the previews' runtime, the dialogs and the chat.</summary>
public sealed class TabulaContext
{
    public required Guid Thread { get; init; }

    public required ArtifactStore Store { get; init; }

    public required TabulaState State { get; init; }

    /// <summary>The artifacts being written now (the skeleton); none when null.</summary>
    public ArtifactStreams? Streams { get; init; }

    /// <summary>The sandboxed previews' runtime; without it, web kinds show their code.</summary>
    public ArtifactRuntime? Runtime { get; init; }

    /// <summary>The web surface the previews open in (null shows code).</summary>
    public Func<WebSurface?> Surface { get; init; } = static () => null;

    /// <summary>The live web control of a host (tests draw a plain border; null is WebView2).</summary>
    public Func<IWebViewSurface, Control>? LiveFactory { get; init; }

    /// <summary>The chats of this thread's project, this one included (the gallery's "This project").</summary>
    public Func<IReadOnlyList<Guid>> ProjectThreads { get; init; } = static () => [];

    /// <summary>Every chat that has artifacts (the gallery's "All threads").</summary>
    public Func<IReadOnlyList<Guid>>? AllThreads { get; init; }

    /// <summary>A chat's name for the gallery's cards.</summary>
    public Func<Guid, string?> ThreadName { get; init; } = static _ => null;

    /// <summary>The folder "Save to project" starts in (the thread's worktree or project); null when it has none.</summary>
    public Func<string?> ProjectFolder { get; init; } = static () => null;

    /// <summary>Posts a turn in the thread (Ask to change, Ask to fix).</summary>
    public Action<string> SendTurn { get; init; } = static _ => { };

    /// <summary>Opens the version in a Visus tab on its own site; null until Visus binds it (the pane expands instead).</summary>
    public Action<ArtifactSnapshot>? OpenInVisus { get; init; }

    /// <summary>Expands the pane (Open in Visus before Visus exists).</summary>
    public Action Expand { get; init; } = static () => { };

    /// <summary>Puts text in the thread's composer (nth.send).</summary>
    public Action<string> Draft { get; init; } = static _ => { };

    /// <summary>Opens an address the artifact linked to, once the user agrees.</summary>
    public Action<string> OpenLink { get; init; } = static url =>
    {
        if (SwiftURL.Create(url) is { } address) Marrow.Services.Port.Workspace.Open.Current(address);
    };

    public ITabulaFiles Files { get; init; } = TabulaSystem.Instance;

    public ITabulaClipboard Clipboard { get; init; } = TabulaSystem.Instance;

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Runs work on the UI thread (the store's events arrive on others).</summary>
    public Action<Action> Post { get; init; } = MainActor.Post;
}

/// <summary>The Tabula pane's registration, its state per thread, and the store's events that open it.</summary>
public static class TabulaPane
{
    private static readonly ConditionalWeakTable<ThreadRuntime, TabulaState> States = [];
    private static IDisposable? binding;

    /// <summary>The web surface previews open in; the app enables one at start-up (AppWebSurface), tests give a fake.</summary>
    public static Func<WebSurface?> Surface { get; set; } = static () => Marrow.App.Port.Web.AppWebSurface.Current;

    /// <summary>The live web control of a host; null is WebView2 (tests draw a plain border).</summary>
    public static Func<IWebViewSurface, Control>? LiveFactory { get; set; }

    public static ITabulaFiles Files { get; set; } = TabulaSystem.Instance;

    public static ITabulaClipboard Clipboard { get; set; } = TabulaSystem.Instance;

    /// <summary>
    /// "Open in Visus" (6.3): Visus (W8a) binds this to open the version in a tab on its sandbox site, full size. Until then
    /// the Tabula pane expands, which is the largest view nth has of it.
    /// </summary>
    public static Action<ThreadRuntime, ArtifactSnapshot>? OpenInVisus { get; set; }

    /// <summary>Registers the pane's content with the workspace and opens it on the store's events (called once at start-up).</summary>
    public static void Register()
    {
        PaneContents.Register(PaneKind.Artifacts, Create);
        // The bridge's cards, file sheet, draft and theme are drawn by the pane that shows the asking artifact (6.5).
        if (ArtifactRuntime.Shared is { } runtime) runtime.Host = TabulaBridgeHost.Instance;
        if (binding is not null) return;
        var host = ArtifactHost.Shared.Current;
        if (!host.IsOn) return;
        binding = Bind(host, Target, MainActor.Post);
    }

    /// <summary>The Tabula state of a thread (loaded from tabula.json the first time).</summary>
    public static TabulaState StateOf(ThreadRuntime runtime) => States.GetValue(runtime, r => TabulaState.Load(r.ThreadID, StateFile(r.ThreadID)));

    /// <summary>Where a thread's choice is saved: beside its artifacts folder, in the thread's data folder.</summary>
    public static string? StateFile(Guid thread) =>
        ArtifactHost.Shared.Current.Store is { } store && Path.GetDirectoryName(store.ArtifactsFolder(thread)) is { } folder
            ? Path.Combine(folder, TabulaState.FileName) : null;

    /// <summary>Opens the thread's Tabula pane on an artifact (a chip in the chat, "Open in Tabula").</summary>
    public static void Show(ThreadRuntime runtime, string id, int? version = null)
    {
        StateOf(runtime).Select(new TabulaRef(runtime.ThreadID, id), version);
        runtime.Workspace = runtime.Workspace.Open(PaneKind.Artifacts);
    }

    /// <summary>The pane's content for a thread, or null when artifacts are off or the runtime has no app (a test runtime).</summary>
    public static PaneContent? Create(PaneKind kind, ThreadRuntime runtime)
    {
        var host = ArtifactHost.Shared.Current;
        if (host.Store is not { } store || runtime.App is not { } app) return null;
        var thread = runtime.ThreadID;
        var context = new TabulaContext
        {
            Thread = thread,
            Store = store,
            State = StateOf(runtime),
            Streams = host.Streams,
            Runtime = ArtifactRuntime.Shared,
            Surface = Surface,
            LiveFactory = LiveFactory,
            ProjectThreads = () => app.Thread(thread) is { } chat
                ? app.Threads.Where(t => t.ProjectID == chat.ProjectID).Select(t => t.Id).Append(thread).Distinct().ToList()
                : [thread],
            AllThreads = () => store.Threads(),
            ThreadName = id => app.Thread(id)?.Title,
            ProjectFolder = () => app.Thread(thread) is { } chat ? chat.WorktreePath ?? app.Project(chat.ProjectID)?.Path : null,
            SendTurn = runtime.SendArtifactTurn,
            OpenInVisus = OpenInVisus is { } open ? snapshot => open(runtime, snapshot) : null,
            Expand = () => runtime.Workspace = runtime.Workspace.Expand(PaneKind.Artifacts),
            Draft = runtime.DraftFromArtifact,
            Files = Files,
            Clipboard = Clipboard,
        };
        return Create(context);
    }

    /// <summary>The pane's content over any context (tests use this).</summary>
    public static PaneContent Create(TabulaContext context) => new TabulaView(context).Content;

    private static TabulaTarget? Target(Guid thread)
    {
        if (Marrow.App.Window.WindowManager.Shared.Model.ExistingRuntime(thread) is not { } runtime) return null;
        return new TabulaTarget(StateOf(runtime), () => runtime.Workspace = runtime.Workspace.Open(PaneKind.Artifacts));
    }

    /// <summary>
    /// Opens a thread's pane on the store's events (6.2 "opens the Tabula pane"; 6.4 "The pane opens the moment
    /// artifact_create starts streaming"): a new artifact is selected and shown, a create stream opens the pane on its
    /// skeleton, a chip's request selects its artifact, and a removed artifact is no longer selected. An update never
    /// opens a closed pane. The events are handled on the UI thread through <paramref name="post"/>.
    /// </summary>
    public static IDisposable Bind(ArtifactHost host, Func<Guid, TabulaTarget?> resolve, Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(host);
        void OnChanged(ArtifactChange change) => post(() => Route(host, resolve, change));
        void OnOpen(Guid thread, string id) => post(() =>
        {
            if (resolve(thread) is not { } target) return;
            target.State.Select(new TabulaRef(thread, id));
            target.OpenPane();
        });
        void OnStream(ArtifactStream stream)
        {
            if (stream.IsUpdate || stream.Key.StartsWith(TabulaView.AskStreamPrefix, StringComparison.Ordinal)) return;
            post(() =>
            {
                if (resolve(stream.Thread) is not { } target) return;
                if (target.State.IsGalleryOpen) target.State.OpenGallery(false);
                target.OpenPane();
            });
        }

        if (host.Store is { } store) store.Changed += OnChanged;
        host.OpenRequested += OnOpen;
        host.Streams.Changed += OnStream;
        return new Unbind(() =>
        {
            if (host.Store is { } bound) bound.Changed -= OnChanged;
            host.OpenRequested -= OnOpen;
            host.Streams.Changed -= OnStream;
        });
    }

    private static void Route(ArtifactHost host, Func<Guid, TabulaTarget?> resolve, ArtifactChange change)
    {
        switch (change.Change)
        {
            case ArtifactChangeKind.Created:
                // The stream that wrote it is done: a tool call's (named by nothing but its call) or the block's.
                host.Streams.EndAll(change.Thread, s => s.Key.StartsWith(ThreadRuntime.ToolStreamPrefix, StringComparison.Ordinal) || s.Id == change.Id);
                if (resolve(change.Thread) is not { } created) return;
                created.State.Select(new TabulaRef(change.Thread, change.Id));
                created.OpenPane();
                break;
            case ArtifactChangeKind.Removed:
                if (resolve(change.Thread) is { } removed && removed.State.Selected == new TabulaRef(change.Thread, change.Id))
                    removed.State.Select(null);
                break;
        }
    }

    // ---- the turns the pane posts ----

    /// <summary>
    /// The turn "Ask to change" posts (6.3): the artifact by name, id and version, the change, the part chosen, and how to
    /// answer, so any model (tools or the fenced block) writes a new version of the same artifact.
    /// </summary>
    public static string AskMessage(Artifact artifact, int version, string change, string? selection)
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"Change the Tabula artifact \"{artifact.Title}\" (id {artifact.Id}, v{version}, {TabulaTokens.KindName(artifact)}): {change.Trim()}"),
        };
        if (!string.IsNullOrWhiteSpace(selection))
        {
            lines.Add("");
            lines.Add("Change this part:");
            lines.AddRange(Quote(selection));
        }
        lines.Add("");
        lines.Add(version == artifact.Current
            ? "Read it with artifact_read if you need to, then write the change with artifact_update on the same id."
            : string.Create(CultureInfo.InvariantCulture, $"Start from v{version} (artifact_read with version {version}), then write the change with artifact_update on the same id."));
        return string.Join("\n", lines);
    }

    /// <summary>The turn "Ask to fix" posts (6.3): the error and its stack.</summary>
    public static string FixMessage(Artifact artifact, int version, ArtifactRuntimeError error)
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"The Tabula artifact \"{artifact.Title}\" (id {artifact.Id}, v{version}) hit an error: {error.Message.Trim()}"),
        };
        if (!string.IsNullOrWhiteSpace(error.Stack))
        {
            lines.Add("");
            lines.Add("```");
            lines.Add(error.Stack.Trim().Replace("```", "` ` `", StringComparison.Ordinal));
            lines.Add("```");
        }
        lines.Add("");
        lines.Add("Fix it with artifact_update on the same id.");
        return string.Join("\n", lines);
    }

    private static IEnumerable<string> Quote(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length > 2000) trimmed = trimmed[..2000] + "…";
        return trimmed.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => line.Length == 0 ? ">" : "> " + line);
    }

    private sealed class Unbind(Action undo) : IDisposable
    {
        private Action? pending = undo;

        public void Dispose() => Interlocked.Exchange(ref pending, null)?.Invoke();
    }
}
