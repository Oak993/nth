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
public sealed class MarkdownBlockViewHeadingFontTests
{
    // SwiftFont is a sealed record (structural equality), so HeadingFont's exact result is compared directly
    // against ChatFont.Chat, rather than reflecting into SwiftFont's own private Style/weight fields.
    private static SwiftFont HeadingFont(int level)
    {
        var method = typeof(MarkdownBlockView).GetMethod("HeadingFont", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (SwiftFont)method.Invoke(null, [level])!;
    }

    [Fact]
    [Trait("Req", "PR-THEME-109")] // DroppyCode/UI/Markdown/MarkdownView.swift:376-383
    public void Level_1_is_title2_semibold()
    {
        Assert.Equal(Marrow.UI.Common.ChatFont.Chat(SwiftFont.TextStyle.Title2, SwiftFont.Weight.Semibold), HeadingFont(1));
    }

    [Fact]
    [Trait("Req", "PR-THEME-109")] // DroppyCode/UI/Markdown/MarkdownView.swift:376-383
    public void Level_2_is_title3_semibold()
    {
        Assert.Equal(Marrow.UI.Common.ChatFont.Chat(SwiftFont.TextStyle.Title3, SwiftFont.Weight.Semibold), HeadingFont(2));
    }

    [Fact]
    [Trait("Req", "PR-THEME-109")] // DroppyCode/UI/Markdown/MarkdownView.swift:376-383; ED-186 (1) and (3)
    public void Level_3_is_headline_through_ChatFont_own_semibold_default()
    {
        Assert.Equal(Marrow.UI.Common.ChatFont.Chat(SwiftFont.TextStyle.Headline), HeadingFont(3));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [Trait("Req", "PR-THEME-109")] // DroppyCode/UI/Markdown/MarkdownView.swift:376-383
    public void Levels_4_to_6_are_subheadline_semibold(int level)
    {
        Assert.Equal(Marrow.UI.Common.ChatFont.Chat(SwiftFont.TextStyle.Subheadline, SwiftFont.Weight.Semibold), HeadingFont(level));
    }
}
