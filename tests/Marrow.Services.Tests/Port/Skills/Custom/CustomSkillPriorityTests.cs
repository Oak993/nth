using System.Text;
using Marrow.Services.Port.Skills.Custom;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Tests.Port.Skills.Generations;

namespace Marrow.Services.Tests.Port.Skills.Custom;

[Trait("Feature", "SK-2")]
[Trait("Rule", "R-TEST-4")]
public sealed class CustomSkillPriorityTests
{
    [Theory]
    [InlineData("pdf", 8000, "pdf")]
    [InlineData("mine", 60, "mine")]
    [InlineData("mine", 8000, "mine,pdf")]
    public async Task Explicit_custom_skills_win_catalogue_names_budget_and_listing_order(string name, int characters, string names)
    {
        var fake = new CustomSourceFake { Files = new() { ["SKILL.md"] = Encoding.UTF8.GetBytes("---\nname: " + name + "\ndescription: O.\n---\nOWN BODY") } };
        var own = fake.Open();
        var candidate = Assert.Single(await own.Check(new(CustomAddKind.Skills, CustomAddSource.Folder, "fixture", "", ""), CancellationToken.None));
        await own.Add(candidate, new CustomSourceProgress(), CancellationToken.None);
        using var fixture = new GenerationFixture();
        var item = fixture.Add("A1", "pdf");
        fixture.Open();
        using var service = new SkillGenerations(fixture.Paths, fixture.Store!, fixture.Protection, _ => [], own, own);
        using var generation = service.Start("native", "fixture", [item], fixture.Switches((item, true)), new(characters));
        Assert.Equal(names.Split(','), generation.Skills.Select(s => s.Name));
        Assert.Contains("OWN BODY", generation.Invoke(name), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_only_custom_skills_stay_out_of_automatic_disclosure_and_remain_invocable()
    {
        var fake = new CustomSourceFake { Files = new() { ["SKILL.md"] = Encoding.UTF8.GetBytes("---\nname: quiet\ndescription: O.\ndisable-model-invocation: true\n---\nOWN BODY") } };
        var own = fake.Open();
        var candidate = Assert.Single(await own.Check(new(CustomAddKind.Skills, CustomAddSource.Folder, "fixture", "", ""), CancellationToken.None));
        await own.Add(candidate, new CustomSourceProgress(), CancellationToken.None);
        using var fixture = new GenerationFixture();
        fixture.Open();
        using var service = new SkillGenerations(fixture.Paths, fixture.Store!, fixture.Protection, _ => [], own, own);
        using var generation = service.Start("native", "fixture", [], fixture.Switches(), fixture.Budget);
        Assert.True(Assert.Single(generation.Skills).ExplicitOnly);
        Assert.Empty(generation.Catalogue);
        Assert.Contains("OWN BODY", generation.Invoke("quiet"), StringComparison.Ordinal);
    }
}
