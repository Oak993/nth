using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W13 (DESIGN.md 6.3 Export of Markdown documents). The reader behind the rendered document,
// its standalone HTML and PDF, and the .docx: the common blocks and inline styles, raw HTML shown as text, and only web,
// mail and in-page links kept.
[Trait("Decision", "ED-NEW-web-w13-5")]
public sealed class ArtifactMarkdownTests
{
    private const string Document = """
        # Release notes

        Orbit **2.0** ships *today* with `nth` support and a [guide](https://example.com/guide).
        A second line joins the paragraph.

        - Faster sync
        - Offline mode
          - Maps
          - Notes
        - Dark theme

        3. Install
        4. Sign in

        > Quoted text

        ```js
        const a = 1 < 2;
        ```

        | Plan | Price |
        |------|------:|
        | Free | $0 |
        | Pro | $8 |

        ---
        Setext title
        ============
        """;

    [Fact]
    public void Blocks_are_read_in_order()
    {
        var blocks = ArtifactMarkdown.Parse(Document);
        Assert.Collection(blocks,
            b => Assert.Equal(new MarkdownHeading(1, "Release notes"), b),
            b => Assert.Contains("A second line joins", Assert.IsType<MarkdownParagraph>(b).Text, StringComparison.Ordinal),
            b =>
            {
                var list = Assert.IsType<MarkdownList>(b);
                Assert.False(list.Ordered);
                Assert.Equal(["Faster sync", "Offline mode", "Dark theme"], list.Items.Select(i => i.Text));
                var nested = Assert.IsType<MarkdownList>(Assert.Single(list.Items[1].Children));
                Assert.Equal(["Maps", "Notes"], nested.Items.Select(i => i.Text));
            },
            b =>
            {
                var list = Assert.IsType<MarkdownList>(b);
                Assert.True(list.Ordered);
                Assert.Equal(3, list.Start);
            },
            b => Assert.Equal("Quoted text", Assert.IsType<MarkdownParagraph>(Assert.Single(Assert.IsType<MarkdownQuote>(b).Blocks)).Text),
            b => Assert.Equal(new MarkdownCode("js", "const a = 1 < 2;"), b),
            b =>
            {
                var table = Assert.IsType<MarkdownTable>(b);
                Assert.Equal(["Plan", "Price"], table.Header);
                Assert.Equal(["Pro", "$8"], table.Rows[1]);
            },
            b => Assert.IsType<MarkdownRule>(b),
            b => Assert.Equal(new MarkdownHeading(1, "Setext title"), b));
    }

    [Fact]
    public void Inline_styles_and_links_become_runs()
    {
        var spans = ArtifactMarkdown.Inline("Orbit **2.0** ships *today* with `nth` and ~~old~~ [guide](https://example.com/guide)");
        Assert.Contains(spans, s => s.Text == "2.0" && s.Bold);
        Assert.Contains(spans, s => s.Text == "today" && s.Italic);
        Assert.Contains(spans, s => s.Text == "nth" && s.Code);
        Assert.Contains(spans, s => s.Text == "old" && s.Strike);
        Assert.Contains(spans, s => s.Text == "guide" && s.Link == "https://example.com/guide");
    }

    [Fact]
    public void Html_escapes_raw_markup_and_drops_unsafe_links()
    {
        var html = ArtifactMarkdown.ToHtml("<script>alert(1)</script> [click](javascript:alert(1)) [mail](mailto:a@example.com) [top](#top)");
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"mailto:a@example.com\">mail</a>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#top\">top</a>", html, StringComparison.Ordinal);
        Assert.Contains("<pre><code class=\"language-js\">const a = 1 &lt; 2;</code></pre>", ArtifactMarkdown.ToHtml(Document), StringComparison.Ordinal);
    }
}
