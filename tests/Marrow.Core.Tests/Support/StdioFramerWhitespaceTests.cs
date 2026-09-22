using System.Text;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class StdioFramerWhitespaceTests
{
    [Fact] // CFUniChar.c isWhitespace includes U+200B despite its Unicode Cf category.
    [Trait("Req", "PR-PROVACP-152")]
    public void Foundation_whitespace_includes_zero_width_space_around_the_name_and_value()
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        var wire = Encoding.UTF8.GetBytes("\u200BContent-Length\u200B:\u200B1\u200B\r\n\r\nZ");
        Assert.Equal(new byte[] { 0x5A }, Assert.Single(framer.Append(wire)));
    }

    [Theory] // These format characters are not Foundation whitespace.
    [Trait("Req", "PR-PROVACP-152")]
    [InlineData("\u180E")]
    [InlineData("\uFEFF")]
    [InlineData("\u200C")]
    public void Other_format_characters_are_not_silently_trimmed(string character)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        var wire = Encoding.UTF8.GetBytes($"Content-Length:{character}1\r\n\r\nZ");
        Assert.Empty(framer.Append(wire));
        Assert.Null(framer.Finish());
    }
}
