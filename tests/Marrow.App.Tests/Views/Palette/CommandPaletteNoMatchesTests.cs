using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Palette;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.CommandPaletteRig;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeColors = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Palette;

// Source: DroppyCode/App/Views/Palette/CommandPalette.swift (B3 52bc792), :66-76 (the "Nothing matches" line).
[Collection("Headless")]
public sealed class CommandPaletteNoMatchesTests(HeadlessSession headless)
{
    private const char Open = '“';
    private const char Close = '”';

    [Fact]
    [Trait("Req", "UI-WIN-031")] // :66-76: body 13 secondary, one line, middle truncation of a long query, insets 18/0/18/16, leading aligned
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-031: type, insets and the middle truncation; the untrimmed query and the rows' return are the test below
    public void The_no_matches_line_truncates_the_middle_of_a_long_query_and_keeps_its_insets()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            // 160 distinct, unmatchable characters, so both ends differ.
            var query = "q" + string.Concat(Enumerable.Range(0, 160).Select(i => (char)('Ā' + i))) + "z";
            harness.Main.OnMain(() =>
            {
                (_, window, palette) = Build(model);
                palette.QueryField.Text = query;
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                var line = palette.NoMatchesText;
                Assert.Same(line, palette.ResultsSlot.Children[0]);
                Assert.Equal("Nothing matches " + Open + query + Close, line.FullText);
                Assert.Equal(1, line.MaxLines);
                Assert.Equal(new Thickness(18, 0, 18, 16), line.Margin);
                Assert.Equal(SwiftFont.Body.PointSize, line.FontSize);
                Assert.Equal(13, line.FontSize);
                Assert.Equal(ChromeColors.SecondaryText.Resolve(line.ActualThemeVariant),
                    Assert.IsAssignableFrom<ISolidColorBrush>(line.Foreground).Color);
                Assert.Equal(HorizontalAlignment.Stretch, line.HorizontalAlignment);
                Assert.Equal(TextAlignment.Start, line.TextAlignment);
                Assert.Equal(564, line.Bounds.Width, 1); // 600 minus the 18-point insets: the line has to truncate to fit

                // Middle truncation: the head ("Nothing matches " + the opening quote) and the tail (the last query
                // characters + the closing quote) stay readable, the ellipsis sits between them.
                var shown = line.Text!;
                Assert.StartsWith("Nothing matches " + Open + "q", shown, StringComparison.Ordinal);
                Assert.EndsWith("z" + Close, shown, StringComparison.Ordinal);
                var ellipsis = shown.IndexOf('…', StringComparison.Ordinal);
                Assert.True(ellipsis > 16 && ellipsis < shown.Length - 3, "the ellipsis is inside the text, not at an end");
                Assert.True(shown.Length < line.FullText.Length);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-031")] // :66-76: the raw, untrimmed query is shown; the rows return when the query is cleared
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-031: the message text and the swap with the rows; the type and truncation are the test above
    public void The_no_matches_line_shows_the_untrimmed_query_and_the_rows_return_when_it_is_cleared()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            harness.Main.OnMain(() =>
            {
                (_, window, palette) = Build(model);
                Assert.Same(palette.RowsPanel, palette.ResultsSlot.Children[0]); // the list while there are rows
                palette.QueryField.Text = "  zzzq ";
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Equal("Nothing matches " + Open + "  zzzq " + Close, palette.NoMatchesText.FullText); // the query as typed, spaces kept
                Assert.Same(palette.NoMatchesText, Assert.Single(palette.ResultsSlot.Children)); // the row list is gone while the line shows
                palette.QueryField.Text = "";
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                Assert.True(palette.RowsPanel.Children.Count > 0);
                Assert.Same(palette.RowsPanel, Assert.Single(palette.ResultsSlot.Children));
            });
        });
    }
}
