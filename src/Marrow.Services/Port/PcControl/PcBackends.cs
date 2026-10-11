// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1; DESIGN.md 3.1, 3.2). The backends the engine drives, each behind an
// interface so every automated test runs on fakes: the window list, the security probe, input, nth's own windows, the
// clock and the app launcher. (The UIA reader, the capture and the approval, confirmation and interruption seams have
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
    public PcAppIdentity App => new(ExePath, Aumid);
    public string ExeName => App.ExeName;
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
}

public sealed record PcLaunch(bool Started, int? ProcessId, string? Problem);

public sealed class SystemPcClock : IPcClock
{
    private readonly Stopwatch watch = Stopwatch.StartNew();
    public long NowMs => watch.ElapsedMilliseconds;
    public Task Delay(int milliseconds, CancellationToken cancellationToken) => Task.Delay(milliseconds, cancellationToken);
}
