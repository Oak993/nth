using Marrow.UI.Markdown;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/SelectableMessageText.swift (B2 1fbf81e), :1-64.
public sealed class SelectableMessageTextTests
{
    [Fact]
    [Trait("Req", "PR-THEME-122")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:10-19
    public void Blocks_are_separated_by_exactly_one_blank_line()
    {
        var text = MessageText.Plain("# Title\n\nSome body.");

        Assert.Equal("Title\n\nSome body.", text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-122")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:23-24
    public void A_heading_loses_its_hashes_and_keeps_only_its_text()
    {
        var text = MessageText.Plain("### Section header");

        Assert.Equal("Section header", text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-122")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:27-29
    public void A_non_empty_non_hydra_code_block_is_copied_verbatim()
    {
        var text = MessageText.Plain("```python\nprint(1)\ndef f():\n    pass\n```");

        Assert.Equal("print(1)\ndef f():\n    pass", text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-122")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:27-29
    public void A_hydra_code_block_is_skipped_case_insensitively()
    {
        Assert.Equal("", MessageText.Plain("```HyDrA\ndelegation payload\n```"));
    }

    [Fact]
    [Trait("Req", "PR-THEME-122")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:27-29
    public void An_empty_code_block_is_skipped()
    {
        Assert.Equal("", MessageText.Plain("```text\n```"));
    }

    [Fact]
    [Trait("Req", "PR-THEME-122")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:10-19, 27-29
    public void A_skipped_code_block_still_consumes_its_blank_line_separator()
    {
        // The reply has a paragraph followed by a delegation card: the card contributes no
        // lines of its own, but the separator ahead of it was already appended, leaving a
        // trailing blank line (PR-THEME-122 failure_cases).
        var text = MessageText.Plain("Some text\n\n```hydra\ndelegation\n```");

        Assert.Equal("Some text\n", text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-122")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:10-19, 27-29
    public void A_reply_whose_only_block_is_a_delegation_card_copies_as_an_empty_string()
    {
        // The delegation card is the first block, so Plain never appends the leading separator
        // for it; its own append contributes nothing either, so the whole clipboard text is "".
        Assert.Equal("", MessageText.Plain("```hydra\ndelegation\n```"));
    }

    [Fact]
    [Trait("Req", "PR-THEME-122")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:32-34
    public void Checkbox_list_items_use_the_ballot_box_markers_with_one_space()
    {
        var text = MessageText.Plain("- [x] done\n- [ ] pending");

        Assert.Equal("☑ done\n☐ pending", text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-122")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:35-36
    public void Ordered_list_items_use_the_number_and_two_spaces()
    {
        var text = MessageText.Plain("5. first\n6. second");

        Assert.Equal("5.  first\n6.  second", text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-122")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:37-38
    public void Unordered_list_items_use_the_bullet_and_two_spaces()
    {
        var text = MessageText.Plain("- alpha\n- beta");

        Assert.Equal("•  alpha\n•  beta", text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-122")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:43-44
    public void Quote_markers_are_dropped_leaving_only_the_inner_blocks()
    {
        var text = MessageText.Plain("> quoted line");

        Assert.Equal("quoted line", text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-122")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:45-47
    public void Table_cells_are_joined_with_two_spaces_header_and_rows_alike()
    {
        var text = MessageText.Plain("| A | B |\n| - | - |\n| 1 | 2 |");

        Assert.Equal("A  B\n1  2", text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-122")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:48-49
    public void A_horizontal_rule_produces_no_line_of_its_own()
    {
        // Plain adds a blank-line separator ahead of every non-first block regardless of what
        // that block itself contributes: the rule block (second) gets one and appends nothing,
        // then the trailing paragraph (third) gets its own separator too, leaving two blank
        // lines between "first" and "second".
        var text = MessageText.Plain("first\n\n---\n\nsecond");

        Assert.Equal("first\n\n\nsecond", text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:55
    public void Image_markup_becomes_its_alt_text()
    {
        Assert.Equal("a picture", MessageText.Plain("![a picture](https://example.com/x.png)"));
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:55
    public void Image_markup_with_an_empty_alt_disappears_entirely()
    {
        Assert.Equal("", MessageText.Plain("![](https://example.com/x.png)"));
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:55
    public void Image_markup_whose_target_contains_whitespace_does_not_match()
    {
        var source = "![alt](https://example.com/a b.png)";

        Assert.Equal(source, MessageText.Plain(source));
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:56-59
    public void A_link_whose_title_differs_from_its_target_becomes_title_space_paren_url_paren()
    {
        var text = MessageText.Plain("[Example site](https://example.com)");

        Assert.Equal("Example site (https://example.com)", text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:56-59
    public void A_link_whose_title_equals_its_target_becomes_the_bare_target()
    {
        var text = MessageText.Plain("[https://example.com](https://example.com)");

        Assert.Equal("https://example.com", text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:56-59
    public void A_link_with_a_quoted_title_attribute_ignores_the_attribute_text()
    {
        var text = MessageText.Plain("[Example](https://example.com \"Example Domain\")");

        Assert.Equal("Example (https://example.com)", text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:56-59
    public void A_link_whose_target_contains_whitespace_does_not_match()
    {
        var source = "[Example](https://example.com/a b)";

        Assert.Equal(source, MessageText.Plain(source));
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:61
    public void An_http_autolink_becomes_the_bare_address()
    {
        Assert.Equal("http://example.com", MessageText.Plain("<http://example.com>"));
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:61
    public void An_https_autolink_becomes_the_bare_address()
    {
        Assert.Equal("https://example.com", MessageText.Plain("<https://example.com>"));
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:61
    public void A_mailto_autolink_becomes_the_bare_address()
    {
        Assert.Equal("mailto:a@example.com", MessageText.Plain("<mailto:a@example.com>"));
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:61
    public void An_autolink_with_an_unsupported_scheme_does_not_match()
    {
        var source = "<ftp://example.com>";

        Assert.Equal(source, MessageText.Plain(source));
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:54-63
    public void Images_links_and_autolinks_all_resolve_within_one_paragraph_in_pass_order()
    {
        var text = MessageText.Plain("![pic](i.png) then [Site](https://s.example) then <mailto:a@example.com>");

        Assert.Equal("pic then Site (https://s.example) then mailto:a@example.com", text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:54-63
    public void Text_with_no_markup_passes_through_inline_unchanged()
    {
        var source = "Just plain words, nothing special.";

        Assert.Equal(source, MessageText.Plain(source));
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:54-63
    public void A_combining_mark_fused_onto_the_opening_bracket_of_an_image_breaks_the_match()
    {
        // U+0301 COMBINING ACUTE ACCENT attaches to the preceding "!" as one extended grapheme
        // cluster ("!" + U+0301), so the literal "!" no longer stands alone at that position and
        // the image pass does not match here (PR-THEME-181 windows_path); a UTF-16 code-unit
        // regex would wrongly still match and consume the "!" too. The "[alt](url)" that is left
        // behind is then still a valid link, which the next pass (link) converts as usual.
        var source = "!́[alt](https://example.com)";

        Assert.Equal("!́alt (https://example.com)", MessageText.Plain(source));
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:54-63
    public void A_combining_mark_fused_onto_the_opening_bracket_of_a_link_breaks_the_match()
    {
        // U+0301 attaches to "[" as one extended grapheme cluster, so the literal "[" does not
        // stand alone and the link pattern does not match at this position.
        var source = "[́title](https://example.com)";

        Assert.Equal(source, MessageText.Plain(source));
    }

    [Fact]
    [Trait("Req", "PR-THEME-181")] // DroppyCode/UI/Markdown/SelectableMessageText.swift:54-63
    public void An_ordinary_bracket_with_no_combining_mark_still_matches_as_a_link()
    {
        // Contrast case for the two combining-mark tests above: an un-fused "[" matches normally.
        Assert.Equal(
            "title (https://example.com)",
            MessageText.Plain("[title](https://example.com)"));
    }
}
