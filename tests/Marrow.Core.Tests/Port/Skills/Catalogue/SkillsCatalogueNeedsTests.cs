using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-1: needs. ONDEMAND-PLAN.md 2.3 "needs: other catalogue ids (T1 for P1, P1 + T3 + T4 for O3, and so on); skill sets list needs per skill"
// and the Needs and Needed-by columns of 2.1 and 2.2; DL-1: "needs form no cycle and name existing ids".
[Trait("Spec", "DL-1")]
public sealed class SkillsCatalogueNeedsTests
{
    private static readonly Dictionary<string, string[]> PlanNeeds = new()
    {
        // 2.1 Needs column, mapped to ids by 2.2's Needed-by column (item-level union).
        ["A1"] = ["T1", "T2", "T7", "T10"],
        ["A2"] = ["T1", "T2", "T3", "T4", "T5", "T6", "P1", "P2", "P6"],
        ["A4"] = ["T1"],
        ["A5"] = ["T1"],
        ["O1"] = ["T1", "T2"],
        ["O2"] = ["T1", "T2", "T7", "T8", "T9"],
        ["O3"] = ["T1", "T2", "T3", "T4", "P1", "P2", "P6"],
        ["O4"] = ["T2"],
        // 2.2: T1p installs the package sets; the package sets run on Python (P6 on Node).
        ["T1p"] = ["T1"],
        ["P1"] = ["T1", "T1p"], ["P2"] = ["T1", "T1p"], ["P3"] = ["T1", "T1p"], ["P4"] = ["T1", "T1p"], ["P5"] = ["T1", "T1p"],
        ["P6"] = ["T2"],
    };

    private static readonly string[] NoNeeds = ["A3", "A6", "O5", "O6", "T1", "T2", "T3", "T4", "T5", "T6", "T7", "T8", "T9", "T10"];

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Each_item_needs_what_the_plan_s_needs_columns_say()
    {
        foreach (var (id, needs) in PlanNeeds)
            Assert.Equal(needs.OrderBy(n => n, StringComparer.Ordinal), CatalogueFixtures.Item(CatalogueFixtures.Real, id).Needs.OrderBy(n => n, StringComparer.Ordinal));
        foreach (var id in NoNeeds) Assert.Empty(CatalogueFixtures.Item(CatalogueFixtures.Real, id).Needs);
        Assert.Equal(PlanNeeds.Count + NoNeeds.Length, CatalogueFixtures.Real.Items.Count);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A1_lists_per_skill_needs_as_the_plan_does_and_the_item_needs_cover_them()
    {
        var a1 = CatalogueFixtures.Item(CatalogueFixtures.Real, "A1");
        string[] Of(string folder) => a1.Skills.Single(s => s.Folder == folder).Needs.ToArray();
        Assert.Equal(["T1"], Of("skill-creator"));
        Assert.Equal(["T1"], Of("slack-gif-creator"));
        Assert.Equal(["T1", "T10"], Of("webapp-testing"));
        Assert.Equal(["T1"], Of("mcp-builder"));
        Assert.Equal(["T2", "T7"], Of("web-artifacts-builder"));
        Assert.Empty(Of("frontend-design"));
        foreach (var item in CatalogueFixtures.Real.Items) Assert.Empty(item.Skills.SelectMany(s => s.Needs).Except(item.Needs));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_need_names_an_existing_item_and_the_real_graph_has_no_cycle()
    {
        var ids = CatalogueFixtures.Real.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(CatalogueFixtures.Real.Items, i => Assert.All(i.Needs.Concat(i.Skills.SelectMany(s => s.Needs)), n => Assert.Contains(n, ids)));
        Assert.DoesNotContain(CatalogueGate.Check(CatalogueFixtures.Real).Violations, v => v.Rule is CatalogueRules.NeedsExist or CatalogueRules.NeedsCycle or CatalogueRules.NeedsKind);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_need_that_names_nothing_is_refused()
    {
        var bad = CatalogueFixtures.Change(CatalogueFixtures.Real, "A1", i => i with { Needs = [.. i.Needs, "T99"] });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(bad), CatalogueRules.NeedsExist, "A1"));
        var badSkill = CatalogueFixtures.Change(CatalogueFixtures.Real, "A1", i => i with { Skills = i.Skills.Select(s => s.Folder == "mcp-builder" ? s with { Needs = ["T99"] } : s).ToList() });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(badSkill), CatalogueRules.NeedsExist, "A1"));
        var emptyName = CatalogueFixtures.Change(CatalogueFixtures.Real, "A1", i => i with { Needs = [.. i.Needs, ""] });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(emptyName), CatalogueRules.NeedsExist, "A1"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_per_skill_need_the_item_does_not_list_is_refused()
    {
        var bad = CatalogueFixtures.Change(CatalogueFixtures.Real, "A1", i => i with { Needs = i.Needs.Where(n => n != "T10").ToList() });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(bad), CatalogueRules.NeedsExist, "A1"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_self_need_a_two_step_cycle_and_a_long_cycle_are_each_refused()
    {
        var self = CatalogueFixtures.Change(CatalogueFixtures.Real, "T2", i => i with { Needs = ["T2"] });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(self), CatalogueRules.NeedsCycle));
        var two = CatalogueFixtures.Change(CatalogueFixtures.Real, "T1", i => i with { Needs = ["T1p"] });
        var twoReport = CatalogueGate.Check(two);
        Assert.True(CatalogueFixtures.Fires(twoReport, CatalogueRules.NeedsCycle));
        Assert.Contains(twoReport.Violations, v => v.Rule == CatalogueRules.NeedsCycle && v.Message.Contains("T1", StringComparison.Ordinal) && v.Message.Contains("T1p", StringComparison.Ordinal));
        var longer = CatalogueFixtures.Change(CatalogueFixtures.Real, "T1", i => i with { Needs = ["P1"] });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(longer), CatalogueRules.NeedsCycle));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_diamond_is_not_a_cycle()
    {
        // A2 and O3 both reach T1 through P1 and directly; the real graph already has that shape and passes.
        var fixture = CatalogueFixtures.Pinned();
        var diamond = CatalogueFixtures.Change(fixture, "X3", i => i with { Needs = ["X1", "X2"] });
        Assert.False(CatalogueFixtures.Fires(CatalogueGate.Check(diamond), CatalogueRules.NeedsCycle));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Tools_and_package_sets_never_need_a_skill_set_or_plugin()
    {
        var bad = CatalogueFixtures.Change(CatalogueFixtures.Real, "T5", i => i with { Needs = ["A1"] });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(bad), CatalogueRules.NeedsKind, "T5"));
        var badSet = CatalogueFixtures.Change(CatalogueFixtures.Real, "P1", i => i with { Needs = ["O2"] });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(badSet), CatalogueRules.NeedsKind, "P1"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_closure_of_A2_is_the_ten_items_whose_sizes_the_confirmation_will_add_up()
    {
        var closure = SkillsCatalogueGraph.Closure(CatalogueFixtures.Real, "A2");
        Assert.Equal(["P1", "P2", "P6", "T1", "T1p", "T2", "T3", "T4", "T5", "T6"], closure.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Empty(SkillsCatalogueGraph.Closure(CatalogueFixtures.Real, "T1"));
        Assert.Throws<ArgumentException>(() => SkillsCatalogueGraph.Closure(CatalogueFixtures.Real, "T99"));
    }
}
