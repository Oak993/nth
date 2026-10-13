using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-5 (DESIGN.md rev 5, 3.4; ED-NEW-dextra-as5-8): the reviewer's verdict is the last VERDICT line of its answer.
// Only a plain PASS passes; FAIL carries its reasons; anything else, or no verdict at all, fails (fail closed). A Fail goes
// back to the lead at most twice; the third Fail fails the mission.
[Trait("Decision", "ED-NEW-dextra-as5-8")]
[Trait("Feature", "AS-5")]
public sealed class ReviewVerdictTests
{
    [Theory]
    [InlineData("All checks hold.\nVERDICT: PASS")]
    [InlineData("VERDICT: PASS.")]
    [InlineData("verdict: pass")]
    [InlineData("**VERDICT: PASS**")]
    [InlineData("> VERDICT: PASS\r\n")]
    [InlineData("VERDICT: FAIL the tests do not run\nI checked again.\nVERDICT: PASS")]
    public void A_plain_pass_passes(string answer)
    {
        var verdict = ReviewVerdict.Parse(answer);
        Assert.True(verdict.Passed);
        Assert.Equal("", verdict.Reasons);
    }

    [Fact]
    public void A_fail_carries_its_reasons_and_the_lines_after_it()
    {
        var verdict = ReviewVerdict.Parse("I read src/login.cs.\nVERDICT: FAIL: the timeout is still 5 s\n- LoginTests do not cover it");
        Assert.False(verdict.Passed);
        Assert.Equal("the timeout is still 5 s\n- LoginTests do not cover it", verdict.Reasons);
    }

    [Fact]
    public void The_last_verdict_line_counts()
    {
        var verdict = ReviewVerdict.Parse("VERDICT: PASS\nOn second look:\nVERDICT: FAIL - the change is not in the branch");
        Assert.False(verdict.Passed);
        Assert.Equal("the change is not in the branch", verdict.Reasons);
    }

    [Theory]
    [InlineData(null, "The reviewer gave no answer.")]
    [InlineData("   ", "The reviewer gave no answer.")]
    [InlineData("Looks good to me.", "The reviewer gave no verdict line.")]
    [InlineData("VERDICT: PASSED", "The reviewer gave no verdict line.")]
    [InlineData("VERDICT: LGTM", "The reviewer gave no verdict line.")]
    [InlineData("VERDICT: FAIL", "The reviewer failed it without reasons.")]
    [InlineData("VERDICT: PASS, but the tests do not build", "The reviewer's verdict was not a plain PASS: VERDICT: PASS, but the tests do not build")]
    [InlineData("VERDICT: PASS\nexcept the docs", "The reviewer's verdict was not a plain PASS: VERDICT: PASS")]
    public void Anything_but_a_plain_pass_fails(string? answer, string reasons)
    {
        var verdict = ReviewVerdict.Parse(answer);
        Assert.False(verdict.Passed);
        Assert.Equal(reasons, verdict.Reasons);
    }

    [Fact]
    public void Long_reasons_are_clipped()
    {
        var verdict = ReviewVerdict.Parse("VERDICT: FAIL " + new string('x', 5_000));
        Assert.Equal(4_001, verdict.Reasons.Length);
        Assert.EndsWith("…", verdict.Reasons, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_fails_redirect_the_lead_and_the_third_fails_the_mission()
    {
        Assert.Equal(2, ReviewVerdict.Redirects);
        Assert.False(ReviewVerdict.FailsMission(0));
        Assert.False(ReviewVerdict.FailsMission(1));
        Assert.True(ReviewVerdict.FailsMission(2));
    }

    [Fact]
    public void The_instruction_names_both_verdicts()
    {
        Assert.Contains("VERDICT: PASS", ReviewVerdict.Instruction, StringComparison.Ordinal);
        Assert.Contains("VERDICT: FAIL", ReviewVerdict.Instruction, StringComparison.Ordinal);
    }
}
