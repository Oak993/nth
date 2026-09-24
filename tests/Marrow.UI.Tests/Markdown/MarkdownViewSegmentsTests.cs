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
public sealed class MarkdownViewSegmentsTests
{
    [Fact]
    [Trait("Req", "PR-THEME-105")] // DroppyCode/UI/Markdown/MarkdownView.swift:25-45
    public void Consecutive_headings_paragraphs_lists_and_quotes_join_into_one_prose_run()
    {
        var heading = new MarkdownBlock.Heading(1, "Title");
        var paragraph = new MarkdownBlock.Paragraph("Body");
        var list = new MarkdownBlock.List(false, 1, ValueList<MarkdownListItem>.Empty);
        var quote = new MarkdownBlock.Quote(ValueList<MarkdownBlock>.Empty);

        var segments = MarkdownView.Segments(new ValueList<MarkdownBlock>([heading, paragraph, list, quote]));

        var single = Assert.Single(segments);
        var run = Assert.IsType<MarkdownSegment.Prose>(single);
        Assert.Equal(new ValueList<MarkdownBlock>([heading, paragraph, list, quote]), run.Value);
    }

    [Fact]
    [Trait("Req", "PR-THEME-105")] // DroppyCode/UI/Markdown/MarkdownView.swift:25-45
    public void Code_table_and_rule_close_the_run_and_stand_alone()
    {
        var paragraph = new MarkdownBlock.Paragraph("Body");
        var code = new MarkdownBlock.Code("swift", "1");
        var table = new MarkdownBlock.Table(ValueList<string>.Empty, ValueList<ValueList<string>>.Empty);
        var rule = new MarkdownBlock.Rule();

        var segments = MarkdownView.Segments(new ValueList<MarkdownBlock>([paragraph, code, table, rule]));

        Assert.Equal(4, segments.Count);
        Assert.IsType<MarkdownSegment.Prose>(segments[0]);
        Assert.Equal(code, Assert.IsType<MarkdownSegment.Block>(segments[1]).Value);
        Assert.Equal(table, Assert.IsType<MarkdownSegment.Block>(segments[2]).Value);
        Assert.Equal(rule, Assert.IsType<MarkdownSegment.Block>(segments[3]).Value);
    }

    [Fact]
    [Trait("Req", "PR-THEME-105")] // DroppyCode/UI/Markdown/MarkdownView.swift:25-45
    public void An_empty_block_list_produces_no_segments()
    {
        Assert.Empty(MarkdownView.Segments(ValueList<MarkdownBlock>.Empty));
    }
}
