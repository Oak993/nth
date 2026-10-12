using Marrow.Core.Models;

namespace Marrow.Core.Tests.Web;

// nth web surfaces, unit W3a: the pane header's overflow rule (work/design/web-surfaces/DESIGN.md 3.2 "Header overflow",
// 6.3, 13.3). Close always stays; the title keeps at least 80 DIP; Expand outlasts the pane's own actions; actions leave
// from the last one back into one More menu; the working chip is the first to go. Widths are the canvas's: a 26 DIP
// button, 6 DIP gaps, and 37 DIP of fixed parts (12 leading, the 14 DIP glyph and its gap, 5 trailing).
[Trait("Decision", "ED-NEW-web-w3a-1")]
public sealed class PaneHeaderFitTests
{
    private static PaneHeaderParts Parts(double width, double title = 200, double chip = 0, bool expand = true, params double[] actions) =>
        new(width, 37, 6, 26, title, actions, chip, expand);

    private static readonly double[] Three = [26, 26, 40];

    [Fact]
    public void A_wide_header_keeps_everything_in_the_row()
    {
        var fit = PaneHeaderFit.Solve(Parts(600, actions: Three));
        Assert.Equal(new PaneHeaderFit(3, false, true, false, 200), fit);
    }

    [Fact]
    public void Actions_leave_from_the_last_one_back_and_More_appears_once_one_has_left()
    {
        // Close 32 + actions 32, 32, 46 + Expand 32 = 174 trailing: at 300 the title still gets 89.
        Assert.Equal(new PaneHeaderFit(3, false, true, false, 89), PaneHeaderFit.Solve(Parts(300, actions: Three)));
        // At 280 it would get 69, so the last action goes and More (32) takes its place: 280 - 37 - 160 = 83.
        Assert.Equal(new PaneHeaderFit(2, true, true, false, 83), PaneHeaderFit.Solve(Parts(280, actions: Three)));
        // At 250 one more leaves: 250 - 37 - 128 = 85.
        Assert.Equal(new PaneHeaderFit(1, true, true, false, 85), PaneHeaderFit.Solve(Parts(250, actions: Three)));
    }

    [Fact]
    public void Expand_outlasts_every_action_then_joins_them_in_More()
    {
        // At 200: no action fits beside Expand and More (200 - 37 - 96 = 67), so Expand goes into More too: 200 - 37 - 64 = 99.
        Assert.Equal(new PaneHeaderFit(0, true, false, false, 99), PaneHeaderFit.Solve(Parts(200, actions: Three)));
        Assert.Equal(new PaneHeaderFit(0, true, true, false, 104), PaneHeaderFit.Solve(Parts(237, actions: Three)));
    }

    [Fact]
    public void The_working_chip_is_the_first_to_go()
    {
        // Everything with the 110 DIP chip needs 37 + 174 + 116 + 80 = 407; without it 291.
        Assert.Equal(new PaneHeaderFit(3, false, true, true, 80), PaneHeaderFit.Solve(Parts(407, chip: 110, actions: Three)));
        Assert.Equal(new PaneHeaderFit(3, false, true, false, 120), PaneHeaderFit.Solve(Parts(331, chip: 110, actions: Three)));
    }

    [Fact]
    public void A_title_shorter_than_the_minimum_needs_only_its_own_width()
    {
        var fit = PaneHeaderFit.Solve(Parts(37 + 174 + 50, title: 50, actions: Three));
        Assert.Equal(new PaneHeaderFit(3, false, true, false, 50), fit);
    }

    [Fact]
    public void Without_actions_Expand_stays_in_the_row_and_no_More_holds_it_alone()
    {
        // A More holding only Expand would be as wide as Expand itself, so Expand stays; the title keeps its 80 and the
        // header clips the trailing items at the title's minimum (PaneHeader's arrange) rather than overlap it.
        Assert.Equal(new PaneHeaderFit(0, false, true, false, 80), PaneHeaderFit.Solve(Parts(120)));
        Assert.Equal(new PaneHeaderFit(0, false, false, false, 80), PaneHeaderFit.Solve(Parts(120, expand: false)));
        Assert.Equal(new PaneHeaderFit(0, false, true, false, 120), PaneHeaderFit.Solve(Parts(221)));
    }

    [Fact]
    public void Trailing_is_a_gap_and_a_width_per_item_with_Close_always_counted()
    {
        var parts = Parts(500, chip: 100, actions: Three);
        Assert.Equal(32, PaneHeaderFit.Trailing(parts, 0, more: false, expand: false, chip: false));
        Assert.Equal(32 + 110 + 32 + 32 + 106, PaneHeaderFit.Trailing(parts, 3, more: true, expand: true, chip: true));
    }

    [Fact]
    public void Across_every_width_each_action_is_in_the_row_or_in_More_and_the_row_only_grows_with_width()
    {
        var previous = -1;
        var previousExpand = false;
        for (var width = 0; width <= 700; width++)
        {
            var parts = Parts(width, chip: 90, actions: [40, 26, 26, 26]);
            var fit = PaneHeaderFit.Solve(parts);
            if (fit.VisibleActions < 4) Assert.True(fit.ShowsMore, $"{width}: an action left the row but there is no More");
            if (!fit.ShowsExpand) Assert.True(fit.ShowsMore, $"{width}: Expand left the row but there is no More");
            Assert.True(fit.VisibleActions >= previous, $"{width}: the row shrank as the header grew");
            Assert.True(fit.ShowsExpand || !previousExpand, $"{width}: Expand left as the header grew");
            if (fit.ShowsChip) Assert.Equal(4, fit.VisibleActions);
            var room = width - 37 - PaneHeaderFit.Trailing(parts, fit.VisibleActions, fit.ShowsMore, fit.ShowsExpand, fit.ShowsChip);
            if (room >= PaneHeaderParts.TitleMinimum) Assert.True(fit.TitleWidth >= PaneHeaderParts.TitleMinimum, $"{width}: the title got under 80");
            previous = fit.VisibleActions;
            previousExpand = fit.ShowsExpand;
        }
    }
}
