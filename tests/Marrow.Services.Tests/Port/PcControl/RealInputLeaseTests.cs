using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-18; DESIGN.md 2.7 "Borrow sequence" and "Watchdog"). The real-input
// lease on fakes: nothing runs until activation and restore are proven; the gate, its words and the user's two answers;
// first come, first served; the re-verify after swallowing; the target checked before any key; every up owed; the user's
// window, control, Z-order and cursor put back; held input replayed with its own tag or kept; Esc and the chords in a
// borrow; the watchdog; a stop in the middle. No test sends real input.
[Trait("Decision", "ED-NEW-pcctl-18")]
public sealed class RealInputLeaseTests
{
    private const ushort Ctrl = 0x11;
    private const ushort Alt = 0x12;

    [Fact]
    public async Task Nothing_is_borrowed_until_activation_and_restore_are_proven()
    {
        var rig = new PcBorrowRig();

        var outcome = await rig.Lease.Borrow(rig.Click(), CancellationToken.None);

        Assert.Equal(PcBorrowStatus.NeedsRealInput, outcome.Status);
        Assert.Equal(PcRealInputReason.ActivationUnproven, outcome.Reason);
        Assert.Empty(rig.Host.Sent);
        Assert.Empty(rig.Hold.Log);

        rig.Prove(PcBorrowProofs.Pointer).Prove(PcBorrowProofs.Restore, "mspaint.exe");
        Assert.Null(rig.Lease.Unproven("mspaint.exe", PcBorrowKind.Pointer));
        Assert.Equal(PcRealInputReason.ActivationUnproven, rig.Lease.Unproven("mspaint.exe", PcBorrowKind.Focus));
        Assert.Equal(PcRealInputReason.ActivationUnproven, rig.Lease.Unproven("notepad.exe", PcBorrowKind.Pointer));
    }

    [Fact]
    public async Task A_click_while_the_user_is_idle_runs_at_once_and_puts_everything_back()
    {
        var rig = new PcBorrowRig().ProveAll();
        var chips = new List<string>();
        rig.Lease.Used += (stream, detail) => chips.Add(detail);

        var outcome = await rig.Lease.Borrow(rig.Click(), CancellationToken.None);

        Assert.True(outcome.Done);
        Assert.True(outcome.ForegroundRestored);
        var (events, tag) = Assert.Single(rig.Host.Sent);
        Assert.NotEqual(0UL, tag);
        Assert.Equal(3, events.Count);
        Assert.Equal(new[] { "begin", "end" }, rig.Hold.Log);
        Assert.Equal(PcBorrowRig.User, rig.Host.Foreground);
        Assert.Equal(PcBorrowRig.UserControl, rig.Host.FocusedControl);
        Assert.Equal(new PcPoint(500, 500), rig.Host.Cursor);
        Assert.Contains("zorder", rig.Host.Calls);
        Assert.Equal(new[] { "one click in mspaint" }, chips);
        // Tactus's own events matched its ledger: they are not the user's activity.
        Assert.Null(rig.Monitor.Snapshot().LastUserMs);
    }

    [Fact]
    public async Task The_gate_waits_while_the_user_moves_the_pointer_and_says_so()
    {
        var rig = new PcBorrowRig().ProveAll();
        var marks = new PcWaitMarks();
        var waits = new List<PcGateCheck>();
        var ended = 0;
        rig.Lease.Waiting += wait => waits.Add(wait.Check);
        rig.Lease.WaitEnded += _ => ended++;
        var chips = 0;
        rig.Lease.Used += (_, _) => chips++;
        rig.UserInput(rig.Move(5, 5));
        var start = rig.Clock.NowMs;

        var outcome = await rig.Drive(rig.Lease.Borrow(rig.Click() with { Waiting = marks.Mark }, CancellationToken.None));

        Assert.True(outcome.Done);
        Assert.True(rig.Clock.NowMs - start >= PcInputGate.PointerStillMs);
        var wait = Assert.Single(waits);
        Assert.Equal(PcGateBlock.Pointer, wait.Block);
        Assert.Equal("Hands off for a moment", wait.Waiting);
        Assert.Equal(new[] { PcWaitReason.HandsOff }, marks.Reasons);
        Assert.Equal(1, marks.Released);
        Assert.Equal(1, ended);
        Assert.Equal(0, chips);
    }

