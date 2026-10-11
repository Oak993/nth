// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-12, ED-NEW-pcctl-15; DESIGN.md 4.4, 10.2). The nine stop paths
// of pill v2, each mapped to its scope: the pill row's x and the chat strip's x stop one app; the pill's Stop stops all and
// also interrupts the provider turns; the stop chord, Esc three times, the tray's "Stop Tactus" and the master switch stop
// all; the chat's Stop stops that chat's apps (with its turn, as before). Every path only sets cancel flags and returns: a
// stop never waits on a worker, works the same while paused or "Pausing", and clears the pause of what it covers
// (PcPauseControl listens to the run control). Esc during a borrow of the real pointer or focus belongs to that borrow
// (unit P3).
namespace Marrow.Services.Port.PcControl;

/// <summary>A stop path of DESIGN.md 10.2.</summary>
public enum PcStopPath
{
    PillRow,
    PillStop,
    StopKey,
    EscapeThreeTimes,
    ChatStop,
    StripRow,
    Tray,
    MasterSwitch,
}

public sealed class PcStopControl(PcRunControl control, Action? interruptTurns = null)
{
    /// <summary>The pill's line after Esc three times.</summary>
    public const string EscapeNotice = "Stopped (Esc pressed 3 times). Ask in chat to continue.";

    private readonly Lock gate = new();
    private (PcStopPath Path, string? Notice)? last;

    /// <summary>Raised after every stop, with its path (the pill words its line from it).</summary>
    public event Action<PcStopPath>? Stopped;

    /// <summary>The latest stop and the pill's line for it, when the path has one.</summary>
    public (PcStopPath Path, string? Notice)? Last
    {
        get
        {
            lock (gate) return last;
        }
    }

    /// <summary>Path 1: the pill row's x, that app.</summary>
    public void PillRow(string app) => Run(PcStopPath.PillRow, PcScope.App(app), PcStopSource.Pill);

    /// <summary>Path 2: the pill's Stop, every app of every chat; the provider turns are interrupted too.</summary>
    public void PillStop()
    {
        Run(PcStopPath.PillStop, PcScope.All, PcStopSource.Pill);
        interruptTurns?.Invoke();
    }

    /// <summary>Path 3: the stop chord, from anywhere.</summary>
    public void StopKey() => Run(PcStopPath.StopKey, PcScope.All, PcStopSource.StopKey);

    /// <summary>Path 5: Esc three times within 1 s, the first in a window Tactus is using.</summary>
    public void EscapeThreeTimes() => Run(PcStopPath.EscapeThreeTimes, PcScope.All, PcStopSource.EscapeThreeTimes, EscapeNotice);

    /// <summary>Path 6: the chat's Stop, that chat's apps (the engine's StopTurn also ends its waiting steps).</summary>
    public void ChatStop(string thread) => Run(PcStopPath.ChatStop, PcScope.Thread(thread), PcStopSource.Chat);

    /// <summary>Path 7: the chat strip's x on one app.</summary>
    public void StripRow(string app) => Run(PcStopPath.StripRow, PcScope.App(app), PcStopSource.Chat);

    /// <summary>Path 8: the tray menu's "Stop Tactus".</summary>
    public void Tray() => Run(PcStopPath.Tray, PcScope.All, PcStopSource.Tray);

    /// <summary>Path 9: the master switch off; no new stream starts until it is on (the engine checks the setting).</summary>
    public void MasterSwitchOff() => Run(PcStopPath.MasterSwitch, PcScope.All, PcStopSource.Settings);

    private void Run(PcStopPath path, PcScope scope, PcStopSource source, string? notice = null)
    {
        control.Stop(scope, source);
        lock (gate) last = (path, notice);
        Stopped?.Invoke(path);
    }
}
