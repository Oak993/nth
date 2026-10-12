using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus (ED-NEW-pcctl-34; DESIGN.md 10.3, 10.5, 10.6). The physical input monitor hands each of the user's
// pointer events on, after it kept it, for the pill's pointer state: moves, buttons and the wheel, swallowed ones with
// their flag; never a key, and never Tactus's own tagged input. On a manual clock.
[Trait("Decision", "ED-NEW-pcctl-34")]
public sealed class PhysicalInputUserPointerTests
{
    private const nint Notepad = 0x100;

    [Fact]
    public void The_users_moves_buttons_and_wheel_are_handed_on_after_the_monitor_kept_them()
    {
        var (clock, monitor) = Healthy();
        var seen = new List<(PcRawKind Kind, PcPoint Point, int Held, int HeldNow)>();
        monitor.UserPointer += raw => seen.Add((raw.Kind, raw.Point, raw.HeldButtons, monitor.Snapshot().HeldButtons));

        monitor.Record(Mouse(clock, PcRawKind.Move, new PcPoint(10, 20), 0));
        monitor.Record(Mouse(clock, PcRawKind.Button, new PcPoint(10, 20), 1, down: true));
        monitor.Record(Mouse(clock, PcRawKind.Wheel, new PcPoint(10, 20), 1));
        monitor.Record(Mouse(clock, PcRawKind.Button, new PcPoint(12, 22), 0));

        Assert.Equal(
            new[]
            {
                (PcRawKind.Move, new PcPoint(10, 20), 0, 0),
                (PcRawKind.Button, new PcPoint(10, 20), 1, 1),
                (PcRawKind.Wheel, new PcPoint(10, 20), 1, 1),
                (PcRawKind.Button, new PcPoint(12, 22), 0, 0),
            },
            seen);
    }

    [Fact]
    public void A_key_and_Tactus_s_own_tagged_input_are_not_handed_on_and_a_swallowed_event_says_so()
    {
        var (clock, monitor) = Healthy();
        var seen = new List<PcRawInput>();
        monitor.UserPointer += seen.Add;
        var ledger = new PcInjectionLedger(0xABCDEF, clock.NowMs, [PcLedgerEvent.Move()]);
        monitor.Open(ledger);

        monitor.Record(new PcRawInput(clock.NowMs, PcRawKind.Key, true, 0x41, false, 0) { Window = Notepad });
        monitor.Record(new PcRawInput(clock.NowMs, PcRawKind.Move, false, 0, true, 0xABCDEF) { Window = Notepad, Point = new PcPoint(5, 5) });
        monitor.Record(Mouse(clock, PcRawKind.Move, new PcPoint(7, 7), 0) with { Swallowed = true });

        var only = Assert.Single(seen);
        Assert.True(only.Swallowed);
        Assert.Equal(new PcPoint(7, 7), only.Point);
        Assert.Equal(0, ledger.Remaining);
    }

    private static (PcManualClock Clock, PhysicalInputMonitor Monitor) Healthy()
    {
        var clock = new PcManualClock();
        clock.Advance(10_000);
        var monitor = new PhysicalInputMonitor(clock);
        monitor.Installed(true);
        monitor.LastInputSeen(clock.NowMs);
        return (clock, monitor);
    }

    private static PcRawInput Mouse(PcManualClock clock, PcRawKind kind, PcPoint point, int heldButtons, bool down = false) =>
        new(clock.NowMs, kind, down, kind == PcRawKind.Button ? (ushort)1 : (ushort)0, false, 0) { Window = Notepad, Point = point, HeldButtons = heldButtons };
}
