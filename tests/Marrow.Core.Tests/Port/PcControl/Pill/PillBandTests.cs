// PORT-ONLY: the band that holds the pill (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.6 "One overlay per monitor"): 120 DIP for
// the pill, 208 while dragging, the card plus 160; room for a dodged pill's shadow; grow before the motion, shrink after;
// the work area's full width.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-23")]
public sealed class PillBandTests
{
    [Fact]
    public void Each_state_gets_its_height()
    {
        Assert.Equal(120, PillBand.Height(PillBandState.Pill));
        Assert.Equal(208, PillBand.Height(PillBandState.Dragging));
        Assert.Equal(300 + 160, PillBand.Height(PillBandState.Card, cardHeight: 300));
        // A card is never shorter than the pill's own row, so its band is at least 48 + 160 DIP.
        Assert.Equal(48 + 160, PillBand.Height(PillBandState.Card, cardHeight: 48));
    }

    [Fact]
    public void A_pill_dodged_below_a_taskbar_keeps_room_for_its_shadow()
    {
        // A top taskbar of 48 DIP: the pill rests at 56 to 104 DIP; its shadow needs 48 DIP below it.
        Assert.Equal(104 + PillBand.ShadowRoom, PillBand.Height(PillBandState.Pill, pillBottom: 104));
        Assert.Equal(120, PillBand.Height(PillBandState.Pill, pillBottom: 56));
    }

    [Fact]
    public void Growing_runs_before_the_motion_and_shrinking_after_it()
    {
        Assert.Equal(PillBandResize.BeforeMotion, PillBand.Plan(120, 460));
        Assert.Equal(PillBandResize.AfterMotion, PillBand.Plan(460, 120));
        Assert.Equal(PillBandResize.None, PillBand.Plan(120, 120));
    }

    [Fact]
    public void The_band_spans_the_work_area_s_full_width_from_its_top()
    {
        var work = new PillArea(0, 40, 1920, 1000);

        Assert.Equal(new PillRect(0, 40, 1920, 120), PillBand.Rect(work, 120));
        Assert.Equal(new PillRect(0, 40, 1920, 1000), PillBand.Rect(work, 5000));
    }
}
