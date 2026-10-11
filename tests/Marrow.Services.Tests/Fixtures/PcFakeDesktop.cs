using System.Globalization;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-6, ED-NEW-pcctl-7, ED-NEW-pcctl-8). A whole fake desktop for the engine's
// tests: windows with mutable accessibility trees that react to UIA patterns and posted messages the way real apps do (a
// toggle flips, a list loads more rows when scrolled, a dialog opens late, a menu command runs), change events delivered a
// few milliseconds after each change, the user's side of the PC (foreground, pointer, focus, keys, last input), and a clock
// that only moves when the engine waits. Nothing here touches Windows.
public sealed class PcFakeNode
{
    public PcFakeNode(string runtimeId, string role, string name)
    {
        RuntimeId = runtimeId;
        Role = role;
        Name = name;
    }

    public string RuntimeId { get; set; }
    public string Role { get; set; }
    public string Name { get; set; }
    public string? AutomationId { get; set; }
    public string? Value { get; set; }
    public string? Shortcut { get; set; }
    public string? AccessKey { get; set; }
    public string? ClassName { get; set; }
    public string? Framework { get; set; }
    public string? DefaultAction { get; set; }
    public nint NativeWindow { get; set; }
    public PcStates States { get; set; }
    public PcActions Actions { get; set; }
    public PcRect Bounds { get; set; }
    public double? ScrollPercent { get; set; }
    public List<PcFakeNode> Children { get; } = [];

    /// <summary>What an Invoke (or a posted click, BM_CLICK or Enter on it) does: open a dialog, change a label.</summary>
    public Action<PcFakeDesktop, PcFakeNode>? OnInvoke { get; set; }

    /// <summary>What a Scroll does (load more rows, move the position).</summary>
    public Action<PcFakeDesktop, PcFakeNode, PcElementCommand>? OnScroll { get; set; }

    /// <summary>How the control stores a value it is given (some reformat it).</summary>
    public Func<string, string>? Accepts { get; set; }

    /// <summary>The pattern call answers but the app does nothing (a control that ignores UIA).</summary>
    public bool Ignores { get; set; }

    /// <summary>An Invoke that opens a modal dialog and does not return for a while (UiaReader answers still running).</summary>
    public bool Modal { get; set; }

    public PcFakeNode Add(PcFakeNode child)
    {
        Children.Add(child);
        return child;
    }

    public PcUiNode Snapshot(bool withChildren) => new(RuntimeId, Role, Name)
    {
        AutomationId = AutomationId,
        Value = States.HasFlag(PcStates.Password) ? null : Value,
        Shortcut = Shortcut,
        AccessKey = AccessKey,
        ClassName = ClassName,
        Framework = Framework,
        DefaultAction = DefaultAction,
        NativeWindow = NativeWindow,
        States = States,
        Actions = Actions,
        Bounds = Bounds,
        ScrollPercent = ScrollPercent,
        Children = withChildren ? Children.Select(c => c.Snapshot(true)).ToList() : [],
    };

    public IEnumerable<PcFakeNode> All() => new[] { this }.Concat(Children.SelectMany(c => c.All()));
}

public sealed class PcFakeApp
{
    public PcFakeApp(nint handle, string title, string exe, int processId, PcRect frame)
    {
        Handle = handle;
        Title = title;
        Exe = exe;
        ProcessId = processId;
        Frame = frame;
        Root = new PcFakeNode("w" + ((long)handle).ToString(CultureInfo.InvariantCulture), "window", title)
        {
            Actions = PcActions.Window,
            Bounds = frame,
        };
    }

    public nint Handle { get; }
    public string Title { get; set; }
    public string Exe { get; }
    public int ProcessId { get; }
    public PcRect Frame { get; set; }
    public bool Minimized { get; set; }
    public string ClassName { get; set; } = "FakeWindow";
    public long StartTime { get; set; } = 1000;
    public string Version { get; set; } = "1.0";
    public PcFakeNode Root { get; }

    /// <summary>The control posted keys and characters go to (the thread's focus).</summary>
    public PcFakeNode? FocusControl { get; set; }

    /// <summary>While set, the app does not answer pattern calls (a hung app) until the test completes it.</summary>
    public TaskCompletionSource? Hang { get; set; }

    /// <summary>The window's menu: the shortcut its item shows, the item, and what its command does.</summary>
    public List<(string Keys, PcMenuItem Item, Action<PcFakeDesktop> Run)> Menu { get; } = [];

