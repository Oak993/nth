// PORT-ONLY: the pill's auto-dodge (OD-0145, ED-NEW-pcctl-13; DESIGN.md 10.3, 10.11): caption buttons give the least
// sideways move toward the centre that clears them by 12 DIP; over 240 DIP it goes down below the caption bar; a showing
// top taskbar pushes it down; no dodge while dragged or hovered; the 500 ms ping-pong guard; the 300 ms return delay.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-13")]
public sealed class PillDodgePlannerTests
{
    private static readonly PillArea Work = new(0, 0, 1920, 1032);

    // A maximized window's minimize, maximize and close buttons at the top right: 3 x 46 DIP wide, 32 DIP high.
    private static readonly PillRect[] TopRight = [new(1782, 0, 46, 32), new(1828, 0, 46, 32), new(1874, 0, 46, 32)];

    private static PillRect At(PillAnchor anchor, double width) => new(PillAnchors.Position(anchor, Work, width).X, 8, width, 48);

    [Fact]
    public void Caption_buttons_under_a_Right_pill_move_it_left_by_the_least_amount_that_clears_them_by_12_DIP()
    {
        var pill = At(PillAnchor.Right, 320);

        var dodge = PillDodgePlanner.Plan(pill, PillAnchor.Right, Work, TopRight, null);

        Assert.Equal(-(pill.Right - (1782 - 12)), dodge.Dx);
        Assert.Equal(0, dodge.Dy);
        Assert.Equal(1782 - 12, pill.Right + dodge.Dx);
    }

    [Fact]
    public void A_sideways_move_over_240_DIP_goes_down_below_the_caption_bar_instead()
    {
        var wide = new PillRect[] { new(1500, 0, 46, 32), new(1546, 0, 46, 32), new(1592, 0, 46, 32), new(1874, 0, 46, 32) };
        var pill = At(PillAnchor.Right, 320);

        var dodge = PillDodgePlanner.Plan(pill, PillAnchor.Right, Work, wide, null);

        Assert.Equal(0, dodge.Dx);
        Assert.Equal(32 + 6 - 8, dodge.Dy);
    }

    [Fact]
    public void A_Centre_pill_takes_the_shorter_way_and_buttons_away_from_it_do_nothing()
    {
        var pill = At(PillAnchor.Centre, 320);
        var middle = new PillRect[] { new(1000, 0, 46, 32), new(1046, 0, 46, 32) };

        var dodge = PillDodgePlanner.Plan(pill, PillAnchor.Centre, Work, middle, null);

        // Left: the pill's right edge to 988 (1120 - 988 = 132); right: its left edge to 1104 (1104 - 800 = 304, over 240 anyway).
        Assert.Equal(-132, dodge.Dx);
        Assert.Equal(PillDodge.None, PillDodgePlanner.Plan(pill, PillAnchor.Centre, Work, TopRight, null));
    }

    [Fact]
    public void A_showing_top_taskbar_pushes_the_pill_below_it()
    {
        var pill = At(PillAnchor.Centre, 320);

        Assert.Equal(new PillDodge(0, 48 + 8 - 8, "taskbar"), PillDodgePlanner.Plan(pill, PillAnchor.Centre, Work, [], 48));
        Assert.Equal(PillDodge.None, PillDodgePlanner.Plan(pill, PillAnchor.Centre, Work, [], null));
    }

    [Fact]
    public void No_dodge_while_dragged_or_hovered_and_none_within_500_ms_of_the_last()
    {
        var planner = new PillDodgePlanner();
        var dodge = new PillDodge(-150, 0, "caption buttons");

        Assert.Equal(PillDodge.None, planner.Update(1_000, dodge, dragging: true, hovered: false));
        Assert.Equal(PillDodge.None, planner.Update(1_000, dodge, dragging: false, hovered: true));
        Assert.Equal(dodge, planner.Update(1_000, dodge, false, false));
        var other = new PillDodge(0, 30, "caption buttons");
        Assert.Equal(dodge, planner.Update(1_400, other, false, false));
        Assert.Equal(other, planner.Update(1_500, other, false, false));
    }

    [Fact]
    public void A_return_waits_until_the_reason_has_been_clear_for_300_ms()
    {
        var planner = new PillDodgePlanner();
        var dodge = new PillDodge(-150, 0, "caption buttons");
        planner.Update(0, dodge, false, false);

        Assert.Equal(dodge, planner.Update(1_000, PillDodge.None, false, false));
        Assert.Equal(1_300d, planner.ReturnDueAt);
        Assert.Equal(dodge, planner.Update(1_299, PillDodge.None, false, false));
        // The reason came back in between: the clock restarts.
        Assert.Equal(dodge, planner.Update(1_299, dodge, false, false));
        Assert.Equal(dodge, planner.Update(1_350, PillDodge.None, false, false));
        Assert.Equal(PillDodge.None, planner.Update(1_650, PillDodge.None, false, false));
        Assert.Null(planner.ReturnDueAt);
    }
}
