// PORT-ONLY: Tactus pill v2 (ED-NEW-pcctl-34; DESIGN.md 10.3 "Auto-dodge" row 3, 10.5, 10.6 "Hit testing"). Where the user's
// pointer is and whether a button is held: the hooks' snapshot while they are healthy, Windows' cursor and buttons while
// they are not; the pointer in a monitor's DIP; a press that began in another app holds until every button is up, a press
// on one of Tactus's own windows does not, and a button the hooks met already held counts as another app's; each record
// for the style-flip test, except one a borrow swallowed. A real input monitor on a manual clock and a fake probe.
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-34")]
public sealed class TactusPointerStateTests
{
    private const nint Word = 0x10;
    private const nint Band = 0x1000;

    [Fact]
    public void The_pointer_comes_from_the_hooks_while_they_are_healthy_and_from_Windows_while_they_are_not()
    {
        var (clock, input) = Healthy();
        var probe = new PcFakePointerProbe { At = new PcPoint(1, 2), Down = true };
        using var state = new TactusPointerState(input, probe, () => 5);

        // healthy, but no pointer event yet: Windows' cursor; the held buttons are the hooks' (none)
        Assert.Equal(new PcPoint(1, 2), state.Cursor());
        Assert.False(state.ButtonHeld());
        input.Record(Mouse(clock, PcRawKind.Move, new PcPoint(300, 40), 0, Word));
        Assert.Equal(new PcPoint(300, 40), state.Cursor());

        input.Installed(false);
        Assert.Equal(new PcPoint(1, 2), state.Cursor());
        Assert.True(state.ButtonHeld());
    }

    [Fact]
    public void The_pointer_reads_in_the_monitor_s_DIP_and_is_null_on_another_monitor()
    {
        var (clock, input) = Healthy();
        using var state = new TactusPointerState(input, new PcFakePointerProbe(), () => 5);
        Assert.Null(state.PointerAt(PcTestMonitors.Main));

        input.Record(Mouse(clock, PcRawKind.Move, new PcPoint(2220, 150), 0, Word));

        Assert.Null(state.PointerAt(PcTestMonitors.Main));
        Assert.Equal(new PillVector(200, 100), state.PointerAt(PcTestMonitors.Right));
    }

    [Fact]
    public void A_press_in_another_app_holds_until_every_button_is_up_and_a_press_on_the_pill_does_not()
    {
        var (clock, input) = Healthy();
        using var state = new TactusPointerState(input, new PcFakePointerProbe(), () => 5);
        state.Own([Band, 0]);
        var changes = new List<bool>();
        state.HeldElsewhereChanged += changes.Add;

        input.Record(Mouse(clock, PcRawKind.Button, new PcPoint(10, 10), 1, Word, down: true));
        input.Record(Mouse(clock, PcRawKind.Move, new PcPoint(500, 10), 1, Band));
        Assert.True(state.HeldElsewhere);
        input.Record(Mouse(clock, PcRawKind.Button, new PcPoint(500, 10), 3, Band, down: true));
        input.Record(Mouse(clock, PcRawKind.Button, new PcPoint(500, 10), 2, Band));
        input.Record(Mouse(clock, PcRawKind.Button, new PcPoint(500, 10), 0, Band));
        Assert.Equal(new[] { true, false }, changes);

        // the pill's own press (its drag, a button) is not elsewhere
        input.Record(Mouse(clock, PcRawKind.Button, new PcPoint(960, 30), 1, Band, down: true));
        input.Record(Mouse(clock, PcRawKind.Move, new PcPoint(800, 30), 1, Word));
        input.Record(Mouse(clock, PcRawKind.Button, new PcPoint(800, 30), 0, Word));
        Assert.Equal(new[] { true, false }, changes);
        Assert.False(state.HeldElsewhere);

        // a button the hooks meet already held (they went in mid-press) is taken as another app's
        input.Record(Mouse(clock, PcRawKind.Move, new PcPoint(800, 30), 1, Band));
        Assert.True(state.HeldElsewhere);
        Assert.Equal(new[] { true, false, true }, changes);
    }

    [Fact]
    public void Each_record_reaches_the_style_flip_test_with_the_pill_s_seconds_except_a_swallowed_one()
    {
        var (clock, input) = Healthy();
        using var state = new TactusPointerState(input, new PcFakePointerProbe(), () => 12.5);
        var moved = new List<(int, int, double)>();
        state.Moved += (x, y, seconds) => moved.Add((x, y, seconds));

        input.Record(Mouse(clock, PcRawKind.Move, new PcPoint(3, 4), 0, Word));
        input.Record(Mouse(clock, PcRawKind.Move, new PcPoint(9, 9), 0, Word) with { Swallowed = true });
        state.Dispose();
        input.Record(Mouse(clock, PcRawKind.Move, new PcPoint(5, 6), 0, Word));

        Assert.Equal(new[] { (3, 4, 12.5) }, moved);
    }

    private static (PcManualClock Clock, PhysicalInputMonitor Input) Healthy()
    {
        var clock = new PcManualClock();
        clock.Advance(10_000);
        var input = new PhysicalInputMonitor(clock);
        input.Installed(true);
        input.LastInputSeen(clock.NowMs);
        return (clock, input);
    }

    private static PcRawInput Mouse(PcManualClock clock, PcRawKind kind, PcPoint point, int heldButtons, nint window, bool down = false) =>
        new(clock.NowMs, kind, down, kind == PcRawKind.Button ? (ushort)1 : (ushort)0, false, 0) { Window = window, Point = point, HeldButtons = heldButtons };
}
