// PORT-ONLY: the grab in flight (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.3 "Grab in flight", 10.6 "Hit testing" 1): the
// zone answers for the shape the pill presents at the record's own time, in the monitor's pixels and scale; a resting or
// cleared shape grabs nothing; the pen and touch signature is recognised.
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-23")]
public sealed class PcGrabZoneTests
{
    // A 150 % monitor right of the main one.
    private static readonly PcRect Monitor = new(1920, 0, 2880, 1620);

    private static PillShape Travel(double start) =>
        PillShape.Moving(PillTravel.Start(start, new PillVector(16, 8), new PillVector(1904, 8), PillVector.Zero, reduceMotion: false), null, 440, null, 48);

    [Fact]
    public void A_press_on_the_moving_shape_at_the_record_s_time_is_inside()
    {
        var zone = new PcGrabZone();
        var shape = Travel(100);
        zone.Publish(shape, Monitor, 1.5, secondsNow: 100, tickNow: 50_000);
        var at = shape.At(100.05);
        var x = Monitor.X + (int)((at.X + 200) * 1.5);
        var y = (int)(30 * 1.5);

        Assert.True(zone.Active);
        Assert.True(zone.Contains(x, y, 50_050));
        // The same point 350 ms later: the pill has gone on, so the press is the app's.
        Assert.False(zone.Contains(x, y, 50_400));
    }

    [Fact]
    public void A_resting_or_cleared_shape_grabs_nothing()
    {
        var zone = new PcGrabZone();
        zone.Publish(PillShape.Resting(new PillRect(740, 8, 440, 48)), Monitor, 1.5, 0, 0);

        Assert.False(zone.Active);
        Assert.False(zone.Contains(Monitor.X + 1440, 45, 0));

        zone.Publish(Travel(0), Monitor, 1.5, 0, 0);
        zone.Clear();

        Assert.False(zone.Active);
        Assert.False(zone.Contains(Monitor.X + 60, 45, 10));
    }

    [Fact]
    public void The_tick_wraps_without_breaking_the_time_mapping()
    {
        var zone = new PcGrabZone();
        var shape = Travel(7);
        zone.Publish(shape, Monitor, 1.5, secondsNow: 7, tickNow: uint.MaxValue - 10);
        var at = shape.At(7.03);
        var x = Monitor.X + (int)((at.X + 100) * 1.5);

        Assert.True(zone.Contains(x, 45, 19));
    }

    [Fact]
    public void Pen_and_touch_promoted_mouse_events_carry_the_signature()
    {
        Assert.True(PcGrabZone.FromPenOrTouch(0xFF515700));
        Assert.True(PcGrabZone.FromPenOrTouch(0xFF515780));
        Assert.False(PcGrabZone.FromPenOrTouch(0));
        Assert.False(PcGrabZone.FromPenOrTouch(0x7AC70000));
    }
}