    public PcWindowInfo Info => new(Handle, Title, ClassName, ProcessId, "C:\\Apps\\" + Exe)
    {
        Frame = Minimized ? new PcRect(-32000, -32000, 160, 28) : Frame,
        Minimized = Minimized,
        Monitor = 1,
        StartTime = StartTime,
        Version = Version,
    };

    public PcFakeNode? Find(string runtimeId) => Root.All().FirstOrDefault(n => n.RuntimeId == runtimeId);

    public PcFakeNode? Native(nint control) => control == Handle ? FocusControl : Root.All().FirstOrDefault(n => n.NativeWindow == control && n.NativeWindow != 0);
}

public sealed class PcFakeDesktop : IWindowCatalog, IUiaReader, IPcChangeFeed, IPcMessageSink, ISecurityProbe, IOwnWindows, IAppLauncher, IForeground
{
    /// <summary>How long after a change its event arrives (ms).</summary>
    public const int EventLatencyMs = 15;

    private readonly Lock gate = new();
    private readonly Dictionary<nint, long> versions = [];
    private readonly Dictionary<nint, long> structures = [];
    private readonly List<(long At, long Order, Action Run)> scheduled = [];
    private long order;

    public PcFakeDesktop()
    {
        Clock.OnDelay = _ => RunDue();
    }

    public PcFakeClock Clock { get; } = new();
    public List<PcFakeApp> Apps { get; } = [];
    public List<PcMonitor> Screens { get; } = [new PcMonitor(1, new PcRect(0, 0, 2560, 1440), new PcRect(0, 0, 2560, 1400), 144, true)];
    public List<string> Log { get; } = [];
    public List<PcRect> OwnRects { get; } = [];
    public HashSet<nint> OwnHandles { get; } = [];
    public HashSet<nint> Watched { get; } = [];
    public bool Secure { get; set; }
    public bool IsLocked { get; set; }
    public HashSet<int> ElevatedProcesses { get; } = [];
    public HashSet<nint> AnsiControls { get; } = [];
    public int TreeReads { get; private set; }
    public int ElementReads { get; private set; }
    public int Performs { get; private set; }
    public int Messages { get; private set; }

    // ----- the user's side -----

    public nint ForegroundWindow { get; set; } = 0x77;
    public PcPoint Cursor { get; set; } = new(5, 5);
    public nint FocusHandle { get; set; }
    public bool ModifiersDown { get; set; }

    /// <summary>When the user last pressed a key or moved the mouse (fake clock ms); long ago by default.</summary>
    public long LastInputAt { get; set; } = -1_000_000;

    /// <summary>The user keeps typing: every read sees input just now.</summary>
    public bool KeepTyping { get; set; }

    /// <summary>Runs before each pattern call (a test stops Tactus here, or changes the app).</summary>
    public Action<PcFakeApp, PcFakeNode, PcElementCommand>? BeforePerform { get; set; }

    /// <summary>Extra time an app takes to answer a pattern call (ms on the fake clock).</summary>
    public int PerformMs { get; set; }

    public PcFakeApp App(nint handle) => Apps.First(a => a.Handle == handle);

    public PcFakeApp AddApp(nint handle, string title, string exe, int processId, PcRect? frame = null)
    {
        var app = new PcFakeApp(handle, title, exe, processId, frame ?? new PcRect(100, 100, 1200, 800));
        List<nint> siblings;
        lock (gate)
        {
            siblings = Apps.Where(a => a.ProcessId == processId).Select(a => a.Handle).ToList();
            Apps.Add(app);
        }
        // Like the real feed: a window a process opens counts as a change in that process's other windows.
        foreach (var sibling in siblings) Notify(sibling, structure: true);
        Notify(PcChangeFeed.Desktop, structure: true);
        return app;
    }

    public void Close(nint handle)
    {
        lock (gate) Apps.RemoveAll(a => a.Handle == handle);
        Notify(handle, structure: true);
        Notify(PcChangeFeed.Desktop, structure: true);
    }

    /// <summary>Something in the window changed: its event arrives EventLatencyMs later.</summary>
    public void Notify(nint window, bool structure = false) => Schedule(EventLatencyMs, () => Bump(window, structure));

    public void Bump(nint window, bool structure = false)
    {
        lock (gate)
        {
            versions[window] = versions.GetValueOrDefault(window) + 1;
            if (structure) structures[window] = structures.GetValueOrDefault(window) + 1;
        }
    }