    [Fact]
    public async Task Not_now_defers_the_step_and_nothing_is_held()
    {
        var rig = new PcBorrowRig().ProveAll();
        rig.Lease.Waiting += wait => wait.NotNow();
        rig.UserInput(rig.Key(0x41, true));

        var outcome = await rig.Drive(rig.Lease.Borrow(rig.Click(), CancellationToken.None));

        Assert.Equal(PcBorrowStatus.Deferred, outcome.Status);
        Assert.Empty(rig.Host.Sent);
        Assert.Empty(rig.Hold.Log);
    }

    [Fact]
    public async Task Not_now_on_a_protected_foreground_answers_foreground_protected()
    {
        var rig = new PcBorrowRig(PcForegroundProtection.Elevated).ProveAll();
        var words = new List<string>();
        rig.Lease.Waiting += wait =>
        {
            words.Add(wait.Check.Waiting);
            wait.NotNow();
        };

        var outcome = await rig.Drive(rig.Lease.Borrow(rig.Click(), CancellationToken.None));

        Assert.Equal(PcBorrowStatus.NeedsRealInput, outcome.Status);
        Assert.Equal(PcRealInputReason.ForegroundProtected, outcome.Reason);
        Assert.Equal(new[] { "Waiting until you leave the admin window" }, words);
        Assert.Empty(rig.Host.Sent);
    }

    [Fact]
    public async Task Do_it_now_passes_the_stillness_but_never_a_held_key()
    {
        var rig = new PcBorrowRig().ProveAll();
        var checks = new List<PcGateBlock>();
        rig.Lease.Waiting += wait =>
        {
            checks.Add(wait.Check.Block);
            wait.DoItNow();
        };
        rig.UserInput(rig.Key(0x41, true) with { HeldKeys = 1 });
        var borrow = rig.Lease.Borrow(rig.Click(), CancellationToken.None);
        await PcTestWait.Until(() => rig.Clock.Pending > 0);
        rig.Clock.Advance(RealInputLease.PollMs);
        Assert.True(await PcTestWait.StillWaiting(borrow));

        rig.UserInput(rig.Key(0x41, false));
        var outcome = await rig.Drive(borrow);

        Assert.True(outcome.Done);
        Assert.Equal(PcGateBlock.Held, checks[0]);
        // The key just came up: the keyboard is not still, but Do it now passes that.
        Assert.True(rig.Clock.NowMs - rig.Monitor.Snapshot().LastKeyMs < PcInputGate.KeysStillMs);
    }

    [Fact]
    public async Task Unhealthy_hooks_answer_monitor_unhealthy_after_two_seconds()
    {
        var rig = new PcBorrowRig().ProveAll();
        var words = new List<string>();
        rig.Lease.Waiting += wait => words.Add(wait.Check.Waiting);
        rig.Monitor.Installed(false);
        rig.Monitor.Installed(true);
        var start = rig.Clock.NowMs;

        var outcome = await rig.Drive(rig.Lease.Borrow(rig.Click(), CancellationToken.None));

        Assert.Equal(PcBorrowStatus.NeedsRealInput, outcome.Status);
        Assert.Equal(PcRealInputReason.MonitorUnhealthy, outcome.Reason);
        Assert.True(rig.Clock.NowMs - start >= RealInputLease.UnhealthyWaitMs);
        Assert.Equal(new[] { "Real mouse and keyboard off for a moment: restarting the input check" }, words);
        Assert.Empty(rig.Host.Sent);
    }

    [Fact]
    public async Task Nths_own_card_holds_the_gate()
    {
        var rig = new PcBorrowRig().ProveAll();
        rig.CardUp = true;
        var borrow = rig.Lease.Borrow(rig.Click(), CancellationToken.None);
        await PcTestWait.Until(() => rig.Clock.Pending > 0);
        rig.Clock.Advance(RealInputLease.PollMs);
        Assert.True(await PcTestWait.StillWaiting(borrow));

        rig.CardUp = false;
        var outcome = await rig.Drive(borrow);

        Assert.True(outcome.Done);
    }

