using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B3 52bc792), struct TableBlock, the body at
// :1284-1323 (the reference file's own numbering; the records cite the B2 numbers :1357-1396). Covers
// UI-THEME-038 and UI-THEME-039 through the rendered control. The Show-all expansion and the semibold header are
// also covered under PR-THEME-119 by MarkdownBlockViewBodyTests.
[Collection("Headless")]
public sealed class TableBlockPanelTests(HeadlessSession headless)
{
    private static IReadOnlyList<IReadOnlyList<string>> Rows(params string[][] rows) => rows;

    private static IReadOnlyList<IReadOnlyList<string>> Numbered(int count) =>
        Enumerable.Range(1, count).Select(i => (IReadOnlyList<string>)[i.ToString(System.Globalization.CultureInfo.InvariantCulture)]).ToList();

    private static Border Panel(TableBlock table) => Assert.IsType<Border>(table.Child);

    private static StackPanel Host(TableBlock table) => Assert.IsType<StackPanel>(Panel(table).Child);

    private static ScrollViewer Scroll(TableBlock table) => Assert.IsType<ScrollViewer>(Host(table).Children[0]);

    private static Grid Cells(TableBlock table) => Assert.IsType<Grid>(Scroll(table).Content);

    private static TextBlock Cell(TableBlock table, int row, int column) =>
        Cells(table).Children.OfType<TextBlock>().Single(text => Grid.GetRow(text) == row && Grid.GetColumn(text) == column);

    private static Button? FooterButton(TableBlock table) => Host(table).Children.OfType<Button>().SingleOrDefault();

    private static string ButtonTitle(Button button) => MarkdownVisualProbe.TextOf(Assert.IsType<TextBlock>(button.Content));

    private static int BodyRowCount(TableBlock table) => Cells(table).Children.OfType<TextBlock>().Select(Grid.GetRow).Distinct().Count() - 1;

    // :1322 .background(.quaternary.opacity(0.35), in: .rect(cornerRadius: 12))
    [Theory]
    [Trait("Req", "UI-THEME-038")]
    [InlineData(false)]
    [InlineData(true)]
    public void The_panel_is_a_12_point_rounded_fill_of_the_quaternary_label_colour_at_35_percent(bool dark) => headless.OnUiThread(() =>
    {
        var table = new TableBlock(["Name"], Rows(["Ada"]));
        var window = MarkdownVisualProbe.Show(table, theme: dark ? ThemeVariant.Dark : ThemeVariant.Light);

        var panel = Panel(table);
        Assert.Equal(new Avalonia.CornerRadius(12), panel.CornerRadius);
        Assert.Equal(MarkdownVisualProbe.Drawn(SwiftColor.Quaternary.Opacity(0.35), window), MarkdownVisualProbe.ColorOf(panel.Background));
        window.Close();
    });

    // :1292 Grid(alignment: .leading, horizontalSpacing: 18, verticalSpacing: 8) and :1309 .padding(12).
    [Fact]
    [Trait("Req", "UI-THEME-038")]
    public void The_grid_is_padded_12_all_round_with_18_between_columns_and_8_between_rows() => headless.OnUiThread(() =>
    {
        var table = new TableBlock(["Name", "Age", "City"], Rows(["Ada", "30", "Oslo"], ["Lin", "40", "Rome"]));
        var window = MarkdownVisualProbe.Show(table);

        var panel = Panel(table);
        var grid = Cells(table);
        var gridAt = MarkdownVisualProbe.Origin(grid, panel);
        Assert.Equal(12, gridAt.X, 3);
        Assert.Equal(12, gridAt.Y, 3);
        Assert.Equal(panel.Bounds.Height, gridAt.Y + grid.Bounds.Height + 12, 3); // 12 below, none extra under the last row

        for (var row = 0; row <= 2; row++)
        for (var column = 0; column < 2; column++)
        {
            var left = Cell(table, row, column);
            var right = Cell(table, row, column + 1);
            Assert.Equal(18, MarkdownVisualProbe.Origin(right, grid).X - (MarkdownVisualProbe.Origin(left, grid).X + left.Bounds.Width), 3);
        }
        for (var row = 0; row < 2; row++)
        for (var column = 0; column < 3; column++)
        {
            var above = Cell(table, row, column);
            var below = Cell(table, row + 1, column);
            Assert.Equal(8, MarkdownVisualProbe.Origin(below, grid).Y - (MarkdownVisualProbe.Origin(above, grid).Y + above.Bounds.Height), 3);
        }
        window.Close();
    });

