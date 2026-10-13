// PORT-ONLY: web surfaces W19, one MCP App in a thread (DESIGN.md 7.6 "Display modes"; ED-NEW-web-w19-3, -4). No Swift
// counterpart. A tool call that draws an app has one session for as long as the process runs: one sandboxed view, one mount,
// one host. The host lives in exactly one place at a time: the card in the thread (Inline), the thread's App pane (Expanded;
// the spec's fullscreen also fills the workspace with it) or a small floating window (Pop out, the spec's pip). Moving it
// moves the same host; the card in the thread shows where it went and brings it back. The app is told each move and its
// new container through host-context-changed. The rows of a thread come and go (a finished turn draws its block again), so
// the card asks the registry for the session rather than owning it.
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Web;

namespace Marrow.App.Port.Web.McpApps;

public sealed class McpAppSession
{
    /// <summary>The inline height before the app says how tall it is.</summary>
    public const double DefaultInlineHeight = 320;

    /// <summary>The smallest inline height (DESIGN 7.6: the app scrolls inside beyond 60 % of the thread).</summary>
    public const double MinimumInlineHeight = 80;

    private readonly Decorator? noSlot = null;
    private string fedInput = "";
    private McpAppToolStage fed = McpAppToolStage.None;
    private Decorator? inlineSlot;
    private Decorator? paneSlot;
    private McpAppWindow? window;
    private bool closed;

