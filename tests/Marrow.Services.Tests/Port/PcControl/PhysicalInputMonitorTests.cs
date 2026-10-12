using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-17; DESIGN.md 2.7). The physical input monitor's state machine on a
// manual clock: whose an event is (a borrow's tag and exact ledger; anything else is the user's), the times, held state
// and windows, dirty versions, swallowed events, the hooks' health against GetLastInputInfo, keeping, and key-downs.
[Trait("Decision", "ED-NEW-pcctl-17")]
public sealed class PhysicalInputMonitorTests
{
    private const nint Notepad = 0x100;
    private const nint Excel = 0x200;

    [Fact]
    public void A_tagged_event_the_ledger_expects_is_Tactus_and_marks_nothing()
    {
        var (clock, monitor) = Healthy();
        var ledger = new PcInjectionLedger(0xABCDEF, clock.NowMs, [PcLedgerEvent.Key(0x41, true), PcLedgerEvent.Key(0x41, false)]);
        monitor.Open(ledger);
        var dirty = new List<nint>();
        monitor.Dirty += dirty.Add;

        monitor.Record(Key(clock, 0x41, down: true, Notepad, injected: true, tag: 0xABCDEF));
        monitor.Record(Key(clock, 0x41, down: false, Notepad, injected: true, tag: 0xABCDEF));

        Assert.Empty(dirty);
        Assert.Null(monitor.Snapshot().LastKeyMs);
        Assert.Equal(0, monitor.Version(Notepad));
        Assert.Equal(0, ledger.Remaining);
    }

    [Fact]
    public void Injected_input_without_a_valid_tag_counts_as_the_users()
    {
        var (clock, monitor) = Healthy();
        var ledger = new PcInjectionLedger(0x1234, clock.NowMs, [PcLedgerEvent.Key(0x41, true)]);
        monitor.Open(ledger);

        monitor.Record(Key(clock, 0x41, down: true, Notepad, injected: true, tag: 0));
        Assert.Equal(1, monitor.Version(Notepad));
        monitor.Record(Key(clock, 0x41, down: true, Notepad, injected: true, tag: 0x9999));
        Assert.Equal(2, monitor.Version(Notepad));
        // The right tag, but not the event the ledger expects next.
        monitor.Record(Key(clock, 0x42, down: true, Notepad, injected: true, tag: 0x1234));
        Assert.Equal(3, monitor.Version(Notepad));
        Assert.Equal(1, ledger.Remaining);
        Assert.Equal(clock.NowMs, monitor.Snapshot().LastKeyMs);
    }

    [Fact]
    public void A_closed_ledger_still_matches_late_events_for_a_second_then_never()
    {
        var (clock, monitor) = Healthy();
        var ledger = new PcInjectionLedger(0x77, clock.NowMs, [PcLedgerEvent.Move(), PcLedgerEvent.Move()]);
        monitor.Open(ledger);
        monitor.Close(ledger);

        clock.Advance(PcInjectionLedger.GraceMs);
        monitor.Record(Mouse(clock, PcRawKind.Move, Notepad, injected: true, tag: 0x77));
        Assert.Equal(0, monitor.Version(Notepad));

        clock.Advance(1);
        monitor.Record(Mouse(clock, PcRawKind.Move, Notepad, injected: true, tag: 0x77));
        Assert.Equal(1, monitor.Version(Notepad));
    }

    [Fact]
    public void A_replay_ledger_marks_its_events_as_the_replay_not_new_activity()
    {
        var (clock, monitor) = Healthy();
        monitor.Open(new PcInjectionLedger(0x55, clock.NowMs, [PcLedgerEvent.Key(0x41, true)], replay: true));
        var downs = new List<PcHookKeyDown>();
        monitor.KeyDown += downs.Add;

        monitor.Record(Key(clock, 0x41, down: true, Notepad, injected: true, tag: 0x55));

        Assert.Empty(downs);
        Assert.Null(monitor.Snapshot().LastKeyMs);
    }

    [Fact]
    public void Ledger_tags_are_random_and_never_zero()
    {
        var tags = Enumerable.Range(0, 64).Select(_ => PcInjectionLedger.NewTag()).ToList();

        Assert.DoesNotContain(0UL, tags);
        Assert.Equal(64, tags.Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcInjectionLedger(0, 0, []));
    }

