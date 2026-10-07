using Marrow.Core.Port.Skills.Detect;

namespace Marrow.Core.Tests.Port.Skills.Detect;

// Unit SK-10: S1, a skill's declared needs (ONDEMAND-PLAN.md 5.1): "The skill is used and one of its needs is neither installed nor found on this PC", from
// Claude Skill rows, Codex skill items, Copilot and Command Code skill rows and Marrow-side use_skill, using "the catalogue's per-skill needs". Skill
// names are folders, and two sets can share one (A1 and O1 both have skill-creator), so an unnamed set is decided by which one is installed.
[Trait("Spec", "DL-10")]
public sealed class SkillNeedTests
{
    private static Detection Used(string skill, string? itemId = null, DetectionContext? context = null) =>
        DetectFixtures.Run(context ?? DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("A1"))), new SkillUsed(skill, itemId));

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_used_skill_offers_each_per_skill_need_the_catalogue_lists_that_is_missing()
    {
        var detection = Used("webapp-testing");
        Assert.Equal(["T1", "T10"], DetectFixtures.Offered(detection));
        Assert.All(detection.Findings, f => Assert.Equal(DetectedSignal.SkillNeed, f.Signal));
        Assert.Equal(["T1", "T10"], detection.Card!.Requested);
        Assert.Equal(["T2", "T7"], DetectFixtures.Offered(Used("web-artifacts-builder")));
        Assert.Equal(["T1"], DetectFixtures.Offered(Used("mcp-builder")));
        Assert.Equal(["T1"], DetectFixtures.Offered(Used("slack-gif-creator")));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_need_that_is_installed_or_found_on_this_PC_is_not_offered()
    {
        var installed = DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("A1"), DetectFixtures.Has("T1")));
        Assert.Equal(["T10"], DetectFixtures.Offered(Used("webapp-testing", context: installed)));
        var found = DetectFixtures.State(DetectFixtures.Has("A1")).MergeFound(DetectFixtures.Catalogue, ["T10"], [new Marrow.Core.Port.Skills.State.FoundOnPc("T10", @"C:\chromium\chrome.exe")]);
        Assert.Equal(["T1"], DetectFixtures.Offered(Used("webapp-testing", context: DetectFixtures.Ctx(found))));
        var all = DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("A1"), DetectFixtures.Has("T1"), DetectFixtures.Has("T10")));
        var none = Used("webapp-testing", context: all);
        Assert.Empty(DetectFixtures.Offered(none));
        Assert.Null(none.Card);
        Assert.Equal([FindingVerdict.Suppressed, FindingVerdict.Suppressed], none.Findings.Select(f => f.Verdict));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_skill_with_no_listed_needs_or_not_in_the_catalogue_gives_nothing()
    {
        Assert.Empty(Used("frontend-design").Findings);
        Assert.Empty(Used("no-such-skill").Findings);
        Assert.Empty(Used("").Findings);
        Assert.Empty(Used("WEBAPP-TESTING").Findings);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void An_unnamed_set_is_taken_from_the_installed_sets_and_an_ambiguous_or_uninstalled_one_gives_nothing()
    {
        // skill-creator is in A1 (needs T1) and in O1 (no listed needs).
        Assert.Equal(["T1"], DetectFixtures.Offered(DetectFixtures.Run(DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("A1"))), new SkillUsed("skill-creator"))));
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("O1"))), new SkillUsed("skill-creator")).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("A1"), DetectFixtures.Has("O1"))), new SkillUsed("skill-creator")).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), new SkillUsed("skill-creator")).Findings);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx(), new SkillUsed("webapp-testing")).Findings);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_named_set_decides_even_when_two_are_installed_and_a_wrong_name_gives_nothing()
    {
        var both = DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("A1"), DetectFixtures.Has("O1")));
        Assert.Equal(["T1"], DetectFixtures.Offered(DetectFixtures.Run(both, new SkillUsed("skill-creator", "A1"))));
        Assert.Empty(DetectFixtures.Run(both, new SkillUsed("skill-creator", "O1")).Findings);
        Assert.Empty(DetectFixtures.Run(both, new SkillUsed("skill-creator", "A2")).Findings);
        Assert.Empty(DetectFixtures.Run(both, new SkillUsed("skill-creator", "Z9")).Findings);
        Assert.Equal(["T1"], DetectFixtures.Offered(DetectFixtures.Run(DetectFixtures.Ctx(), new SkillUsed("skill-creator", "A1"))));
    }
}
