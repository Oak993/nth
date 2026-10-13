// PORT-ONLY: Tactus, PC control (OD-0140, OD-0144, OD-0145, ED-NEW-pcctl-40; DESIGN.md 2.7, 2.10, 3.2, 3.3, 4.1, 4.4, 4.6).
// The one place the engine's parts are put together. The platform hands in its backends (Win32, UI Automation, the hooks,
// capture; fakes in tests) and the app hands in its stop key; this builds what every chat shares (the run control, the
// lanes, window holds, Pause, the ask broker, the stop paths, the real-input lease and the action ladder) and gives each
// chat thread its own parts with its own audit log. Approvals, guarded-action confirms and the "Show this window?"
// question go through the ask broker, so the pill's card and the chat answer the same entry; standing ("Always") grants
// and nth's own card focus come from the surface the pill and the grant store bind (all safe when unbound: no standing
// grant, no card up). The real mouse and keyboard (R4) follow the setting, which is off by default.
using System.Collections.Concurrent;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;

namespace Marrow.Services.Port.PcControl;

/// <summary>What the platform gives Tactus: the native backends in the app, fakes in tests.</summary>
public sealed class PcBackends
{
    public required IWindowCatalog Catalog { get; init; }
    public required ISecurityProbe Security { get; init; }
    public required IUiaReader Reader { get; init; }
    public required IPcChangeFeed Feed { get; init; }
    public required IPcMessageSink Messages { get; init; }
    public required IForeground Foreground { get; init; }
    public required IOwnWindows Own { get; init; }
    public required IAppLauncher Launcher { get; init; }
    public required IPcClock Clock { get; init; }

    /// <summary>The physical input monitor (its hooks are installed only while someone keeps it).</summary>
    public required PhysicalInputMonitor Input { get; init; }

    /// <summary>Where a borrow holds the user's input (the hooks' core in the app).</summary>
    public required IPcInputHold Hold { get; init; }

    public required IRealInputHost RealHost { get; init; }

    /// <summary>Screenshots; none (tree text only) when the platform has no encoder.</summary>
    public PcCapturePipeline? Capture { get; init; }

    /// <summary>One audit log per chat thread (DESIGN.md 4.6).</summary>
    public Func<string, IPcAudit> Audit { get; init; } = thread => new PcAuditLog(thread);

    /// <summary>The clock of the waits for the user; the engine clock when null.</summary>
    public IPcClock? WaitClock { get; init; }

    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;

    /// <summary>
    /// Where what Tactus learned is kept between runs (nth's data folder in the app, ED-NEW-pcctl-45); null keeps it in
    /// memory only, as in tests.
    /// </summary>
    public Func<string?> CapabilitiesFile { get; init; } = () => null;

    /// <summary>The NP-4 smoke's proofs file, imported once at start in development builds only; null in the app.</summary>
    public string? DevProofsFile { get; init; }

    /// <summary>Native helpers that live as long as Tactus (hook and event threads); disposed with the assembly.</summary>
    public IReadOnlyList<IDisposable> Owned { get; init; } = [];

    /// <summary>The separate workspace (DESIGN.md 2.13, preview; ED-NEW-pcws-1); none when null.</summary>
    public IWorkspaceHost? Workspace { get; init; }
}

/// <summary>
/// The pieces the pill, its cards and the grant store bring (DESIGN.md 4.1, 10.4). Each has a safe default, so Tactus
/// works without them: no standing grant (every app is asked), and nth's card never counts as holding the keyboard.
/// </summary>
public sealed class PcSurface
{
    /// <summary>An "Always" grant the user made for this app and capability (the grant store, unit 11); null when none.</summary>
    public Func<PcAppIdentity, PcCapability, PcApproval?> Standing { get; init; } = (_, _) => null;

    /// <summary>Whether nth's own card has the keyboard now (the card's focus, P27): a borrow then waits.</summary>
    public Func<bool>? OwnCardUp { get; init; }

    /// <summary>
    /// The grant store (unit 11, ED-NEW-pcctl-25, ED-NEW-pcctl-32): with it the approver reads the "Always" grants from it,
    /// writes every grant the card or the chat makes to it, and a chat's grant covers that chat's later steps; without it
    /// <see cref="Standing"/> alone answers.
    /// </summary>
    public PcGrants? Grants { get; init; }
}

public sealed class PcAssembly : IDisposable
{
    /// <summary>A thread name no chat uses, to ask whether any chat holds a window.</summary>
    private const string NoThread = "\0";

    private readonly PcBackends backends;
    private readonly Func<PcSettings> settings;
    private readonly ConcurrentDictionary<string, PcEngineParts> perThread = new(StringComparer.Ordinal);
    private readonly PcCapabilityFile? capabilityFile;
    private int disposed;

    /// <param name="stopKey">Makes the stop key from the shared parts (the app's chords need the stop paths, Pause and the lanes).</param>
    /// <param name="interruptTurns">The pill's Stop also interrupts the provider turns (DESIGN.md 10.2 path 2).</param>
    public PcAssembly(PcBackends backends, Func<PcSettings> settings, Func<PcAssembly, IPcStopKey> stopKey, PcSurface? surface = null, Action? interruptTurns = null)
    {
        ArgumentNullException.ThrowIfNull(backends);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(stopKey);
        this.backends = backends;
        this.settings = settings;
        Surface = surface ?? new PcSurface();
        var clock = backends.Clock;
        Control = new PcRunControl();
        Lanes = new PcLanes();
        Ownership = new PcWindowOwnership(clock);
        Pause = new PcPauseControl(clock, Control);
        Asks = new PcAskBroker(backends.WaitClock ?? clock, Control);
        Stop = new PcStopControl(Control, interruptTurns);
        Capabilities = new PcCapabilities(backends.Now);
        if (backends.CapabilitiesFile() is { Length: > 0 } learnedAt)
            capabilityFile = PcCapabilityFile.Open(Capabilities, learnedAt, backends.Now, backends.DevProofsFile);
        Lease = new RealInputLease(backends.Input, backends.Hold, backends.RealHost, clock, Capabilities, Surface.OwnCardUp, Control);
        Ladder = new PcActionLadder([new PcUiaRung(backends.Reader), new PcMessageRung(backends.Messages), new PcBorrowRung(settings, Lease, backends.Reader)], Capabilities);
        Approver = Surface.Grants is { } grants
            ? new PcBrokerApprover(Asks, grants)
            : new PcBrokerApprover(Asks, (app, capability) => Surface.Standing(app, capability));
        Confirmer = new PcBrokerConfirmer(Asks);
        StopKey = stopKey(this);
        Shared = Make(PcNoAudit.Instance);
    }

