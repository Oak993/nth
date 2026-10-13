// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.4, 12.6; canvas 5 "Tray menu"; unit AS-10; ED-NEW-dextra-as10-14). No Swift twin.
// What the scheduler puts in nth's one tray icon: "Pause duties for 1 hour" (which becomes "Resume duties" while it is on) and the
// two background switches, each shown with its state and flipped by a click. The switches are off until tapped. The icon shows
// Working while a duty runs and Paused while the duties are paused; it shows at all when the window is hidden, as AppTray decides.
using Marrow.App.Tray;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed class DutyTray : IDisposable
{
    public const string Section = "Duties";

    public const string PauseId = "duties.pause";

    public const string KeepId = "duties.keep";

    public const string StartId = "duties.start";

    /// <summary>"Pause duties for 1 hour".</summary>
    public static readonly TimeSpan PauseSpan = TimeSpan.FromHours(1);

    private readonly AppTray tray;
    private readonly AssistantScheduler scheduler;
    private readonly BackgroundSwitches switches;
    private readonly IDisposable contribution;

    private DutyTray(AppTray tray, AssistantScheduler scheduler, BackgroundSwitches switches)
    {
        this.tray = tray;
        this.scheduler = scheduler;
        this.switches = switches;
        contribution = tray.Contribute(Section, Contribute);
        scheduler.Changed += Refresh;
        switches.Changed += Refresh;
    }

    public static DutyTray Bind(AppTray tray, AssistantScheduler scheduler, BackgroundSwitches switches)
    {
        ArgumentNullException.ThrowIfNull(tray);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(switches);
        return new DutyTray(tray, scheduler, switches);
    }

    public void Dispose()
    {
        scheduler.Changed -= Refresh;
        switches.Changed -= Refresh;
        contribution.Dispose();
    }

    private TrayContribution Contribute()
    {
        var paused = scheduler.PausedUntil is not null;
        var running = scheduler.Duties.Any(v => v.Running);
        var items = new List<TrayItem>
        {
            new(PauseId, paused ? "Resume duties" : "Pause duties for 1 hour", () =>
            {
                if (scheduler.PausedUntil is null) scheduler.PauseFor(PauseSpan);
                else scheduler.ResumeDuties();
            }),
            new(KeepId, $"{BackgroundSwitches.KeepWorkingWords}: {(switches.KeepWorkingWhenClosed ? "On" : "Off")}", () => _ = switches.SetKeepWorking(!switches.KeepWorkingWhenClosed)),
            new(StartId, $"{BackgroundSwitches.StartWithWindowsWords}: {(switches.StartWithWindowsOn ? "On" : "Off")}", () => _ = switches.SetStartWithWindows(!switches.StartWithWindowsOn)),
        };
        return new TrayContribution(items, Live: running, State: running ? TrayState.Working : paused ? TrayState.Paused : TrayState.Ready);
    }

    private void Refresh() => tray.Refresh();
}
