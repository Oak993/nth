using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-2 (BUILD-PLAN 2.2; OD-0096 and the OD-0098 amendments "Look I, Words W1, Download D3"): the numbers of the locked look I "Sheet",
// measured in headless Chrome from E:/Droppy/work/design/decisions-2026-10-03/getting-ready/getting-ready.html
// (tests/fixtures/getting-ready/measure.cjs -> sheet.json). Pure: no window, no clock.
public sealed class GettingReadyLookTests
{
    private const double SheetHeight = 224.25;

    [Fact] // GR-2.fixture
    [Trait("Decision", "ED-288")]
    public void The_sheets_metrics_are_the_fixtures()
    {
        var css = GettingReadyFixture.Css();
        var sheet = css.GetProperty("sheet");
        Assert.Equal(GettingReadyLook.PaddingTop, GettingReadyFixture.Px(sheet, "paddingTop"));
        Assert.Equal(GettingReadyLook.PaddingSide, GettingReadyFixture.Px(sheet, "paddingLeft"));
        Assert.Equal(GettingReadyLook.PaddingSide, GettingReadyFixture.Px(sheet, "paddingRight"));
        Assert.Equal(GettingReadyLook.PaddingBottom, GettingReadyFixture.Px(sheet, "paddingBottom"));
        Assert.Equal(GettingReadyLook.CornerRadius, GettingReadyFixture.Px(sheet, "borderBottomLeftRadius"));
        Assert.Equal(GettingReadyLook.CornerRadius, GettingReadyFixture.Px(sheet, "borderBottomRightRadius"));
        Assert.Equal(0, GettingReadyFixture.Px(sheet, "borderTopLeftRadius")); // the sheet hangs from the top: its top corners are square
        Assert.Equal(GettingReadyLook.SheetWidth, GettingReadyFixture.Number(GettingReadyFixture.Frame("workEnd").GetProperty("sheet"), "w"));
        Assert.Equal(SheetHeight, GettingReadyFixture.Number(GettingReadyFixture.Frame("workEnd").GetProperty("sheet"), "h"));

        var title = css.GetProperty("title");
        Assert.Equal(GettingReadyLook.TitleSize, GettingReadyFixture.Px(title, "fontSize"));
        Assert.Equal(GettingReadyLook.TitleLine, GettingReadyFixture.Px(title, "lineHeight"));
        var line = css.GetProperty("sub");
        Assert.Equal(GettingReadyLook.SubSize, GettingReadyFixture.Px(line, "fontSize"));
        Assert.Equal(GettingReadyLook.SubLine, GettingReadyFixture.Px(line, "lineHeight"));
        Assert.Equal(GettingReadyLook.SubTop, GettingReadyFixture.Px(line, "marginTop"));
        Assert.Equal(GettingReadyLook.SubBottom, GettingReadyFixture.Px(line, "marginBottom"));

        var list = css.GetProperty("list");
        Assert.Equal("194px 194px", list.GetProperty("gridTemplateColumns").GetString());
        Assert.Equal(GettingReadyLook.Columns, 2);
        Assert.Equal(GettingReadyLook.ColumnGap, GettingReadyFixture.Px(list, "columnGap"));
        Assert.Equal(GettingReadyLook.ListBottom, GettingReadyFixture.Px(list, "marginBottom"));
        var row = css.GetProperty("row");
        Assert.Equal(GettingReadyLook.RowHeight, GettingReadyFixture.Px(row, "height"));
        Assert.Equal(GettingReadyLook.GlyphGap, GettingReadyFixture.Px(row, "gap"));
        Assert.Equal(GettingReadyLook.LabelSize, GettingReadyFixture.Px(css.GetProperty("rowLabel"), "fontSize"));
        Assert.Equal(GettingReadyLook.GlyphSize, GettingReadyFixture.Px(css.GetProperty("glyph"), "width"));
        Assert.Equal(GettingReadyLook.MarkSize, GettingReadyFixture.Px(css.GetProperty("mark"), "width"));
        Assert.Equal(GettingReadyLook.BarHeight, GettingReadyFixture.Px(css.GetProperty("bar"), "height"));
    }