    [Fact]
    public async Task Borrows_are_served_first_come_first_served_one_waiting_per_stream()
    {
        var rig = new PcBorrowRig().ProveAll();
        var queued = new List<(string Stream, bool Queued)>();
        rig.Lease.Waiting += wait => queued.Add((wait.Stream, wait.Queued));
        rig.UserInput(rig.Move(1, 1));

        var first = rig.Lease.Borrow(rig.Click(PcBorrowRig.Paint), CancellationToken.None);
        await PcTestWait.Until(() => rig.Lease.Waits.Count == 1);
        var second = rig.Lease.Borrow(rig.Click(PcBorrowRig.Excel), CancellationToken.None);
        await PcTestWait.Until(() => rig.Lease.Waits.Count == 2);
        var again = await rig.Lease.Borrow(rig.Click(PcBorrowRig.Paint), CancellationToken.None);

        Assert.Equal(PcBorrowStatus.NeedsRealInput, again.Status);
        Assert.Equal(new[] { PcBorrowRig.Paint, PcBorrowRig.Excel }, rig.Lease.Waits.Select(w => w.Stream));
        Assert.True((await rig.Drive(first)).Done);
        Assert.True((await rig.Drive(second)).Done);
        Assert.Equal(2, rig.Host.Sent.Count);
        Assert.Contains((PcBorrowRig.Excel, true), queued);
        Assert.DoesNotContain((PcBorrowRig.Paint, true), queued);
    }

    [Fact]
    public async Task Input_in_the_gap_between_the_gate_and_the_hold_lets_go_and_waits_again()
    {
        var rig = new PcBorrowRig().ProveAll();
        rig.Hold.OnBegin = () =>
        {
            rig.Hold.OnBegin = null;
            rig.Hold.Deliver();
        };

        var outcome = await rig.Drive(rig.Lease.Borrow(rig.Click(), CancellationToken.None));

        Assert.True(outcome.Done);
        Assert.Equal(new[] { "begin", "end", "begin", "end" }, rig.Hold.Log);
        Assert.Single(rig.Host.Sent);
    }

    [Fact]
    public async Task The_users_held_keys_go_back_to_their_window_with_their_own_tag_and_moves_land_on_the_cursor()
    {
        var rig = new PcBorrowRig().ProveAll();
        rig.Host.OnSend = _ =>
        {
            rig.Host.OnSend = null;
            rig.UserInput(rig.Key(0x42, true));
            rig.UserInput(rig.Key(0x42, false));
            rig.UserInput(rig.Move(7, -3));
        };

        var outcome = await rig.Lease.Borrow(rig.Click(), CancellationToken.None);

        Assert.True(outcome.Done);
        Assert.Equal(2, rig.Host.Sent.Count);
        var replay = rig.Host.Sent[1];
        Assert.NotEqual(rig.Host.Sent[0].Tag, replay.Tag);
        Assert.Equal(new[] { PcInputEvent.Key(0x42, 0x142, 0), PcInputEvent.Key(0x42, 0x142, PcInputPlan.KEY_UP) }, replay.Events);
        Assert.Equal(PcBorrowRig.User, rig.Host.SentWhileFront[1]);
        Assert.Equal(new PcPoint(507, 497), rig.Host.Cursor);
        // The held keys were the user's activity, and marked no window.
        Assert.NotNull(rig.Monitor.Snapshot().LastKeyMs);
        Assert.Equal(0, rig.Monitor.Version(PcBorrowRig.Target));
    }

    [Fact]
    public async Task A_failed_restore_keeps_the_users_keys_until_their_click_then_puts_them_back()
    {
        var rig = new PcBorrowRig().ProveAll();
        rig.Host.RestoreFailures = int.MaxValue;
        var holding = new List<bool>();
        rig.Lease.HoldingKeysChanged += holding.Add;
        rig.Host.OnSend = _ =>
        {
            rig.Host.OnSend = null;
            rig.UserInput(rig.Key(0x43, true));
            rig.UserInput(rig.Key(0x43, false));
        };

        var outcome = await rig.Drive(rig.Lease.Borrow(rig.Click(), CancellationToken.None));

        Assert.True(outcome.Done);
        Assert.False(outcome.ForegroundRestored);
        Assert.Equal(1, outcome.KeptKeys);
        Assert.Equal(RealInputLease.RestoreTries, rig.Host.Calls.Count(c => c == "restore"));
        Assert.True(rig.Lease.HoldingKeys);
        Assert.Equal(new[] { "begin", "keys" }, rig.Hold.Log);

        // Keys typed now are held too (never to the target); the mouse is free.
        rig.UserInput(rig.Key(0x44, true));
        rig.UserInput(rig.Move(2, 2));
        Assert.Equal(2, rig.Lease.KeptKeys);
        Assert.False(rig.Lease.PutBack());
        Assert.Single(rig.Host.Sent);

        // The user's own click moves the foreground back: the hold ends.
        rig.Host.Foreground = PcBorrowRig.User;
        rig.Host.FocusedControl = PcBorrowRig.UserControl;
        await PcTestWait.Until(() => rig.Clock.Pending > 0);
        rig.Clock.Advance(RealInputLease.KeyHoldPollMs);
        await PcTestWait.Until(() => !rig.Lease.HoldingKeys);
        Assert.Equal(new[] { true, false }, holding);
        Assert.Equal("end", rig.Hold.Log[^1]);

        Assert.True(rig.Lease.PutBack());
        Assert.Equal(0, rig.Lease.KeptKeys);
        var putBack = rig.Host.Sent[^1].Events;
        Assert.Equal(new ushort[] { 0x43, 0x43, 0x44 }, putBack.Select(e => e.Vk));
        Assert.Equal(PcBorrowRig.User, rig.Host.SentWhileFront[^1]);
    }

