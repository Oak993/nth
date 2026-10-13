using System.Text;
using Marrow.Core.Models;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.2 fallback: a fenced nth-artifact block in the answer, parsed while it
// streams, with a compact chip in its place). Every split of a stream must show the same text and find the same blocks.
[Trait("Decision", "ED-NEW-web-w12-2")]
public sealed class ArtifactFenceParserTests
{
    private const string Sample =
        "Here is the calculator:\n"
        + "```nth-artifact id=pricing kind=html title=\"Pricing calculator\"\n"
        + "<h3>Orbit pricing</h3>\n"
        + "```\n"
        + "Ask for changes any time.";

    private const string Mixed =
        "Example of the syntax, not a block:\n"
        + "```markdown\n"
        + "```nth-artifact kind=svg title=\"Not me\"\n"
        + "```\n"
        + "Now a document with code inside:\r\n"
        + "```nth-artifact id=notes kind=markdown title='Release notes'\r\n"
        + "# Notes\r\n"
        + "```python\r\n"
        + "print(1)\r\n"
        + "```\r\n"
        + "~~~\r\n"
        + "plain\r\n"
        + "~~~\r\n"
        + "Done.\r\n"
        + "```\r\n"
        + "And a project:\n"
        + "````nth-artifact kind=project title=\"Orbit site\" entry=index.html\n"
        + "```html path=index.html\n"
        + "<link href=\"style.css\">\n"
        + "```\n"
        + "```\n"
        + "bare inner fence\n"
        + "```\n"
        + "````\n"
        + "`` inline `` text and ~ tilde\n"
        + "  ```nth-artifact\n"
        + "cut off";

    private static (string Display, List<ArtifactFenceEvent> Events) Stream(string text, IEnumerable<int> cuts)
    {
        var parser = new ArtifactFenceParser();
        var display = new StringBuilder();
        var events = new List<ArtifactFenceEvent>();
        var at = 0;
        foreach (var cut in cuts.Append(text.Length))
        {
            var step = parser.Feed(text[at..cut]);
            display.Append(step.Display);
            events.AddRange(step.Events);
            at = cut;
        }
        var last = parser.Finish();
        display.Append(last.Display);
        events.AddRange(last.Events);
        return (display.ToString(), events);
    }

    [Fact]
    public void A_block_becomes_a_chip_on_its_own_line_and_its_content_is_reported_when_it_closes()
    {
        var (display, blocks) = ArtifactFenceParser.Parse(Sample);
        Assert.Equal("Here is the calculator:\n\n[Pricing calculator · HTML app](nth-artifact://pricing)\n\nAsk for changes any time.", display);
        var block = Assert.Single(blocks);
        Assert.Equal((0, "<h3>Orbit pricing</h3>\n", true), (block.Index, block.Content, block.Complete));
        Assert.Equal(new ArtifactFenceHeader("pricing", ArtifactKind.Html, "Pricing calculator", null, null), block.Header);
        Assert.Equal("[Orbit \\[beta\\] · SVG image](nth-artifact://x)\n", ArtifactFenceParser.Chip("x", "Orbit [beta]", ArtifactKind.Svg, null));
        Assert.Equal("[Untitled](nth-artifact://y)\n", ArtifactFenceParser.Chip("y", "", null, null));
        Assert.StartsWith("[Pricing calculator", ArtifactFenceParser.Parse(Sample[24..]).Display, StringComparison.Ordinal); // no blank line before a block that starts the answer
    }

    [Fact]
    public void Every_split_point_shows_the_same_text_and_finds_the_same_blocks()
    {
        foreach (var text in new[] { Sample, Mixed })
        {
            var (whole, wholeEvents) = Stream(text, []);
            for (var cut = 1; cut < text.Length; cut++)
            {
                var (display, events) = Stream(text, [cut]);
                Assert.Equal(whole, display);
                Assert.Equal(wholeEvents, events);
            }
            var (byChar, charEvents) = Stream(text, Enumerable.Range(1, text.Length - 1));
            Assert.Equal(whole, byChar);
            Assert.Equal(wholeEvents, charEvents);
        }
    }

