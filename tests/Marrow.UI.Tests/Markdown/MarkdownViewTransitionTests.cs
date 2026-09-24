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
public sealed class MarkdownViewTransitionTests
{
    private static SwiftTransition Transition(MarkdownBlock block)
    {
        var method = typeof(MarkdownView).GetMethod("Transition", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (SwiftTransition)method.Invoke(null, [block])!;
    }

    [Theory]
    [InlineData("hydra")]
    [InlineData("hydra-sent")]
    [InlineData("hydra-leaving")]
    [InlineData("HYDRA")]
    [Trait("Req", "PR-THEME-107")] // DroppyCode/UI/Markdown/MarkdownView.swift:51-56, :58-61
    public void A_delegation_code_block_leaves_with_no_transition(string info)
    {
        var transition = Transition(new MarkdownBlock.Code(info, "{}"));

        Assert.Equal(
            SwiftTransition.Asymmetric(insertion: SwiftTransition.SoftAppear, removal: SwiftTransition.Identity),
            transition);
    }

    [Fact]
    [Trait("Req", "PR-THEME-107")] // DroppyCode/UI/Markdown/MarkdownView.swift:51-56
    public void A_non_delegation_code_block_gets_softAppear()
    {
        Assert.Equal(SwiftTransition.SoftAppear, Transition(new MarkdownBlock.Code("swift", "1")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-107")] // DroppyCode/UI/Markdown/MarkdownView.swift:51-56
    public void A_heading_gets_softAppear()
    {
        Assert.Equal(SwiftTransition.SoftAppear, Transition(new MarkdownBlock.Heading(1, "T")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-108")] // DroppyCode/UI/Markdown/MarkdownView.swift:59-61
    public void IsDelegationInfo_is_true_for_exactly_the_three_spellings()
    {
        Assert.True(MarkdownView.IsDelegationInfo("hydra"));
        Assert.True(MarkdownView.IsDelegationInfo("hydra-sent"));
        Assert.True(MarkdownView.IsDelegationInfo("hydra-leaving"));
        Assert.False(MarkdownView.IsDelegationInfo("hydra-x"));
        Assert.False(MarkdownView.IsDelegationInfo("HYDRA")); // the Swift compares the already-lower-cased info string
        Assert.False(MarkdownView.IsDelegationInfo(""));
    }
}