    [Fact]
    public async Task Esc_in_a_borrow_stops_that_stream_and_is_not_replayed()
    {
        var rig = new PcBorrowRig().ProveAll();
        var stops = new List<(PcScope Scope, PcStopSource Source)>();
        rig.Control.Stopped += (scope, source) =>
        {
            lock (stops) stops.Add((scope, source));
        };
        var escaped = new List<string>();
        rig.Lease.EscapeInBorrow += escaped.Add;
        rig.Host.OnSend = _ =>
        {
            rig.Host.OnSend = null;
            rig.UserInput(rig.Key(0x1B, true));
            rig.UserInput(rig.Key(0x1B, false));
            rig.UserInput(rig.Key(0x45, true));
            rig.UserInput(rig.Key(0x45, false));
        };

        await rig.Lease.Borrow(rig.Click(), CancellationToken.None);

        Assert.Equal(new[] { PcBorrowRig.Paint }, escaped);
        await PcTestWait.Until(() =>
        {
            lock (stops) return stops.Count == 1;
        });
        Assert.Equal((PcScope.App(PcBorrowRig.Paint), PcStopSource.Escape), stops[0]);
        Assert.Equal(new ushort[] { 0x45, 0x45 }, rig.Host.Sent[^1].Events.Select(e => e.Vk));
    }

    [Fact]
    public async Task A_Tactus_chord_swallowed_in_a_borrow_is_not_replayed_but_its_modifiers_are()
    {
        var rig = new PcBorrowRig().ProveAll();
        rig.Lease.Chord = (key, modifiers) => key == 0x20 && modifiers == 3;
        rig.Host.OnSend = _ =>
        {
            rig.Host.OnSend = null;
            rig.UserInput(rig.Key(Ctrl, true));
            rig.UserInput(rig.Key(Alt, true));
            rig.UserInput(rig.Key(0x20, true) with { Modifiers = 3 });
            rig.UserInput(rig.Key(0x20, false) with { Modifiers = 3 });
            rig.UserInput(rig.Key(Alt, false));
            rig.UserInput(rig.Key(Ctrl, false));
        };

        await rig.Lease.Borrow(rig.Click(), CancellationToken.None);

        Assert.Equal(new[] { Ctrl, Alt, Alt, Ctrl }, rig.Host.Sent[^1].Events.Select(e => e.Vk));
    }

    [Fact]
    public async Task A_focus_step_brings_the_target_in_front_focuses_its_control_and_checks_before_any_key()
    {
        var rig = new PcBorrowRig().ProveAll();
        var focused = 0;
        var request = rig.Keys(PcBorrowRig.Paint, 0x41) with
        {
            Focus = _ =>
            {
                focused++;
                return Task.FromResult(true);
            },
            FocusControl = PcBorrowRig.TargetControl,
        };

        var outcome = await rig.Lease.Borrow(request, CancellationToken.None);

        Assert.True(outcome.Done);
        Assert.Equal(1, focused);
        Assert.Contains("set-foreground", rig.Host.Calls);
        Assert.Equal(PcBorrowRig.Target, Assert.Single(rig.Host.SentWhileFront));
        Assert.Equal(PcBorrowRig.User, rig.Host.Foreground);
    }

