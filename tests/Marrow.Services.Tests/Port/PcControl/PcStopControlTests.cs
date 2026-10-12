using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-15; DESIGN.md 10.2). The eight stop paths: each stops what it
// covers with its own source; only the pill's Stop also interrupts the provider turns; Esc three times leaves the notice
// the chat shows; a stop works while Tactus is paused, cancels a held step and clears the pause it covers.
[Trait("Decision", "ED-NEW-pcctl-15")]
public sealed class PcStopControlTests
{
    private const string Excel = "exe:c:\\office\\excel.exe";
    private const string Outlook = "exe:c:\\office\\outlook.exe";

    [Fact]
    public void Each_path_stops_what_it_covers_with_its_own_source()
    {
        var control = new PcRunControl();
        var stops = new List<(PcScope Scope, PcStopSource Source)>();
        control.Stopped += (scope, source) => stops.Add((scope, source));
        var paths = new List<PcStopPath>();
        var stop = new PcStopControl(control);
        stop.Stopped += paths.Add;

        stop.PillRow(Excel);
        stop.PillStop();
        stop.StopKey();
        stop.EscapeThreeTimes();
        stop.ChatStop("chat-1");
        stop.StripRow(Outlook);
        stop.Tray();
        stop.MasterSwitchOff();

        Assert.Equal(
            new[]
            {
                (PcScope.App(Excel), PcStopSource.Pill),
                (PcScope.All, PcStopSource.Pill),
                (PcScope.All, PcStopSource.StopKey),
                (PcScope.All, PcStopSource.EscapeThreeTimes),
                (PcScope.Thread("chat-1"), PcStopSource.Chat),
                (PcScope.App(Outlook), PcStopSource.Chat),
                (PcScope.All, PcStopSource.Tray),
                (PcScope.All, PcStopSource.Settings),
            },
            stops);
        Assert.Equal(Enum.GetValues<PcStopPath>(), paths);
    }

    [Fact]
    public void Only_the_pills_Stop_interrupts_the_provider_turns()
    {
        var interrupted = 0;
        var stop = new PcStopControl(new PcRunControl(), () => interrupted++);

        stop.PillRow(Excel);
        stop.StopKey();
        stop.EscapeThreeTimes();
        stop.ChatStop("chat-1");
        stop.StripRow(Excel);
        stop.Tray();
        stop.MasterSwitchOff();
        Assert.Equal(0, interrupted);

        stop.PillStop();
        Assert.Equal(1, interrupted);
    }

    [Fact]
    public void Esc_three_times_leaves_the_notice_and_the_other_paths_none()
    {
        var stop = new PcStopControl(new PcRunControl());

        Assert.Null(stop.Last);
        stop.EscapeThreeTimes();
        var escape = Assert.NotNull(stop.Last);
        Assert.Equal(PcStopPath.EscapeThreeTimes, escape.Path);
        Assert.Equal("Stopped (Esc pressed 3 times). Ask in chat to continue.", escape.Notice);

        stop.Tray();
        var tray = Assert.NotNull(stop.Last);
        Assert.Equal(PcStopPath.Tray, tray.Path);
        Assert.Null(tray.Notice);
    }

    [Fact]
    public async Task A_stop_while_paused_cancels_the_held_step_and_clears_the_pause()
    {
        var control = new PcRunControl();
        using var pause = new PcPauseControl(new PcFakeClock(), control);
        var stop = new PcStopControl(control);
        using var joined = pause.Join("chat-1", Excel, new PcWaitState());
        using var watch = control.Watch(CancellationToken.None, "chat-1", Excel, 0x1234);
        pause.Pause(PcScope.All, PcPauseSource.Pill);
        var held = pause.Held(Excel, watch.Token);
        Assert.False(held.IsCompleted);

        stop.StopKey();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => held.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(PcStopSource.StopKey, watch.StoppedBy);
        Assert.False(pause.IsPaused(Excel));
        Assert.False(pause.Latched);
    }

    [Fact]
    public void A_pill_row_stop_clears_only_that_apps_pause()
    {
        var control = new PcRunControl();
        using var pause = new PcPauseControl(new PcFakeClock(), control);
        var stop = new PcStopControl(control);
        using var excel = pause.Join("chat-1", Excel, new PcWaitState());
        using var outlook = pause.Join("chat-1", Outlook, new PcWaitState());
        pause.Pause(PcScope.App(Excel), PcPauseSource.Row);
        pause.Pause(PcScope.App(Outlook), PcPauseSource.Row);

        stop.PillRow(Excel);

        Assert.Equal(new[] { Outlook }, pause.PausedApps);
    }
}
