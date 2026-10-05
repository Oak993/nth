using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Tests.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.State;

// Unit SK-7: "dependents of an item (from SK-1's needs graph)". ONDEMAND-PLAN.md 2.4: "The detail of an item shows what it needs and what uses it";
// 3.8: "If other installed items need it, the confirmation names them". Expected lists are worked out by hand from the needs columns of 2.1 and 2.2
// as SK-1's SkillsCatalogueNeedsTests transcribes them, not read back from the code under test.
[Trait("Spec", "DL-7")]
public sealed class ItemDependentsTests
{
    private static string[] Direct(string id) => ItemDependents.Direct(StateFixtures.Catalogue, id).ToArray();

    private static string[] Transitive(string id) => ItemDependents.Transitive(StateFixtures.Catalogue, id).ToArray();

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void The_direct_dependents_of_each_tool_are_the_items_whose_needs_name_it()
    {
        Assert.Equal(["A1", "A2", "A4", "A5", "O1", "O2", "O3", "P1", "P2", "P3", "P4", "P5", "T1p"], Direct("T1"));
        Assert.Equal(["P1", "P2", "P3", "P4", "P5"], Direct("T1p"));
        Assert.Equal(["A1", "A2", "O1", "O2", "O3", "O4", "P6"], Direct("T2"));
        Assert.Equal(["A2", "O3"], Direct("T3"));
        Assert.Equal(["A2", "O3"], Direct("T4"));
        Assert.Equal(["A2"], Direct("T5"));
        Assert.Equal(["A2"], Direct("T6"));
        Assert.Equal(["A1", "O2"], Direct("T7"));
        Assert.Equal(["O2"], Direct("T8"));
        Assert.Equal(["O2"], Direct("T9"));
        Assert.Equal(["A1"], Direct("T10"));
        Assert.Equal(["A2", "O3"], Direct("P1"));
        Assert.Equal(["A2", "O3"], Direct("P2"));
        Assert.Equal(["A2", "O3"], Direct("P6"));
        Assert.Empty(Direct("P3"));
        Assert.Empty(Direct("A1"));
        Assert.Empty(Direct("A3"));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void The_transitive_dependents_follow_the_graph_all_the_way_and_list_each_item_once()
    {
        Assert.Equal(["A1", "A2", "A4", "A5", "O1", "O2", "O3", "P1", "P2", "P3", "P4", "P5", "T1p"], Transitive("T1"));
        Assert.Equal(["A2", "O3", "P1", "P2", "P3", "P4", "P5"], Transitive("T1p"));
        Assert.Equal(["A2", "O3"], Transitive("P1"));
        Assert.Equal(["A1", "A2", "O1", "O2", "O3", "O4", "P6"], Transitive("T2"));
        Assert.Empty(Transitive("A2"));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void The_two_directions_agree_for_every_pair_in_the_real_catalogue_and_with_SK_1_s_closure()
    {
        var catalogue = StateFixtures.Catalogue;
        foreach (var a in catalogue.Items)
        {
            var direct = ItemDependents.Direct(catalogue, a.Id);
            Assert.Equal(catalogue.Items.Where(b => b.Needs.Contains(a.Id)).Select(b => b.Id).OrderBy(i => i, StringComparer.Ordinal), direct);
            var transitive = ItemDependents.Transitive(catalogue, a.Id);
            foreach (var b in catalogue.Items)
                Assert.Equal(SkillsCatalogueGraph.Closure(catalogue, b.Id).Contains(a.Id), transitive.Contains(b.Id));
            Assert.DoesNotContain(a.Id, transitive);
        }
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void The_installed_dependents_are_those_with_an_installed_version_that_is_not_being_removed()
    {
        var state = AddonsState.Restore(
        [
            StateFixtures.Rec("A1", ItemState.Installed, "pin"),
            StateFixtures.Rec("A2", ItemState.UpdateAvailable, "pin", "new"),
            StateFixtures.Rec("A4", ItemState.Damaged, "pin"),
            StateFixtures.Rec("A5", ItemState.Removing, "pin"),
            StateFixtures.Rec("O1", ItemState.Downloading, null, "pin"),
            StateFixtures.Rec("O2", ItemState.Downloading, "pin", "new"),
            StateFixtures.Rec("O3", ItemState.Failed, null, "pin", "boom"),
            StateFixtures.Rec("P1", ItemState.Failed, "pin", "new", "boom"),
        ], [], [], []);
        Assert.Equal(["A1", "A2", "A4", "O2", "P1"], ItemDependents.Installed(StateFixtures.Catalogue, state, "T1"));
        Assert.Empty(ItemDependents.Installed(StateFixtures.Catalogue, AddonsState.Empty, "T1"));
        Assert.Equal(["A1"], ItemDependents.Installed(StateFixtures.Catalogue, state, "T10"));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void An_unknown_id_is_refused_and_a_cycle_does_not_hang_it()
    {
        Assert.Throws<ArgumentException>(() => ItemDependents.Direct(StateFixtures.Catalogue, "T99"));
        Assert.Throws<ArgumentException>(() => ItemDependents.Transitive(StateFixtures.Catalogue, ""));
        Assert.Throws<ArgumentException>(() => ItemDependents.Installed(StateFixtures.Catalogue, AddonsState.Empty, "T99"));
        var cyclic = CatalogueFixtures.Change(CatalogueFixtures.Change(StateFixtures.Catalogue, "T1", i => i with { Needs = ["T1p"] }), "T1p", i => i with { Needs = ["T1"] });
        Assert.Contains("T1p", ItemDependents.Transitive(cyclic, "T1"));
        Assert.DoesNotContain("T1", ItemDependents.Transitive(cyclic, "T1"));
    }
}