    public PcSurface Surface { get; }
    public PcRunControl Control { get; }
    public PcLanes Lanes { get; }
    public PcWindowOwnership Ownership { get; }
    public PcPauseControl Pause { get; }
    public PcAskBroker Asks { get; }
    public PcStopControl Stop { get; }
    public PcCapabilities Capabilities { get; }
    public RealInputLease Lease { get; }
    public PcActionLadder Ladder { get; }
    public IPcApprover Approver { get; }
    public IPcConfirmer Confirmer { get; }
    public IPcStopKey StopKey { get; }

    /// <summary>Where each action lands, for the Tactus pointers (the pill's pointer view reads it).</summary>
    public PcPointerFeed Pointer { get; } = new();

    /// <summary>The writes waiting while the user types, for the pill's "Waiting while you type".</summary>
    public PcTypingWaits Typing { get; } = new();

    /// <summary>Where new apps open, shared by every chat (it remembers the apps that open only on the desk; ED-NEW-pcws-3).</summary>
    public WorkspaceRouter Router { get; } = new();

    public PhysicalInputMonitor Input => backends.Input;
    public IPcClock Clock => backends.Clock;

    /// <summary>The settings now (read on every use).</summary>
    public PcSettings Settings => settings();

    /// <summary>The shared parts with no audit log of their own: for the settings and on-checks, never for an engine.</summary>
    public PcEngineParts Shared { get; }

    /// <summary>A chat thread's parts: everything shared, with that thread's audit log. The same object every time.</summary>
    public PcEngineParts PartsFor(string thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        return perThread.GetOrAdd(thread, key => Make(backends.Audit(key)));
    }

    /// <summary>
    /// The parts of the workspace agent's engine (ED-NEW-pcws-7): everything shared, with no audit of its own, its window and
    /// look ids marked as the workspace's, and the user's approvals and guarded-action cards asked through nth outside.
    /// </summary>
    public PcEngineParts WorkspaceParts(IPcApprover approver, IPcConfirmer confirmer) => Make(PcNoAudit.Instance, approver, confirmer, inWorkspace: true);

    /// <summary>Whether Tactus is using the window now: work in flight there, or a chat holds it (the first Esc of three must be there).</summary>
    public bool IsTactusWindow(nint window) =>
        window != 0 && (Control.ActiveWindows.Contains(window) || Ownership.Holder(window, NoThread) is not null);

    /// <summary>The master switch went off (DESIGN.md 10.2 path 9): everything stops, and no new stream starts while it is off.</summary>
    public void SwitchedOff() => Stop.MasterSwitchOff();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;
        capabilityFile?.Dispose();
        Pause.Dispose();
        Asks.Dispose();
        if (StopKey is IDisposable key) key.Dispose();
        foreach (var owned in backends.Owned.Reverse())
        {
            try
            {
                owned.Dispose();
            }
            catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
            {
            }
        }
    }

    private PcEngineParts Make(IPcAudit audit, IPcApprover? approver = null, IPcConfirmer? confirmer = null, bool inWorkspace = false) => new()
    {
        Catalog = backends.Catalog,
        Security = backends.Security,
        Reader = backends.Reader,
        Feed = backends.Feed,
        Ladder = Ladder,
        Foreground = backends.Foreground,
        Input = backends.Input,
        Lease = Lease,
        Own = backends.Own,
        Approver = approver ?? Approver,
        Confirmer = confirmer ?? Confirmer,
        StopKey = StopKey,
        Control = Control,
        Pause = Pause,
        Asks = Asks,
        Audit = audit,
        Clock = backends.Clock,
        Launcher = backends.Launcher,
        Lanes = Lanes,
        Ownership = Ownership,
        Settings = settings,
        Capture = backends.Capture,
        Pointer = Pointer,
        Typing = Typing,
        WaitClock = backends.WaitClock,
        Now = backends.Now,
        Workspace = inWorkspace ? null : backends.Workspace,
        InWorkspace = inWorkspace,
        Router = Router,
    };
}

/// <summary>The shared parts' audit: they never run a tool call, so nothing is written.</summary>
internal sealed class PcNoAudit : IPcAudit
{
    public static PcNoAudit Instance { get; } = new();

    public void Write(PcAuditEntry entry)
    {
    }
}

/// <summary>
/// "Attended" for Hydra heads (DESIGN.md 2.10, critique C14): physical input in any app within the last 10 minutes, and
/// the lead chat's window not minimized. The lead chat itself is never limited by it.
/// </summary>
public static class PcAttendance
{
    public const long AwayAfterMs = 10 * 60 * 1000;

    public static bool Attended(bool head, long idleMs, bool leadMinimized) => !head || (idleMs >= 0 && idleMs <= AwayAfterMs && !leadMinimized);
}
