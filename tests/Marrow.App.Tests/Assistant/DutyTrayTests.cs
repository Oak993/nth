using System.Text.Json.Nodes;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tray;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.4, 12.6; canvas 5 "Tray menu"; ED-NEW-dextra-as10-14): what the scheduler puts in nth's one tray icon:
// "Pause duties for 1 hour" (it becomes "Resume duties" while on) and the two background switches, shown with their state and flipped by a
// click, both off until tapped. The icon wears Working while a duty runs and Paused while the duties are paused.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-14")]
public sealed class DutyTrayTests
{
    private const string Exe = @"C:\Users\me\AppData\Local\nth\current\nth.exe";

    private readonly FakeTrayHost host = new();
    private readonly FakeRunKey key = new();
    private JsonObject? stored;

    private (AppTray Tray, SchedulerRig Rig, BackgroundSwitches Switches, DutyTray Duties) Make(bool windowHidden = true)
    {
        var rig = new SchedulerRig { AutoEnd = false };
        rig.Store.Put("dextra", SchedulerRig.Every("d1", 10));
        rig.Begin();
        var switches = new BackgroundSwitches(() => stored?.DeepClone() as JsonObject, document => stored = (JsonObject)document.DeepClone(), new StartWithWindows(key), () => Exe);
        var tray = new AppTray(host, () => windowHidden, open: () => { });
        return (tray, rig, switches, DutyTray.Bind(tray, rig.Scheduler!, switches));
    }

    [Fact]
    public void The_menu_has_the_pause_and_the_two_switches_all_off_and_nothing_is_written_to_windows()
    {
        var (_, rig, _, duties) = Make();
        using var ignored = rig;
        using var also = duties;
        Assert.Equal("Open nth|Pause duties for 1 hour|Keep working when the window is closed: Off|Start with Windows, hidden: Off", host.Menu());
        Assert.Empty(key.Values);
        Assert.Null(stored);
    }

    [Fact]
    public void Pause_for_an_hour_holds_the_duties_and_flips_to_resume_and_back()
    {
        var (_, rig, _, duties) = Make();
        using var ignored = rig;
        using var also = duties;
        host.Click(DutyTray.PauseId);
        Assert.NotNull(rig.Scheduler!.PausedUntil);
        Assert.Contains("Resume duties", host.Menu(), StringComparison.Ordinal);
        Assert.Equal(TrayState.Paused, host.Last!.State);
        host.Click(DutyTray.PauseId);
        Assert.Null(rig.Scheduler.PausedUntil);
        Assert.Contains("Pause duties for 1 hour", host.Menu(), StringComparison.Ordinal);
        Assert.Equal(TrayState.Ready, host.Last!.State);
    }

    [Fact]
    public void A_click_on_a_switch_flips_it_and_the_label_shows_the_state()
    {
        var (_, rig, switches, duties) = Make();
        using var ignored = rig;
        using var also = duties;
        host.Click(DutyTray.KeepId);
        Assert.True(switches.KeepWorkingWhenClosed);
        Assert.Contains("Keep working when the window is closed: On", host.Menu(), StringComparison.Ordinal);
        host.Click(DutyTray.StartId);
        Assert.Single(key.Values);
        Assert.Contains("Start with Windows, hidden: On", host.Menu(), StringComparison.Ordinal);
        host.Click(DutyTray.KeepId);
        Assert.Contains("Keep working when the window is closed: Off", host.Menu(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_duty_that_is_running_shows_the_icon_as_working_even_with_the_window_up()
    {
        var (tray, rig, _, duties) = Make(windowHidden: false);
        using var ignored = rig;
        using var also = duties;
        Assert.False(tray.Visible);
        rig.Advance(TimeSpan.FromMinutes(10)); // the duty fires and its mission goes on
        Assert.True(tray.Visible);
        Assert.Equal(TrayState.Working, host.Last!.State);
        rig.Scheduler!.MissionEnded(rig.Launcher.LastMission!.Value, Marrow.Core.Port.Assistant.DutyRunOutcome.Done, "ok");
        Assert.False(tray.Visible);
    }

    [Fact]
    public void Taking_the_tray_away_takes_the_items_away()
    {
        var (tray, rig, _, duties) = Make();
        using var ignored = rig;
        duties.Dispose();
        Assert.Equal("Open nth", host.Menu());
        Assert.NotNull(tray);
    }
}