    [Fact]
    public void Inner_fences_code_blocks_of_the_answer_and_a_cut_off_block_are_each_handled()
    {
        var (display, events) = Stream(Mixed, []);
        Assert.Contains("```nth-artifact kind=svg title=\"Not me\"\n", display, StringComparison.Ordinal); // inside the answer's own code block: shown, not parsed
        var closed = events.OfType<ArtifactFenceEvent.Closed>().ToList();
        Assert.Equal(new[] { "notes", null, null }, closed.Select(c => c.Header.Id));
        Assert.Equal("# Notes\r\n```python\r\nprint(1)\r\n```\r\n~~~\r\nplain\r\n~~~\r\nDone.\r\n", closed[0].Content);
        Assert.Equal(("Release notes", ArtifactKind.Markdown), (closed[0].Header.Title, closed[0].Header.Kind));
        Assert.Equal("```html path=index.html\n<link href=\"style.css\">\n```\n```\nbare inner fence\n```\n", closed[1].Content);
        Assert.Equal((ArtifactKind.Project, "index.html", true), (closed[1].Header.Kind, closed[1].Header.Entry, closed[1].Complete));
        Assert.Equal(("cut off", false), (closed[2].Content, closed[2].Complete));
        Assert.Equal(new[] { 0, 1, 2 }, events.OfType<ArtifactFenceEvent.Opened>().Select(o => o.Index));
        Assert.Contains("`` inline `` text and ~ tilde\n", display, StringComparison.Ordinal);
        Assert.DoesNotContain("print(1)", display, StringComparison.Ordinal);
        Assert.DoesNotContain("bare inner fence", display, StringComparison.Ordinal);
        Assert.Equal(3, display.Split("](nth-artifact://", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Text_is_shown_at_once_and_only_a_line_that_can_still_open_a_block_waits()
    {
        var parser = new ArtifactFenceParser();
        Assert.Equal("Hello wor", parser.Feed("Hello wor").Display);
        Assert.Equal("ld\n", parser.Feed("ld\n").Display);
        Assert.Equal("", parser.Feed("  ``").Display);
        Assert.Equal("", parser.Feed("`nth-art").Display);
        Assert.Null(parser.Open);
        var opened = parser.Feed("ifact kind=svg title=Logo\n<svg>");
        Assert.Equal("\n[Logo · SVG image](nth-artifact://)\n", opened.Display);
        Assert.IsType<ArtifactFenceEvent.Opened>(Assert.Single(opened.Events));
        Assert.Equal("", parser.Feed("</svg>\n").Display); // the open block's text never reaches the chat
        Assert.Equal((0, "<svg></svg>\n"), (parser.Open!.Value.Index, parser.Open.Value.Content));
        Assert.Equal("", parser.Feed("```").Display);
        var closed = parser.Feed("\n```py");
        Assert.Equal("\n```py", closed.Display); // a code fence of another language is shown as it arrives
        Assert.Equal("<svg></svg>\n", Assert.IsType<ArtifactFenceEvent.Closed>(Assert.Single(closed.Events)).Content);
        Assert.Null(parser.Open);
        Assert.Equal("thon\n```nth-artifact\n", parser.Feed("thon\n```nth-artifact\n").Display); // inside a code block of the answer
    }

    [Fact]
    public void Header_attributes_take_quotes_aliases_and_a_bare_kind_word()
    {
        Assert.Equal(new ArtifactFenceHeader("a1", ArtifactKind.React, "Say \"hi\"", "tsx", null), ArtifactFenceHeader.Parse("nth-artifact id=a1 type=jsx name=\"Say \\\"hi\\\"\" lang=tsx"));
        Assert.Equal(new ArtifactFenceHeader(null, ArtifactKind.Mermaid, "It's a flow", null, "x"), ArtifactFenceHeader.Parse("NTH-ARTIFACT   mermaid title=\"It's a flow\" entry=x junk=1"));
        Assert.Equal(new ArtifactFenceHeader(null, null, null, null, null), ArtifactFenceHeader.Parse("nth-artifact"));
        Assert.Equal(new ArtifactFenceHeader(null, ArtifactKind.Code, "a b", null, null), ArtifactFenceHeader.Parse("nth-artifact kind=code title='a b'"));
        Assert.Null(ArtifactFenceHeader.Parse("nth-artifactx kind=html"));
        Assert.Null(ArtifactFenceHeader.Parse("python"));
        Assert.Equal("pricing", ArtifactFenceParser.ChipId("nth-artifact://Pricing/"));
        Assert.Null(ArtifactFenceParser.ChipId("https://example.com"));
        Assert.Null(ArtifactFenceParser.ChipId(null));
    }

    [Fact]
    public void A_project_block_gives_each_inner_block_that_names_a_path_as_a_file()
    {
        var content =
            "Files follow.\n"
            + "```html path=index.html\n<p>Hi</p>\n```\n"
            + "```css style.css\nbody{}\n```\n"
            + "```js file=\"src/app.js\"\nexport {};\n```\n"
            + "```markdown README.md\n# Site\n```sh\nnpm start\n```\nEnd.\n```\n"
            + "```python\nignored()\n```\n"
            + "```json data/x.json\n{}\n";
        var files = ArtifactFenceProject.Files(content);
        Assert.Equal(new[] { "README.md", "data/x.json", "index.html", "src/app.js", "style.css" }, files.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("<p>Hi</p>\n", files["index.html"]);
        Assert.Equal("# Site\n```sh\nnpm start\n```\nEnd.\n", files["README.md"]);
        Assert.Equal("{}\n", files["data/x.json"]); // a file cut off by the end of the block keeps what arrived
        Assert.Empty(ArtifactFenceProject.Files("no files here\n```python\nx\n```\n"));
    }
}
