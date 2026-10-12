using Marrow.Core.Models;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W14, Tabula's streaming render (DESIGN.md 6.4 "Code shows the text arriving"). The open block's text
// as the pane draws it: every line so far and the partial last line, except a partial line that could still be the block's
// closing fence, which is never shown as content.
[Trait("Decision", "ED-NEW-web-w14-1")]
public sealed class ArtifactFenceStreamingTests
{
    [Fact]
    public void The_open_block_shows_its_partial_line_unless_it_could_still_close_the_block()
    {
        var parser = new ArtifactFenceParser();
        Assert.Null(parser.Streaming);
        parser.Feed("Sure:\n```nth-artifact id=pricing kind=html\n<h3>Orbit");
        Assert.Equal("<h3>Orbit", parser.Streaming);
        parser.Feed(" pricing</h3>\n``");
        Assert.Equal("<h3>Orbit pricing</h3>\n", parser.Streaming);
        parser.Feed("`");
        Assert.Equal("<h3>Orbit pricing</h3>\n", parser.Streaming);
        parser.Feed("\nDone.");
        Assert.Null(parser.Streaming);
    }

    [Fact]
    public void A_tilde_block_holds_back_only_a_partial_tilde_line()
    {
        var parser = new ArtifactFenceParser();
        parser.Feed("~~~nth-artifact id=notes kind=markdown\n# Notes\n   ``");
        Assert.Equal("# Notes\n   ``", parser.Streaming);
        parser.Feed("`js\n");
        parser.Feed("  ~");
        Assert.StartsWith("# Notes\n", parser.Streaming, StringComparison.Ordinal);
        Assert.DoesNotContain("  ~", parser.Streaming, StringComparison.Ordinal);
    }
}
