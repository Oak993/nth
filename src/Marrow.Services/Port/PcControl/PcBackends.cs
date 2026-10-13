// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1; DESIGN.md 3.1, 3.2). The backends the engine drives, each behind an
// interface so every automated test runs on fakes: the window list, the security probe, input, the user's foreground
// state, nth's own windows, the clock and the app launcher. (The UIA reader, the capture and the approval, confirmation and interruption seams have
// their own files.) Win32 implementations live in Native/.
using System.Diagnostics;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

/// <summary>A top-level window: handle, title, class, process, exe, AUMID (packaged apps), frame in physical pixels.</summary>
public sealed record PcWindowInfo(nint Handle, string Title, string ClassName, int ProcessId, string ExePath)
{
    public string? Aumid { get; init; }
    public PcRect Frame { get; init; }
    public bool Minimized { get; init; }
    public nint Owner { get; init; }
    public int Monitor { get; init; }

    /// <summary>When the window's process started (UTC ticks); with the process id and class it tells a reused handle apart.</summary>
    public long StartTime { get; init; }

    /// <summary>The exe's file version, for capability learning (DESIGN.md 2.6); empty when unknown.</summary>
    public string Version { get; init; } = "";
    public PcAppIdentity App => new(ExePath, Aumid);
    public string ExeName => App.ExeName;

    /// <summary>
    /// Whether <paramref name="live"/> is still this window and not another one that Windows gave the same handle
    /// (critique C17): the same process id, process start time and class.
    /// </summary>
    public bool SameWindow(PcWindowInfo live) =>
        live.Handle == Handle && live.ProcessId == ProcessId && live.StartTime == StartTime && string.Equals(live.ClassName, ClassName, StringComparison.Ordinal);
}

/// <summary>A monitor: its index, bounds and work area in physical pixels, its DPI, whether it is the primary.</summary>
public sealed record PcMonitor(int Index, PcRect Bounds, PcRect WorkArea, int Dpi, bool Primary);

/// <summary>An installed app from the Start menu (shell:AppsFolder): its name and its id (an AUMID or a parsing name).</summary>
public sealed record PcInstalledApp(string Name, string AppId);

public interface IWindowCatalog
{
    /// <summary>Visible, uncloaked, non-tool, non-empty top-level windows, front to back.</summary>
    IReadOnlyList<PcWindowInfo> Windows();

    /// <summary>The live facts of one window; null when it is gone.</summary>
    PcWindowInfo? Window(nint handle);

    IReadOnlyList<PcMonitor> Monitors();

    /// <summary>The union of every monitor, in physical pixels (SM_*VIRTUALSCREEN under per-monitor DPI awareness).</summary>
    PcRect VirtualDesktop();

    /// <summary>The root owner of the window under a screen point; 0 when there is none.</summary>
    nint RootAt(PcPoint point);

    /// <summary>The root owner of the foreground window; 0 when there is none (the secure desktop, for one).</summary>
    nint Foreground();

    /// <summary>Restores and brings the window forward; true when it is the foreground window afterwards.</summary>
    bool Activate(nint handle);

    /// <summary>
    /// Shows the window to the user without activating it (DESIGN.md 2.2 pc_focus_window): restored if minimized
    /// (SW_SHOWNOACTIVATE) and raised (SetWindowPos HWND_TOP, SWP_NOACTIVATE). The user's foreground and focus stay.
    /// </summary>
    bool Show(nint handle);

    /// <summary>
    /// The quiet peek (DESIGN.md 2.10, critique C18; ED-NEW-pcctl-46): a minimized window restored without being activated
    /// (SW_SHOWNOACTIVATE) and put at the bottom of the Z-order (HWND_BOTTOM, SWP_NOACTIVATE), where it can still be
    /// captured and acted on; disposing it minimizes the window again without activation (SW_SHOWMINNOACTIVE) and puts
    /// back its exact placement. Null when the window is gone, is not minimized, or could not be shown. Tactus asks the
    /// user first ("Show Outlook for a moment?") until NP-12 proves a peek unseen.
    /// </summary>
    IDisposable? Peek(nint handle) => null;
}

/// <summary>
/// The user's side of the PC at one moment (DESIGN.md 2.6 delta check, critique C16): the foreground window (its root),
/// the real cursor, the focused control of the foreground thread, and whether a modifier key is down. The user's own
/// input is the physical input monitor's (IPhysicalInput).
/// </summary>
public readonly record struct PcUserState(nint Foreground, PcPoint Cursor, nint Focus, bool ModifiersDown);

/// <summary>Reads the user's state. Reading only: nothing here changes the foreground, the focus or the cursor.</summary>
public interface IForeground
{
    PcUserState Read();
}

public interface ISecurityProbe
{
    PcProcessSecurity Process(int processId);

    /// <summary>True when the input desktop is not the user's default desktop (UAC, Ctrl+Alt+Del, the lock screen).</summary>
    bool SecureDesktop();

    /// <summary>True when the session is locked (the lock app is in front).</summary>
    bool Locked();
}

/// <summary>Input, in physical screen pixels. Implementations never run in automated tests.</summary>
public interface IInputInjector
{
    Task Move(PcPoint point, CancellationToken cancellationToken);
    Task Click(PcPoint point, PcMouseButton button, int count, IReadOnlyList<ushort> modifiers, CancellationToken cancellationToken);
    Task Wheel(PcPoint point, PcScrollDirection direction, int notches, CancellationToken cancellationToken);
    Task Drag(IReadOnlyList<PcPoint> path, IReadOnlyList<ushort> modifiers, int holdMs, CancellationToken cancellationToken);
    Task Keys(KeyChord chord, int repeat, CancellationToken cancellationToken);
    Task Text(string text, CancellationToken cancellationToken);
}

/// <summary>
/// nth's own windows (ED-NEW-pcctl-2): where they are, so no pointer action lands on them, which handles are nth's, and a
/// scope that hides nth's overlays while it captures or acts.
/// </summary>
public interface IOwnWindows
{
    IReadOnlyList<PcRect> Rects();
    bool Owns(nint handle);
    IDisposable Suppress();
}

public interface IPcClock
{
    long NowMs { get; }
    Task Delay(int milliseconds, CancellationToken cancellationToken);
}

public interface IAppLauncher
{
    IReadOnlyList<PcInstalledApp> Installed();

    /// <summary>Starts an app by its AppsFolder id or an .exe path, with no arguments and no shell; the process id when known.</summary>
    PcLaunch Launch(string appIdOrPath);

    /// <summary>
    /// The same with arguments nth chose itself, never the model's: the separate workspace's browser profile (DESIGN.md
    /// 2.13). A launcher that takes none starts the app without them.
    /// </summary>
    PcLaunch Launch(string appIdOrPath, string? arguments) => Launch(appIdOrPath);
}

public sealed record PcLaunch(bool Started, int? ProcessId, string? Problem);

public sealed class SystemPcClock : IPcClock
{
    private readonly Stopwatch watch = Stopwatch.StartNew();
    public long NowMs => watch.ElapsedMilliseconds;
    public Task Delay(int milliseconds, CancellationToken cancellationToken) => Task.Delay(milliseconds, cancellationToken);
}
