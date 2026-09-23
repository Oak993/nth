using System.Linq;
using Marrow.Core.Port;
using Marrow.UI.Markdown;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownParser.swift (B2 1fbf81e), :1-339.
public sealed class MarkdownParserTests
{
    [Fact]
    [Trait("Req", "PR-THEME-089")] // DroppyCode/UI/Markdown/MarkdownParser.swift:3-18
    public void A_document_with_every_block_kind_parses_to_exactly_those_seven_blocks_in_order()
    {
        var text = "# Title\n\nSome paragraph text.\n\n```python\nprint(1)\n```\n\n- item one\n\n> quoted line\n\n| A | B |\n| - | - |\n| 1 | 2 |\n\n---";

        var blocks = MarkdownParser.MarkdownParserParse(text);

        Assert.Equal(7, blocks.Count);
        Assert.Equal(new MarkdownBlock.Heading(1, "Title"), blocks[0]);
        Assert.Equal(new MarkdownBlock.Paragraph("Some paragraph text."), blocks[1]);
        Assert.Equal(new MarkdownBlock.Code("python", "print(1)"), blocks[2]);
        Assert.Equal(
            new MarkdownBlock.List(false, 1, new ValueList<MarkdownListItem>([
                new MarkdownListItem("item one", null, ValueList<MarkdownBlock>.Empty),
            ])),
            blocks[3]);
        Assert.Equal(
            new MarkdownBlock.Quote(new ValueList<MarkdownBlock>([new MarkdownBlock.Paragraph("quoted line")])),
            blocks[4]);
        Assert.Equal(
            new MarkdownBlock.Table(
                new ValueList<string>(["A", "B"]),
                new ValueList<ValueList<string>>([new ValueList<string>(["1", "2"])])),
            blocks[5]);
        Assert.Equal(new MarkdownBlock.Rule(), blocks[6]);
    }

    [Fact]
    [Trait("Req", "PR-THEME-090")] // DroppyCode/UI/Markdown/MarkdownParser.swift:53-64
    public void CRLF_and_LF_text_parse_identically()
    {
        var lf = MarkdownParser.MarkdownParserParse("# Title\n\nBody\n");
        var crlf = MarkdownParser.MarkdownParserParse("# Title\r\n\r\nBody\r\n");

        Assert.Equal(lf.ToList(), crlf.ToList());
        Assert.Equal(2, lf.Count);
        Assert.Equal(new MarkdownBlock.Heading(1, "Title"), lf[0]);
        Assert.Equal(new MarkdownBlock.Paragraph("Body"), lf[1]);
    }

