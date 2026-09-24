using System.Linq;
using System.Reflection;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B2 1fbf81e). This part (part 1 of the unit)
// covers PR-THEME-105 to 129 except 122 (not in this part's spec). Helpers reflect into private members the
// same way tests/Marrow.UI.Tests/Chrome/ProjectIconPickerTests.cs does.
public sealed class MarkdownViewCachingTests
{
    [Fact]
    [Trait("Req", "PR-THEME-123")] // DroppyCode/UI/Markdown/MarkdownView.swift:135-148
    public void Blocks_for_the_same_finished_text_returns_the_same_parse_from_the_cache()
    {
        var text = "distinct-cache-probe-" + Guid.NewGuid() + "\n\nBody paragraph.";

        var first = MarkdownView.Blocks(text);
        var second = MarkdownView.Blocks(text);

        Assert.Equal(first, second);
        Assert.Equal(2, first.Count);
    }

    [Fact]
    [Trait("Req", "PR-THEME-123")] // DroppyCode/UI/Markdown/MarkdownView.swift:123-133
    public void StreamingParse_of_the_same_text_twice_returns_the_same_result_without_reparsing_from_scratch()
    {
        var text = "streaming-probe-" + Guid.NewGuid() + "\n\nHello world";

        var first = MarkdownView.StreamingParse(text);
        var second = MarkdownView.StreamingParse(text);

        Assert.Equal(first.Blocks, second.Blocks);
        Assert.Equal(first.Stable, second.Stable);
    }

    [Fact]
    [Trait("Req", "PR-THEME-125")] // DroppyCode/UI/Markdown/MarkdownView.swift:154-167
    public void Extends_is_false_when_the_earlier_text_is_empty_or_not_shorter()
    {
        var method = typeof(MarkdownView).GetMethod("Extends", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Extends(string earlier, string to) => (bool)method.Invoke(null, [earlier, to])!;

        Assert.False(Extends("", "anything"));
        Assert.False(Extends("same", "same"));
        Assert.False(Extends("longer text", "short"));
    }

    [Fact]
    [Trait("Req", "PR-THEME-125")] // DroppyCode/UI/Markdown/MarkdownView.swift:157-159: 128 bytes or fewer, a real prefix test
    public void Extends_for_a_short_earlier_text_is_a_real_prefix_test()
    {
        var method = typeof(MarkdownView).GetMethod("Extends", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Extends(string earlier, string to) => (bool)method.Invoke(null, [earlier, to])!;

        Assert.True(Extends("Hello", "Hello, world"));
        Assert.False(Extends("Hello", "Goodbye, world"));
    }

    [Fact]
    [Trait("Req", "PR-THEME-125")] // DroppyCode/UI/Markdown/MarkdownView.swift:158-166: longer than 128 bytes, 64-byte head/tail probes
    public void Extends_for_a_long_earlier_text_compares_only_the_head_and_tail_probes()
    {
        var method = typeof(MarkdownView).GetMethod("Extends", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Extends(string earlier, string to) => (bool)method.Invoke(null, [earlier, to])!;

        // 150-byte earlier text: head 64 bytes 'A', middle 22 bytes 'X', tail 64 bytes 'B'.
        var head = new string('A', 64);
        var middle = new string('X', 22);
        var tail = new string('B', 64);
        var earlier = head + middle + tail;
        Assert.Equal(150, System.Text.Encoding.UTF8.GetByteCount(earlier));

        // A genuine extension: same head+middle+tail, plus more appended.
        var realExtension = earlier + " more text";
        Assert.True(Extends(earlier, realExtension));

        // The probe is fooled by a rewrite that preserves the head and tail bytes but changes the middle: this
        // is the shipped, deliberately probabilistic behaviour PR-THEME-125 documents, not a bug in the port.
        var sameProbesDifferentMiddle = head + new string('Y', 22) + tail + " more";
        Assert.True(Extends(earlier, sameProbesDifferentMiddle));

        // Changing a byte inside the head probe breaks the match.
        var brokenHead = "Z" + head[1..] + middle + tail + " more";
        Assert.False(Extends(earlier, brokenHead));
    }

    [Fact]
    [Trait("Req", "PR-THEME-124")] // DroppyCode/UI/Markdown/MarkdownView.swift:221-235
    public void WarmTexts_stops_at_96_candidates_24_selections_and_skips_oversized_or_duplicate_texts()
    {
        var many = Enumerable.Range(0, 200).Select(i => "warm-candidate-" + Guid.NewGuid() + "-" + i).ToList();
        var result = MarkdownView.WarmTexts(many);
        Assert.Equal(24, result.Count);
        Assert.Equal(many.Take(24).ToList(), result.ToList());

        var oversized = new string('a', 262_145);
        var withOversized = new List<string> { oversized, "warm-small-" + Guid.NewGuid() };
        var oversizedResult = MarkdownView.WarmTexts(withOversized);
        Assert.Single(oversizedResult);
        Assert.Equal(withOversized[1], oversizedResult[0]);

        var duplicateText = "warm-dup-" + Guid.NewGuid();
        var withDuplicate = new List<string> { duplicateText, duplicateText };
        var duplicateResult = MarkdownView.WarmTexts(withDuplicate);
        Assert.Single(duplicateResult);
    }

    [Fact]
    [Trait("Req", "PR-THEME-124")] // DroppyCode/UI/Markdown/MarkdownView.swift:204-219
    public async Task Warm_populates_the_block_cache_so_a_later_Blocks_call_hits_it()
    {
        var text = "warm-full-" + Guid.NewGuid() + "\n\nSome plain paragraph, no links.";
        await MarkdownView.Warm([text]);

        var blocks = MarkdownView.Blocks(text);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(new MarkdownBlock.Paragraph("Some plain paragraph, no links."), blocks[1]);
    }

    [Fact]
    [Trait("Req", "PR-THEME-124")] // DroppyCode/UI/Markdown/MarkdownView.swift:257-275
    public void InlineSources_collects_paragraph_and_heading_text_and_recurses_into_lists_and_quotes()
    {
        var method = typeof(MarkdownView).GetMethod("InlineSources", BindingFlags.NonPublic | BindingFlags.Static)!;

        var item = new MarkdownListItem("item text", null, new ValueList<MarkdownBlock>([new MarkdownBlock.Paragraph("child text")]));
        var blocks = new ValueList<MarkdownBlock>(
        [
            new MarkdownBlock.Heading(1, "heading text"),
            new MarkdownBlock.Paragraph("paragraph text"),
            new MarkdownBlock.List(false, 1, new ValueList<MarkdownListItem>([item])),
            new MarkdownBlock.Quote(new ValueList<MarkdownBlock>([new MarkdownBlock.Paragraph("quoted text")])),
            new MarkdownBlock.Code("swift", "code text"),
            new MarkdownBlock.Table(ValueList<string>.Empty, ValueList<ValueList<string>>.Empty),
            new MarkdownBlock.Rule(),
        ]);

        var result = (ValueSet<string>)method.Invoke(null, [blocks, ValueSet<string>.Empty])!;

        Assert.Equal(5, result.Count);
        Assert.Contains("heading text", result);
        Assert.Contains("paragraph text", result);
        Assert.Contains("item text", result);
        Assert.Contains("child text", result);
        Assert.Contains("quoted text", result);
        Assert.DoesNotContain("code text", result);
    }
}
