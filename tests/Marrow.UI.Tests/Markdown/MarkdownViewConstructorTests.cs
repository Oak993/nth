using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.UI.Markdown;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B2 1fbf81e), :9-104 (part 2 of the unit).
//
// UNTESTABLE for now, and why: a heading, paragraph, list or quote block always constructs an InlineText or
// VeiledText somewhere under it (MarkdownBlockView.Build, already implemented), and every InlineText path
// (this part) ends at either LinkParagraphView or VeiledText(AttributedText) -- the sibling LinkParagraphView
// and StreamVeil units' own MARROW_STUB, both throwing on construction. Markdown text made only of rules and
// tables avoids InlineText entirely (MarkdownBlockView.Build's .table and .rule cases draw directly), so the
// tests below use "---" (a rule) and a one-row table to exercise MarkdownView's own wiring (PR-THEME-105)
// without tripping either sibling stub. The non-streaming, proseRuns-true branch (:71-83) is reachable this way
// too, because a rule/table block is always a Segment.Block (Segments() only routes headings, paragraphs, lists
// and quotes into a Segment.Prose/ProseRunView run); a prose block in that branch stays untested here.
[Collection("Headless")]
public sealed class MarkdownViewConstructorTests(HeadlessSession headless)
{
    private const string TwoRules = "---\n\n***"; // two thematic breaks
    private const string ThreeRules = "---\n\n***\n\n___";

    // ---- :21-23 Equatable ----

    [Fact]
    [Trait("Req", "PR-THEME-105")] // DroppyCode/UI/Markdown/MarkdownView.swift:21-23
    public void Equatable_compares_text_and_isStreaming_only()
    {
        headless.OnUiThread(() =>
        {
            var a = new MarkdownView(TwoRules);
            var b = new MarkdownView(TwoRules);
            var c = new MarkdownView(ThreeRules);
            var d = new MarkdownView(TwoRules, isStreaming: true);
            Assert.True(MarkdownView.Equatable(a, b)); // same text, same isStreaming
            Assert.False(MarkdownView.Equatable(a, c)); // different text
            Assert.False(MarkdownView.Equatable(a, d)); // different isStreaming
            Assert.False(a.IsStreaming);
            Assert.True(d.IsStreaming);
        });
    }

    // ---- :84-98 the streaming branch, and the non-streaming/proseRuns branch for a rule-only body ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-THEME-105")] // DroppyCode/UI/Markdown/MarkdownView.swift:72-98
    public void View_builds_one_MarkdownBlockView_per_block_in_a_vertical_stack(bool isStreaming)
    {
        headless.OnUiThread(() =>
        {
            var view = new MarkdownView(ThreeRules, isStreaming: isStreaming);
            var stack = Assert.IsType<StackPanel>(view.Child);
            Assert.Equal(Orientation.Vertical, stack.Orientation); // :72, :86
            Assert.Equal(12, stack.Spacing); // :72, :86
            var blocks = stack.Children.OfType<MarkdownBlockView>().ToList();
            Assert.Equal(3, blocks.Count); // three rules
        });
    }

    [Fact]
    [Trait("Req", "PR-THEME-105")] // DroppyCode/UI/Markdown/MarkdownView.swift:89-91
    public void Streaming_view_marks_only_the_last_block_as_streaming()
    {
        headless.OnUiThread(() =>
        {
            var view = new MarkdownView(TwoRules, isStreaming: true);
            var stack = Assert.IsType<StackPanel>(view.Child);
            var blocks = stack.Children.OfType<MarkdownBlockView>().ToList();
            Assert.Equal(2, blocks.Count);
            Assert.False(MarkdownView.GetMarkdownStreaming(blocks[0])); // :89-91 isStreaming && index == last
            Assert.True(MarkdownView.GetMarkdownStreaming(blocks[1]));
        });
    }

    [Fact]
    [Trait("Req", "PR-THEME-105")] // DroppyCode/UI/Markdown/MarkdownView.swift:82 the finished (non-streaming) branch never sets markdownStreaming
    public void Finished_view_leaves_every_block_at_the_default_markdownStreaming()
    {
        headless.OnUiThread(() =>
        {
            var view = new MarkdownView(TwoRules, isStreaming: false);
            var stack = Assert.IsType<StackPanel>(view.Child);
            foreach (var block in stack.Children.OfType<MarkdownBlockView>())
                Assert.False(MarkdownView.GetMarkdownStreaming(block)); // :73-80 no .environment(\.markdownStreaming, ...) in this branch
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-THEME-105")] // DroppyCode/UI/Markdown/MarkdownView.swift:80, :96 .environment(\.markdownVeiled, isStreaming)
    public void Root_carries_markdownVeiled_equal_to_isStreaming(bool isStreaming)
    {
        headless.OnUiThread(() =>
        {
            var view = new MarkdownView(TwoRules, isStreaming: isStreaming);
            Assert.Equal(isStreaming, StreamVeil.GetMarkdownVeiled(view.Child!));
        });
    }
}