    [Fact]
    public async Task A_target_that_does_not_come_in_front_gets_no_keys()
    {
        var rig = new PcBorrowRig().ProveAll();
        rig.Host.ForegroundAllowed = false;

        var outcome = await rig.Lease.Borrow(rig.Keys(PcBorrowRig.Paint, 0x41), CancellationToken.None);

        Assert.Equal(PcBorrowStatus.NotSent, outcome.Status);
        Assert.Equal("the window did not come in front; nothing was typed", outcome.Reason);
        Assert.Empty(rig.Host.Sent);
        Assert.Equal(new[] { "begin", "end" }, rig.Hold.Log);
        Assert.DoesNotContain("restore", rig.Host.Calls);
    }

    [Fact]
    public async Task A_control_that_does_not_take_the_focus_gets_no_keys()
    {
        var rig = new PcBorrowRig().ProveAll();
        rig.Host.FocusOf[PcBorrowRig.Target] = PcBorrowRig.Other;

        var outcome = await rig.Lease.Borrow(rig.Keys(PcBorrowRig.Paint, 0x41) with { FocusControl = PcBorrowRig.TargetControl }, CancellationToken.None);

        Assert.Equal(PcBorrowStatus.NotSent, outcome.Status);
        Assert.Empty(rig.Host.Sent);
        Assert.Equal(PcBorrowRig.User, rig.Host.Foreground);
    }

    [Fact]
    public async Task Caption_activation_clicks_the_caption_under_the_same_tag_first()
    {
        var rig = new PcBorrowRig().Prove(PcBorrowProofs.Caption).Prove(PcBorrowProofs.Restore);
        rig.Host.CaptionPoint = new PcPoint(400, 10);

        var outcome = await rig.Lease.Borrow(rig.Keys(PcBorrowRig.Paint, 0x41), CancellationToken.None);

        Assert.True(outcome.Done);
        Assert.Equal(2, rig.Host.Sent.Count);
        Assert.Equal(rig.Host.Sent[0].Tag, rig.Host.Sent[1].Tag);
        Assert.Contains(rig.Host.Sent[0].Events, e => e.Flags == PcInputPlan.LEFTDOWN);
        Assert.DoesNotContain("set-foreground", rig.Host.Calls);
        Assert.Null(rig.Monitor.Snapshot().LastUserMs);

        rig.Host.CaptionPoint = null;
        var none = await rig.Lease.Borrow(rig.Keys(PcBorrowRig.Paint, 0x41), CancellationToken.None);
        Assert.Equal(PcBorrowStatus.NotSent, none.Status);
        Assert.Equal(2, rig.Host.Sent.Count);
    }

    [Fact]
    public async Task A_covered_point_is_raised_once_and_otherwise_not_clicked()
    {
        var rig = new PcBorrowRig().ProveAll();
        rig.Host.Under = _ => PcBorrowRig.Other;

        var outcome = await rig.Lease.Borrow(rig.Click(), CancellationToken.None);

        Assert.Equal(PcBorrowStatus.NotSent, outcome.Status);
        Assert.Equal(PcErrorCode.OccludedPoint, outcome.Code);
        Assert.Contains("raise", rig.Host.Calls);
        Assert.Empty(rig.Host.Sent);
    }

    [Fact]
    public async Task The_watchdog_lets_go_of_a_borrow_past_its_cap_and_sends_every_up()
    {
        var rig = new PcBorrowRig().ProveAll();
        var desktop = rig.Host.Desktop();
        var (arrays, _) = PcBorrowRung.DragArrays([new PcPoint(10, 10), new PcPoint(50, 50)], [Ctrl], 0, desktop, rig.Host.ScanCode);
        var request = new PcBorrowRequest(PcBorrowRig.Paint, PcBorrowKind.Pointer, PcBorrowRig.Target, "mspaint.exe")
        {
            Chunks = arrays,
            GapsMs = [800, 16],
            HoldCapMs = RealInputLease.DragCapMs,
        };

        var outcome = await rig.Drive(rig.Lease.Borrow(request, CancellationToken.None));

        Assert.Equal(PcBorrowStatus.Failed, outcome.Status);
        Assert.Equal(PcErrorCode.Timeout, outcome.Code);
        Assert.Equal(2, rig.Host.Sent.Count);
        var ups = rig.Host.Sent[1].Events;
        Assert.Equal(PcInputPlan.LEFTUP, ups[0].Flags);
        Assert.Equal(Ctrl, ups[1].Vk);
        Assert.Equal(PcInputPlan.KEY_UP, ups[1].Flags & PcInputPlan.KEY_UP);
        Assert.Equal("end", rig.Hold.Log[^1]);
        Assert.Equal(PcBorrowRig.User, rig.Host.Foreground);

        // The abandoned worker never sends after the watchdog let go.
        rig.Clock.Advance(1_000);
        await Task.Delay(50);
        Assert.Equal(2, rig.Host.Sent.Count);
    }