    internal McpAppSession(ThreadRuntime runtime, string key, McpAppToolEntry app, McpAppsHost host, WebSurface surface)
    {
        Runtime = runtime;
        Key = key;
        App = app;
        AppsHost = host;
        View = surface.Open(WebSurfaceKind.McpApp, "main");
        Host = WebSurfaces.Host(View);
        Mount = host.Mount(View, new McpAppLaunch(runtime.ThreadID, app.Server, app.ResourceUri)
        {
            ToolName = app.Tool,
            Title = app.Title,
            Tool = app.Definition?.DeepClone() as JsonObject ?? new JsonObject { ["name"] = app.Tool },
            ToolCallId = key,
        });
        Mount.DisplayModeHandler = wanted =>
        {
            SetMode(wanted, fill: wanted == McpAppDisplayMode.Pane);
            return Mode;
        };
        Mount.SizeRequested += size =>
        {
            if (size.Height is { } height) RequestedHeight = height;
            Changed?.Invoke();
        };
        Mount.Failed += _ => Changed?.Invoke();
        Mount.Loaded += () => Changed?.Invoke();
        // The App pane closed from its own header (or the workspace dropped it): the app comes back inline.
        runtime.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ThreadRuntime.Workspace)) return;
            MainActor.Post(() =>
            {
                if (!closed && Mode == McpAppDisplayMode.Pane && !Runtime.Workspace.IsOpen(PaneKind.App)) SetMode(McpAppDisplayMode.Inline);
            });
        };
    }

    internal McpAppsHost AppsHost { get; }

    public ThreadRuntime Runtime { get; }

    /// <summary>The tool call's entry id in the thread.</summary>
    public string Key { get; }

    public McpAppToolEntry App { get; }

    public string Server => App.Server;

    /// <summary>What the card and the placeholder call the app: the tool's title ("Release board"), else its name.</summary>
    public string Title => App.Title is { Length: > 0 } title ? title : App.Tool;

    public WebSurfaceView View { get; }

    public McpAppMount Mount { get; }

    public WebViewHost Host { get; }

    public McpAppDisplayMode Mode { get; private set; } = McpAppDisplayMode.Inline;

    /// <summary>The height the app asked for (size-changed), before the thread clamps it.</summary>
    public double? RequestedHeight { get; private set; }

    /// <summary>The pop-out window while the app is in one.</summary>
    public McpAppWindow? Window => window;

    /// <summary>The mode, the size the app asked for, its load or its failure changed.</summary>
    public event Action? Changed;

    /// <summary>The words of the card's placeholder while the app is elsewhere, or null inline.</summary>
    public string? PlaceholderText => Mode switch
    {
        McpAppDisplayMode.Pane => UiText.Format("mcpapps.session.in_pane", Title),
        McpAppDisplayMode.PopOut => UiText.Format("mcpapps.session.in_window", Title),
        _ => null,
    };

    public static string BringBackText => UiText.Get("mcpapps.session.bring_back");

    // ---- the tool call ----

    /// <summary>Tells the app what its call says now: arguments as they stream, the full input, the result or the cancel.</summary>
    public void Feed(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var (stage, arguments) = McpAppToolData.Read(call);
        switch (stage)
        {
            case McpAppToolStage.Partial when fed <= McpAppToolStage.Partial && arguments is not null:
                var text = arguments.ToJsonString();
                if (text == fedInput) return;
                fedInput = text;
                fed = stage;
                Mount.SetToolInputPartial(arguments);
                return;
            case McpAppToolStage.Input when fed < McpAppToolStage.Input:
                fed = stage;
                Mount.SetToolInput(arguments ?? []);
                return;
            case McpAppToolStage.Result when fed < McpAppToolStage.Result:
                if (fed < McpAppToolStage.Input && arguments is not null) Mount.SetToolInput(arguments);
                fed = stage;
                Mount.SetToolResult(McpAppToolData.Result(call));
                return;
            case McpAppToolStage.Cancelled when fed < McpAppToolStage.Result:
                fed = stage;
                Mount.SetToolCancelled(McpAppToolData.CancelReason(call));
                return;
        }
    }

    // ---- where it is ----

    /// <summary>The card in the thread offers its slot (a new row's card replaces an old one's).</summary>
    internal void AttachInline(Decorator slot)
    {
        inlineSlot = slot;
        Place();
    }

    /// <summary>A card left the thread; nothing when a newer card already holds the slot.</summary>
    internal void DetachInline(Decorator slot)
    {
        if (inlineSlot != slot) return;
        if (Host.Parent == slot) TakeOut(slot);
        inlineSlot = null;
        Place();
    }

    /// <summary>The App pane offers its slot.</summary>
    internal void AttachPane(Decorator slot)
    {
        paneSlot = slot;
        Place();
    }

    /// <summary>The App pane's body left the window (closed, or another thread shown).</summary>
    internal void DetachPane(Decorator slot)
    {
        if (paneSlot != slot) return;
        if (Host.Parent == slot) TakeOut(slot);
        paneSlot = null;
        Place();
    }

    /// <summary>The card's slot, while a card shows this app.</summary>
    internal Decorator? InlineSlot => inlineSlot;

    /// <summary>Moves the app: Inline, Expanded (the App pane; <paramref name="fill"/> fills the workspace) or Pop out.</summary>
    public void SetMode(McpAppDisplayMode mode, bool fill = false)
    {
        if (mode == Mode)
        {
            if (mode == McpAppDisplayMode.Pane) OpenPane(fill);
            else if (mode == McpAppDisplayMode.PopOut) window?.Activate();
            return;
        }

        var from = Mode;
        Mode = mode;
        Leave(from, closePane: mode != McpAppDisplayMode.Pane);
        switch (mode)
        {
            case McpAppDisplayMode.Pane:
                // One App pane per thread: the app it showed goes back to its card, and the pane stays open for this one.
                if (McpAppSessions.Expanded(Runtime.ThreadID) is { } other && other != this) other.ReturnInline();
                McpAppSessions.SetExpanded(Runtime.ThreadID, this);
                OpenPane(fill);
                break;
            case McpAppDisplayMode.PopOut:
                window = WindowFactory(this);
                window.ShowNear(inlineSlot);
                break;
        }

        Place();
        Changed?.Invoke();
    }

    /// <summary>Makes the pop-out window (tests count and skip the real show).</summary>
    internal static Func<McpAppSession, McpAppWindow> WindowFactory { get; set; } = static session => new McpAppWindow(session);

    // Another app takes the thread's App pane: this one goes back to its card without closing the pane.
    private void ReturnInline()
    {
        var from = Mode;
        Mode = McpAppDisplayMode.Inline;
        Leave(from, closePane: false);
        Place();
        Changed?.Invoke();
    }

    private void Leave(McpAppDisplayMode from, bool closePane)
    {
        if (from == McpAppDisplayMode.PopOut && window is { } open)
        {
            window = null;
            open.CloseQuietly();
        }

        if (from != McpAppDisplayMode.Pane) return;
        if (paneSlot is { } slot && Host.Parent == slot) TakeOut(slot);
        if (McpAppSessions.Expanded(Runtime.ThreadID) != this) return;
        McpAppSessions.SetExpanded(Runtime.ThreadID, null);
        if (closePane && Runtime.Workspace.IsOpen(PaneKind.App)) Runtime.Workspace = Runtime.Workspace.Close(PaneKind.App);
    }

    private void OpenPane(bool fill)
    {
        var workspace = Runtime.Workspace.Open(PaneKind.App);
        if (fill) workspace = workspace.Expand(PaneKind.App);
        if (workspace != Runtime.Workspace) Runtime.Workspace = workspace;
    }

    /// <summary>The window closed by itself (its close button, Alt+F4): the app comes back inline.</summary>
    internal void WindowClosed(McpAppWindow closed)
    {
        if (window != closed) return;
        window = null;
        SetMode(McpAppDisplayMode.Inline);
    }

    /// <summary>
    /// Takes the page out of a slot. Leaving a window queues the page's controls with that window's layout manager, and a page
    /// that is then put into another window (the card, the App pane and the pop-out window are different roots) makes the first
    /// window's next pass throw ("wrong LayoutManager"). So the window runs its pass once the page is out and before it goes in
    /// elsewhere: the pass drops what is no longer inside it, and nothing of the page is left queued behind.
    /// </summary>
    internal static void TakeOut(Decorator slot)
    {
        if (slot.Child is null) return;
        var top = TopLevel.GetTopLevel(slot);
        slot.Child = null;
        top?.UpdateLayout();
    }

    /// <summary>Puts the host where the mode says, and tells the app its container.</summary>
    internal void Place()
    {
        var target = Mode switch
        {
            McpAppDisplayMode.Inline => inlineSlot,
            McpAppDisplayMode.Pane => paneSlot,
            McpAppDisplayMode.PopOut => window?.Slot,
            _ => noSlot,
        };
        if (Host.Parent is Decorator current && current != target) TakeOut(current);
        if (target is not null && target.Child != Host)
        {
            target.Child = null;
            target.Child = Host;
        }

        View.SetShown(target is not null);
        UpdateContext(target);
    }

    /// <summary>The container changed size (the card's width, the pane, the window, the thread's height).</summary>
    internal void UpdateContext(Control? container = null, double? threadHeight = null)
    {
        container ??= Host.Parent as Control;
        var size = container?.Bounds.Size ?? default;
        Mount.UpdateContext(c => Mode switch
        {
            McpAppDisplayMode.Inline => c with
            {
                DisplayMode = Mode,
                Width = null,
                Height = null,
                MaxWidth = size.Width > 0 ? Math.Round(size.Width) : c.MaxWidth,
                MaxHeight = threadHeight is { } thread && thread > 0 ? Math.Round(thread * InlineWebLive.MaxThreadShare) : c.MaxHeight,
            },
            _ => c with
            {
                DisplayMode = Mode,
                Width = size.Width > 0 ? Math.Round(size.Width) : c.Width,
                Height = size.Height > 0 ? Math.Round(size.Height) : c.Height,
                MaxWidth = null,
                MaxHeight = null,
            },
        });
    }

    /// <summary>The inline height for a thread this tall.</summary>
    public double InlineHeight(double threadHeight) =>
        InlineWebLive.Height(RequestedHeight, threadHeight > 0 ? threadHeight : 900, DefaultInlineHeight, MinimumInlineHeight);

    /// <summary>The pop-out window's size for this server (the last one the user gave it), or the default.</summary>
    internal (double Width, double Height) WindowSize =>
        AppsHost.Grants.Get(Server).WindowSize is { } size ? size : (McpAppWindow.DefaultWidth, McpAppWindow.DefaultHeight);

    /// <summary>The user resized the pop-out window: the next one for this server opens at that size.</summary>
    internal void RememberWindowSize(double width, double height) =>
        AppsHost.Grants.Update(Server, g => g with { WindowSize = (Math.Round(width), Math.Round(height)) });

    /// <summary>Closes the app: it is asked to save its state, then its view goes.</summary>
    public async Task CloseAsync()
    {
        if (closed) return;
        closed = true;
        if (Mode != McpAppDisplayMode.Inline) SetMode(McpAppDisplayMode.Inline);
        McpAppHostUi.Withdraw(Mount);
        await Mount.TeardownAsync();
        Mount.Dispose();
        if (Host.Parent is Decorator parent) TakeOut(parent);
        View.SetShown(false);
        WebSurfaces.Shared.Close(View);
    }
}