    [Fact]
    public void User_input_keeps_times_windows_and_the_pressed_window()
    {
        var (clock, monitor) = Healthy();

        monitor.Record(Key(clock, 0, down: true, Notepad, focus: 0x101, heldKeys: 1));
        clock.Advance(20);
        monitor.Record(Mouse(clock, PcRawKind.Button, Excel, down: true, code: 1, point: new PcPoint(40, 50), heldButtons: 1, heldKeys: 1));
        var pressed = monitor.Snapshot();

        Assert.Equal(clock.NowMs - 20, pressed.LastKeyMs);
        Assert.Equal(clock.NowMs, pressed.LastPointerMs);
        Assert.Equal(Notepad, pressed.KeyWindow);
        Assert.Equal((nint)0x101, pressed.KeyFocus);
        Assert.Equal(Excel, pressed.PointerWindow);
        Assert.Equal(Excel, pressed.PressedWindow);
        Assert.Equal(new PcPoint(40, 50), pressed.PressedPoint);
        Assert.Equal(1, pressed.HeldKeys);
        Assert.Equal(1, pressed.HeldButtons);
        Assert.Equal(clock.NowMs, pressed.LastUserMs);

        monitor.Record(Mouse(clock, PcRawKind.Button, Excel, down: false, code: 1, point: new PcPoint(40, 50)));
        Assert.Equal(0, monitor.Snapshot().PressedWindow);
        Assert.Equal(0, monitor.Snapshot().HeldButtons);
    }

    [Fact]
    public void User_input_marks_its_window_dirty_once_per_event()
    {
        var (clock, monitor) = Healthy();
        var dirty = new List<nint>();
        monitor.Dirty += dirty.Add;

        monitor.Record(Key(clock, 0, down: true, Notepad));
        monitor.Record(Key(clock, 0, down: false, Notepad));
        monitor.Record(Mouse(clock, PcRawKind.Wheel, Excel));

        Assert.Equal(new[] { Notepad, Notepad, Excel }, dirty);
        Assert.Equal(2, monitor.Version(Notepad));
        Assert.Equal(1, monitor.Version(Excel));
        Assert.Equal(0, monitor.Version(0x999));
    }

    [Fact]
    public void A_swallowed_event_is_activity_but_marks_no_window()
    {
        var (clock, monitor) = Healthy();
        var swallowed = new List<PcRawInput>();
        monitor.Swallowed += swallowed.Add;
        monitor.Record(Mouse(clock, PcRawKind.Move, Notepad, point: new PcPoint(10, 10)));
        clock.Advance(5);

        monitor.Record(Key(clock, 0x41, down: true, Excel) with { Swallowed = true });
        monitor.Record(Mouse(clock, PcRawKind.Move, Excel, point: new PcPoint(90, 90)) with { Swallowed = true });

        var state = monitor.Snapshot();
        Assert.Equal(2, swallowed.Count);
        Assert.Equal(clock.NowMs, state.LastKeyMs);
        Assert.Equal(clock.NowMs, state.LastPointerMs);
        Assert.Equal(0, monitor.Version(Excel));
        Assert.Equal(Notepad, state.PointerWindow);
        Assert.Equal(new PcPoint(10, 10), state.Pointer);
        Assert.Equal(0, state.KeyWindow);
    }

    [Fact]
    public void Hooks_read_unhealthy_until_installed_and_a_heartbeat_agrees()
    {
        var clock = new PcManualClock();
        var monitor = new PhysicalInputMonitor(clock);
        var health = new List<bool>();
        monitor.HealthChanged += health.Add;

        Assert.False(monitor.Snapshot().Healthy);
        Assert.False(monitor.LastInputSeen(0));
        monitor.Installed(true);
        Assert.False(monitor.Snapshot().Healthy);

        clock.Advance(250);
        Assert.False(monitor.LastInputSeen(clock.NowMs - 300));

        Assert.True(monitor.Snapshot().Healthy);
        Assert.Equal(new[] { true }, health);
    }

    [Fact]
    public void Input_the_hooks_missed_fails_closed_until_a_fresh_event_agrees()
    {
        var (clock, monitor) = Healthy();
        var health = new List<bool>();
        monitor.HealthChanged += health.Add;
        monitor.Record(Key(clock, 0, down: true, Notepad));
        clock.Advance(400);

        // Within the slack: fine.
        Assert.False(monitor.LastInputSeen(clock.NowMs - 400 + PhysicalInputMonitor.HealthSlackMs));
        Assert.True(monitor.Snapshot().Healthy);

        // Newer than anything the hooks delivered: reinstall, unhealthy, and the user's activity at that tick.
        var tick = clock.NowMs - 100;
        Assert.True(monitor.LastInputSeen(tick));
        var state = monitor.Snapshot();
        Assert.False(state.Healthy);
        Assert.Equal(tick, state.LastUnseenMs);
        Assert.Equal(tick, state.LastUserMs);

        // A stale event does not bring it back; a fresh one does.
        monitor.Record(Key(clock, 0, down: false, Notepad) with { TimeMs = tick - 200 });
        Assert.False(monitor.Snapshot().Healthy);
        monitor.Record(Key(clock, 0, down: true, Notepad));
        Assert.True(monitor.Snapshot().Healthy);
        Assert.Equal(new[] { false, true }, health);
    }