    public void Schedule(int delayMs, Action run)
    {
        lock (gate) scheduled.Add((Clock.NowMs + delayMs, ++order, run));
    }

    private void RunDue()
    {
        while (true)
        {
            (long At, long Order, Action Run) next;
            lock (gate)
            {
                var due = scheduled.Where(s => s.At <= Clock.NowMs).OrderBy(s => s.At).ThenBy(s => s.Order).ToList();
                if (due.Count == 0) return;
                next = due[0];
                scheduled.Remove(next);
            }
            next.Run();
        }
    }

    private void Log1(string line)
    {
        lock (Log) Log.Add(line);
    }

    // ----- IPcChangeFeed: the clock jumps to the next event, or by the whole timeout -----

    public void Watch(nint window, int processId)
    {
        lock (gate) Watched.Add(window);
    }

    public long Version(nint window)
    {
        lock (gate) return versions.GetValueOrDefault(window);
    }

    public long StructureVersion(nint window)
    {
        lock (gate) return structures.GetValueOrDefault(window);
    }

    public Task<bool> Changed(nint window, long seen, int timeoutMs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deadline = Clock.NowMs + Math.Max(0, timeoutMs);
        while (true)
        {
            if (Version(window) > seen) return Task.FromResult(true);
            (long At, long Order, Action Run)? next;
            lock (gate)
            {
                next = scheduled.Where(s => s.At <= deadline).OrderBy(s => s.At).ThenBy(s => s.Order).Cast<(long, long, Action)?>().FirstOrDefault();
                if (next is { } item) scheduled.Remove(item);
            }
            if (next is not { } run)
            {
                Clock.NowMs = Math.Max(Clock.NowMs, deadline);
                return Task.FromResult(Version(window) > seen);
            }
            Clock.NowMs = Math.Max(Clock.NowMs, run.At);
            run.Run();
        }
    }

    // ----- IWindowCatalog -----

    public IReadOnlyList<PcWindowInfo> Windows()
    {
        lock (gate) return Apps.Select(a => a.Info).ToList();
    }

    public PcWindowInfo? Window(nint handle)
    {
        lock (gate) return Apps.FirstOrDefault(a => a.Handle == handle)?.Info;
    }

    public IReadOnlyList<PcMonitor> Monitors() => Screens;

    public PcRect VirtualDesktop() => PcRect.Union(Screens.Select(s => s.Bounds));

    public nint RootAt(PcPoint point)
    {
        lock (gate) return Apps.LastOrDefault(a => !a.Minimized && a.Frame.Contains(point))?.Handle ?? 0;
    }

    public nint Foreground() => ForegroundWindow;

    public bool Activate(nint handle)
    {
        Log1("activate " + PcWindowRef.Format(handle));
        ForegroundWindow = handle;
        return true;
    }

    public bool Show(nint handle)
    {
        var app = Apps.FirstOrDefault(a => a.Handle == handle);
        if (app is null) return false;
        Log1("show " + PcWindowRef.Format(handle));
        app.Minimized = false;
        Notify(handle);
        return true;
    }

    // ----- IUiaReader -----