/// <summary>The sessions of every thread, and which one each thread's App pane shows.</summary>
public static class McpAppSessions
{
    private static readonly Dictionary<(Guid Thread, string Key), McpAppSession> Sessions = [];
    private static readonly Dictionary<Guid, McpAppSession> ExpandedByThread = [];

    /// <summary>The host the sessions mount in (bound at start; tests set their own).</summary>
    public static Func<McpAppsHost?> HostSource { get; set; } = static () => McpAppsHost.Shared;

    /// <summary>The thread's App pane changed what it shows.</summary>
    public static event Action<Guid>? ExpandedChanged;

    /// <summary>The session of a tool call, made on first use; null when the MCP Apps host is not bound.</summary>
    public static McpAppSession? For(ThreadRuntime runtime, string key, McpAppToolEntry app)
    {
        if (Sessions.TryGetValue((runtime.ThreadID, key), out var session)) return session;
        if (HostSource() is not { } host) return null;
        session = new McpAppSession(runtime, key, app, host, WebSurfaces.Shared);
        Sessions[(runtime.ThreadID, key)] = session;
        return session;
    }

    public static McpAppSession? Find(Guid thread, string key) => Sessions.GetValueOrDefault((thread, key));

    public static IReadOnlyList<McpAppSession> In(Guid thread) => [.. Sessions.Where(p => p.Key.Thread == thread).Select(p => p.Value)];

    /// <summary>Every session of every thread (tests close their windows).</summary>
    internal static IReadOnlyList<McpAppSession> All() => [.. Sessions.Values];

    public static McpAppSession? Expanded(Guid thread) => ExpandedByThread.GetValueOrDefault(thread);

    internal static void SetExpanded(Guid thread, McpAppSession? session)
    {
        if (session is null) ExpandedByThread.Remove(thread);
        else ExpandedByThread[thread] = session;
        ExpandedChanged?.Invoke(thread);
    }

    /// <summary>Tests: forgets every session.</summary>
    internal static void Reset()
    {
        Sessions.Clear();
        ExpandedByThread.Clear();
    }
}