    [Fact]
    [Trait("Req", "PR-THEME-090")] // DroppyCode/UI/Markdown/MarkdownParser.swift:53-64
    public void A_lone_carriage_return_not_followed_by_a_line_feed_stays_inside_the_line()
    {
        var blocks = MarkdownParser.MarkdownParserParse("A\rB");

        var paragraph = Assert.IsType<MarkdownBlock.Paragraph>(Assert.Single(blocks));
        Assert.Equal("A\rB", paragraph.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-091")] // DroppyCode/UI/Markdown/MarkdownParser.swift:66-72
    public void Fewer_than_three_lines_reports_zero_stable_and_zero_restart()
    {
        var parse = MarkdownParser.MarkdownParserParse("A", null);

        Assert.Equal(0, parse.Stable);
        Assert.Equal(0, parse.Restart);
    }

    [Fact]
    [Trait("Req", "PR-THEME-091")] // DroppyCode/UI/Markdown/MarkdownParser.swift:66-72
    public void Stable_is_the_index_of_the_last_top_level_block_starting_two_or_more_lines_above_the_end()
    {
        // Lines: 0 "A", 1 "", 2 "B", 3 "", 4 "C" (still being written). blockLines = [0, 2, 4].
        // lines.count - 3 == 2, so only blockLines[0]=0 and blockLines[1]=2 qualify; the last
        // qualifying index is 1, so only block "A" (index < 1) is fully stable.
        var parse = MarkdownParser.MarkdownParserParse("A\n\nB\n\nC", null);

        Assert.Equal(3, parse.Blocks.Count);
        Assert.Equal(new MarkdownBlock.Paragraph("A"), parse.Blocks[0]);
        Assert.Equal(new MarkdownBlock.Paragraph("B"), parse.Blocks[1]);
        Assert.Equal(new MarkdownBlock.Paragraph("C"), parse.Blocks[2]);
        Assert.Equal(1, parse.Stable);
        Assert.Equal(3, parse.Restart); // UTF-8 byte offset of line 2 ("B") in "A\n\nB\n\nC".
    }

    [Fact]
    [Trait("Req", "PR-THEME-092")] // DroppyCode/UI/Markdown/MarkdownParser.swift:39-51
    public void Extending_a_prefix_reuses_stable_blocks_and_combines_stable_and_restart_arithmetic()
    {
        var previousText = "A\n\nB\n\nC";
        var previous = MarkdownParser.MarkdownParserParse(previousText, null);
        Assert.Equal(1, previous.Stable);
        Assert.Equal(3, previous.Restart);

        var newText = previousText + "\n\nD";
        var extended = MarkdownParser.MarkdownParserParse(newText, (previousText, previous));
        var fresh = MarkdownParser.MarkdownParserParse(newText, null);

        Assert.Equal(fresh.Blocks.ToList(), extended.Blocks.ToList());
        Assert.Equal(new[]
        {
            new MarkdownBlock.Paragraph("A"),
            new MarkdownBlock.Paragraph("B"),
            new MarkdownBlock.Paragraph("C"),
            new MarkdownBlock.Paragraph("D"),
        }, extended.Blocks.ToList());
        Assert.Equal(2, extended.Stable); // previous.Stable(1) + tail.Stable(1)
        Assert.Equal(6, extended.Restart); // previous.Restart(3) + tail.Restart(3)
        Assert.Equal(fresh.Stable, extended.Stable);
        Assert.Equal(fresh.Restart, extended.Restart);
    }

    [Fact]
    [Trait("Req", "PR-THEME-092")] // DroppyCode/UI/Markdown/MarkdownParser.swift:39-51
    public void A_rewrite_that_does_not_extend_the_previous_text_falls_back_to_a_full_parse()
    {
        var previousText = "A\n\nB\n\nC";
        var previous = MarkdownParser.MarkdownParserParse(previousText, null);
        Assert.True(previous.Stable > 0);

        var rewritten = "X\n\nB\n\nC\n\nD"; // Same length class, but does not start with previousText.
        var extended = MarkdownParser.MarkdownParserParse(rewritten, (previousText, previous));
        var fresh = MarkdownParser.MarkdownParserParse(rewritten, null);

        Assert.Equal(fresh.Blocks.ToList(), extended.Blocks.ToList());
        Assert.Equal(new MarkdownBlock.Paragraph("X"), extended.Blocks[0]);
        Assert.Equal(fresh.Stable, extended.Stable);
        Assert.Equal(fresh.Restart, extended.Restart);
    }

    [Fact]
    [Trait("Req", "PR-THEME-092")] // DroppyCode/UI/Markdown/MarkdownParser.swift:39-42
    public void No_previous_parse_falls_back_to_a_full_parse()
    {
        var extended = MarkdownParser.MarkdownParserParse("# Title\n\nBody", null);
        var fresh = MarkdownParser.MarkdownParserParse("# Title\n\nBody");

        Assert.Equal(fresh.ToList(), extended.Blocks.ToList());
    }

    [Fact]
    [Trait("Req", "PR-THEME-093")] // DroppyCode/UI/Markdown/MarkdownParser.swift:74-76,138-152
    public void Quotes_nest_at_most_eight_levels_and_the_ninth_stays_literal_text()
    {
        var text = new string('>', 9) + " deep";

        var blocks = MarkdownParser.MarkdownParserParse(text);

        var block = Assert.Single(blocks);
        MarkdownBlock expected = new MarkdownBlock.Paragraph("> deep");
        for (var i = 0; i < 8; i++)
        {
            expected = new MarkdownBlock.Quote(new ValueList<MarkdownBlock>([expected]));
        }
        Assert.Equal(expected, block);
    }

    [Fact]
    [Trait("Req", "PR-THEME-093")] // DroppyCode/UI/Markdown/MarkdownParser.swift:74-76,138-152
    public void Exactly_eight_quote_markers_all_nest_with_no_leftover_marker_characters()
    {
        var text = new string('>', 8) + " deep";

        var blocks = MarkdownParser.MarkdownParserParse(text);

        var block = Assert.Single(blocks);
        MarkdownBlock expected = new MarkdownBlock.Paragraph("deep");
        for (var i = 0; i < 8; i++)
        {
            expected = new MarkdownBlock.Quote(new ValueList<MarkdownBlock>([expected]));
        }
        Assert.Equal(expected, block);
    }

    [Fact]
    [Trait("Req", "PR-THEME-094")] // DroppyCode/UI/Markdown/MarkdownParser.swift:105-119,299-305
    public void A_fence_captures_its_info_string_and_body_and_closes_on_a_matching_fence_line()
    {
        var blocks = MarkdownParser.MarkdownParserParse("```python\nprint(1)\nprint(2)\n```\nAfter");

        Assert.Equal(2, blocks.Count);
        Assert.Equal(new MarkdownBlock.Code("python", "print(1)\nprint(2)"), blocks[0]);
        Assert.Equal(new MarkdownBlock.Paragraph("After"), blocks[1]);
    }

    [Fact]
    [Trait("Req", "PR-THEME-094")] // DroppyCode/UI/Markdown/MarkdownParser.swift:105-119
    public void An_unclosed_fence_runs_to_the_end_of_the_text_and_still_produces_a_code_block()
    {
        var blocks = MarkdownParser.MarkdownParserParse("```\nline1\nline2");

        var code = Assert.IsType<MarkdownBlock.Code>(Assert.Single(blocks));
        Assert.Null(code.Language);
        Assert.Equal("line1\nline2", code.CodeText);
    }

    [Fact]
    [Trait("Req", "PR-THEME-094")] // DroppyCode/UI/Markdown/MarkdownParser.swift:112-113
    public void A_longer_closing_fence_run_closes_a_shorter_opener()
    {
        var blocks = MarkdownParser.MarkdownParserParse("```\nbody\n`````\nAfter");

        Assert.Equal(2, blocks.Count);
        Assert.Equal(new MarkdownBlock.Code(null, "body"), blocks[0]);
        Assert.Equal(new MarkdownBlock.Paragraph("After"), blocks[1]);
    }

    [Fact]
    [Trait("Req", "PR-THEME-094")] // DroppyCode/UI/Markdown/MarkdownParser.swift:112-113
    public void A_fence_of_a_different_character_does_not_close_the_opener()
    {
        var blocks = MarkdownParser.MarkdownParserParse("```\nbody\n~~~\nmore");

        var code = Assert.IsType<MarkdownBlock.Code>(Assert.Single(blocks));
        Assert.Null(code.Language);
        Assert.Equal("body\n~~~\nmore", code.CodeText);
    }

    [Theory]
    [InlineData("# H1", 1, "H1")]
    [InlineData("## H2", 2, "H2")]
    [InlineData("###### H6", 6, "H6")]
    [InlineData("######", 6, "")]
    [Trait("Req", "PR-THEME-095")] // DroppyCode/UI/Markdown/MarkdownParser.swift:121-128,307-312
    public void One_to_six_hashes_form_a_heading_of_that_level(string line, int level, string text)
    {
        var heading = Assert.IsType<MarkdownBlock.Heading>(Assert.Single(MarkdownParser.MarkdownParserParse(line)));
        Assert.Equal(level, heading.Level);
        Assert.Equal(text, heading.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-095")] // DroppyCode/UI/Markdown/MarkdownParser.swift:307-312
    public void Seven_hashes_is_not_a_heading()
    {
        var paragraph = Assert.IsType<MarkdownBlock.Paragraph>(Assert.Single(MarkdownParser.MarkdownParserParse("####### not a heading")));
        Assert.Equal("####### not a heading", paragraph.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-095")] // DroppyCode/UI/Markdown/MarkdownParser.swift:307-312
    public void Hashes_immediately_followed_by_a_non_space_are_not_a_heading()
    {
        var paragraph = Assert.IsType<MarkdownBlock.Paragraph>(Assert.Single(MarkdownParser.MarkdownParserParse("##Title")));
        Assert.Equal("##Title", paragraph.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-095")] // DroppyCode/UI/Markdown/MarkdownParser.swift:124-125
    public void Trailing_hashes_and_spaces_are_trimmed_from_the_heading_text()
    {
        var heading = Assert.IsType<MarkdownBlock.Heading>(Assert.Single(MarkdownParser.MarkdownParserParse("## Title ##")));
        Assert.Equal(2, heading.Level);
        Assert.Equal("Title", heading.Text);
    }

    [Theory]
    [InlineData("---")]
    [InlineData("***")]
    [InlineData("___")]
    [InlineData("- - -")]
    [Trait("Req", "PR-THEME-096")] // DroppyCode/UI/Markdown/MarkdownParser.swift:130-136,314-323
    public void Three_or_more_of_one_rule_character_with_optional_spaces_is_a_rule(string line)
    {
        Assert.Equal(new MarkdownBlock.Rule(), Assert.Single(MarkdownParser.MarkdownParserParse(line)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-096")] // DroppyCode/UI/Markdown/MarkdownParser.swift:317-321
    public void Mixing_two_rule_characters_is_not_a_rule()
    {
        var paragraph = Assert.IsType<MarkdownBlock.Paragraph>(Assert.Single(MarkdownParser.MarkdownParserParse("-*-")));
        Assert.Equal("-*-", paragraph.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-096")] // DroppyCode/UI/Markdown/MarkdownParser.swift:317-321
    public void A_tab_between_rule_characters_disqualifies_the_rule_and_the_line_can_read_as_a_list_marker_instead()
    {
        var list = Assert.IsType<MarkdownBlock.List>(Assert.Single(MarkdownParser.MarkdownParserParse("-\t-\t-")));
        Assert.False(list.Ordered);
        var item = Assert.Single(list.Items);
        Assert.Equal("-\t-", item.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-097")] // DroppyCode/UI/Markdown/MarkdownParser.swift:138-152
    public void Consecutive_quote_lines_merge_and_a_blank_line_ends_the_quote_producing_two_blocks()
    {
        var blocks = MarkdownParser.MarkdownParserParse("> line1\n>  line2\n\n> line3");

        Assert.Equal(2, blocks.Count);
        var first = Assert.IsType<MarkdownBlock.Quote>(blocks[0]);
        var firstParagraph = Assert.IsType<MarkdownBlock.Paragraph>(Assert.Single(first.Blocks));
        // Only one space after '>' is stripped, so "  line2" keeps its second leading space.
        Assert.Equal("line1\n line2", firstParagraph.Text);

        var second = Assert.IsType<MarkdownBlock.Quote>(blocks[1]);
        var secondParagraph = Assert.IsType<MarkdownBlock.Paragraph>(Assert.Single(second.Blocks));
        Assert.Equal("line3", secondParagraph.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-098")] // DroppyCode/UI/Markdown/MarkdownParser.swift:154-168,325-337
    public void A_table_with_an_alignment_delimiter_row_keeps_cells_and_drops_alignment()
    {
        var blocks = MarkdownParser.MarkdownParserParse("| A | B |\n| :--- | ---: |\n| 1 | 2 |");

        var table = Assert.IsType<MarkdownBlock.Table>(Assert.Single(blocks));
        Assert.Equal(["A", "B"], table.Header.ToList());
        Assert.Single(table.Rows);
        Assert.Equal(["1", "2"], table.Rows[0].ToList());
    }

    [Fact]
    [Trait("Req", "PR-THEME-098")] // DroppyCode/UI/Markdown/MarkdownParser.swift:154-168
    public void A_row_with_fewer_cells_than_the_header_is_kept_as_is_not_padded()
    {
        var blocks = MarkdownParser.MarkdownParserParse("| A | B |\n| - | - |\n| 1 |");

        var table = Assert.IsType<MarkdownBlock.Table>(Assert.Single(blocks));
        Assert.Single(table.Rows);
        Assert.Equal(["1"], table.Rows[0].ToList());
    }

    [Fact]
    [Trait("Req", "PR-THEME-098")] // DroppyCode/UI/Markdown/MarkdownParser.swift:154-157,325-329
    public void A_delimiter_row_with_no_dash_is_not_a_table_and_both_lines_stay_one_paragraph()
    {
        var blocks = MarkdownParser.MarkdownParserParse("| A | B |\n| x | y |");

        var paragraph = Assert.IsType<MarkdownBlock.Paragraph>(Assert.Single(blocks));
        Assert.Equal("| A | B |\n| x | y |", paragraph.Text);
    }

    [Theory]
    [InlineData("- item", false, 1, "item")]
    [InlineData("* item", false, 1, "item")]
    [InlineData("+ item", false, 1, "item")]
    [InlineData("*\titem", false, 1, "item")]
    [InlineData("1. a", true, 1, "a")]
    [InlineData("1) a", true, 1, "a")]
    [InlineData("1.", true, 1, "")]
    [InlineData("123456789. x", true, 123456789, "x")]
    [Trait("Req", "PR-THEME-099")] // DroppyCode/UI/Markdown/MarkdownParser.swift:272-289
    public void Valid_bullet_and_ordered_markers_produce_a_single_item_list(string line, bool ordered, int start, string content)
    {
        var list = Assert.IsType<MarkdownBlock.List>(Assert.Single(MarkdownParser.MarkdownParserParse(line)));
        Assert.Equal(ordered, list.Ordered);
        Assert.Equal(start, list.Start);
        var item = Assert.Single(list.Items);
        Assert.Equal(content, item.Text);
        Assert.Null(item.Checked);
        Assert.Empty(item.Children);
    }

    [Theory]
    [InlineData("-item")] // No space or tab after the bullet character.
    [InlineData("1234567890. x")] // Ten digits exceeds the nine-digit cap.
    [InlineData("1.x")] // No space after the delimiter and the remainder is not empty.
    [InlineData("1.\tx")] // Ordered markers do not accept a tab, unlike bullet markers.
    [Trait("Req", "PR-THEME-099")] // DroppyCode/UI/Markdown/MarkdownParser.swift:272-289
    public void Lines_that_fail_the_marker_grammar_stay_paragraph_text(string line)
    {
        var paragraph = Assert.IsType<MarkdownBlock.Paragraph>(Assert.Single(MarkdownParser.MarkdownParserParse(line)));
        Assert.Equal(line, paragraph.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-100")] // DroppyCode/UI/Markdown/MarkdownParser.swift:170
    public void An_ordered_marker_other_than_one_inside_an_open_paragraph_stays_paragraph_text()
    {
        var paragraph = Assert.IsType<MarkdownBlock.Paragraph>(
            Assert.Single(MarkdownParser.MarkdownParserParse("Paragraph text\n5. continued")));
        Assert.Equal("Paragraph text\n5. continued", paragraph.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-100")] // DroppyCode/UI/Markdown/MarkdownParser.swift:170
    public void An_ordered_marker_starting_at_one_starts_a_list_even_inside_an_open_paragraph()
    {
        var blocks = MarkdownParser.MarkdownParserParse("Paragraph text\n1. continued");

        Assert.Equal(2, blocks.Count);
        Assert.Equal(new MarkdownBlock.Paragraph("Paragraph text"), blocks[0]);
        var list = Assert.IsType<MarkdownBlock.List>(blocks[1]);
        Assert.True(list.Ordered);
        Assert.Equal("continued", Assert.Single(list.Items).Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-100")] // DroppyCode/UI/Markdown/MarkdownParser.swift:170
    public void An_unordered_marker_always_starts_a_list_even_inside_an_open_paragraph()
    {
        var blocks = MarkdownParser.MarkdownParserParse("Paragraph text\n- item");

        Assert.Equal(2, blocks.Count);
        Assert.Equal(new MarkdownBlock.Paragraph("Paragraph text"), blocks[0]);
        var list = Assert.IsType<MarkdownBlock.List>(blocks[1]);
        Assert.False(list.Ordered);
        Assert.Equal("item", Assert.Single(list.Items).Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-101")] // DroppyCode/UI/Markdown/MarkdownParser.swift:235-242
    public void A_sibling_marker_of_a_different_ordered_kind_ends_the_list_rather_than_joining_it()
    {
        var blocks = MarkdownParser.MarkdownParserParse("- a\n1. b");

        Assert.Equal(2, blocks.Count);
        var first = Assert.IsType<MarkdownBlock.List>(blocks[0]);
        Assert.False(first.Ordered);
        Assert.Equal("a", Assert.Single(first.Items).Text);
        var second = Assert.IsType<MarkdownBlock.List>(blocks[1]);
        Assert.True(second.Ordered);
        Assert.Equal("b", Assert.Single(second.Items).Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-101")] // DroppyCode/UI/Markdown/MarkdownParser.swift:243-252
    public void An_indented_marker_becomes_a_nested_child_list()
    {
        var blocks = MarkdownParser.MarkdownParserParse("- a\n  - nested");

        var outer = Assert.IsType<MarkdownBlock.List>(Assert.Single(blocks));
        var item = Assert.Single(outer.Items);
        Assert.Equal("a", item.Text);
        var childList = Assert.IsType<MarkdownBlock.List>(Assert.Single(item.Children));
        Assert.Equal("nested", Assert.Single(childList.Items).Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-101")] // DroppyCode/UI/Markdown/MarkdownParser.swift:243-249
    public void Indented_prose_before_any_nested_block_continues_the_items_own_text()
    {
        var blocks = MarkdownParser.MarkdownParserParse("- a\n  continued text");

        var list = Assert.IsType<MarkdownBlock.List>(Assert.Single(blocks));
        var item = Assert.Single(list.Items);
        Assert.Equal("a\ncontinued text", item.Text);
        Assert.Empty(item.Children);
    }

    [Fact]
    [Trait("Req", "PR-THEME-101")] // DroppyCode/UI/Markdown/MarkdownParser.swift:253-257
    public void Unindented_prose_continues_the_items_text_while_it_has_no_children()
    {
        var blocks = MarkdownParser.MarkdownParserParse("- a\nmore text");

        var list = Assert.IsType<MarkdownBlock.List>(Assert.Single(blocks));
        var item = Assert.Single(list.Items);
        Assert.Equal("a\nmore text", item.Text);
        Assert.Empty(item.Children);
    }

    [Fact]
    [Trait("Req", "PR-THEME-101")] // DroppyCode/UI/Markdown/MarkdownParser.swift:222-228
    public void A_blank_line_followed_by_indented_content_does_not_end_the_list()
    {
        var blocks = MarkdownParser.MarkdownParserParse("- a\n\n  more");

        var list = Assert.IsType<MarkdownBlock.List>(Assert.Single(blocks));
        var item = Assert.Single(list.Items);
        Assert.Equal("a", item.Text);
        var child = Assert.IsType<MarkdownBlock.Paragraph>(Assert.Single(item.Children));
        Assert.Equal("more", child.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-101")] // DroppyCode/UI/Markdown/MarkdownParser.swift:222-234
    public void A_blank_line_followed_by_unrelated_text_ends_the_list()
    {
        var blocks = MarkdownParser.MarkdownParserParse("- a\n\nParagraph after");

        Assert.Equal(2, blocks.Count);
        var list = Assert.IsType<MarkdownBlock.List>(blocks[0]);
        Assert.Equal("a", Assert.Single(list.Items).Text);
        Assert.Equal(new MarkdownBlock.Paragraph("Paragraph after"), blocks[1]);
    }

    [Fact]
    [Trait("Req", "PR-THEME-101")] // DroppyCode/UI/Markdown/MarkdownParser.swift:229-232
    public void A_blank_line_followed_by_a_sibling_marker_does_not_end_the_list()
    {
        var blocks = MarkdownParser.MarkdownParserParse("- a\n\n- b");

        var list = Assert.IsType<MarkdownBlock.List>(Assert.Single(blocks));
        Assert.Equal(2, list.Items.Count);
        Assert.Equal("a", list.Items[0].Text);
        Assert.Equal("b", list.Items[1].Text);
    }

    [Theory]
    [InlineData("- [ ] task", false, "task")]
    [InlineData("- [x] task", true, "task")]
    [InlineData("- [X] task", true, "task")]
    [Trait("Req", "PR-THEME-102")] // DroppyCode/UI/Markdown/MarkdownParser.swift:203-217
    public void A_bracket_prefix_becomes_a_checkbox_and_is_removed_from_the_text(string line, bool isChecked, string text)
    {
        var list = Assert.IsType<MarkdownBlock.List>(Assert.Single(MarkdownParser.MarkdownParserParse(line)));
        var item = Assert.Single(list.Items);
        Assert.Equal(isChecked, item.Checked);
        Assert.Equal(text, item.Text);
    }

    [Theory]
    [InlineData("- [] task", "[] task")]
    [InlineData("- [ ]task", "[ ]task")]
    [Trait("Req", "PR-THEME-102")] // DroppyCode/UI/Markdown/MarkdownParser.swift:203-217
    public void A_bracket_form_that_does_not_match_exactly_stays_ordinary_text(string line, string text)
    {
        var list = Assert.IsType<MarkdownBlock.List>(Assert.Single(MarkdownParser.MarkdownParserParse(line)));
        var item = Assert.Single(list.Items);
        Assert.Null(item.Checked);
        Assert.Equal(text, item.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-103")] // DroppyCode/UI/Markdown/MarkdownParser.swift:86-93,179-181
    public void Paragraph_lines_join_with_newlines_and_only_the_outer_whitespace_is_trimmed()
    {
        var paragraph = Assert.IsType<MarkdownBlock.Paragraph>(
            Assert.Single(MarkdownParser.MarkdownParserParse("  Line one  \nLine two  ")));

        Assert.Equal("Line one  \nLine two", paragraph.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-103")] // DroppyCode/UI/Markdown/MarkdownParser.swift:87-91
    public void An_empty_text_produces_no_blocks()
    {
        Assert.Empty(MarkdownParser.MarkdownParserParse(""));
    }

    [Fact]
    [Trait("Req", "PR-THEME-103")] // DroppyCode/UI/Markdown/MarkdownParser.swift:87-91,99-103
    public void A_whitespace_only_text_produces_no_blocks()
    {
        Assert.Empty(MarkdownParser.MarkdownParserParse("   "));
    }

    [Fact]
    [Trait("Req", "PR-THEME-104")] // DroppyCode/UI/Markdown/MarkdownParser.swift:264-270
    public void A_line_padded_with_non_ascii_whitespace_is_still_trimmed_on_the_slow_path()
    {
        // U+00A0 NO-BREAK SPACE is not printable ASCII, so this line takes the slow, unconditional
        // trimmingCharacters(in: .whitespaces) path rather than the printable-ASCII fast path.
        var paragraph = Assert.IsType<MarkdownBlock.Paragraph>(
            Assert.Single(MarkdownParser.MarkdownParserParse(" Hello ")));

        Assert.Equal("Hello", paragraph.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-104")] // DroppyCode/UI/Markdown/MarkdownParser.swift:264-270
    public void A_line_that_starts_and_ends_with_printable_ASCII_is_used_unchanged_on_the_fast_path()
    {
        var paragraph = Assert.IsType<MarkdownBlock.Paragraph>(Assert.Single(MarkdownParser.MarkdownParserParse("Hello")));

        Assert.Equal("Hello", paragraph.Text);
    }
}