    public Task<PcUiNode?> Tree(nint window, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            TreeReads++;
            return Task.FromResult(Apps.FirstOrDefault(a => a.Handle == window)?.Root.Snapshot(true));
        }
    }

    public Task<PcUiNode?> Element(nint window, string runtimeId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ElementReads++;
            return Task.FromResult(Apps.FirstOrDefault(a => a.Handle == window)?.Find(runtimeId)?.Snapshot(false));
        }
    }

    public Task<PcUiNode?> Focused(nint window, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(Apps.FirstOrDefault(a => a.Handle == window)?.FocusControl?.Snapshot(false));
    }

    public Task<string?> ReadText(nint window, string runtimeId, CancellationToken cancellationToken)
    {
        var node = Apps.FirstOrDefault(a => a.Handle == window)?.Find(runtimeId);
        return Task.FromResult(node?.Value ?? (node is null ? null : string.Join('\n', node.Children.Select(c => c.Name))));
    }

    public Task<PcUiaOutcome> Perform(nint window, string runtimeId, PcElementCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hung = Apps.FirstOrDefault(a => a.Handle == window)?.Hang;
        return hung is null ? Do(window, runtimeId, command) : After(hung.Task, window, runtimeId, command, cancellationToken);
    }

    // A hung app answers only when the test lets it (or the call is cancelled).
    private async Task<PcUiaOutcome> After(Task hang, nint window, string runtimeId, PcElementCommand command, CancellationToken cancellationToken)
    {
        await hang.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await Do(window, runtimeId, command).ConfigureAwait(false);
    }

    private Task<PcUiaOutcome> Do(nint window, string runtimeId, PcElementCommand command)
    {
        var app = Apps.FirstOrDefault(a => a.Handle == window);
        var node = app?.Find(runtimeId);
        if (app is null || node is null) return Task.FromResult(PcUiaOutcome.Fail(PcErrorCode.StaleElement, "the element is gone"));
        // A stop that arrives now lands while the app is already doing the action: it finishes as one piece.
        BeforePerform?.Invoke(app, node, command);
        Performs++;
        if (PerformMs > 0) Clock.NowMs += PerformMs;
        Log1(command.Action.ToString().ToLowerInvariant() + " " + node.Name + (command.Value is null ? "" : " = " + command.Value));
        if (node.States.HasFlag(PcStates.Disabled)) return Task.FromResult(PcUiaOutcome.Fail(PcErrorCode.Disabled, "not enabled"));
        if (node.Ignores) return Task.FromResult(PcUiaOutcome.Ok);
        var structure = false;
        switch (command.Action)
        {
            case PcElementAction.Invoke:
                node.OnInvoke?.Invoke(this, node);
                structure = true;
                if (node.Modal)
                {
                    Notify(window, structure);
                    return Task.FromResult(PcUiaOutcome.Running);
                }
                break;
            case PcElementAction.Toggle:
                node.States ^= PcStates.Checked;
                break;
            case PcElementAction.Select:
                foreach (var sibling in app.Root.All().Where(n => n.Children.Contains(node)).SelectMany(p => p.Children)) sibling.States &= ~PcStates.Selected;
                node.States |= PcStates.Selected;
                break;
            case PcElementAction.Expand:
                node.States = (node.States & ~PcStates.Collapsed) | PcStates.Expanded;
                break;
            case PcElementAction.Collapse:
                node.States = (node.States & ~PcStates.Expanded) | PcStates.Collapsed;
                break;
            case PcElementAction.SetValue:
                if (node.States.HasFlag(PcStates.ReadOnly)) return Task.FromResult(PcUiaOutcome.Fail(PcErrorCode.NotSupported, "read-only"));
                node.Value = node.Accepts is { } accepts ? accepts(command.Value ?? "") : command.Value;
                break;
            case PcElementAction.Scroll:
                node.OnScroll?.Invoke(this, node, command);
                structure = true;
                break;
            case PcElementAction.Focus:
                foreach (var other in app.Root.All()) other.States &= ~PcStates.Focused;
                node.States |= PcStates.Focused;
                app.FocusControl = node;
                break;
            case PcElementAction.Close:
                Close(window);
                return Task.FromResult(PcUiaOutcome.Ok);
            case PcElementAction.Maximize:
                app.Frame = new PcRect(0, 0, 2560, 1400);
                break;
        }
        Notify(window, structure);
        return Task.FromResult(PcUiaOutcome.Ok);
    }

    // ----- IPcMessageSink -----

    private (PcFakeApp App, PcFakeNode? Node)? Control(nint control)
    {
        foreach (var app in Apps)
        {
            if (app.Handle == control || app.Root.All().Any(n => n.NativeWindow == control && control != 0)) return (app, app.Native(control));
        }
        return null;
    }

    private bool Message(nint control, string what, Action<PcFakeApp, PcFakeNode?> effect)
    {
        Messages++;
        Log1(what);
        if (Control(control) is not { } found) return false;
        effect(found.App, found.Node);
        Notify(found.App.Handle);
        return true;
    }

    public nint FocusControl(nint window)
    {
        var app = Apps.FirstOrDefault(a => a.Handle == window);
        if (app?.FocusControl is not { } focus) return 0;
        return focus.NativeWindow != 0 ? focus.NativeWindow : window;
    }

    public nint ChildAt(nint window, PcPoint screen)
    {
        var app = Apps.FirstOrDefault(a => a.Handle == window);
        var child = app?.Root.All().LastOrDefault(n => n.NativeWindow != 0 && n.Bounds.Contains(screen));
        return child?.NativeWindow ?? window;
    }

    public bool IsUnicode(nint control) => !AnsiControls.Contains(control);

    public bool ButtonClick(nint control) => Message(control, "bm_click " + Hex(control), (_, node) => node?.OnInvoke?.Invoke(this, node));

    public bool ReplaceSelection(nint control, string text, bool all) =>
        Message(control, (all ? "em_replaceall " : "em_replacesel ") + text, (_, node) =>
        {
            if (node is not null) node.Value = all ? text : (node.Value ?? "") + text;
        });

    public bool SetText(nint control, string text) => Message(control, "wm_settext " + text, (_, node) =>
    {
        if (node is not null) node.Value = text;
    });

    public bool Chars(nint control, string text) => Message(control, "wm_char " + text, (_, node) =>
    {
        if (node is not null) node.Value = (node.Value ?? "") + text;
    });

    public bool Key(nint control, ushort virtualKey, bool extended, int repeat) =>
        Message(control, "post key 0x" + virtualKey.ToString("x2", CultureInfo.InvariantCulture), (_, node) =>
        {
            if (node is not null && virtualKey == KeyChordParser.Return && node.OnInvoke is { } enter) enter(this, node);
        });

    public bool Mouse(nint control, PcPoint screen, PcMouseButton button, int count) =>
        Message(control, "post_mouse " + screen.X.ToString(CultureInfo.InvariantCulture) + "," + screen.Y.ToString(CultureInfo.InvariantCulture), (app, _) =>
        {
            var hit = app.Root.All().LastOrDefault(n => n.Bounds.Contains(screen) && n != app.Root);
            if (hit?.OnInvoke is { } invoke) invoke(this, hit);
            else if (hit is not null && hit.Actions.HasFlag(PcActions.Toggle)) hit.States ^= PcStates.Checked;
        });

    public bool Drag(nint control, IReadOnlyList<PcPoint> path) => Message(control, "post_drag " + path.Count.ToString(CultureInfo.InvariantCulture), (_, _) => { });

    public PcMenuItem? MenuItem(nint window, KeyChord chord)
    {
        var app = Apps.FirstOrDefault(a => a.Handle == window);
        if (app is null) return null;
        foreach (var (keys, item, _) in app.Menu)
        {
            if (KeyChordParser.Parse(keys).Chord is { } shown && shown.Key == chord.Key && shown.Modifiers.Order().SequenceEqual(chord.Modifiers.Order())) return item;
        }
        return null;
    }

    public bool Command(nint window, int id) => Message(window, "wm_command " + id.ToString(CultureInfo.InvariantCulture), (app, _) =>
    {
        foreach (var (_, item, run) in app.Menu.Where(m => m.Item.Id == id)) run(this);
    });

    public bool ShowNoActivate(nint window, bool minimize) => Message(window, minimize ? "sw_showminnoactive" : "sw_shownoactivate", (app, _) => app.Minimized = minimize);

    private static string Hex(nint value) => ((long)value).ToString("x", CultureInfo.InvariantCulture);

    // ----- ISecurityProbe, IOwnWindows -----

    public PcProcessSecurity Process(int processId) => new(ElevatedProcesses.Contains(processId), false, false);

    public bool SecureDesktop() => Secure;

    public bool Locked() => IsLocked;

    public IReadOnlyList<PcRect> Rects() => OwnRects;

    public bool Owns(nint handle) => OwnHandles.Contains(handle);

    public IDisposable Suppress() => new PcNothing();

    // ----- IAppLauncher -----

    public List<PcInstalledApp> InstalledApps { get; } = [];

    /// <summary>What launching starts: the app id or path, and the window it opens (after LaunchMs).</summary>
    public Func<string, PcFakeApp?>? OnLaunch { get; set; }

    public int LaunchMs { get; set; } = 400;

    public IReadOnlyList<PcInstalledApp> Installed() => InstalledApps;

    public PcLaunch Launch(string appIdOrPath)
    {
        Log1("launch " + appIdOrPath);
        if (OnLaunch is null) return new PcLaunch(false, null, "not found");
        Schedule(LaunchMs, () => OnLaunch(appIdOrPath));
        return new PcLaunch(true, null, null);
    }

    // ----- IForeground -----

    public PcUserState Read()
    {
        if (KeepTyping) LastInputAt = Clock.NowMs;
        return new PcUserState(ForegroundWindow, Cursor, FocusHandle, ModifiersDown, Clock.NowMs - LastInputAt);
    }
}

public sealed class PcNothing : IDisposable
{
    public void Dispose()
    {
    }
}
