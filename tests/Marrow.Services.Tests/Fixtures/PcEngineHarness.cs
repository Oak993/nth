using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-6, ED-NEW-pcctl-7, ED-NEW-pcctl-8, ED-NEW-pcctl-19). The engine wired to the
// fake desktop: the real lanes, run control, pause control, window holds, action ladder (R1 UI Automation, R2 messages, R4
// refusing), capability store, settle and checks, the physical input monitor (healthy, on the desktop's clock; a test plays
// the user's keys and pointer into it) and the real-input lease on a fake host; fake approval, confirmation, stop key,
// audit and capture. The 25 s budget runs on a manual clock that moves only when a test advances it. Tactus is on unless a
// test turns it off.
public sealed class PcEngineHarness
{
    public static readonly DateTimeOffset Today = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    public PcEngineHarness()
    {
        Capabilities = new PcCapabilities(() => Now);
        Input = new PhysicalInputMonitor(Desktop.Clock);
        Input.Installed(true);
        Input.LastInputSeen(Desktop.Clock.NowMs);
        RealHost = new PcFakeRealInputHost(Input, Desktop.Clock);
        Lease = new RealInputLease(Input, Hold, RealHost, Desktop.Clock, Capabilities, control: Control);
        Ladder = new PcActionLadder([new PcUiaRung(Desktop), new PcMessageRung(Desktop), new PcBorrowRung(() => Settings, Lease)], Capabilities);
        Capture = new PcCapturePipeline([CaptureSource], Encoder);
        Ownership = new PcWindowOwnership(Desktop.Clock);
        Pause = new PcPauseControl(Desktop.Clock, Control);
        Asks = new PcAskBroker(AskClock, Control);
    }

    public PcFakeDesktop Desktop { get; } = new();
    public PcRunControl Control { get; } = new();
    public PcLanes Lanes { get; } = new();
    public PcFakeApprover Approver { get; } = new();
    public PcFakeConfirmer Confirmer { get; } = new();
    public PcFakeStopKey StopKey { get; } = new();
    public PcFakeAudit Audit { get; } = new();
    public PcFakeCapture CaptureSource { get; } = new("wgc", PcFakeCapture.Solid(1200, 800, 90));
    public PcFakeEncoder Encoder { get; } = new();
    public PcManualClock WaitClock { get; } = new();
    public PcCapabilities Capabilities { get; }
    public PcActionLadder Ladder { get; }
    public PcActionLadder? OverrideLadder { get; set; }
    public RealInputLease? OverrideLease { get; set; }

    /// <summary>The physical input monitor: healthy, with no input until a test plays the user's.</summary>
    public PhysicalInputMonitor Input { get; }

    public PcFakeInputHold Hold { get; } = new();
    public PcFakeRealInputHost RealHost { get; }
    public RealInputLease Lease { get; }
    public PcCapturePipeline Capture { get; }
    public PcWindowOwnership Ownership { get; }
    public PcPauseControl Pause { get; }

    /// <summary>The ask broker, on its own clock so a question's 30 minutes pass only when a test advances it.</summary>
    public PcAskBroker Asks { get; }

    public PcManualClock AskClock { get; } = new();

    /// <summary>Where each action lands, for the Tactus pointers.</summary>
    public PcPointerFeed Pointer { get; } = new();

    /// <summary>The writes waiting while the user types (the pill's "Waiting while you type").</summary>
    public PcTypingWaits Typing { get; } = new();
    public PcSettings Settings { get; set; } = PcSettings.Default with { Enabled = true };
    public DateTimeOffset Now { get; set; } = Today;

    public PcEngineParts Parts => new()
    {
        Catalog = Desktop,
        Security = Desktop,
        Reader = Desktop,
        Feed = Desktop,
        Ladder = OverrideLadder ?? Ladder,
        Foreground = Desktop,
        Input = Input,
        Lease = OverrideLease ?? Lease,
        Own = Desktop,
        Approver = Approver,
        Confirmer = Confirmer,
        StopKey = StopKey,
        Control = Control,
        Pause = Pause,
        Asks = Asks,
        Audit = Audit,
        Clock = Desktop.Clock,
        Launcher = Desktop,
        Lanes = Lanes,
        Ownership = Ownership,
        Settings = () => Settings,
        Capture = Capture,
        Pointer = Pointer,
        Typing = Typing,
        WaitClock = WaitClock,
        Now = () => Now,
    };

