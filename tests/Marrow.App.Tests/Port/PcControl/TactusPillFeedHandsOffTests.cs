// PORT-ONLY: Tactus, what the pill says about the real mouse and keyboard (ED-NEW-pcctl-38; DESIGN.md 2.7 "Borrow sequence",
// 4.5 "Hands off", "Done", "Borrow chip", "Kept keys", "Protected foreground", "Monitor restarting", "Keys held after a
// failed restore"). One request for every shown wait, the first app's step with "+1 more", its sentence in the hint row
// and the ring on the mark; "Release the mouse first" when Do it now meets a held key; Not now answers it; the gate's own
// words with Not now behind a protected window and "Real mouse and keyboard off for a moment" while the input check
// restarts; "Esc stops this step" while the borrow runs, then "Done, all yours"; the 1 s chip for a borrow that went at
// once; after a failed restore the held keys, then the kept keys with Put them back; "Stop key is taken" until the stop
// key works or Settings opens; "Put back 6 keys". On the lease's fakes and a manual clock.
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-38")]
public sealed class TactusPillFeedHandsOffTests
{
    private static PcBorrowWait Still(string stream, string name) =>
        new(stream, "a step in " + name) { Words = PcLeaseFeedRig.ClickSave };

    [Fact]
    public async Task One_request_shows_the_first_app_s_step_with_one_more_and_Do_it_now_with_a_held_key_says_release_first()
    {
        using var rig = new PcLeaseFeedRig();
        var asked = 0;
        rig.Feed.Asked += () => asked++;
        rig.UserInput(rig.Key(0x41, true));
        var paint = Still(PcLeaseFeedRig.Paint, "Mspaint");
        var excel = Still(PcLeaseFeedRig.Excel, "Excel");

        var first = rig.Lease.Still(paint, null, CancellationToken.None);
        var second = rig.Lease.Still(excel, null, CancellationToken.None);
        await PcTestWait.Until(() => rig.Feed.Facts().Notices.Any(n => n.Line.EndsWith("+1 more", StringComparison.Ordinal)));

        var both = rig.View();
        Assert.Equal("Hands off for a moment", both.Title);
        Assert.Equal("Click Save in Mspaint +1 more", both.Line);
        Assert.Equal("Tactus needs a still moment for one step: click Save in Mspaint.", both.Hint);
        Assert.Equal(PillGroupKind.HandsOff, both.Group);
        Assert.Equal(TactusMarkState.HandsOff, both.Mark);
        Assert.Same(paint, rig.Feed.HandsOff);
        Assert.Equal(1, asked);

        rig.Feed.DoItNow();
        Assert.True(paint.DoItNowChosen);
        Assert.Equal("Release the keys first", rig.View().Line);

        excel.NotNow();
        Assert.Equal(PcBorrowStatus.Deferred, await rig.Drive(second));
        rig.UserInput(rig.Key(0x41, false));
        Assert.Equal(PcBorrowStatus.Done, await rig.Drive(first));
        Assert.Null(rig.Feed.HandsOff);
        Assert.DoesNotContain(rig.Feed.Facts().Notices, n => n.Kind == PillNoticeKind.HandsOff);
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Not_now_answers_the_request_the_pill_shows_and_the_next_one_takes_the_pill()
    {
        using var rig = new PcLeaseFeedRig();
        rig.UserInput(rig.Key(0x41, true));
        var paint = Still(PcLeaseFeedRig.Paint, "Mspaint");
        var excel = Still(PcLeaseFeedRig.Excel, "Excel");
        var first = rig.Lease.Still(paint, null, CancellationToken.None);
        var second = rig.Lease.Still(excel, null, CancellationToken.None);
        await PcTestWait.Until(() => rig.Feed.Facts().Notices.Any(n => n.Line.EndsWith("+1 more", StringComparison.Ordinal)));

        rig.Feed.NotNow();
        Assert.Equal(PcBorrowStatus.Deferred, await rig.Drive(first));
        Assert.False(excel.NotNowChosen);
        Assert.Equal("Click Save in Excel", rig.View().Line);
        Assert.Same(excel, rig.Feed.HandsOff);

        rig.Feed.NotNow();
        Assert.Equal(PcBorrowStatus.Deferred, await rig.Drive(second));
    }

    [Fact]
    public async Task A_borrow_that_waited_says_Esc_stops_it_while_it_runs_then_Done_and_one_at_once_shows_the_chip()
    {
        using var rig = new PcLeaseFeedRig();
        string? running = null;
        rig.Lease.RunningChanged += _ => running ??= rig.View().Hint;

        var chip = await rig.Lease.Borrow(rig.Click(), CancellationToken.None);
        Assert.True(chip.Done);
        Assert.Contains(rig.Feed.Facts().Notices, n => n.Kind == PillNoticeKind.Borrowed && n.Title == "Used the mouse for one click in Mspaint" && n.Lasts == 1);
        Assert.Null(running);

        rig.UserInput(rig.Move(5, 5));
        var waited = await rig.Drive(rig.Lease.Borrow(rig.Click(), CancellationToken.None));

        Assert.True(waited.Done);
        Assert.Equal("Tactus needs the mouse for one step: click Save in Mspaint. Esc stops this step", running);
        var done = Assert.Single(rig.Feed.Facts().Notices, n => n.Kind == PillNoticeKind.Done);
        Assert.Equal("Done, all yours", done.Title);
        Assert.Equal("Clicked Save in Mspaint", done.Line);
        Assert.Equal(1.2, done.Lasts);
        Assert.DoesNotContain(rig.Feed.Facts().Notices, n => n.Kind == PillNoticeKind.HandsOff);
    }

    [Fact]
    public async Task Behind_a_protected_window_the_gate_s_words_show_with_Not_now()
    {
        using var rig = new PcLeaseFeedRig(PcForegroundProtection.Elevated);
        var borrow = rig.Lease.Borrow(rig.Click(), CancellationToken.None);
        await PcTestWait.Until(() => rig.Feed.Facts().Notices.Any(n => n.Kind == PillNoticeKind.ProtectedForeground));

        var view = rig.View();
        Assert.Equal("Waiting until you leave the admin window", view.Title);
        Assert.Equal(PillGroupKind.NotNow, view.Group);
        Assert.Null(rig.Feed.HandsOff);

        rig.Feed.NotNow();
        var outcome = await rig.Drive(borrow);
        Assert.Equal(PcBorrowStatus.NeedsRealInput, outcome.Status);
        Assert.Equal(PcRealInputReason.ForegroundProtected, outcome.Reason);
    }

    [Fact]
    public async Task While_the_input_check_restarts_the_pill_says_the_real_mouse_and_keyboard_are_off()
    {
        using var rig = new PcLeaseFeedRig();
        rig.Assembly.Input.Installed(false);
        using var stop = new CancellationTokenSource();
        var borrow = rig.Lease.Borrow(rig.Click(), stop.Token);
        await PcTestWait.Until(() => rig.Feed.Facts().Notices.Any(n => n.Kind == PillNoticeKind.InputRestarting));

        var view = rig.View();
        Assert.Equal("Real mouse and keyboard off for a moment", view.Title);
        Assert.Equal("Restarting the input check", view.Line);
        Assert.Null(rig.Feed.HandsOff);

        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Drive(borrow));
        Assert.DoesNotContain(rig.Feed.Facts().Notices, n => n.Kind == PillNoticeKind.InputRestarting);
    }

