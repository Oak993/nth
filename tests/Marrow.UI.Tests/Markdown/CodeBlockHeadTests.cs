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
public sealed class CodeBlockHeadTests
{
    private static string Head(string code, int lines)
    {
        var method = typeof(CodeBlock).GetMethod("Head", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [code, lines])!;
    }

    [Fact]
    [Trait("Req", "PR-THEME-118")] // DroppyCode/UI/Markdown/MarkdownView.swift:1324-1335
    public void Head_returns_the_text_up_to_but_not_including_the_Nth_line_feed()
    {
        var code = "line1\nline2\nline3\nline4\nline5";
        Assert.Equal("line1\nline2\nline3", Head(code, 3));
    }

    [Fact]
    [Trait("Req", "PR-THEME-118")] // DroppyCode/UI/Markdown/MarkdownView.swift:1324-1335: fewer line feeds than requested returns the whole text
    public void Head_returns_the_whole_text_when_there_are_fewer_lines_than_requested()
    {
        var code = "only\ntwo";
        Assert.Equal(code, Head(code, 5));
    }

    [Fact]
    [Trait("Req", "PR-THEME-118")] // DroppyCode/UI/Markdown/MarkdownView.swift:1281 lineCount = 1 + line feeds
    public void A_block_of_exactly_120_lines_is_not_truncated()
    {
        var code = string.Join("\n", Enumerable.Range(0, 120).Select(i => "l" + i));
        Assert.Equal(120, 1 + code.Count(c => c == '\n'));
        Assert.Equal(code, Head(code, 120));
    }
}
