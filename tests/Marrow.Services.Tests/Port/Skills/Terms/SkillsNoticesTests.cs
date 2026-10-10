// PORT-ONLY: SK-13 / DL-15. The words the terms step and the Settings footer use (skills-ONDEMAND-PLAN.md 2.5, skills-PLAN.md 3.1 "Footer").
using Marrow.Core.Port.Skills.Terms;

namespace Marrow.Services.Tests.Port.Skills.Terms;

[Trait("Feature", "SK-13")]
[Trait("Spec", "DL-15")]
public sealed class SkillsNoticesTests
{
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void The_Settings_footer_carries_the_not_affiliated_line_word_for_word()
    {
        Assert.Equal(
            "nth is not affiliated with or endorsed by Anthropic or OpenAI. Claude and Anthropic are trademarks of Anthropic, PBC. " +
            "ChatGPT, Codex and OpenAI are trademarks of OpenAI. Other names belong to their owners.",
            SkillsNotices.Footer);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void The_terms_words_are_the_plans()
    {
        Assert.Equal("I have read these terms", SkillsNotices.ReadControl);
        Assert.Equal("Under Anthropic's terms", SkillsNotices.UnderTerms("Anthropic"));
        Assert.Equal("Under OpenAI's terms", SkillsNotices.UnderTerms("OpenAI"));
    }
}
