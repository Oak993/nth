using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using static Marrow.UI.Tests.Fixtures.StreamVeilFlowHost;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source: DroppyCode/UI/Markdown/MarkdownView.swift:819-824 (markdownBlockSelection is true everywhere: "a finished reply's blocks are copyable at all times, one
// paragraph at a time"), :853-863 (MarkdownBlockSelection is `.textSelection(.enabled)`), applied to a paragraph of mentions :318, to InlineText :1131 (a heading, a
// list item, a link-free paragraph), to a code block's lines :1229 and to a table :1308; DroppyCode/App/Views/Settings/SettingsView.swift:1030-1033 (the licences
// popover's documents are `.textSelection(.enabled)` too). PR-THEME-126: "Text selection is available in every markdown block, one block at a time; a drag does not
// extend across block boundaries", verified by "a drag selects within one block and stops at its edge".
//
// The port drew these blocks' words in a plain TextBlock, which cannot select, and its MarkdownBlockSelection.Body was the identity "because the content renders its
// own selectable text": it did not, so a code block, a heading, a list item or a table could be neither selected nor copied (found by the defects-priority review,
// round 3, on the licences popover). Every test here drives real input in a real window: a press, a drag, a release and Control+C, then reads the clipboard.
[Collection("Headless")]
public sealed class VeiledTextSelectionTests(HeadlessSession headless)
{
    private const string Sentinel = "<nothing copied>";

    private const string Document =
        "# Heading words here\n\n" +
        "A paragraph of plain words that a reader can select.\n\n" +
        "- first item of a list\n\n" +
        "```\nlet answer = 42\n```\n\n" +
        "| left | right |\n|---|---|\n| one | two |\n\n" +
        "Closing paragraph below the table.";

    // The text a text block shows: its inlines when it has them (VeiledText renders runs), else its Text.
    private static string Shown(TextBlock block) => block.Inlines is { Count: > 0 } inlines ? inlines.Text ?? "" : block.Text ?? "";

    private static TextBlock Block(Control root, string startsWith) =>
        root.GetVisualDescendants().OfType<TextBlock>().First(text => Shown(text).StartsWith(startsWith, StringComparison.Ordinal));

    // A press at `from`, a drag to `to` with the left button down, a release, then Control+C: what the clipboard holds afterwards. The press is checked to land on the
    // text under test, so a miss can never pass for "nothing to select".
    private static string DragThenCopy(Window window, Control target, Point from, Point to, bool landsOnTarget = true)
    {
        window.Clipboard!.SetTextAsync(Sentinel).GetAwaiter().GetResult();
        var hit = window.InputHitTest(from) as Visual;
        if (landsOnTarget)
            Assert.True(hit is not null && (ReferenceEquals(hit, target) || hit.GetVisualAncestors().Contains(target)),
                $"the press lands on the text under test (it lands on {hit?.GetType().Name ?? "nothing"})");
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Pump();
        window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
        window.KeyRelease(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
        Pump();
        return window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() ?? "";
    }

    // From 2 points in to `toX` points in (60, or the block's right edge when that is nearer), at the vertical middle of the block's first line.
    private static string DragAcross(Window window, TextBlock block, double toX = 60, bool landsOnTarget = true)
    {
        var y = Math.Min(8, block.Bounds.Height / 2);
        var from = block.TranslatePoint(new Point(2, y), window)!.Value;
        var to = block.TranslatePoint(new Point(Math.Min(toX, block.Bounds.Width - 2), y), window)!.Value;
        return DragThenCopy(window, block, from, to, landsOnTarget);
    }

    private static MarkdownView Streaming(string text) => new(text, isStreaming: true);

    [Fact]
    [Trait("Req", "PR-THEME-126")] // MarkdownView.swift:1131 (a heading, a list item, a link-free paragraph), :1229 (a code block's lines), :1308 (a table): .modifier(MarkdownBlockSelection())
    public void A_drag_over_a_heading_a_paragraph_a_list_item_a_code_line_and_a_table_cell_then_Control_C_copies_what_the_drag_covered() => headless.OnUiThread(() =>
    {
        var view = Streaming(Document);
        var window = Show(view);
        try
        {
            foreach (var start in new[] { "Heading words here", "A paragraph of plain words", "first item of a list", "let answer = 42", "left", "one", "Closing paragraph" })
            {
                var block = Block(view, start);
                var shown = Shown(block);
                Assert.IsAssignableFrom<SelectableTextBlock>(block);
                var copied = DragAcross(window, block);
                Assert.True(copied.Length > 0 && copied != Sentinel, $"a drag over \"{start}\" then Control+C copied nothing of it (the clipboard holds '{copied}')");
                Assert.Contains(copied, shown, StringComparison.Ordinal);
                Assert.Equal(((SelectableTextBlock)block).SelectedText, copied);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-THEME-126")] // MarkdownView.swift:819-824 ("one paragraph at a time"); the record's verification: "a drag selects within one block and stops at its edge"
    public void A_drag_that_starts_in_one_block_and_ends_over_another_selects_the_first_block_to_its_end_and_nothing_of_the_second() => headless.OnUiThread(() =>
    {
        var view = Streaming(Document);
        var window = Show(view);
        try
        {
            var paragraph = Block(view, "A paragraph of plain words");
            var code = Block(view, "let answer = 42");
            var y = Math.Min(8, paragraph.Bounds.Height / 2);
            var from = paragraph.TranslatePoint(new Point(2, y), window)!.Value;
            // Down into the code block's line and out past the paragraph's last word (the window is 400 wide, the paragraph's line is shorter).
            var into = new Point(380, code.TranslatePoint(new Point(0, Math.Min(8, code.Bounds.Height / 2)), window)!.Value.Y);
            Assert.True(paragraph.Bounds.Width > 0 && into.Y > paragraph.TranslatePoint(new Point(0, paragraph.Bounds.Height), window)!.Value.Y, "the drag ends below the paragraph, over the code block");
            var copied = DragThenCopy(window, paragraph, from, into);
            Assert.True(copied.Length > 0 && copied != Sentinel, $"the drag copied nothing ('{copied}')");
            Assert.Contains(copied, Shown(paragraph), StringComparison.Ordinal);
            Assert.DoesNotContain("let answer", copied, StringComparison.Ordinal);
            Assert.Equal(Shown(paragraph).Length, copied.Length); // the drag went past the paragraph's end, so the paragraph is all of it
            Assert.Equal(0, ((SelectableTextBlock)code).SelectionEnd - ((SelectableTextBlock)code).SelectionStart); // and the code block selected nothing
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-THEME-126")] // MarkdownView.swift:853-863 .textSelection(.enabled): the selection is shown (the system's selection colour, as ParagraphTextPresenter draws it)
    public void A_selection_in_a_veiled_text_or_a_table_cell_is_painted_in_the_selected_text_background_colour() => headless.OnUiThread(() =>
    {
        var view = Streaming(Document);
        var window = Show(view);
        try
        {
            foreach (var start in new[] { "A paragraph of plain words", "let answer = 42", "one" })
            {
                var block = Assert.IsAssignableFrom<SelectableTextBlock>(Block(view, start));
                var brush = Assert.IsAssignableFrom<ISolidColorBrush>(block.SelectionBrush);
                Assert.Equal(SwiftColor.SelectedTextBackgroundColor.Resolve(block.ActualThemeVariant), brush.Color);
                Assert.True(brush.Color.A > 0, "the highlight is not transparent");
            }
        }
        finally { window.Close(); }
    });

    // SettingsView.swift:1030-1033: the licences popover's documents are `.textSelection(.enabled)`, an environment value that reaches every Text below it: the caption of a
    // code block (MarkdownView.swift:1286-1289, `Text(language ?? "code")`) is selectable there. In a chat message nothing above the markdown view sets it, and the caption is
    // not selectable: MarkdownView's own MarkdownBlockSelection covers the code lines (:1229), not the caption.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-THEME-126")] // MarkdownView.swift:1286-1289, :1229; SettingsView.swift:1033
    [Trait("Req", "UI-UPDATE-032")] // SettingsView.swift:1033 .textSelection(.enabled): "Select and copy text"
    public void The_caption_of_a_code_block_selects_and_copies_only_where_an_ancestor_enables_text_selection(bool enabled) => headless.OnUiThread(() =>
    {
        var view = Streaming("```\nlet answer = 42\n```");
        MarkdownView.SetTextSelectionEnabled(view, enabled);
        var window = Show(view);
        try
        {
            string CaptionDrag(bool selectable)
            {
                var caption = Block(view, "code");
                Assert.Equal("code", Shown(caption));
                var copied = DragAcross(window, caption, toX: caption.Bounds.Width, landsOnTarget: selectable);
                if (selectable)
                {
                    Assert.True(copied.Length > 0 && copied != Sentinel, $"a drag over the caption then Control+C copied nothing ('{copied}')");
                    Assert.Contains(copied, "code", StringComparison.Ordinal);
                    Assert.Equal(Assert.IsAssignableFrom<SelectableTextBlock>(caption).SelectedText, copied);
                }
                else
                {
                    Assert.Equal(Sentinel, copied); // nothing of the caption is selected, and nothing else is either
                    Assert.Equal(0, Assert.IsAssignableFrom<SelectableTextBlock>(caption).SelectionEnd);
                }

                return copied;
            }

            CaptionDrag(enabled);

            // SwiftUI's environment value can change under a view that is on screen (the block is not built again for it): the caption follows.
            MarkdownView.SetTextSelectionEnabled(view, !enabled);
            Pump();
            CaptionDrag(!enabled);

            // The code lines below it are selectable either way (MarkdownBlockSelection, :1229).
            var lines = Block(view, "let answer = 42");
            var copiedLines = DragAcross(window, lines);
            Assert.True(copiedLines.Length > 0 && copiedLines != Sentinel);
            Assert.Contains(copiedLines, "let answer = 42", StringComparison.Ordinal);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-THEME-126")] // MarkdownView.swift:1131: InlineText's link-free paragraph keeps its selection through a streamed reply's flushes (every flush builds a new VeiledText, as the rows do)
    public void A_streamed_paragraph_can_be_selected_and_copied_after_each_flush_and_once_its_words_have_settled() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        var host = new StreamVeilFlowHost(runtime, "Hello");
        try
        {
            host.Flush(runtime, "Hello there");
            runtime.AdvanceMilliseconds(600);
            runtime.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(600)); Pump(); });
            var block = Block(host.Decorator, "Hello there");
            Assert.IsAssignableFrom<SelectableTextBlock>(block);
            var copied = DragAcross(host.Window, block, toX: block.Bounds.Width);
            Assert.Equal("Hello there", copied); // the drag runs past the end of the short line, so it covers all of it, the words that arrived by a flush included
        }
        finally { host.Window.Close(); }
    });
}
