using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using ChromeNs = Marrow.UI.Chrome;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B3 52bc792), MarkdownBlockView's list case
// (:337-353) and marker(ordered:number:checked:) (:465-497), through the finished block tree: a list built in a
// real view, the markers a viewer sees. Covers UI-THEME-029 (bullet), UI-THEME-030 (ordered number) and
// UI-THEME-031 (checkbox). The prose-run path (text characters) is covered by ProseRunViewTests.
[Collection("Headless")]
public sealed class MarkdownBlockViewMarkerTests(HeadlessSession headless)
{
    private static MarkdownListItem Item(string text, bool? @checked = null, params MarkdownBlock[] children) =>
        new(text, @checked, new ValueList<MarkdownBlock>(children));

    private static MarkdownBlock List(bool ordered, int start, params MarkdownListItem[] items) =>
        new MarkdownBlock.List(ordered, start, new ValueList<MarkdownListItem>(items));

    private static MarkdownBlock Quote(params MarkdownBlock[] blocks) => new MarkdownBlock.Quote(new ValueList<MarkdownBlock>(blocks));

    // The marker of each list row, in document order: the first child of every row of every list in the tree.
    private static System.Collections.Generic.List<Control> Markers(Control root) =>
        VisualQuery.Descendants<StackPanel>(root)
            .Where(panel => panel.Orientation == Avalonia.Layout.Orientation.Horizontal && panel.Children.Count == 2 && panel.Children[1] is StackPanel)
            .Select(row => row.Children[0])
            .ToList();

    private static void AssertDrawn(SwiftColor expected, IBrush? actual, Control on) =>
        Assert.Equal(MarkdownVisualProbe.Drawn(expected, on), MarkdownVisualProbe.ColorOf(actual));

    // :482-491 a filled 5 x 5 circle at the top level, a 5.5 x 5.5 ring stroked at 1.2 one list deeper (:350 markdownListDepth + 1).
    [Fact]
    [Trait("Req", "UI-THEME-029")]
    public void A_top_level_bullet_is_a_filled_5_point_dot_and_every_nested_one_a_5_5_point_ring_stroked_at_1_2() => headless.OnUiThread(() =>
    {
        var view = new MarkdownBlockView(List(false, 1, Item("top", null, List(false, 1, Item("nested", null, List(false, 1, Item("deep")))))));
        var window = MarkdownVisualProbe.Show(view);

        // :350 each list reads one level deeper than the one around it: 0 for the outer block, then 1, then 2.
        Assert.Equal([0, 1, 2], new[] { view }.Concat(VisualQuery.Descendants<MarkdownBlockView>(view)).Select(MarkdownView.GetMarkdownListDepth));

        var markers = Markers(view);
        Assert.Equal(3, markers.Count);
        var dot = Assert.IsType<Ellipse>(markers[0]);
        Assert.Equal((5.0, 5.0), (dot.Bounds.Width, dot.Bounds.Height));
        Assert.NotNull(dot.Fill);
        Assert.Null(dot.Stroke);
        foreach (var marker in markers.Skip(1))
        {
            var ring = Assert.IsType<Ellipse>(marker);
            Assert.Equal((5.5, 5.5), (ring.Width, ring.Height));
            Assert.Equal(1.2, ring.StrokeThickness);
            Assert.NotNull(ring.Stroke);
            Assert.Null(ring.Fill);
        }
        window.Close();
    });

    // :495-497 Chrome.accent at 0.9, or 0.55 when dimmed; :364 a quote's content is dimmed, nested lists included.
    [Theory]
    [Trait("Req", "UI-THEME-029")]
    [Trait("Req", "UI-THEME-030")]
    [InlineData(false)]
    [InlineData(true)]
    public void Bullets_and_numbers_are_the_accent_at_90_percent_and_at_55_percent_inside_a_quote(bool dark) => headless.OnUiThread(() =>
    {
        var plain = new MarkdownBlockView(List(false, 1, Item("a", null, List(false, 1, Item("b"))), Item("c")));
        var ordered = new MarkdownBlockView(List(true, 1, Item("n")));
        var quoted = new MarkdownBlockView(Quote(List(false, 1, Item("a", null, List(false, 1, Item("b")))), List(true, 1, Item("n"))));
        var column = new StackPanel();
        column.Children.Add(plain);
        column.Children.Add(ordered);
        column.Children.Add(quoted);
        var window = MarkdownVisualProbe.Show(column, theme: dark ? ThemeVariant.Dark : ThemeVariant.Light);

        var accent90 = ChromeNs.Chrome.Accent.Opacity(0.9);
        var accent55 = ChromeNs.Chrome.Accent.Opacity(0.55);
        var outside = Markers(plain).Concat(Markers(ordered)).ToList();
        Assert.Equal(4, outside.Count);
        foreach (var marker in outside) AssertDrawn(accent90, marker is Ellipse ellipse ? ellipse.Fill ?? ellipse.Stroke : ((TextBlock)marker).Foreground, window);

        var inside = Markers(quoted);
        Assert.Equal(3, inside.Count);
        foreach (var marker in inside) AssertDrawn(accent55, marker is Ellipse ellipse ? ellipse.Fill ?? ellipse.Stroke : ((TextBlock)marker).Foreground, window);
        window.Close();
    });