    // :1285-1288, :1299 with no rows at all the grid is only the header row: no vertical spacing is added.
    [Fact]
    [Trait("Req", "UI-THEME-038")]
    public void A_table_with_no_rows_is_only_the_header_with_no_spacing_under_it() => headless.OnUiThread(() =>
    {
        var table = new TableBlock(["Name", "Age"], Rows());
        var window = MarkdownVisualProbe.Show(table);

        var grid = Cells(table);
        Assert.Equal(Cell(table, 0, 0).Bounds.Height, grid.Bounds.Height, 3);
        window.Close();
    });

    // :1295-1297 header cells semibold; :1303 body cells primary at 90 percent; every cell leading-aligned (UI-THEME-038 notes).
    [Theory]
    [Trait("Req", "UI-THEME-038")]
    [InlineData(false)]
    [InlineData(true)]
    public void Header_cells_are_semibold_in_the_primary_colour_and_body_cells_regular_at_90_percent(bool dark) => headless.OnUiThread(() =>
    {
        var table = new TableBlock(["Name", "Age"], Rows(["Ada", "30"]));
        var window = MarkdownVisualProbe.Show(table, theme: dark ? ThemeVariant.Dark : ThemeVariant.Light);

        var primary = MarkdownVisualProbe.Drawn(SwiftColor.Primary, window);
        foreach (var column in new[] { 0, 1 })
        {
            var header = Cell(table, 0, column);
            Assert.Equal(FontWeight.SemiBold, header.FontWeight);
            // No colour of its own: it takes the inherited primary label colour, never the dimmed body colour.
            if (header.Foreground is not null) Assert.Equal(primary, MarkdownVisualProbe.ColorOf(header.Foreground));

            var body = Cell(table, 1, column);
            Assert.NotEqual(FontWeight.SemiBold, body.FontWeight);
            Assert.Equal(MarkdownVisualProbe.Drawn(SwiftColor.Primary.Opacity(0.9), window), MarkdownVisualProbe.ColorOf(body.Foreground));
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, body.HorizontalAlignment); // fills its column, text at its leading edge
            Assert.Equal(TextAlignment.Start, body.TextAlignment);
        }
        window.Close();
    });

    // :1308 .modifier(MarkdownBlockSelection()) on the grid: its live branch is .textSelection(.enabled) (:890-899; the
    // environment value has no writer, dead_code.md 30), so every cell's text, header and body, is selectable in its own block, with
    // the I-beam over it (PR-CHAT-166 windows_path) and a selection drawn in the system's selected-text colour (a SelectableTextBlock
    // draws none with no control theme loaded). Selection runs within one cell: whether a Mac drag runs on across cells of the grid is
    // UNRESOLVED (ED-NEW-parity-timelinerows).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-THEME-126 and PR-CHAT-166 for the table block, no drag across cells: MarkdownView.swift:1308, :890-899
    public void Every_header_and_body_cell_is_selectable_text_with_the_I_beam_and_a_selection_in_the_system_colour() => headless.OnUiThread(() =>
    {
        var table = new TableBlock(["Name", "Age"], Rows(["Ada", "30"], ["Lin", "40"]));
        var window = MarkdownVisualProbe.Show(table);

        var system = MarkdownVisualProbe.Drawn(SwiftColor.SelectedTextBackgroundColor, window);
        Assert.NotEqual(0, system.A);
        var cells = Cells(table).Children.OfType<TextBlock>().ToList();
        Assert.Equal(6, cells.Count); // 2 header + 2 x 2 body
        foreach (var cell in cells)
        {
            var text = Assert.IsType<SelectableTextBlock>(cell);
            Assert.Equal("Ibeam", text.Cursor?.ToString());
            Assert.Equal(system, MarkdownVisualProbe.ColorOf(text.SelectionBrush));
            text.SelectAll();
            Assert.Equal(text.Text, text.SelectedText);
        }
        window.Close();
    });

    // :1302 column < cells[row].count ? cells[row][column] : "": short rows are padded, extra cells never appear.
    [Fact]
    [Trait("Req", "UI-THEME-038")]
    public void A_short_row_is_padded_with_empty_cells_and_extra_cells_are_dropped() => headless.OnUiThread(() =>
    {
        var table = new TableBlock(["A", "B"], Rows(["only"], ["x", "y", "extra"]));
        var window = MarkdownVisualProbe.Show(table);

        Assert.Equal("only", MarkdownVisualProbe.TextOf(Cell(table, 1, 0)));
        Assert.Equal("", MarkdownVisualProbe.TextOf(Cell(table, 1, 1)));
        Assert.Equal(["x", "y"], new[] { Cell(table, 2, 0), Cell(table, 2, 1) }.Select(MarkdownVisualProbe.TextOf));
        Assert.Equal(6, Cells(table).Children.OfType<TextBlock>().Count()); // 2 header + 2 x 2 body
        Assert.DoesNotContain(MarkdownVisualProbe.TextBlocksSaying(table, "extra"), _ => true);
        window.Close();
    });

    // :1289 ScrollView(.horizontal, showsIndicators: false).
    [Fact]
    [Trait("Req", "UI-THEME-038")]
    public void A_wide_table_scrolls_sideways_without_a_visible_indicator() => headless.OnUiThread(() =>
    {
        var wide = string.Concat(Enumerable.Repeat("0123456789", 20));
        var table = new TableBlock(["A", "B"], Rows([wide, wide]));
        var window = MarkdownVisualProbe.Show(table, width: 300);

        var scroll = Scroll(table);
        Assert.True(scroll.Extent.Width > scroll.Viewport.Width + 100);
        Assert.Equal(ScrollBarVisibility.Hidden, scroll.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.VerticalScrollBarVisibility);
        Assert.DoesNotContain(VisualQuery.Descendants<ScrollBar>(scroll), bar => bar.IsVisible);
        window.Close();
    });

    // :1271, :1285, :1311: 30 rows show whole; the 31st collapses the table to 30 under "Show all 31 rows".
    [Fact]
    [Trait("Req", "UI-THEME-038")]
    [Trait("Req", "UI-THEME-039")]
    public void A_table_of_exactly_30_rows_shows_every_row_and_no_button() => headless.OnUiThread(() =>
    {
        var table = new TableBlock(["N"], Numbered(30));
        var window = MarkdownVisualProbe.Show(table);

        Assert.Null(FooterButton(table));
        Assert.Equal(30, BodyRowCount(table));
        window.Close();
    });

    // :1311-1313 the button names the total row count; :1285 the first 30 rows show until it is pressed;
    // once expanded it is gone for good (no "Show less").
    [Fact]
    [Trait("Req", "UI-THEME-038")]
    [Trait("Req", "UI-THEME-039")]
    public void A_longer_table_collapses_to_30_rows_under_a_show_all_button_that_is_gone_once_pressed() => headless.OnUiThread(() =>
    {
        var table = new TableBlock(["N"], Numbered(41));
        var window = MarkdownVisualProbe.Show(table);

        var button = Assert.IsType<Button>(FooterButton(table));
        Assert.Equal("Show all 41 rows", ButtonTitle(button));
        Assert.Equal(30, BodyRowCount(table));
        Assert.Equal("30", MarkdownVisualProbe.TextOf(Cell(table, 30, 0)));

        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        MarkdownVisualProbe.Pump();

        Assert.Equal(41, BodyRowCount(table));
        Assert.Equal("41", MarkdownVisualProbe.TextOf(Cell(table, 41, 0)));
        Assert.Null(FooterButton(table));
        Assert.DoesNotContain(VisualQuery.Descendants<TextBlock>(table), text => MarkdownVisualProbe.TextOf(text).StartsWith("Show", StringComparison.Ordinal));
        window.Close();
    });

    // :1315-1319 link button, caption, Chrome.secondaryText, padding horizontal 12, bottom 10.
    [Theory]
    [Trait("Req", "UI-THEME-039")]
    [InlineData(false)]
    [InlineData(true)]
    public void The_show_all_button_is_caption_text_in_the_label_colour_at_70_percent_padded_12_and_10(bool dark) => headless.OnUiThread(() =>
    {
        var table = new TableBlock(["N"], Numbered(35));
        var window = MarkdownVisualProbe.Show(table, theme: dark ? ThemeVariant.Dark : ThemeVariant.Light);

        var button = Assert.IsType<Button>(FooterButton(table));
        var label = Assert.IsType<TextBlock>(button.Content);
        Assert.Equal(10, label.FontSize);
        Assert.Equal("Inter", label.FontFamily.Name);
        Assert.Equal(MarkdownVisualProbe.Drawn(SwiftColor.Primary.Opacity(0.7), window), MarkdownVisualProbe.ColorOf(label.Foreground)); // Chrome.secondaryText

        var panel = Panel(table);
        var at = MarkdownVisualProbe.Origin(label, panel);
        Assert.Equal(12, at.X, 3); // horizontal 12
        Assert.Equal(panel.Bounds.Height, at.Y + label.Bounds.Height + 10, 3); // bottom 10
        Assert.Equal(Scroll(table).Bounds.Bottom, MarkdownVisualProbe.Origin(button, panel).Y, 3); // under the scroll view, stack spacing 0
        window.Close();
    });
}