    public PcControlEngine Engine(string thread = "chat-1", bool attended = true, bool vision = true, string? head = null) =>
        new(Parts, new PcCaller(thread, () => attended, vision) { Head = head });

    /// <summary>The user presses a key in the window (it comes in front, its focus as the desktop reports it).</summary>
    public void UserTypes(nint window, ushort key = 0x41)
    {
        Desktop.ForegroundWindow = window;
        var now = Desktop.Clock.NowMs;
        var focus = Desktop.FocusHandle;
        Input.Record(new PcRawInput(now, PcRawKind.Key, true, key, false, 0) { Window = window, Focus = focus });
        Input.Record(new PcRawInput(now, PcRawKind.Key, false, key, false, 0) { Window = window, Focus = focus });
    }

    /// <summary>The user keeps typing in the window: a key now and one every 100 ms for the next <paramref name="forMs"/> of the desktop clock.</summary>
    public void KeepTyping(nint window, int forMs = 12_000)
    {
        UserTypes(window);
        for (var at = 100; at <= forMs; at += 100) Desktop.Schedule(at, () => UserTypes(window));
    }

    /// <summary>The user moves the pointer over the window, to a point.</summary>
    public void UserPoints(nint window, PcPoint at) =>
        Input.Record(new PcRawInput(Desktop.Clock.NowMs, PcRawKind.Move, false, 0, false, 0) { Window = window, Point = at, Moved = new PcPoint(1, 1) });

    /// <summary>The user presses (or lets go of) the left button over the window.</summary>
    public void UserButton(nint window, PcPoint at, bool down) =>
        Input.Record(new PcRawInput(Desktop.Clock.NowMs, PcRawKind.Button, down, 1, false, 0) { Window = window, Point = at, HeldButtons = down ? 1 : 0 });

    /// <summary>One tool call with JSON arguments.</summary>
    public static Task<PcResult> Call(PcControlEngine engine, string tool, string json, CancellationToken cancellationToken = default) =>
        engine.Call(tool, (JsonObject)JsonNode.Parse(json)!, cancellationToken);

    /// <summary>
    /// A notepad-like app: [2] a document field, [3] a Save button with Ctrl+S, [4] a checkbox. Its UIA framework is
    /// unknown, so only R1 (patterns) is used in it.
    /// </summary>
    public PcFakeApp Notepad(nint handle = 0x1a, int processId = 101, string exe = "notepad.exe")
    {
        var app = Desktop.AddApp(handle, "Untitled - " + Path.GetFileNameWithoutExtension(exe), exe, processId);
        var x = app.Frame.X;
        var y = app.Frame.Y;
        app.Root.Add(new PcFakeNode("1." + Id(handle) + ".2", "edit", "Text editor") { Actions = PcActions.Value | PcActions.Text | PcActions.Focus, Value = "", Bounds = new PcRect(x + 10, y + 60, 1100, 600) });
        app.Root.Add(new PcFakeNode("1." + Id(handle) + ".3", "button", "Save") { Actions = PcActions.Invoke, Shortcut = "Ctrl+S", Bounds = new PcRect(x + 10, y + 10, 80, 30) });
        app.Root.Add(new PcFakeNode("1." + Id(handle) + ".4", "check box", "Word wrap") { Actions = PcActions.Toggle, Bounds = new PcRect(x + 100, y + 10, 120, 30) });
        app.FocusControl = app.Root.Children[0];
        return app;
    }

