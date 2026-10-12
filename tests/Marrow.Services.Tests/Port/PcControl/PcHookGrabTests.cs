// PORT-ONLY: the grab in flight in the hook (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.3 "Grab in flight", 10.6 "Hit testing"
// 1): a physical mouse press on the moving pill is swallowed with its up (pair-aware) and the moves between are marked
// grabbed but delivered; pen and touch pass through; a press off the shape is the app's; a borrow's own swallow wins; the
// monitor raises the grab's records without marking any window.
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-23")]
public sealed class PcHookGrabTests
{
    private const ushort Left = 1;
    private const ushort Right = 2;

    [Fact]
    public void A_press_on_the_moving_pill_is_swallowed_with_its_up_and_the_moves_between_are_marked()
    {
        var core = new PcHookCore { Grab = new PcFixedGrabZone(inside: true) };

        var down = core.Mouse(PcRawKind.Button, Left, true, 500, 20, 0, false, 0, 1, out var swallowDown);
        var move = core.Mouse(PcRawKind.Move, 0, false, 540, 24, 0, false, 0, 2, out var swallowMove);
        var up = core.Mouse(PcRawKind.Button, Left, false, 560, 24, 0, false, 0, 3, out var swallowUp);
        var after = core.Mouse(PcRawKind.Move, 0, false, 561, 24, 0, false, 0, 4, out var swallowAfter);

        Assert.True(swallowDown);
        Assert.True(down.Grabbed);
        Assert.False(down.Swallowed);
        Assert.False(swallowMove);
        Assert.True(move.Grabbed);
        Assert.True(swallowUp);
        Assert.True(up.Grabbed);
        Assert.False(swallowAfter);
        Assert.False(after.Grabbed);
        Assert.Equal(0, core.SwallowedCount);
    }

    [Fact]
    public void A_press_off_the_shape_pen_and_touch_and_injected_presses_never_grab()
    {
        var off = new PcHookCore { Grab = new PcFixedGrabZone(inside: false) };
        off.Mouse(PcRawKind.Button, Left, true, 5, 5, 0, false, 0, 1, out var swallowOff);

        var core = new PcHookCore { Grab = new PcFixedGrabZone(inside: true) };
        var pen = core.Mouse(PcRawKind.Button, Left, true, 5, 5, 0, false, 0xFF515700, 1, out var swallowPen);
        core.Mouse(PcRawKind.Button, Left, false, 5, 5, 0, false, 0xFF515700, 2, out _);
        var touch = core.Mouse(PcRawKind.Button, Left, true, 5, 5, 0, false, 0xFF515780, 3, out var swallowTouch);
        core.Mouse(PcRawKind.Button, Left, false, 5, 5, 0, false, 0xFF515780, 4, out _);
        var injected = core.Mouse(PcRawKind.Button, Left, true, 5, 5, 0, true, 0, 5, out var swallowInjected);

        Assert.False(swallowOff);
        Assert.False(swallowPen || pen.Grabbed);
        Assert.False(swallowTouch || touch.Grabbed);
        Assert.False(swallowInjected || injected.Grabbed);
    }

    [Fact]
    public void Only_the_grabbing_button_s_up_is_swallowed()
    {
        var core = new PcHookCore { Grab = new PcFixedGrabZone(inside: true) };
        core.Mouse(PcRawKind.Button, Left, true, 5, 5, 0, false, 0, 1, out _);

        core.Mouse(PcRawKind.Button, Right, true, 5, 5, 0, false, 0, 2, out var swallowRightDown);
        core.Mouse(PcRawKind.Button, Right, false, 5, 5, 0, false, 0, 3, out var swallowRightUp);
        core.Mouse(PcRawKind.Button, Left, false, 5, 5, 0, false, 0, 4, out var swallowLeftUp);

        Assert.False(swallowRightDown);
        Assert.False(swallowRightUp);
        Assert.True(swallowLeftUp);
    }

    [Fact]
    public void A_borrow_s_own_swallow_wins_over_a_grab()
    {
        var core = new PcHookCore { Grab = new PcFixedGrabZone(inside: true) };
        core.BeginSwallow();

        var down = core.Mouse(PcRawKind.Button, Left, true, 5, 5, 0, false, 0, 1, out var swallow);

        Assert.True(swallow);
        Assert.True(down.Swallowed);
        Assert.False(down.Grabbed);
        Assert.Equal(1, core.SwallowedCount);
    }

    [Fact]
    public void The_monitor_raises_the_grab_s_records_as_the_user_s_activity_without_marking_a_window()
    {
        var clock = new PcManualClock();
        var monitor = new PhysicalInputMonitor(clock);
        var grabbed = new List<PcRawInput>();
        var dirty = new List<nint>();
        monitor.Grabbed += grabbed.Add;
        monitor.Dirty += dirty.Add;

        monitor.Record(new PcRawInput(clock.NowMs, PcRawKind.Button, true, Left, false, 0) { Window = 0x300, Grabbed = true, HeldButtons = 1 });

        Assert.Single(grabbed);
        Assert.Empty(dirty);
        Assert.Equal(0, monitor.Presses(0x300));
        Assert.Equal(clock.NowMs, monitor.Snapshot().LastPointerMs);
        Assert.Equal(1, monitor.Snapshot().HeldButtons);
    }
}
