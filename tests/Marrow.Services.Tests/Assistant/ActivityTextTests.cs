using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: text from the audit log is made safe to show (DESIGN.md 12.5; ED-NEW-dextra-as21-2). An agent chose the file
// path or the command a line holds, so a control character or an invisible mark that reorders text is shown as its escape.
[Trait("Decision", "ED-NEW-dextra-as21-2")]
[Trait("Feature", "AS-21")]
public sealed class ActivityTextTests
{
    [Fact]
    public void Ordinary_text_is_returned_as_the_same_string()
    {
        const string plain = "E:/work/repo — café 😀 日本 Ошибка\tand a line\r\nbreak";

        Assert.Same(plain, ActivityText.Safe(plain));
        Assert.Equal("", ActivityText.Safe(null));
        Assert.Equal("", ActivityText.Safe(""));
    }

    [Theory]
    [InlineData("evil\u202Efdp.exe", "evil\\u202Efdp.exe")] // right-to-left override
    [InlineData("a\u2066b\u2069c", "a\\u2066b\\u2069c")] // isolates
    [InlineData("zero\u200Bwidth\u200Djoin", "zero\\u200Bwidth\\u200Djoin")]
    [InlineData("\uFEFFbom", "\\uFEFFbom")]
    [InlineData("bell\u0007", "bell\\u0007")]
    [InlineData("esc\u001b[31m", "esc\\u001B[31m")]
    [InlineData("nul\0end", "nul\\u0000end")]
    [InlineData("c1\u0085x", "c1\\u0085x")]
    [InlineData("line\u2028sep\u2029para", "line\\u2028sep\\u2029para")]
    [InlineData("del\u007Fx", "del\\u007Fx")]
    [InlineData("private\uE000use", "private\\uE000use")]
    public void A_character_that_hides_or_reorders_is_shown_as_its_escape(string text, string expected) =>
        Assert.Equal(expected, ActivityText.Safe(text));

    [Fact]
    public void A_lone_surrogate_is_shown_as_an_escape_and_a_pair_is_kept()
    {
        Assert.Equal("a\\uD800b", ActivityText.Safe("a\uD800b"));
        Assert.Equal("a\\uDC00b", ActivityText.Safe("a\uDC00b"));
        Assert.Equal("a😀b", ActivityText.Safe("a😀b"));
        Assert.Equal("\\uD83D", ActivityText.Safe("\uD83D"));
    }

    [Fact]
    public void One_line_turns_breaks_and_tabs_into_spaces_and_cuts_the_text_with_an_ellipsis()
    {
        Assert.Equal("line 1 line 2 end", ActivityText.OneLine("line 1\nline 2\tend"));
        Assert.Equal("a  b", ActivityText.OneLine("a\r\nb"));
        Assert.Equal("", ActivityText.OneLine(null));
        Assert.Equal("abcde", ActivityText.OneLine("abcde", 5));
        Assert.Equal("abcde…", ActivityText.OneLine("abcdefgh", 5));
    }
}
