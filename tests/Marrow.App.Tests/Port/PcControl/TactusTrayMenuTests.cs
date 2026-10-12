using Marrow.App.Port.PcControl;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Mark;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0145, OD-0146, ED-NEW-pcctl-15; DESIGN.md 10.1, 10.2 path 8, 10.10). The tray's Tactus
// menu: Pause, or Resume when every live app is paused; Stop; Answer only while an ask waits; the mark's drawing; and the
// icon only while a stream is live.
[Trait("Decision", "ED-NEW-pcctl-15")]
public sealed class TactusTrayMenuTests
{
    private const string Excel = "exe:c:\\office\\excel.exe";
    private const string Outlook = "exe:c:\\office\\outlook.exe";

    [Fact]
    public void The_menu_offers_Pause_and_Stop_and_its_Pause_pauses_every_app()
    {
        var control = new PcRunControl();
        using var pause = new PcPauseControl(new PcFakeClock(), control);
        var stop = new PcStopControl(control);
        using var excel = pause.Join("chat-1", Excel, new PcWaitState());

        var items = TactusTrayMenu.Items(pause, stop);

        Assert.Equal(new[] { "Pause Tactus", "Stop Tactus" }, items.Select(i => i.Label));
        items[0].Run();
        Assert.True(pause.IsPaused(Excel));
        Assert.True(pause.Latched);
        Assert.Equal(PcPauseSource.Tray, pause.Status(Excel)?.Source);
    }

    [Fact]
    public void It_reads_Resume_only_when_every_live_app_is_paused()
    {
        var control = new PcRunControl();
        using var pause = new PcPauseControl(new PcFakeClock(), control);
        var stop = new PcStopControl(control);
        using var excel = pause.Join("chat-1", Excel, new PcWaitState());
        using var outlook = pause.Join("chat-2", Outlook, new PcWaitState());

        pause.Pause(PcScope.App(Excel), PcPauseSource.Row);
        Assert.Equal(TactusTrayMenu.PauseLabel, TactusTrayMenu.Items(pause, stop)[0].Label);

        pause.Pause(PcScope.App(Outlook), PcPauseSource.Row);
        var resume = TactusTrayMenu.Items(pause, stop)[0];
        Assert.Equal(TactusTrayMenu.ResumeLabel, resume.Label);
        resume.Run();
        Assert.Empty(pause.PausedApps);
    }

    [Fact]
    public void Its_Stop_stops_everything_from_the_tray()
    {
        var control = new PcRunControl();
        var stops = new List<(PcScope, PcStopSource)>();
        control.Stopped += (scope, source) => stops.Add((scope, source));
        using var pause = new PcPauseControl(new PcFakeClock(), control);
        var stop = new PcStopControl(control);

        TactusTrayMenu.Items(pause, stop)[1].Run();

        Assert.Equal(new[] { (PcScope.All, PcStopSource.Tray) }, stops);
    }

    [Fact]
    public void Answer_is_offered_only_while_an_ask_waits()
    {
        var control = new PcRunControl();
        using var pause = new PcPauseControl(new PcFakeClock(), control);
        var stop = new PcStopControl(control);
        var waiting = false;
        var answered = 0;

        Assert.Equal(2, TactusTrayMenu.Items(pause, stop, () => waiting, () => answered++).Count);
        waiting = true;
        Assert.Equal(2, TactusTrayMenu.Items(pause, stop, () => waiting).Count);
        var items = TactusTrayMenu.Items(pause, stop, () => waiting, () => answered++);
        Assert.Equal(TactusTrayMenu.AnswerLabel, items[2].Label);
        items[2].Run();
        Assert.Equal(1, answered);
    }

    [Fact]
    public void The_mark_reads_Ready_Working_or_Paused_and_shows_only_while_a_stream_is_live()
    {
        var control = new PcRunControl();
        using var pause = new PcPauseControl(new PcFakeClock(), control);
        var lanes = new PcLanes();

        Assert.False(TactusTrayMenu.Shows(pause, lanes));
        Assert.Equal(TactusSmallState.Ready, TactusTrayMenu.State(pause, lanes));

        var call = pause.Join("chat-1", Excel, new PcWaitState());
        Assert.True(TactusTrayMenu.Shows(pause, lanes));
        Assert.Equal(TactusSmallState.Ready, TactusTrayMenu.State(pause, lanes));

        pause.Pause(PcScope.All, PcPauseSource.Chord);
        Assert.Equal(TactusSmallState.Paused, TactusTrayMenu.State(pause, lanes));

        pause.Resume(PcScope.All, PcPauseSource.Chord);
        call.Dispose();
        pause.EndTurn("chat-1");
        Assert.False(TactusTrayMenu.Shows(pause, lanes));

        pause.Pause(PcScope.All, PcPauseSource.Tray);
        Assert.True(TactusTrayMenu.Shows(pause, lanes));
        Assert.Equal(TactusSmallState.Paused, TactusTrayMenu.State(pause, lanes));
    }

    [Fact]
    public async Task A_running_call_shows_the_Working_mark()
    {
        var control = new PcRunControl();
        using var pause = new PcPauseControl(new PcFakeClock(), control);
        var lanes = new PcLanes();
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = lanes.Run(Excel, async _ =>
        {
            entered.TrySetResult();
            return await release.Task;
        }, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(TactusTrayMenu.Shows(pause, lanes));
        Assert.Equal(TactusSmallState.Working, TactusTrayMenu.State(pause, lanes));
        release.SetResult(1);
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(TactusTrayMenu.Shows(pause, lanes));
    }
}
