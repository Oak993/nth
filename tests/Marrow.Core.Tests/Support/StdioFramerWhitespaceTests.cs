using System.Text;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// Foundation .whitespaces (StdioFramer.swift:75-76) is tab, every Zs character and U+200B (PR-PROVACP-152).
public sealed class StdioFramerWhitespaceTests
{
    // CFUniChar.c isWhitespace includes U+200B despite its Unicode Cf category.
    // R-TEST-4 negative control (string.Trim() keeps U+200B): tests/Marrow.Core.Tests/Port/SwiftStringTests.cs:172-180.
    [Fact]
    [Trait("Req", "PR-PROVACP-152")] // StdioFramer.swift:75-76
    public void Foundation_whitespace_includes_zero_width_space_around_the_name_and_value()
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        var wire = Encoding.UTF8.GetBytes("\U0000200BContent-Length\U0000200B:\U0000200B1\U0000200B\r\n\r\nZ"); // U+200B ZERO WIDTH SPACE
        Assert.Equal(new byte[] { 0x5A }, Assert.Single(framer.Append(wire)));
        Assert.Null(framer.Finish());
    }

    // These are not Foundation whitespace: a value that keeps one is not an Int, so the whole block is nil.
    // U+200C also joins the colon into one Character; either way the line gives no length.
    [Theory]
    [Trait("Req", "PR-PROVACP-152")] // StdioFramer.swift:73-76
    [InlineData("\U0000180E")] // U+180E MONGOLIAN VOWEL SEPARATOR
    [InlineData("\U0000FEFF")] // U+FEFF ZERO WIDTH NO-BREAK SPACE
    [InlineData("\U0000200C")] // U+200C ZERO WIDTH NON-JOINER
    public void Other_format_characters_are_not_silently_trimmed(string character)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        var wire = Encoding.UTF8.GetBytes("Content-Length:" + character + "1\r\n\r\nZ");
        Assert.Empty(framer.Append(wire));
        Assert.Null(framer.Finish());
    }

    // A newline inside the value is not trimmed: it splits the line, and "Content-Length:" alone has one part.
    [Theory]
    [Trait("Req", "PR-PROVACP-152")] // StdioFramer.swift:72-74
    [InlineData("\n")]
    [InlineData("\U00002028")] // U+2028 LINE SEPARATOR
    public void A_newline_after_the_colon_leaves_a_one_part_line_that_is_skipped(string newline)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        var wire = Encoding.UTF8.GetBytes("Content-Length:" + newline + "Content-Length: 1\r\n\r\nZ");
        Assert.Equal(new byte[] { 0x5A }, Assert.Single(framer.Append(wire)));
        Assert.Null(framer.Finish());
    }
}
