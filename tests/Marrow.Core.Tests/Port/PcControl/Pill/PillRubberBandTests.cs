// PORT-ONLY: the pill's rubber bands while dragged (OD-0145, ED-NEW-pcctl-13; DESIGN.md 10.3, 10.11): shown = D (1 - 1 /
// (d 0.55 / D + 1)), 120 DIP down, 8 DIP up, 16 DIP at a side with no monitor beyond; the canvas's 300 DIP shows 14.5.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-13")]
public sealed class PillRubberBandTests
{
    [Fact]
    public void The_formula_and_the_canvas_reading()
    {
        Assert.Equal(16 * (1 - (1 / ((300 * 0.55 / 16) + 1))), PillRubberBand.Shown(300, PillRubberBand.Side), 12);
        Assert.InRange(PillRubberBand.Shown(300, PillRubberBand.Side), 14.5, 14.6);
        Assert.Equal(0, PillRubberBand.Shown(0, PillRubberBand.Side));
        Assert.Equal(-PillRubberBand.Shown(40, PillRubberBand.Side), PillRubberBand.Shown(-40, PillRubberBand.Side), 12);
    }

    [Fact]
    public void It_starts_at_the_pointers_pace_and_never_reaches_its_limit()
    {
        // The slope at zero is 0.55: a small pull moves the pill a little more than half as far.
        Assert.Equal(0.55, PillRubberBand.Shown(0.001, PillRubberBand.Down) / 0.001, 3);
        foreach (var limit in new[] { PillRubberBand.Down, PillRubberBand.Up, PillRubberBand.Side })
        {
            Assert.True(PillRubberBand.Shown(1e6, limit) < limit);
            Assert.True(PillRubberBand.Shown(1e6, limit) > limit * 0.99);
        }
    }

    [Fact]
    public void Down_gives_up_to_120_DIP_and_up_only_8()
    {
        Assert.Equal(PillRubberBand.Shown(200, 120), PillRubberBand.Vertical(200), 12);
        Assert.Equal(PillRubberBand.Shown(-200, 8), PillRubberBand.Vertical(-200), 12);
        Assert.True(PillRubberBand.Vertical(-10_000) > -8);
        Assert.True(PillRubberBand.Vertical(10_000) < 120);
    }
}
