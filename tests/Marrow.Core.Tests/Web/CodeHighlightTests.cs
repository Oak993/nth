using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W14, Tabula's Code view (DESIGN.md 6.1 "code (any language, shown with highlighting)"; the approved
// canvas .code .k/.s/.c). The lexer behind the native code view: keywords, strings, comments, numbers, tags and attributes,
// in order and never overlapping, by a family chosen from the language or the file's extension.
[Trait("Decision", "ED-NEW-web-w14-1")]
public sealed class CodeHighlightTests
{
    private static List<(string Text, CodeTokenKind Kind)> Runs(string text, string language) =>
        CodeHighlight.Tokens(text, CodeHighlight.FamilyOf(language)).Select(t => (text.Substring(t.Start, t.Length), t.Kind)).ToList();

    [Fact]
    public void Script_colours_keywords_strings_numbers_and_both_comment_forms()
    {
        var runs = Runs("const per = () => billing.value === \"yearly\" ? 10 : 12; // monthly\n/* note */ return x;", "js");
        Assert.Contains(("const", CodeTokenKind.Keyword), runs);
        Assert.Contains(("\"yearly\"", CodeTokenKind.String), runs);
        Assert.Contains(("10", CodeTokenKind.Number), runs);
        Assert.Contains(("// monthly", CodeTokenKind.Comment), runs);
        Assert.Contains(("/* note */", CodeTokenKind.Comment), runs);
        Assert.Contains(("return", CodeTokenKind.Keyword), runs);
        Assert.DoesNotContain(runs, r => r.Text is "per" or "billing");
    }

    [Fact]
    public void Markup_colours_tags_attributes_their_values_and_comments()
    {
        var runs = Runs("<!-- Orbit -->\n<div class=\"calc\"><h3>Orbit</h3></div>", "html");
        Assert.Equal(("<!-- Orbit -->", CodeTokenKind.Comment), runs[0]);
        Assert.Contains(("<div", CodeTokenKind.Tag), runs);
        Assert.Contains(("class", CodeTokenKind.Attribute), runs);
        Assert.Contains(("\"calc\"", CodeTokenKind.String), runs);
        Assert.Contains(("</h3", CodeTokenKind.Tag), runs);
        Assert.DoesNotContain(runs, r => r.Text.Contains("Orbit</", StringComparison.Ordinal));
    }

    [Fact]
    public void Hash_languages_take_hash_comments_and_markdown_its_headings_and_code()
    {
        var python = Runs("def price(seats):  # per month\n    return seats * 10", "py");
        Assert.Contains(("def", CodeTokenKind.Keyword), python);
        Assert.Contains(("# per month", CodeTokenKind.Comment), python);
        var markdown = Runs("# Pricing\nUse `nth.storage` here.\n", "md");
        Assert.Contains(("# Pricing", CodeTokenKind.Keyword), markdown);
        Assert.Contains(("`nth.storage`", CodeTokenKind.String), markdown);
    }

    [Theory]
    [InlineData("js")]
    [InlineData("html")]
    [InlineData("css")]
    [InlineData("py")]
    [InlineData("sql")]
    [InlineData("md")]
    [InlineData("json")]
    [InlineData("mermaid")]
    public void Runs_are_in_order_inside_the_text_and_never_overlap(string language)
    {
        const string text = "<a href='x'>/* \"q\" */ select 1 from t; # h\n`c` { \"k\": 2 } graph TD\n// end \"open";
        var at = 0;
        foreach (var token in CodeHighlight.Tokens(text, CodeHighlight.FamilyOf(language)))
        {
            Assert.True(token.Start >= at && token.Length > 0 && token.Start + token.Length <= text.Length);
            at = token.Start + token.Length;
        }
    }

    [Fact]
    public void Plain_text_and_text_over_the_limit_are_not_coloured_and_a_path_names_its_language()
    {
        Assert.Empty(CodeHighlight.Tokens("const x = 1", CodeFamily.Plain));
        Assert.Empty(CodeHighlight.Tokens(new string('a', CodeHighlight.MaxLength + 1), CodeFamily.CLike));
        Assert.Equal(CodeFamily.Plain, CodeHighlight.FamilyOf("klingon"));
        Assert.Equal(CodeFamily.CLike, CodeHighlight.FamilyOf("TSX"));
        Assert.Equal("css", CodeHighlight.LanguageOfPath("src/Style.CSS"));
        Assert.Null(CodeHighlight.LanguageOfPath("Makefile"));
    }
}
