using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B3 52bc792), MarkdownBlockView's heading (:302-305,
// :376-383), quote (:354-365) and rule (:368-372) cases, through the finished block tree. Covers UI-THEME-028 (the
// streaming / standalone path), UI-THEME-032 and UI-THEME-033. The finished-reply prose path of the same records
// (point size, indent, characters) is covered in MarkdownProsePathTests.
[Collection("Headless")]
public sealed class MarkdownBlockViewBlocksTests(HeadlessSession headless)
{
    private static MarkdownBlock Quote(params MarkdownBlock[] blocks) => new MarkdownBlock.Quote(new ValueList<MarkdownBlock>(blocks));

    private static MarkdownBlock Code(string code) => new MarkdownBlock.Code(null, code);

    private static InlineText HeadingText(MarkdownBlockView view) => Assert.IsType<InlineText>(view.Child);

    private static TextBlock TextOf(Visual root, string text) =>
        MarkdownVisualProbe.TextBlocksSaying(root, text).Single();

    // ---- UI-THEME-028: heading, streaming or standalone path ----

    // :302-305 .padding(.top, level <= 2 ? 6 : 2)
    [Theory]
    [Trait("Req", "UI-THEME-028")]
    [InlineData(1, 6)]
    [InlineData(2, 6)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    [InlineData(6, 2)]
    public void A_heading_has_6_points_above_for_levels_1_and_2_and_2_for_the_rest(int level, double above) => headless.OnUiThread(() =>
    {
        var view = new MarkdownBlockView(new MarkdownBlock.Heading(level, "Title"));
        var window = MarkdownVisualProbe.Show(view);

        Assert.Equal(above, MarkdownVisualProbe.Origin(HeadingText(view), view).Y, 3);
        Assert.Equal(0, MarkdownVisualProbe.Origin(HeadingText(view), view).X, 3);
        window.Close();
    });

    // :376-383 title2 / title3 semibold, headline (semibold by the helper's default), subheadline semibold; no zoom.
    [Theory]
    [Trait("Req", "UI-THEME-028")]
    [InlineData(1, 17, 600)]
    [InlineData(2, 15, 600)]
    [InlineData(3, 13, 600)]
    [InlineData(4, 11, 600)]
    [InlineData(5, 11, 600)]
    [InlineData(6, 11, 600)]
    public void A_heading_is_drawn_in_the_text_style_of_its_level_semibold(int level, double size, int weight) => headless.OnUiThread(() =>
    {
        var view = new MarkdownBlockView(new MarkdownBlock.Heading(level, "Title"));
        var window = MarkdownVisualProbe.Show(view);

        var text = TextOf(view, "Title");
        Assert.Equal(size, text.FontSize);
        Assert.Equal(weight, (int)text.FontWeight);
        Assert.Equal("Inter", text.FontFamily.Name);
        window.Close();
    });

    // UI-THEME-028 colours: the primary label colour, and inside a quote the label colour at 70 percent.
    [Theory]
    [Trait("Req", "UI-THEME-028")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_heading_is_the_primary_label_colour_and_the_label_colour_at_70_percent_inside_a_quote(bool dark) => headless.OnUiThread(() =>
    {
        var plain = new MarkdownBlockView(new MarkdownBlock.Heading(2, "Plain"));
        var quoted = new MarkdownBlockView(Quote(new MarkdownBlock.Heading(2, "Quoted")));
        var column = new StackPanel();
        column.Children.Add(plain);
        column.Children.Add(quoted);
        var window = MarkdownVisualProbe.Show(column, theme: dark ? ThemeVariant.Dark : ThemeVariant.Light);

        var primary = MarkdownVisualProbe.Drawn(SwiftColor.Primary, window);
        var plainBrush = TextOf(plain, "Plain").Foreground;
        if (plainBrush is not null) Assert.Equal(primary, MarkdownVisualProbe.ColorOf(plainBrush));
        Assert.Equal(MarkdownVisualProbe.Drawn(SwiftColor.Primary.Opacity(0.7), window), MarkdownVisualProbe.ColorOf(TextOf(quoted, "Quoted").Foreground));
        window.Close();
    });

    // ---- UI-THEME-032: block quote ----

    // :355-363 HStack(alignment: .top, spacing: 10) { Capsule().fill(.quaternary).frame(width: 3); VStack(spacing: 8) }
    [Fact]
    [Trait("Req", "UI-THEME-032")]
    public void A_quote_is_a_3_point_quaternary_capsule_the_full_height_of_its_content_10_points_before_it() => headless.OnUiThread(() =>
    {
        var view = new MarkdownBlockView(Quote(Code("one"), Code("two")));
        var window = MarkdownVisualProbe.Show(view);

        var grid = Assert.IsType<Grid>(view.Child);
        var bar = grid.Children.OfType<Border>().Single();
        var content = grid.Children.OfType<StackPanel>().Single();
        Assert.Equal(3, bar.Bounds.Width, 3);
        Assert.Equal(content.Bounds.Height, bar.Bounds.Height, 3); // runs the full height
        Assert.Equal(0, bar.Bounds.X, 3);
        Assert.Equal(1.5, bar.CornerRadius.TopLeft, 3); // a capsule: fully rounded ends
        Assert.Equal(10, content.Bounds.X - bar.Bounds.Right, 3);
        Assert.Equal(MarkdownVisualProbe.Drawn(SwiftColor.Quaternary, window), MarkdownVisualProbe.ColorOf(bar.Background));
        window.Close();
    });

    // :357 VStack(alignment: .leading, spacing: 8)
    [Fact]
    [Trait("Req", "UI-THEME-032")]
    public void The_blocks_inside_a_quote_are_8_points_apart() => headless.OnUiThread(() =>
    {
        var view = new MarkdownBlockView(Quote(Code("one"), Code("two"), Code("three")));
        var window = MarkdownVisualProbe.Show(view);

        var content = Assert.IsType<Grid>(view.Child).Children.OfType<StackPanel>().Single();
        Assert.Equal(3, content.Children.Count);
        Assert.Equal(8, content.Children[1].Bounds.Top - content.Children[0].Bounds.Bottom, 3);
        Assert.Equal(8, content.Children[2].Bounds.Top - content.Children[1].Bounds.Bottom, 3);
        window.Close();
    });

    // :364 .foregroundStyle(Chrome.secondaryText): the quoted text is the label colour at 70 percent, the bar is not.
    [Theory]
    [Trait("Req", "UI-THEME-032")]
    [InlineData(false)]
    [InlineData(true)]
    public void Quoted_text_is_the_label_colour_at_70_percent(bool dark) => headless.OnUiThread(() =>
    {
        var view = new MarkdownBlockView(Quote(new MarkdownBlock.Heading(3, "Quoted")));
        var window = MarkdownVisualProbe.Show(view, theme: dark ? ThemeVariant.Dark : ThemeVariant.Light);

        Assert.Equal(MarkdownVisualProbe.Drawn(SwiftColor.Primary.Opacity(0.7), window), MarkdownVisualProbe.ColorOf(TextOf(view, "Quoted").Foreground));
        window.Close();
    });

    // :365 .environment(\.markdownDimmed, true) reaches a quote inside a quote and a list inside a quote.
    [Fact]
    [Trait("Req", "UI-THEME-032")]
    public void Everything_inside_a_quote_is_dimmed_however_deep() => headless.OnUiThread(() =>
    {
        var view = new MarkdownBlockView(Quote(Quote(Code("deep"))));
        var window = MarkdownVisualProbe.Show(view);

        var blocks = VisualQuery.Descendants<MarkdownBlockView>(view).ToList();
        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, block => Assert.True(MarkdownView.GetMarkdownDimmed(block)));
        Assert.False(MarkdownView.GetMarkdownDimmed(view));
        window.Close();
    });

    // ---- UI-THEME-033: horizontal rule ----

    // :368-372 Rectangle().fill(.quaternary).frame(height: 1).padding(.vertical, 4), full width.
    [Theory]
    [Trait("Req", "UI-THEME-033")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_rule_is_a_full_width_1_point_quaternary_line_with_4_points_above_and_below(bool dark) => headless.OnUiThread(() =>
    {
        var view = new MarkdownBlockView(new MarkdownBlock.Rule());
        var window = MarkdownVisualProbe.Show(view, width: 320, theme: dark ? ThemeVariant.Dark : ThemeVariant.Light);

        var line = Assert.IsType<Rectangle>(view.Child);
        Assert.Equal(1, line.Bounds.Height, 3);
        Assert.Equal(320, line.Bounds.Width, 3);
        Assert.Equal(4, MarkdownVisualProbe.Origin(line, view).Y, 3);
        Assert.Equal(1 + 4 + 4, view.Bounds.Height, 3);
        Assert.Equal(MarkdownVisualProbe.Drawn(SwiftColor.Quaternary, window), MarkdownVisualProbe.ColorOf(line.Fill));
        window.Close();
    });

    // :86 the message's VStack(spacing: 12) puts 12 points between blocks, so the line is 16 from its neighbours.
    [Fact]
    [Trait("Req", "UI-THEME-033")]
    public void A_rule_between_two_blocks_is_16_points_from_each_inside_the_messages_12_point_spacing() => headless.OnUiThread(() =>
    {
        var view = new MarkdownView("```\none\n```\n\n---\n\n```\ntwo\n```", isStreaming: true);
        var window = MarkdownVisualProbe.Show(view);

        var blocks = VisualQuery.Descendants<MarkdownBlockView>(view).ToList();
        Assert.Equal(3, blocks.Count);
        var line = Assert.IsType<Rectangle>(blocks[1].Child);
        Assert.Equal(16, MarkdownVisualProbe.Origin(line, view).Y - blocks[0].Bounds.Bottom, 3);
        Assert.Equal(16, blocks[2].Bounds.Top - (MarkdownVisualProbe.Origin(line, view).Y + line.Bounds.Height), 3);
        window.Close();
    });
}
