using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-2: the sheet at rest laid out against the fixture measured from look I (window 960 x 680, sidebar 250: the sheet is centred
// on the 710-wide detail pane and hangs from the window's top edge). Fonts differ between Chrome and the app, so the boxes the
// app sets itself are compared (the sheet, the title and line boxes, the rows, glyphs, marks, the bar); a label's text is
// compared by its left edge and the middle of its line.
[Collection("Headless")]
public sealed class GettingReadyGeometryTests(HeadlessSession headless)
{
    private const double Tolerance = 0.75;

    private static Rect Fixture(JsonElement box, double dy = 0) => new(GettingReadyFixture.Number(box, "x") - GettingReadyFixture.Sidebar,
        GettingReadyFixture.Number(box, "y") + dy, GettingReadyFixture.Number(box, "w"), GettingReadyFixture.Number(box, "h"));

    /// <summary>The page sets the line under the title in one line of Segoe UI Variable. The app's own font is wider and the sentence runs
    /// to a second line in the 408 px (ED-306): the sheet is that many line heights taller and all below the line moves down.</summary>
    private static double Extra(GettingReadyRig rig) => (Math.Round(rig.Rect(rig.Sheet.LineText).Height / GettingReadyLook.SubLine) - 1) * GettingReadyLook.SubLine;

    private static Rect Taller(Rect rect, double extra) => new(rect.X, rect.Y, rect.Width, rect.Height + extra);

    private static void Near(Rect expected, Rect actual, string what)
    {
        Assert.True(Math.Abs(expected.X - actual.X) <= Tolerance && Math.Abs(expected.Y - actual.Y) <= Tolerance
            && Math.Abs(expected.Width - actual.Width) <= Tolerance && Math.Abs(expected.Height - actual.Height) <= Tolerance,
            $"{what}: the fixture has {expected}, the sheet {actual}");
    }

    [Fact] // GR-2.geometry
    [Trait("Decision", "ED-288")]
    public void The_sheet_hangs_from_the_top_centred_on_the_pane_and_its_parts_sit_where_the_fixtures_do() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        var frame = GettingReadyFixture.Frame("workEnd");
        var extra = Extra(rig);
        Assert.InRange(extra, 0, GettingReadyLook.SubLine); // one line or two
        var sheet = Fixture(frame.GetProperty("sheet"));
        Near(Taller(sheet, extra), rig.Rect(rig.Sheet.Card), "the sheet");
        Near(Fixture(frame.GetProperty("title")), rig.Rect(rig.Sheet.TitleBox), "the title");
        var line = Fixture(frame.GetProperty("sub"));
        Near(Taller(line, extra), rig.Rect(rig.Sheet.LineText), "the line");
        Near(Fixture(frame.GetProperty("list"), extra), rig.Rect(rig.Sheet.List), "the list");
        // step 15: the percentage sits at the bar's right (a 34 px figure and a 10 px gap), so the track is that much shorter than the full-width one the page was measured with; its place and its 4 px height are the fixture's
        var barTrack = Fixture(frame.GetProperty("bar"), extra);
        Near(new Rect(barTrack.X, barTrack.Y, barTrack.Width - GettingReadyLook.PercentWidth - GettingReadyLook.PercentGap, barTrack.Height), rig.Rect(rig.Sheet.BarTrack), "the bar's track");
        Assert.Equal(0, rig.Rect(rig.Sheet.Card).Top, 3); // it hangs from the top edge
        Assert.Equal((GettingReadyRig.PaneWidth - GettingReadyLook.SheetWidth) / 2, rig.Rect(rig.Sheet.Card).Left, 3); // centred on the detail pane
    });

    /// <summary>The page's line under the title, laid out by Chrome 154 in Segoe UI Variable Text 12.5 px with normal tracking: one line
    /// of 405.5 px in the 408 px box (work/scratch/f-gr-2-fix/subwidth.cjs, span.getClientRects(), canvas measureText 405.499).</summary>
    private const double PageLineWidth = 405.5;

    [Fact] // GR-2.geometry
    [Trait("Decision", "ED-288")]
    public void The_line_under_the_title_breaks_as_the_pages_does_one_line_of_the_pages_length_and_height() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        var line = (TextBlock)rig.Sheet.LineText;
        var lines = line.TextLayout.TextLines;
        Assert.True(lines.Count == 1, $"{lines.Count} lines: {string.Join(" | ", lines.Select(l => $"{l.Width:0.00} of {line.Bounds.Width:0.00}"))}");
        Assert.InRange(lines[0].Width, PageLineWidth - 1, PageLineWidth + 1);
        Assert.Equal(0, Extra(rig)); // the sheet is the page's height
        Near(Fixture(GettingReadyFixture.Frame("workEnd").GetProperty("sub")), rig.Rect(line), "the line");
        Assert.Equal(GettingReadyFixture.Line, line.Text); // W1's words, unchanged
    });

    [Fact] // GR-2.geometry
    [Trait("Decision", "ED-288")]
    public void The_six_steps_are_two_columns_in_the_plans_order_each_row_placed_as_the_fixtures() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        var fixtureRows = GettingReadyFixture.Frame("workEnd").GetProperty("rows").EnumerateArray()
            .Where(row => row.GetProperty("st").GetString() != "skip").ToList();
        Assert.Equal(6, fixtureRows.Count);
        Assert.Equal(fixtureRows.Count, rig.Sheet.Rows.Count);
        var extra = Extra(rig);
        for (var index = 0; index < fixtureRows.Count; index++)
        {
            var expected = fixtureRows[index];
            var row = rig.Sheet.Rows[index];
            Assert.Equal(expected.GetProperty("id").GetString(), row.Id);
            Assert.Equal(expected.GetProperty("label").GetString(), row.Label);
            Near(Fixture(expected.GetProperty("box"), extra), rig.Rect(row.Box), $"row {row.Id}");
            Near(Fixture(expected.GetProperty("glyph"), extra), rig.Rect(row.Glyph), $"glyph of {row.Id}");
            Near(Fixture(expected.GetProperty("mark"), extra), rig.Rect(row.Mark), $"mark of {row.Id}");
            var text = Fixture(expected.GetProperty("text"), extra);
            var actual = rig.Rect(row.Text);
            Assert.True(Math.Abs(text.X - actual.X) <= Tolerance, $"label of {row.Id}: left {actual.X}, fixture {text.X}");
            Assert.True(Math.Abs((text.Y + text.Height / 2) - (actual.Y + actual.Height / 2)) <= 1.25, $"label of {row.Id}: middle {actual.Y + actual.Height / 2}, fixture {text.Y + text.Height / 2}");
        }
    });

    [Fact] // GR-2.geometry
    [Trait("Decision", "ED-288")]
    public void Five_steps_until_the_voice_hardware_check_registers_fill_three_rows_the_same_height() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig(GettingReadyRig.W1Plan().Take(5).ToList());
        var frame = GettingReadyFixture.Frame("workEnd");
        Assert.Equal(5, rig.Sheet.Rows.Count);
        var extra = Extra(rig);
        var sheet = Fixture(frame.GetProperty("sheet"));
        Near(Taller(sheet, extra), rig.Rect(rig.Sheet.Card), "the sheet");
        var fifth = Fixture(frame.GetProperty("rows")[4].GetProperty("box"), extra);
        Near(fifth, rig.Rect(rig.Sheet.Rows[4].Box), "the fifth row, alone in its row, in the left column");
    });

    [Theory] // GR-2.geometry
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(6, 3)]
    [Trait("Decision", "ED-288")]
    public void The_list_is_as_many_rows_of_two_as_the_plan_needs(int steps, int rows) => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig(GettingReadyRig.W1Plan().Take(steps).ToList());
        Assert.Equal(rows * GettingReadyLook.RowHeight, rig.Rect(rig.Sheet.List).Height, 3);
        Assert.InRange(rig.Rect(rig.Sheet.Card).Height - Extra(rig), 224.25 - (3 - rows) * GettingReadyLook.RowHeight - Tolerance, 224.25 - (3 - rows) * GettingReadyLook.RowHeight + Tolerance);
    });

    [Fact] // GR-2.geometry
    [Trait("Decision", "ED-288")]
    public void The_sheets_title_and_line_say_W1s_words_and_the_bars_fill_is_the_progress() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        Assert.Equal(GettingReadyFixture.Title, rig.Sheet.TitleWord);
        Assert.Equal(GettingReadyFixture.Line, ((TextBlock)rig.Sheet.LineText).Text);
        Assert.Equal(0, rig.Sheet.BarFraction, 3); // nothing is done yet
        var track = rig.Rect(rig.Sheet.BarTrack);
        Assert.Equal(4, track.Height, 3); // step 15: the 4 px bar
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Done, 1));
        rig.Run(1.2);
        Assert.Equal(1.0 / 6, rig.Sheet.BarFraction, 2); // the fill is one sixth of the track
        var percent = rig.Rect(rig.Sheet.Percent);
        Assert.Equal(GettingReadyLook.PercentWidth, percent.Width, 3); // the figure: 34 px wide, 10 px from the track
        Assert.Equal(track.Right + GettingReadyLook.PercentGap, percent.Left, 3);
        Assert.Equal("16%", rig.Sheet.PercentText);
    });
}