    [Fact]
    public async Task A_hung_focus_call_is_abandoned_when_the_watchdog_lets_go()
    {
        var rig = new PcBorrowRig().ProveAll();
        var never = new TaskCompletionSource<bool>();
        var request = rig.Keys(PcBorrowRig.Paint, 0x41) with { Focus = _ => never.Task };

        var outcome = await rig.Drive(rig.Lease.Borrow(request, CancellationToken.None));

        Assert.Equal(PcBorrowStatus.Failed, outcome.Status);
        Assert.Empty(rig.Host.Sent);
        Assert.Equal(PcBorrowRig.User, rig.Host.Foreground);
        Assert.False(rig.Hold.Swallowing);
    }

    [Fact]
    public async Task A_stop_in_the_middle_of_a_drag_still_sends_the_ups_and_puts_things_back()
    {
        var rig = new PcBorrowRig().ProveAll();
        var (arrays, gaps) = PcBorrowRung.DragArrays([new PcPoint(10, 10), new PcPoint(50, 50), new PcPoint(90, 90)], [], 100, rig.Host.Desktop(), rig.Host.ScanCode);
        var request = new PcBorrowRequest(PcBorrowRig.Paint, PcBorrowKind.Pointer, PcBorrowRig.Target, "mspaint.exe")
        {
            Chunks = arrays,
            GapsMs = gaps,
            HoldCapMs = RealInputLease.DragCapMs,
        };
        using var stop = new CancellationTokenSource();
        var borrow = rig.Lease.Borrow(request, stop.Token);
        await PcTestWait.Until(() => rig.Host.Sent.Count == 1);

        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => borrow.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(PcInputPlan.LEFTUP, rig.Host.Sent[^1].Events[0].Flags);
        Assert.Equal(PcBorrowRig.User, rig.Host.Foreground);
        Assert.False(rig.Hold.Swallowing);
        Assert.Empty(rig.Lease.Waits);
    }

    [Fact]
    public async Task A_key_held_from_before_the_hold_that_repeats_ends_the_borrow()
    {
        var rig = new PcBorrowRig().ProveAll();
        var (arrays, _) = PcBorrowRung.DragArrays([new PcPoint(10, 10), new PcPoint(50, 50)], [], 0, rig.Host.Desktop(), rig.Host.ScanCode);
        var request = new PcBorrowRequest(PcBorrowRig.Paint, PcBorrowKind.Pointer, PcBorrowRig.Target, "mspaint.exe") { Chunks = arrays, HoldCapMs = RealInputLease.DragCapMs };
        rig.Host.OnSend = _ =>
        {
            rig.Host.OnSend = null;
            rig.Hold.AbortRequested = true;
        };

        var outcome = await rig.Drive(rig.Lease.Borrow(request, CancellationToken.None));

        Assert.Equal(PcBorrowStatus.Failed, outcome.Status);
        Assert.Equal(2, rig.Host.Sent.Count);
        Assert.Equal(PcInputPlan.LEFTUP, rig.Host.Sent[1].Events[0].Flags);
    }

    [Fact]
    public void Tactus_input_does_not_read_as_input_the_hooks_missed()
    {
        var rig = new PcBorrowRig();
        var ledger = new PcInjectionLedger(0x5, rig.Clock.NowMs, [PcLedgerEvent.Move()]);
        rig.Monitor.Open(ledger);
        rig.Clock.Advance(300);

        Assert.False(rig.Monitor.LastInputSeen(rig.Clock.NowMs));
        rig.Monitor.Close(ledger);
        rig.Clock.Advance(300);
        Assert.False(rig.Monitor.LastInputSeen(rig.Clock.NowMs - 300));
        Assert.True(rig.Monitor.LastInputSeen(rig.Clock.NowMs));
    }
}
