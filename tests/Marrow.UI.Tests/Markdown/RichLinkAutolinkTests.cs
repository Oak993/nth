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
public sealed class RichLinkAutolinkTests
{
    private static AttributedText AutolinkedBareDomains(AttributedText parsed)
    {
        var method = typeof(RichLink).GetMethod("AutolinkedBareDomains", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (AttributedText)method.Invoke(null, [parsed])!;
    }

    private static AttributedText BaseAttributed(string source)
    {
        var method = typeof(RichLink).GetMethod("BaseAttributed", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (AttributedText)method.Invoke(null, [source])!;
    }

    [Fact]
    [Trait("Req", "PR-THEME-129")] // DroppyCode/UI/Markdown/MarkdownView.swift:811 bareDomainTLDs membership
    public void A_bare_domain_whose_TLD_is_curated_becomes_an_https_link()
    {
        var parsed = new AttributedText([new AttributedRun("Visit getdroppy.app today")]);

        var result = AutolinkedBareDomains(parsed);

        var linkRun = Assert.Single(result.Runs, r => r.Link is not null);
        Assert.Equal("getdroppy.app", linkRun.Text);
        Assert.Equal("https://getdroppy.app", linkRun.Link!.RelativeString);
        Assert.Equal("Visit ", result.Runs[0].Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-129")] // DroppyCode/UI/Markdown/MarkdownView.swift:757-768: a file name's extension is not a curated TLD
    public void A_dotted_filename_whose_extension_is_not_a_curated_TLD_is_not_linked()
    {
        var parsed = new AttributedText([new AttributedRun("See MarkdownView.swift for details")]);

        var result = AutolinkedBareDomains(parsed);

        Assert.DoesNotContain(result.Runs, r => r.Link is not null);
    }

    [Fact]
    [Trait("Req", "PR-THEME-129")] // DroppyCode/UI/Markdown/MarkdownView.swift:757-768: .app IS curated, reproducing the upstream file-name consequence
    public void A_dotted_filename_whose_extension_happens_to_be_a_curated_TLD_IS_linked()
    {
        // Upstream consequence named by PR-THEME-129's failure_cases: Foo.app links.
        var parsed = new AttributedText([new AttributedRun("Open Foo.app now")]);

        var result = AutolinkedBareDomains(parsed);

        var linkRun = Assert.Single(result.Runs, r => r.Link is not null);
        Assert.Equal("Foo.app", linkRun.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-128")] // DroppyCode/UI/Markdown/MarkdownView.swift:750-755: the look-behind excludes an email's domain part
    public void A_domain_immediately_preceded_by_an_at_sign_is_not_matched()
    {
        var parsed = new AttributedText([new AttributedRun("Email me at name@getdroppy.app please")]);

        var result = AutolinkedBareDomains(parsed);

        Assert.DoesNotContain(result.Runs, r => r.Link is not null);
    }

    [Fact]
    [Trait("Req", "PR-THEME-128")] // DroppyCode/UI/Markdown/MarkdownView.swift:753-755: a path after the domain is included in the match
    public void A_domain_with_a_path_links_the_whole_match()
    {
        var parsed = new AttributedText([new AttributedRun("www.droppy.app/pricing?a=1 is the page")]);

        var result = AutolinkedBareDomains(parsed);

        var linkRun = Assert.Single(result.Runs, r => r.Link is not null);
        Assert.Equal("www.droppy.app/pricing?a=1", linkRun.Text);
        Assert.Equal("https://www.droppy.app/pricing?a=1", linkRun.Link!.RelativeString);
    }

    [Fact]
    [Trait("Req", "PR-THEME-128")] // DroppyCode/UI/Markdown/MarkdownView.swift:809-816: trailing sentence punctuation is trimmed off the link
    public void Trailing_sentence_punctuation_is_not_part_of_the_link()
    {
        var parsed = new AttributedText([new AttributedRun("See getdroppy.app.")]);

        var result = AutolinkedBareDomains(parsed);

        var linkRun = Assert.Single(result.Runs, r => r.Link is not null);
        Assert.Equal("getdroppy.app", linkRun.Text);
        Assert.Equal(".", result.Runs[^1].Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-127")] // DroppyCode/UI/Markdown/MarkdownView.swift:772-780: a run already a link or code is skipped
    public void A_run_already_a_link_or_code_is_left_untouched_by_the_autolinker()
    {
        var link = SwiftURL.Create("https://example.com")!;
        var linkRun = new AttributedRun("example.com", Link: link);
        var codeRun = new AttributedRun("getdroppy.app", IsCode: true);
        var parsed = new AttributedText([linkRun, codeRun]);

        var result = AutolinkedBareDomains(parsed);

        Assert.Equal([linkRun, codeRun], result.Runs);
    }

    [Fact]
    [Trait("Req", "PR-THEME-127")] // DroppyCode/UI/Markdown/MarkdownView.swift:828-834: whitespace, including newlines, is preserved
    public void BaseAttributed_preserves_whitespace_and_newlines_in_plain_text()
    {
        var source = "Line one\n\nLine two with  two spaces and a\ttab.";

        var result = BaseAttributed(source);

        Assert.Equal(source, result.PlainText);
    }

    [Fact]
    [Trait("Req", "PR-THEME-127")] // DroppyCode/UI/Markdown/MarkdownView.swift:828-834: an inline code span is recognised
    public void BaseAttributed_recognises_an_inline_code_span()
    {
        var result = BaseAttributed("Run `git status` now");

        var codeRun = Assert.Single(result.Runs, r => r.IsCode);
        Assert.Equal("git status", codeRun.Text);
        Assert.Equal("Run git status now", result.PlainText); // backticks are markers, not text (:828-834)
    }

    [Fact]
    [Trait("Req", "PR-THEME-127")] // DroppyCode/UI/Markdown/MarkdownView.swift:828-834: an [text](url) link is recognised
    public void BaseAttributed_recognises_a_markdown_link()
    {
        var result = BaseAttributed("See [our site](https://getdroppy.app) now");

        var linkRun = Assert.Single(result.Runs, r => r.Link is not null);
        Assert.Equal("our site", linkRun.Text);
        Assert.Equal("https://getdroppy.app", linkRun.Link!.RelativeString);
    }

    [Fact]
    [Trait("Req", "PR-THEME-127")] // DroppyCode/UI/Markdown/MarkdownView.swift:792-797 AttributedString(markdown:) reads link text as inline content (CommonMark: code spans and emphasis inside a link's text)
    public void BaseAttributed_reads_code_and_emphasis_inside_a_links_text_and_keeps_the_link_on_every_piece()
    {
        var code = BaseAttributed("Open [`ls -la`](https://getdroppy.app) here");
        var codeLink = Assert.Single(code.Runs, r => r.Link is not null);
        Assert.Equal("ls -la", codeLink.Text);
        Assert.True(codeLink.IsCode);
        Assert.Equal("Open ls -la here", code.PlainText);

        var mixed = BaseAttributed("[**Droppy** site](https://getdroppy.app)");
        Assert.Equal("Droppy site", mixed.PlainText);
        Assert.All(mixed.Runs, r => Assert.Equal("https://getdroppy.app", r.Link!.RelativeString));
        Assert.True(Assert.Single(mixed.Runs, r => r.Text == "Droppy").IsBold);
        Assert.False(Assert.Single(mixed.Runs, r => r.Text == " site").IsBold);
    }

    [Fact]
    [Trait("Req", "PR-THEME-127")] // DroppyCode/UI/Markdown/MarkdownView.swift:828-834: a malformed marker degrades to plain text, never throws
    public void BaseAttributed_never_throws_on_malformed_markup()
    {
        var result = BaseAttributed("unclosed `code and *emphasis and [bracket");

        Assert.Equal("unclosed `code and *emphasis and [bracket", result.PlainText);
    }

    [Fact]
    [Trait("Req", "PR-THEME-127")] // DroppyCode/UI/Markdown/MarkdownView.swift:828-834: an empty source parses to no runs
    public void BaseAttributed_of_empty_text_is_empty()
    {
        Assert.True(BaseAttributed("").IsEmpty);
    }
}
