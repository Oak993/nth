using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-19; DESIGN.md 2.9). What the monitor keeps per window for the
// same-window rules: every physical event bumps the window's version (its cached tree is dirty), key-downs and button
// presses also count as presses (what can change a control), and the time of the last input there. Tactus's own tagged
// events and swallowed events count for no window.
[Trait("Decision", "ED-NEW-pcctl-19")]
public sealed class PhysicalInputWindowTests
{
    private const nint Window = 0x1a;

    [Fact]
    public void Moves_dirty_a_window_but_only_keys_and_presses_count_as_presses()
    {
        var (clock, monitor) = Healthy();

        monitor.Record(new PcRawInput(clock.NowMs, PcRawKind.Move, false, 0, false, 0) { Window = Window, Moved = new PcPoint(2, 0) });
        monitor.Record(new PcRawInput(clock.NowMs, PcRawKind.Wheel, false, 0, false, 0) { Window = Window, Delta = -120 });
        Assert.Equal(2, monitor.Version(Window));
        Assert.Equal(0, monitor.Presses(Window));

        clock.Advance(40);
        monitor.Record(new PcRawInput(clock.NowMs, PcRawKind.Button, true, 1, false, 0) { Window = Window, HeldButtons = 1 });
        monitor.Record(new PcRawInput(clock.NowMs, PcRawKind.Button, false, 1, false, 0) { Window = Window });
        monitor.Record(new PcRawInput(clock.NowMs, PcRawKind.Key, true, 0x41, false, 0) { Window = Window, Focus = 0x1a1 });
        monitor.Record(new PcRawInput(clock.NowMs, PcRawKind.Key, false, 0x41, false, 0) { Window = Window, Focus = 0x1a1 });

        Assert.Equal(6, monitor.Version(Window));
        Assert.Equal(2, monitor.Presses(Window));
        Assert.Equal(clock.NowMs, monitor.LastIn(Window));
        Assert.Null(monitor.LastIn(0x2b));
        var state = monitor.Snapshot();
        Assert.Equal(clock.NowMs, state.KeyWindowMs);
        Assert.Equal((nint)0x1a1, state.KeyFocus);
    }

    [Fact]
    public void Tactus_own_input_and_swallowed_input_count_for_no_window()
    {
        var (clock, monitor) = Healthy();
        monitor.Open(new PcInjectionLedger(0x99, clock.NowMs, [PcLedgerEvent.Key(0x41, true), PcLedgerEvent.Button(1, true)]));

        monitor.Record(new PcRawInput(clock.NowMs, PcRawKind.Key, true, 0x41, true, 0x99) { Window = Window });
        monitor.Record(new PcRawInput(clock.NowMs, PcRawKind.Button, true, 1, true, 0x99) { Window = Window });
        monitor.Record(new PcRawInput(clock.NowMs, PcRawKind.Key, true, 0x42, false, 0) { Window = Window, Swallowed = true });

        Assert.Equal(0, monitor.Version(Window));
        Assert.Equal(0, monitor.Presses(Window));
        Assert.Null(monitor.LastIn(Window));
        Assert.Null(monitor.Snapshot().KeyWindowMs);
    }

    private static (PcManualClock Clock, PhysicalInputMonitor Monitor) Healthy()
    {
        var clock = new PcManualClock();
        clock.Advance(5_000);
        var monitor = new PhysicalInputMonitor(clock);
        monitor.Installed(true);
        monitor.LastInputSeen(clock.NowMs);
        return (clock, monitor);
    }
}
