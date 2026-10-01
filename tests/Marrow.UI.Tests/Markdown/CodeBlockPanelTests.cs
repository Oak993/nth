using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Styling;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B3 52bc792), struct CodeBlock, the body at
// :1205-1248 (the reference file's own numbering; the records cite the B2 numbers :1278-1321). Covers
// PR-THEME-117, UI-THEME-034, UI-THEME-035 and UI-THEME-037 through the rendered control.
[Collection("Headless")]
public sealed class CodeBlockPanelTests(HeadlessSession headless)
{
    private static string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => "line " + i));

    private static Border Panel(CodeBlock block) => Assert.IsType<Border>(block.Child);

    private static StackPanel Stack(CodeBlock block) => Assert.IsType<StackPanel>(Panel(block).Child);

    private static Grid Header(CodeBlock block) => Assert.IsType<Grid>(Stack(block).Children[0]);

    private static ScrollViewer Scroll(CodeBlock block) => Assert.IsType<ScrollViewer>(Stack(block).Children[1]);

    private static TextBlock Label(CodeBlock block) => Header(block).Children.OfType<TextBlock>().Single();

    private static Control CopyControl(CodeBlock block) => Header(block).Children.OfType<Control>().Single(child => child is not TextBlock);

    private static VeiledText Code(CodeBlock block) => Assert.IsType<VeiledText>(Scroll(block).Content);

    private static TextBlock CodeText(CodeBlock block) => VisualQuery.Descendants<TextBlock>(Code(block)).Single();

    private static Button? FooterButton(CodeBlock block) => Stack(block).Children.Count > 2 ? Assert.IsType<Button>(Stack(block).Children[2]) : null;

    private static string ButtonTitle(Button button) => MarkdownVisualProbe.TextOf(Assert.IsType<TextBlock>(button.Content));

    private static void Click(Button button)
    {
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        MarkdownVisualProbe.Pump();
    }

    // :1246 .background(.quaternary.opacity(0.45), in: .rect(cornerRadius: 12))
    [Theory]
    [Trait("Req", "PR-THEME-117")]
    [Trait("Req", "UI-THEME-034")]
    [InlineData(false)]
    [InlineData(true)]
    public void The_panel_is_a_12_point_rounded_fill_of_the_quaternary_label_colour_at_45_percent(bool dark) => headless.OnUiThread(() =>
    {
        var block = new CodeBlock("swift", "let x = 1");
        var window = MarkdownVisualProbe.Show(block, theme: dark ? ThemeVariant.Dark : ThemeVariant.Light);

        var panel = Panel(block);
        Assert.Equal(new CornerRadius(12), panel.CornerRadius);
        Assert.Equal(MarkdownVisualProbe.Drawn(SwiftColor.Quaternary.Opacity(0.45), window), MarkdownVisualProbe.ColorOf(panel.Background));
        window.Close();
    });

    // :1216 Text(language ?? "code"); UI-THEME-035 notes: shown as written, not lower-cased.
    [Theory]
    [Trait("Req", "PR-THEME-117")]
    [Trait("Req", "UI-THEME-035")]
    [InlineData("Swift", "Swift")]
    [InlineData("python", "python")]
    [InlineData(null, "code")]
    public void The_header_label_is_the_info_string_as_written_or_the_word_code(string? language, string expected) => headless.OnUiThread(() =>
    {
        var block = new CodeBlock(language, "x");
        Assert.Equal(expected, MarkdownVisualProbe.TextOf(Label(block)));
    });

    // :1217-1218 .font(.chat(.caption)) and .foregroundStyle(Chrome.secondaryText) = label colour at 70 percent.
    [Theory]
    [Trait("Req", "PR-THEME-117")]
    [Trait("Req", "UI-THEME-035")]
    [InlineData(false)]
    [InlineData(true)]
    public void The_header_label_is_caption_text_in_the_label_colour_at_70_percent(bool dark) => headless.OnUiThread(() =>
    {
        var block = new CodeBlock("swift", "x");
        var window = MarkdownVisualProbe.Show(block, theme: dark ? ThemeVariant.Dark : ThemeVariant.Light);

        var label = Label(block);
        Assert.Equal(10, label.FontSize); // .caption, ChatZoom table (PR-CHAT-193)
        Assert.Equal("Inter", label.FontFamily.Name);
        Assert.Equal(MarkdownVisualProbe.Drawn(SwiftColor.Primary.Opacity(0.7), window), MarkdownVisualProbe.ColorOf(label.Foreground)); // Chrome.secondaryText
        window.Close();
    });

    // :1222-1224 header padding leading 12, trailing 6, top 6; :1219-1220 Spacer then the copy control.
    [Fact]
    [Trait("Req", "PR-THEME-117")]
    [Trait("Req", "UI-THEME-034")]
    [Trait("Req", "UI-THEME-035")]
    public void The_header_is_padded_12_6_6_and_the_label_sits_12_points_from_the_panel_edge() => headless.OnUiThread(() =>
    {
        var block = new CodeBlock("swift", "x");
        var window = MarkdownVisualProbe.Show(block);

        var panel = Panel(block);
        var labelAt = MarkdownVisualProbe.Origin(Label(block), panel);
        Assert.Equal(12, labelAt.X, 3);
        Assert.Equal(6, labelAt.Y, 3);
        var copy = CopyControl(block);
        var copyAt = MarkdownVisualProbe.Origin(copy, panel);
        Assert.Equal(panel.Bounds.Width - 6, copyAt.X + copy.Bounds.Width, 3); // 6 trailing
        Assert.Equal(6, copyAt.Y, 3);
        window.Close();
    });

    // :1214 spacing 0 and :1231-1233 padding horizontal 12, bottom 12, top 4 around the code.
    [Fact]
    [Trait("Req", "UI-THEME-034")]
    public void The_code_sits_directly_under_the_header_padded_12_horizontally_4_above_and_12_below() => headless.OnUiThread(() =>
    {
        var block = new CodeBlock("swift", "let x = 1");
        var window = MarkdownVisualProbe.Show(block);

        var scroll = Scroll(block);
        Assert.Equal(Header(block).Bounds.Bottom, scroll.Bounds.Top, 3); // stack spacing 0
        var code = Code(block);
        var at = MarkdownVisualProbe.Origin(code, scroll);
        Assert.Equal(12, at.X, 3);
        Assert.Equal(4, at.Y, 3);
        Assert.Equal(code.Bounds.Height + 4 + 12, scroll.Bounds.Height, 3);
        Assert.Equal(Panel(block).Bounds.Height, scroll.Bounds.Bottom, 3); // no footer under 120 lines
        window.Close();
    });

    // :1228 .font(.chat(.callout, design: .monospaced)): callout 12 pt in the monospaced face (OD-0009).
    [Fact]
    [Trait("Req", "PR-THEME-117")]
    [Trait("Req", "UI-THEME-034")]
    public void The_code_is_callout_text_in_the_monospaced_face() => headless.OnUiThread(() =>
    {
        var block = new CodeBlock("swift", "let x = 1");
        var window = MarkdownVisualProbe.Show(block);

        var text = CodeText(block);
        Assert.Equal("let x = 1", MarkdownVisualProbe.TextOf(text));
        Assert.Equal(12, text.FontSize);
        Assert.Equal("Cascadia Mono", text.FontFamily.Name);
        window.Close();
    });

    // :1225 ScrollView(.horizontal, showsIndicators: false) and :1230 fixedSize(horizontal: true): a long line never wraps.
    [Fact]
    [Trait("Req", "PR-THEME-117")]
    [Trait("Req", "UI-THEME-034")]
    public void A_long_line_scrolls_sideways_without_a_visible_indicator_and_never_wraps() => headless.OnUiThread(() =>
    {
        var oneLine = new CodeBlock("swift", "x");
        var window = MarkdownVisualProbe.Show(oneLine);
        var lineHeight = CodeText(oneLine).Bounds.Height;
        window.Close();

        var longLine = new CodeBlock("swift", string.Concat(Enumerable.Repeat("0123456789", 60)));
        window = MarkdownVisualProbe.Show(longLine, width: 300);
        var scroll = Scroll(longLine);
        Assert.Equal(lineHeight, CodeText(longLine).Bounds.Height, 3); // still one line
        Assert.True(scroll.Extent.Width > scroll.Viewport.Width + 100); // it scrolls instead
        Assert.Equal(ScrollBarVisibility.Hidden, scroll.HorizontalScrollBarVisibility); // scrolls, draws no bar
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.VerticalScrollBarVisibility); // horizontal only
        Assert.DoesNotContain(VisualQuery.Descendants<ScrollBar>(scroll), bar => bar.IsVisible);
        window.Close();
    });

    // :1190, :1247: the copy control is fully transparent until the pointer is over the block, and its room is kept.
    [Fact]
    [Trait("Req", "PR-THEME-117")]
    public void The_copy_control_is_transparent_but_present_until_the_pointer_is_over_the_block_and_the_code_does_not_reflow() => headless.OnUiThread(() =>
    {
        var block = new CodeBlock("swift", "let x = 1");
        var window = MarkdownVisualProbe.Show(block);

        var copy = CopyControl(block);
        var codeBefore = Code(block);
        var boundsBefore = codeBefore.Bounds;
        Assert.Equal(0, copy.Opacity);
        Assert.True(copy.IsVisible);
        Assert.Equal(22, copy.Bounds.Width, 3); // the room is reserved, not removed

        window.MouseMove(new Point(40, Panel(block).Bounds.Height / 2));
        MarkdownVisualProbe.Pump();
        Assert.Equal(1, copy.Opacity);
        Assert.Same(codeBefore, Code(block)); // hovering does not rebuild the code text
        Assert.Equal(boundsBefore, Code(block).Bounds);

        window.MouseMove(new Point(40, 580)); // below the block
        MarkdownVisualProbe.Pump();
        Assert.Equal(0, copy.Opacity);
        Assert.Same(codeBefore, Code(block));
        window.Close();
    });

    // :1208-1210 lineCount = 1 + line feeds; collapsed only above :1203's 120 lines.
    [Fact]
    [Trait("Req", "UI-THEME-037")]
    [Trait("Req", "UI-THEME-034")]
    public void A_block_of_exactly_120_lines_shows_every_line_and_no_button() => headless.OnUiThread(() =>
    {
        var block = new CodeBlock("txt", Lines(120));
        var window = MarkdownVisualProbe.Show(block);

        Assert.Null(FooterButton(block));
        Assert.Equal(120, MarkdownVisualProbe.TextOf(CodeText(block)).Split('\n').Length);
        window.Close();
    });

    // :1235-1244: 150 lines collapse to 120 under a "Show all 150 lines" button, N the whole block's lines;
    // the button toggles to "Show less" and back.
    [Fact]
    [Trait("Req", "UI-THEME-037")]
    [Trait("Req", "UI-THEME-034")]
    public void A_longer_block_collapses_to_120_lines_under_a_show_all_button_that_toggles_to_show_less() => headless.OnUiThread(() =>
    {
        var block = new CodeBlock("txt", Lines(150));
        var window = MarkdownVisualProbe.Show(block);

        var button = Assert.IsType<Button>(FooterButton(block));
        Assert.Equal("Show all 150 lines", ButtonTitle(button));
        var collapsed = MarkdownVisualProbe.TextOf(CodeText(block)).Split('\n');
        Assert.Equal(120, collapsed.Length);
        Assert.Equal("line 120", collapsed[^1]);

        Click(button);
        Assert.Equal(150, MarkdownVisualProbe.TextOf(CodeText(block)).Split('\n').Length);
        var showLess = Assert.IsType<Button>(FooterButton(block));
        Assert.Equal("Show less", ButtonTitle(showLess));

        Click(showLess);
        Assert.Equal(120, MarkdownVisualProbe.TextOf(CodeText(block)).Split('\n').Length);
        Assert.Equal("Show all 150 lines", ButtonTitle(Assert.IsType<Button>(FooterButton(block))));
        window.Close();
    });

    // :1239-1243 link button, caption, Chrome.secondaryText, padding horizontal 12, bottom 10.
    [Fact]
    [Trait("Req", "UI-THEME-037")]
    public void The_footer_button_is_caption_text_in_the_label_colour_at_70_percent_padded_12_and_10() => headless.OnUiThread(() =>
    {
        var block = new CodeBlock("txt", Lines(130));
        var window = MarkdownVisualProbe.Show(block);

        var button = Assert.IsType<Button>(FooterButton(block));
        var label = Assert.IsType<TextBlock>(button.Content);
        Assert.Equal(10, label.FontSize);
        Assert.Equal("Inter", label.FontFamily.Name);
        Assert.Equal(MarkdownVisualProbe.Drawn(SwiftColor.Primary.Opacity(0.7), window), MarkdownVisualProbe.ColorOf(label.Foreground)); // Chrome.secondaryText

        var panel = Panel(block);
        var at = MarkdownVisualProbe.Origin(label, panel);
        Assert.Equal(12, at.X, 3); // horizontal 12
        Assert.Equal(panel.Bounds.Height, at.Y + label.Bounds.Height + 10, 3); // bottom 10
        Assert.Equal(Scroll(block).Bounds.Bottom, MarkdownVisualProbe.Origin(button, panel).Y, 3); // spacing 0 above it
        window.Close();
    });
}
