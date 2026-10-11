using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-14; DESIGN.md 10.1). The pause state shared by every chat: a row
// pause is per app and is not a latch; Pause all latches, so an app that starts work starts paused, until Resume all; a
// chat's Pause covers the apps it used; the chord resumes only when every live app is paused; "Pausing" while a step is
// still running, "Paused" when none is, with the 2 s and 5 s marks; a stop clears what it covers, sleep keeps it.
[Trait("Decision", "ED-NEW-pcctl-14")]
public sealed class PcPauseControlTests
{
    private const string Excel = "exe:c:\\office\\excel.exe";
    private const string Outlook = "exe:c:\\office\\outlook.exe";

    [Fact]
    public void A_row_pause_holds_one_app_and_is_not_a_latch()
    {
        var clock = new PcFakeClock();
        using var pause = new PcPauseControl(clock, new PcRunControl());
        using var excel = pause.Join("chat-1", Excel, new PcWaitState());

        pause.Pause(PcScope.App(Excel), PcPauseSource.Row);

        Assert.True(pause.IsPaused(Excel));
        Assert.False(pause.Latched);
        using var outlook = pause.Join("chat-1", Outlook, new PcWaitState());
        Assert.False(pause.IsPaused(Outlook));
        Assert.Equal(new[] { Excel }, pause.PausedApps);
    }

    [Fact]
    public void Pause_all_latches_so_new_work_starts_paused_and_Resume_all_clears_everything()
    {
        var clock = new PcFakeClock();
        using var pause = new PcPauseControl(clock, new PcRunControl());
        var paused = new List<string>();
        pause.AppPaused += (app, _) => paused.Add(app);
        using var excel = pause.Join("chat-1", Excel, new PcWaitState());

        pause.Pause(PcScope.All, PcPauseSource.Pill);
        var wait = new PcWaitState();
        using var outlook = pause.Join("head-2", Outlook, wait);

        Assert.True(pause.Latched);
        Assert.True(pause.IsPaused(Outlook));
        Assert.Equal(PcWaitReason.Paused, wait.Reason);
        Assert.Equal(new[] { Excel, Outlook }, paused);

        pause.Resume(PcScope.All, PcPauseSource.Pill);
        Assert.False(pause.Latched);
        Assert.Empty(pause.PausedApps);
        Assert.Null(wait.Reason);
    }

    [Fact]
    public void A_chats_Pause_covers_the_apps_that_chat_used_and_its_Resume_only_those()
    {
        var clock = new PcFakeClock();
        using var pause = new PcPauseControl(clock, new PcRunControl());
        using var a = pause.Join("chat-1", Excel, new PcWaitState());
        using var b = pause.Join("chat-2", Outlook, new PcWaitState());

        pause.Pause(PcScope.Thread("chat-1"), PcPauseSource.Chat);
        Assert.Equal(new[] { Excel }, pause.PausedApps);

        pause.Pause(PcScope.App(Outlook), PcPauseSource.Row);
        pause.Resume(PcScope.Thread("chat-1"), PcPauseSource.Chat);
        Assert.Equal(new[] { Outlook }, pause.PausedApps);
    }

    [Fact]
    public void The_chord_pauses_all_when_one_app_runs_and_resumes_all_when_all_are_paused()
    {
        var clock = new PcFakeClock();
        using var pause = new PcPauseControl(clock, new PcRunControl());
        using var a = pause.Join("chat-1", Excel, new PcWaitState());
        using var b = pause.Join("chat-1", Outlook, new PcWaitState());
        pause.Pause(PcScope.App(Excel), PcPauseSource.Row);

        // Outlook still runs: the chord pauses all, so Excel (paused by hand) does not resume unseen.
        Assert.False(pause.Chord());
        Assert.Equal(new[] { Excel, Outlook }, pause.PausedApps);
        Assert.True(pause.Chord());
        Assert.Empty(pause.PausedApps);
    }