    [Fact]
    public async Task After_a_failed_restore_the_held_keys_and_the_kept_keys_show_until_the_user_clicks_and_puts_them_back()
    {
        using var rig = new PcLeaseFeedRig();
        rig.Host.RestoreFailures = int.MaxValue;
        rig.Host.OnSend = _ =>
        {
            rig.Host.OnSend = null;
            rig.UserInput(rig.Key(0x43, true));
            rig.UserInput(rig.Key(0x43, false));
        };

        var outcome = await rig.Drive(rig.Lease.Borrow(rig.Click(), CancellationToken.None));
        Assert.True(outcome.Done);
        Assert.True(rig.Lease.HoldingKeys);
        // while the keys are held, the click is what helps (Put them back cannot work yet)
        var notices = rig.Feed.Facts().Notices;
        Assert.Contains(notices, n => n.Kind == PillNoticeKind.KeysHeld);
        Assert.DoesNotContain(notices, n => n.Kind == PillNoticeKind.KeptKeys);
        Assert.Equal("Click where you were working to keep typing", rig.View().Title);

        rig.Host.Foreground = PcLeaseFeedRig.User;
        rig.Host.FocusedControl = PcLeaseFeedRig.UserControl;
        await PcTestWait.Until(() => rig.Clock.Pending > 0);
        rig.Clock.Advance(RealInputLease.KeyHoldPollMs);
        await PcTestWait.Until(() => !rig.Lease.HoldingKeys);
        await PcTestWait.Until(() => !rig.Feed.Facts().Notices.Any(n => n.Kind == PillNoticeKind.KeysHeld));
        var kept = rig.View();
        Assert.Equal("Tactus kept 1 key you typed", kept.Title);
        Assert.Equal(PillGroupKind.PutBack, kept.Group);

        Assert.True(rig.Lease.PutBack());
        Assert.DoesNotContain(rig.Feed.Facts().Notices, n => n.Kind == PillNoticeKind.KeptKeys);
    }

    [Fact]
    public void The_stop_key_notice_shows_once_while_the_stop_key_is_taken_and_clears_when_it_works_or_Settings_opens()
    {
        using var rig = new PcLeaseFeedRig();
        var changes = 0;
        rig.Feed.Changed += () => changes++;

        rig.Feed.StopKeyTaken();
        rig.Feed.StopKeyTaken();
        var taken = Assert.Single(rig.Feed.Facts().Notices, n => n.Kind == PillNoticeKind.StopKeyTaken);
        var view = rig.View();
        Assert.Equal("Stop key is taken by another app", view.Title);
        Assert.Equal("New steps wait for a stop key that works", view.Line);
        Assert.Equal(PillGroupKind.Settings, view.Group);
        Assert.Equal(0, taken.Lasts);
        Assert.Equal(1, changes);

        rig.Feed.StopKeyWorks();
        Assert.DoesNotContain(rig.Feed.Facts().Notices, n => n.Kind == PillNoticeKind.StopKeyTaken);
        rig.Feed.StopKeyWorks();
        Assert.Equal(2, changes);

        rig.Feed.StopKeyTaken();
        rig.Feed.SettingsOpened();
        Assert.DoesNotContain(rig.Feed.Facts().Notices, n => n.Kind == PillNoticeKind.StopKeyTaken);
        rig.Feed.PutBack(6);
        var put = Assert.Single(rig.Feed.Facts().Notices);
        Assert.Equal("Put back 6 keys", put.Title);
        Assert.Equal("Where you typed them", put.Line);
    }
}