    /// <summary>
    /// A classic Win32 dialog app (UIA framework Win32): [2] a stock Edit control with its own window, [3] a stock OK button
    /// (automation id 1) with its own window, [4] a label. R2 messages are allowed in it.
    /// </summary>
    public PcFakeApp Classic(nint handle = 0x2c, int processId = 202, string exe = "legacy.exe")
    {
        var app = Desktop.AddApp(handle, "Settings", exe, processId);
        app.ClassName = "#32770";
        app.Root.Framework = "Win32";
        var x = app.Frame.X;
        var y = app.Frame.Y;
        app.Root.Add(new PcFakeNode("1." + Id(handle) + ".2", "edit", "Name") { Actions = PcActions.Value | PcActions.Focus, ClassName = "Edit", Framework = "Win32", NativeWindow = handle + 0x100, Value = "", Bounds = new PcRect(x + 20, y + 40, 300, 24) });
        app.Root.Add(new PcFakeNode("1." + Id(handle) + ".3", "button", "OK") { Actions = PcActions.Invoke, AutomationId = "1", ClassName = "Button", Framework = "Win32", NativeWindow = handle + 0x200, Bounds = new PcRect(x + 20, y + 80, 80, 26) });
        app.Root.Add(new PcFakeNode("1." + Id(handle) + ".4", "text", "Ready") { Framework = "Win32", Bounds = new PcRect(x + 20, y + 120, 200, 20) });
        app.FocusControl = app.Root.Children[0];
        return app;
    }

    public static string Id(nint handle) => ((long)handle).ToString(CultureInfo.InvariantCulture);

    public static string W(nint handle) => PcWindowRef.Format(handle);
}

public sealed class PcFakeApprover : IPcApprover
{
    public PcApproval Answer { get; set; } = PcApproval.Chat;
    public List<(string App, PcCapability Capability, bool AllowAlways)> Asked { get; } = [];
    public Dictionary<(string Exe, PcCapability Capability), PcApproval> Standings { get; } = [];
    public TaskCompletionSource<PcApproval>? Hold { get; set; }

    /// <summary>What the user said with a denial (the card's "Reply instead"), handed to the engine once.</summary>
    public string? Said { get; set; }

    public string? Reason(string thread, PcAppIdentity app, PcCapability capability)
    {
        var said = Said;
        Said = null;
        return said;
    }

    public PcApproval? Standing(PcAppIdentity app, PcCapability capability) =>
        Standings.TryGetValue((app.ExeName, capability), out var standing) ? standing : null;

    public Task<PcApproval> Ask(PcAskSource source, PcAppIdentity app, string appName, PcCapability capability, bool risky, bool allowAlways, CancellationToken cancellationToken)
    {
        lock (Asked) Asked.Add((app.ExeName, capability, allowAlways));
        return Hold is { } hold ? hold.Task.WaitAsync(cancellationToken) : Task.FromResult(Answer);
    }
}

public sealed class PcFakeConfirmer : IPcConfirmer
{
    public bool Answer { get; set; } = true;
    public PcShowAnswer ShowAnswer { get; set; } = PcShowAnswer.Show;
    public List<string> Asked { get; } = [];
    public List<string> Shown { get; } = [];
    public TaskCompletionSource<bool>? Hold { get; set; }
    public TaskCompletionSource<PcShowAnswer>? ShowHold { get; set; }

    /// <summary>What the user typed with Cancel; given once.</summary>
    public string? Said { get; set; }

    public string? Note(string thread)
    {
        var said = Said;
        Said = null;
        return said;
    }

    public Task<bool> Confirm(PcAskSource source, string word, string elementName, string appName, CancellationToken cancellationToken)
    {
        lock (Asked) Asked.Add(word + " " + elementName);
        return Hold is { } hold ? hold.Task.WaitAsync(cancellationToken) : Task.FromResult(Answer);
    }

    public Task<PcShowAnswer> Show(PcAskSource source, string appName, string windowTitle, CancellationToken cancellationToken)
    {
        lock (Shown) Shown.Add(appName + " " + windowTitle);
        return ShowHold is { } hold ? hold.Task.WaitAsync(cancellationToken) : Task.FromResult(ShowAnswer);
    }
}

public sealed class PcFakeStopKey : IPcStopKey
{
    public bool Works { get; set; } = true;
    public int Arms { get; private set; }
    public string Chord => "Ctrl+Alt+End";

    public bool Arm()
    {
        Arms++;
        return Works;
    }
}

public sealed class PcFakeAudit : IPcAudit
{
    public List<PcAuditEntry> Entries { get; } = [];

    public void Write(PcAuditEntry entry)
    {
        lock (Entries) Entries.Add(entry);
    }
}