    [Fact]
    public void Pausing_while_a_step_runs_then_Paused_with_the_2_s_and_5_s_marks()
    {
        var clock = new PcFakeClock { NowMs = 10_000 };
        using var pause = new PcPauseControl(clock, new PcRunControl());
        var unit = pause.Acting(Excel);
        pause.Pause(PcScope.App(Excel), PcPauseSource.Pill);

        Assert.Equal(PcPausePhase.Pausing, pause.Status(Excel)!.Phase);
        Assert.False(pause.Status(Excel)!.LongUnit);
        clock.NowMs = 12_000;
        Assert.True(pause.Status(Excel)!.LongUnit);
        Assert.False(pause.Status(Excel)!.OffersStop);
        clock.NowMs = 15_000;
        Assert.True(pause.Status(Excel)!.OffersStop);

        unit.Dispose();
        var status = pause.Status(Excel)!;
        Assert.Equal(PcPausePhase.Paused, status.Phase);
        Assert.False(status.OffersStop);
        Assert.Equal(5_000, status.ElapsedMs);
        Assert.Null(pause.Status(Outlook));
    }

    [Fact]
    public async Task Held_waits_for_the_Resume_and_a_stop_token_ends_it()
    {
        var clock = new PcFakeClock();
        using var pause = new PcPauseControl(clock, new PcRunControl());
        Assert.True(pause.Held(Excel, CancellationToken.None).IsCompleted);

        pause.Pause(PcScope.App(Excel), PcPauseSource.Pill);
        var held = pause.Held(Excel, CancellationToken.None);
        using var stop = new CancellationTokenSource();
        var stopped = pause.Held(Excel, stop.Token);
        Assert.True(await PcTestWait.StillWaiting(held));

        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopped);
        clock.NowMs = 4_000;
        pause.Resume(PcScope.App(Excel), PcPauseSource.Pill);
        await held;
        Assert.Equal(new PcPauseSpan(0, 4_000), pause.LastPause(Excel));
    }

    [Fact]
    public void Each_pause_counts_and_cancels_the_apps_paused_token()
    {
        var clock = new PcFakeClock();
        using var pause = new PcPauseControl(clock, new PcRunControl());
        var epoch = pause.Epoch(Excel);
        var token = pause.PausedToken(Excel);

        pause.Pause(PcScope.App(Excel), PcPauseSource.Pill);

        Assert.True(token.IsCancellationRequested);
        Assert.True(pause.PausedSince(Excel, epoch));
        Assert.True(pause.PausedToken(Excel).IsCancellationRequested);
        pause.Resume(PcScope.App(Excel), PcPauseSource.Pill);
        Assert.False(pause.PausedToken(Excel).IsCancellationRequested);
        Assert.False(pause.PausedSince(Excel, pause.Epoch(Excel)));
    }

    [Fact]
    public void A_stop_clears_the_pause_of_what_it_covers_and_sleep_keeps_it()
    {
        var clock = new PcFakeClock();
        var control = new PcRunControl();
        using var pause = new PcPauseControl(clock, control);
        var resumed = 0;
        pause.AppResumed += (_, _) => resumed++;
        using var a = pause.Join("chat-1", Excel, new PcWaitState());
        using var b = pause.Join("chat-2", Outlook, new PcWaitState());
        using var c = pause.Join("chat-1", Outlook, new PcWaitState());
        pause.Pause(PcScope.All, PcPauseSource.Pill);

        control.Sleeping();
        Assert.Equal(2, pause.PausedApps.Count);

        // chat-1's Stop: Outlook is also chat-2's, so only Excel is cleared.
        control.Stop(PcScope.Thread("chat-1"), PcStopSource.Chat);
        Assert.Equal(new[] { Outlook }, pause.PausedApps);
        Assert.True(pause.Latched);

        control.Stop(PcScope.All, PcStopSource.Pill);
        Assert.Empty(pause.PausedApps);
        Assert.False(pause.Latched);
        // A stop is not a Resume: the chat is not continued.
        Assert.Equal(0, resumed);
    }

    [Fact]
    public void The_end_of_a_turn_keeps_a_paused_app_as_the_chats()
    {
        var clock = new PcFakeClock();
        using var pause = new PcPauseControl(clock, new PcRunControl());
        using (pause.Join("chat-1", Excel, new PcWaitState()))
        using (pause.Join("chat-1", Outlook, new PcWaitState()))
            pause.Pause(PcScope.App(Excel), PcPauseSource.Row);

        pause.EndTurn("chat-1");
        pause.Pause(PcScope.App(Outlook), PcPauseSource.Row);
        pause.Resume(PcScope.Thread("chat-1"), PcPauseSource.Chat);

        // Excel stayed the chat's and resumed with it; Outlook was forgotten at the end of the turn.
        Assert.Equal(new[] { Outlook }, pause.PausedApps);
    }
}