    [Fact]
    public void Removing_the_hooks_reads_unhealthy_and_forgets_held_input()
    {
        var (clock, monitor) = Healthy();
        monitor.Record(Key(clock, 0, down: true, Notepad, heldKeys: 2));

        monitor.Installed(false);

        var state = monitor.Snapshot();
        Assert.False(state.Healthy);
        Assert.Equal(0, state.HeldKeys);
        Assert.False(monitor.LastInputSeen(clock.NowMs + 10_000));
    }

    [Fact]
    public void Keeping_is_counted_and_a_keep_releases_once()
    {
        var monitor = new PhysicalInputMonitor(new PcManualClock());
        var changes = new List<bool>();
        monitor.KeepChanged += changes.Add;

        var first = monitor.Keep();
        var second = monitor.Keep();
        Assert.True(monitor.Kept);
        first.Dispose();
        first.Dispose();
        Assert.True(monitor.Kept);
        second.Dispose();

        Assert.False(monitor.Kept);
        Assert.Equal(new[] { true, false }, changes);
    }

    [Fact]
    public void Key_downs_name_Esc_and_chord_keys_and_nothing_else()
    {
        var (clock, monitor) = Healthy();
        var watched = new List<Func<ushort, bool>>();
        monitor.ChordsWatched += watched.Add;
        monitor.WatchChords(key => key == 0x20);
        var downs = new List<PcHookKeyDown>();
        monitor.KeyDown += downs.Add;

        monitor.Record(Key(clock, 0x1B, down: true, Notepad));
        monitor.Record(Key(clock, 0x20, down: true, Notepad) with { Modifiers = 3, Repeat = true });
        monitor.Record(Key(clock, 0, down: true, Notepad));
        monitor.Record(Key(clock, 0x1B, down: false, Notepad));
        monitor.Record(Key(clock, 0x1B, down: true, Notepad, injected: true, tag: 0) with { Swallowed = true });

        Assert.Single(watched);
        Assert.Equal(
            new[]
            {
                new PcHookKeyDown(PcHookKey.Escape, 0x1B, false, false),
                new PcHookKeyDown(PcHookKey.Chord, 0x20, true, false) { Modifiers = 3 },
                new PcHookKeyDown(PcHookKey.Other, 0, false, false),
                new PcHookKeyDown(PcHookKey.Escape, 0x1B, false, true) { Swallowed = true },
            },
            downs);
    }

    [Fact]
    public void A_composition_closes_in_its_own_window_or_anywhere()
    {
        var monitor = new PhysicalInputMonitor(new PcManualClock());

        monitor.Composition(Notepad, open: true);
        monitor.Composition(Excel, open: false);
        Assert.Equal(Notepad, monitor.Snapshot().Composition);
        monitor.Composition(0, open: false);
        Assert.Equal(0, monitor.Snapshot().Composition);

        monitor.ImeLanguage(true);
        Assert.True(monitor.Snapshot().ImeLanguage);
    }

    private static (PcManualClock Clock, PhysicalInputMonitor Monitor) Healthy()
    {
        var clock = new PcManualClock();
        clock.Advance(10_000);
        var monitor = new PhysicalInputMonitor(clock);
        monitor.Installed(true);
        monitor.LastInputSeen(clock.NowMs);
        Assert.True(monitor.Snapshot().Healthy);
        return (clock, monitor);
    }

    private static PcRawInput Key(PcManualClock clock, ushort code, bool down, nint window, nint focus = 0, int heldKeys = 0, bool injected = false, ulong tag = 0) =>
        new(clock.NowMs, PcRawKind.Key, down, code, injected, tag) { Window = window, Focus = focus, HeldKeys = heldKeys };

    private static PcRawInput Mouse(PcManualClock clock, PcRawKind kind, nint window, bool down = false, ushort code = 0, PcPoint point = default, int heldButtons = 0, int heldKeys = 0, bool injected = false, ulong tag = 0) =>
        new(clock.NowMs, kind, down, code, injected, tag) { Window = window, Point = point, HeldButtons = heldButtons, HeldKeys = heldKeys };
}
