// SK-11f / WU-12 (skills-PLAN.md 3.3 native row, 2.7; ONDEMAND-PLAN.md 4.1) and the OD-0099 progressive-disclosure amendment: the catalogue block lists the
// installed, enabled skills by name and description in every session, and a skill's text is never in it.
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-11f")]
public sealed class NativeSkillCatalogueTests
{
    private const string Header = "Skills (call use_skill with a skill's name to read its instructions):";

    [Fact, Trait("Rule", "R-TEST-4")]
    public void The_block_is_one_header_line_and_one_name_colon_description_line_per_skill_and_never_a_body()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        using var generation = kit.Start();
        var block = new NativeSkillTools(generation).CatalogueBlock();
        Assert.Equal(Header + "\npdf: Fixture description", block);
        Assert.DoesNotContain("BODY SENTINEL", block, StringComparison.Ordinal);
        Assert.DoesNotContain("REFERENCE SENTINEL", block, StringComparison.Ordinal);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void Only_skills_that_are_installed_and_switched_on_are_listed()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        kit.Add("O3", "doc", on: false);
        kit.Add("O3b", "xlsx");
        kit.Add("A9", "pptx", installed: false);
        using var generation = kit.Start();
        var lines = new NativeSkillTools(generation).CatalogueBlock()!.Split('\n');
        Assert.Equal([Header, "pdf: Fixture description", "xlsx: Fixture description"], lines);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void Explicit_only_skills_are_not_in_the_block()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        kit.Add("A2", "screenshot");
        kit.Add("A3", "yeet");
        kit.Add("A4", "review-agent");
        using var generation = kit.Start();
        Assert.Equal(4, generation.Skills.Count);
        Assert.Equal(Header + "\npdf: Fixture description", new NativeSkillTools(generation).CatalogueBlock());
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void With_nothing_to_list_there_is_no_block_at_all()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf", on: false);
        using var generation = kit.Start();
        Assert.Empty(generation.Skills);
        Assert.Null(new NativeSkillTools(generation).CatalogueBlock());
        Assert.Null(new NativeSkillTools(null).CatalogueBlock());
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void Fifty_skills_and_the_users_own_stay_inside_the_budget_and_the_extra_ones_are_left_out()
    {
        using var kit = new NativeSkillsKit { Budget = new GenerationBudget(2000) };
        for (var i = 0; i < 50; i++) kit.Add($"S{i:D2}", $"skill-{i:D2}");
        var own = new[] { new ExistingSkill("own-one", new string('a', 200)), new ExistingSkill("own-two", new string('b', 200)) };
        using var generation = kit.Start(existing: own);
        var room = kit.Budget.Room(own);
        Assert.Equal(1000, room);
        var lines = new NativeSkillTools(generation).CatalogueBlock()!.Split('\n').Skip(1).ToArray();
        Assert.Equal(generation.Skills.Count, lines.Length);
        Assert.InRange(lines.Length, 1, 49);
        Assert.NotEmpty(generation.LeftOut);
        Assert.True(lines.Sum(l => l.Length + 1) <= room, "the listed lines must fit the room the budget leaves Nth");
        Assert.Equal(50, generation.Skills.Count + generation.LeftOut.Count);
    }
}