    [Fact] // GR-2.timing
    [Trait("Decision", "ED-288")]
    public void The_hold_and_the_hand_over_run_as_long_as_the_fixtures_and_its_pane_dims_as_it_does()
    {
        var plan = GettingReadyFixture.Theme(true).GetProperty("plan");
        Assert.Equal(GettingReadyLook.Hold, plan.GetProperty("HOLD").GetDouble());
        Assert.Equal(GettingReadyLook.HandOverSeconds, plan.GetProperty("HAND").GetDouble());
        var rest = GettingReadyFixture.HandFrames()[0].GetProperty("paneStyle"); // the pane while the sheet hangs
        Assert.Equal(GettingReadyLook.PaneOpacityRest, double.Parse(rest.GetProperty("opacity").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(GettingReadyLook.BlurSigma, GettingReadyFixture.BlurPx(rest.GetProperty("filter").GetString()!));
        Assert.Equal(GettingReadyLook.HiSpan, 0.56); // --hi = clamp(h / .56): the sheet is gone and the pane whole by 56 % of the hand-over (gr_lib.py:612-613)
        Assert.Equal(GettingReadyLook.LiftExtra, 30);
    }

    [Theory] // GR-2.timing
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Decision", "ED-288")]
    public void The_hand_over_curve_the_sheets_lift_and_the_panes_return_follow_the_fixtures_frames(bool dark)
    {
        var frames = GettingReadyFixture.HandFrames(dark);
        Assert.Equal(21, frames.Count);
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            var hand = GettingReadyFixture.Number(frame, "h");
            Assert.True(Math.Abs(GettingReadyLook.Ease(index / 20.0) - hand) <= 0.0006, $"hand-over {index}/20: eased {GettingReadyLook.Ease(index / 20.0):0.0000}, fixture {hand:0.0000}");
            var sheetStyle = frame.GetProperty("sheetStyle");
            var paneStyle = frame.GetProperty("paneStyle");
            var lift = GettingReadyFixture.TranslateY(sheetStyle.GetProperty("transform").GetString()!);
            Assert.True(Math.Abs(GettingReadyLook.LiftOffset(hand, SheetHeight) - lift) <= 0.05, $"{index}: lift {GettingReadyLook.LiftOffset(hand, SheetHeight):0.000}, fixture {lift:0.000}");
            Assert.True(Math.Abs(GettingReadyLook.SheetOpacity(hand) - double.Parse(sheetStyle.GetProperty("opacity").GetString()!, System.Globalization.CultureInfo.InvariantCulture)) <= 0.0005, $"{index}: sheet opacity");
            var filter = paneStyle.GetProperty("filter").GetString()!;
            Assert.True(Math.Abs(GettingReadyLook.PaneBlurSigma(hand) - GettingReadyFixture.BlurPx(filter)) <= 0.005, $"{index}: pane blur");
            Assert.True(Math.Abs(GettingReadyLook.PaneOpacity(hand) - double.Parse(paneStyle.GetProperty("opacity").GetString()!, System.Globalization.CultureInfo.InvariantCulture)) <= 0.0005, $"{index}: pane opacity");
        }
    }

    [Fact] // GR-2.words
    [Trait("Decision", "ED-288")]
    public void The_words_are_W1s()
    {
        Assert.Equal("Getting ready", GettingReadyFixture.Title);
        Assert.Equal(GettingReadyLook.Title, GettingReadyFixture.Title);
        Assert.Equal("nth is preparing its graphics and voice for this PC. This happens once.", GettingReadyFixture.Line);
        Assert.Equal(GettingReadyLook.Line, GettingReadyFixture.Line);
        Assert.Equal("Ready", GettingReadyFixture.Done);
        Assert.Equal(GettingReadyLook.Done, GettingReadyFixture.Done);
        Assert.Equal(GettingReadyFixture.Words, FirstRunSteps.W1.Select(step => step.Name)); // GR-1's names are the page's, in its order
    }
}
