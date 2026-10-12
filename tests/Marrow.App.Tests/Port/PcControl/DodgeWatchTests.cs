// PORT-ONLY: when the dodge looks again (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.3 "Dodge rules"): a foreground change and
// a display change read at once, a window's moves are debounced 100 ms past the last one, a reorder also asks for the bands
// on top again, and a due read is handed out once.
using Marrow.App.Port.PcControl.Overlay;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-23")]
public sealed class DodgeWatchTests
{
    [Fact]
    public void Foreground_and_display_changes_read_at_once_and_only_once()
    {
        var watch = new DodgeWatch();
        watch.Event(DodgeEventKind.Foreground, 1);

        Assert.True(watch.Due(1, out var covered));
        Assert.False(covered);
        Assert.False(watch.Due(1.5, out _));

        watch.Event(DodgeEventKind.Display, 2);
        Assert.True(watch.Due(2, out _));
    }

    [Fact]
    public void A_dragged_window_reads_100_ms_after_its_last_move()
    {
        var watch = new DodgeWatch();
        watch.Event(DodgeEventKind.Location, 1.00);
        watch.Event(DodgeEventKind.Location, 1.05);
        watch.Event(DodgeEventKind.Location, 1.12);

        Assert.False(watch.Due(1.20, out _));
        Assert.True(watch.Pending);
        Assert.True(watch.Due(1.23, out _));
        Assert.False(watch.Pending);
    }

    [Fact]
    public void A_move_never_delays_a_read_that_is_already_due_and_a_reorder_says_covered()
    {
        var watch = new DodgeWatch();
        watch.Event(DodgeEventKind.Reorder, 3);
        watch.Event(DodgeEventKind.Location, 3);

        Assert.True(watch.Due(3, out var covered));
        Assert.True(covered);
        watch.Event(DodgeEventKind.Foreground, 4);
        Assert.True(watch.Due(4, out var again));
        Assert.False(again);
    }
}