    // :471-475 Text("\(number).") with monospacedDigit and fontWeight(.medium), the number counting from the list's start.
    [Fact]
    [Trait("Req", "UI-THEME-030")]
    public void An_ordered_marker_is_the_number_and_a_full_stop_in_medium_weight_with_fixed_width_digits() => headless.OnUiThread(() =>
    {
        var view = new MarkdownBlockView(List(true, 3, Item("a"), Item("b"), Item("c")));
        var window = MarkdownVisualProbe.Show(view);

        var markers = Markers(view).Cast<TextBlock>().ToList();
        Assert.Equal(["3.", "4.", "5."], markers.Select(MarkdownVisualProbe.TextOf));
        foreach (var marker in markers)
        {
            Assert.Equal(FontWeight.Medium, marker.FontWeight);
            Assert.Contains(marker.FontFeatures!, feature => feature.Tag == "tnum" && feature.Value == 1);
        }
        window.Close();
    });

    // UI-THEME-030 notes: a full stop even when the source closed the number with a bracket (:472 interpolates "." itself).
    [Fact]
    [Trait("Req", "UI-THEME-030")]
    public void A_list_numbered_with_closing_brackets_still_shows_full_stops() => headless.OnUiThread(() =>
    {
        var view = new MarkdownView("1) first\n2) second", isStreaming: true);
        var window = MarkdownVisualProbe.Show(view);

        Assert.Equal(["1.", "2."], Markers(view).Cast<TextBlock>().Select(MarkdownVisualProbe.TextOf));
        window.Close();
    });

    // :466-470 checked: filled circle with the check in the tint; unchecked: an empty circle in the tertiary colour; small scale.
    [Fact]
    [Trait("Req", "UI-THEME-031")]
    public void A_checkbox_is_a_small_symbol_checked_in_the_full_accent_and_unchecked_in_the_tertiary_colour() => headless.OnUiThread(() =>
    {
        var view = new MarkdownBlockView(List(false, 1, Item("done", true), Item("todo", false)));
        var window = MarkdownVisualProbe.Show(view);

        var markers = Markers(view).Cast<SwiftImage>().ToList();
        var done = markers[0];
        Assert.Equal("checkmark.circle.fill", done.SymbolName);
        Assert.Equal(SwiftImage.Scale.Small, done.SymbolImageScale);
        AssertDrawn(ChromeNs.Chrome.Accent, done.Foreground, window); // .tint, not the 0.9 bullet accent
        var todo = markers[1];
        Assert.Equal("circle", todo.SymbolName);
        Assert.Equal(SwiftImage.Scale.Small, todo.SymbolImageScale);
        AssertDrawn(SwiftColor.Tertiary, todo.Foreground, window);
        window.Close();
    });

    // :466 a checkbox wins over the ordered number; :469 neither of its colours is dimmed inside a quote.
    [Fact]
    [Trait("Req", "UI-THEME-031")]
    public void A_checkbox_replaces_the_number_and_keeps_its_colours_inside_a_quote() => headless.OnUiThread(() =>
    {
        var view = new MarkdownBlockView(Quote(List(true, 1, Item("done", true), Item("todo", false))));
        var window = MarkdownVisualProbe.Show(view);

        var markers = Markers(view).Cast<SwiftImage>().ToList();
        Assert.Equal(["checkmark.circle.fill", "circle"], markers.Select(marker => marker.SymbolName));
        AssertDrawn(ChromeNs.Chrome.Accent, markers[0].Foreground, window);
        AssertDrawn(SwiftColor.Tertiary, markers[1].Foreground, window);
        window.Close();
    });

    // :340 HStack(spacing: 8): 8 points between the marker and the item text, whichever marker it is.
    [Fact]
    [Trait("Req", "UI-THEME-029")]
    [Trait("Req", "UI-THEME-030")]
    [Trait("Req", "UI-THEME-031")]
    public void Every_kind_of_marker_sits_8_points_from_the_item_text() => headless.OnUiThread(() =>
    {
        var view = new MarkdownBlockView(List(false, 1, Item("bullet"), Item("done", true), Item("todo", false)));
        var ordered = new MarkdownBlockView(List(true, 1, Item("one")));
        var column = new StackPanel();
        column.Children.Add(view);
        column.Children.Add(ordered);
        var window = MarkdownVisualProbe.Show(column);

        var rows = VisualQuery.Descendants<StackPanel>(column)
            .Where(panel => panel.Orientation == Avalonia.Layout.Orientation.Horizontal && panel.Children.Count == 2 && panel.Children[1] is StackPanel)
            .ToList();
        Assert.Equal(4, rows.Count);
        foreach (var row in rows)
        {
            var marker = row.Children[0];
            var content = row.Children[1];
            Assert.Equal(8, MarkdownVisualProbe.Origin(content, row).X - (MarkdownVisualProbe.Origin(marker, row).X + marker.Bounds.Width), 3);
        }
        window.Close();
    });
}
