using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Tests.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.State;

// Unit SK-7: "'update available' computed from the catalogue pin after an app update". ONDEMAND-PLAN.md 3.7: "After an app update, any installed item whose
// catalogue pin differs shows Update available with 'old -> new' and the download size ... A pin moves only with a Marrow release." The comparison is
// between the pin an item was installed at and the pin the (new) embedded catalogue carries; nothing here asks a vendor for 'latest'.
[Trait("Spec", "DL-7")]
public sealed class UpdateCheckTests
{
    private const string OldCommit = "8a1541c4a3ffa5a20a5a91de0dcf3f0bab1d1ef4";
    private const string NewCommit = "2222222222222222222222222222222222222222";

    private static SkillsCatalogue Catalogue => StateFixtures.Catalogue;

    private static AddonsState Installed(params (string Id, string Pin)[] items) =>
        AddonsState.Restore(items.Select(i => StateFixtures.Rec(i.Id, ItemState.Installed, i.Pin)).ToList(), [], [], []);

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void An_installed_item_at_the_catalogue_s_pin_stays_installed_and_nothing_is_listed()
    {
        var state = Installed(("A1", OldCommit), ("T1", "3.14.8"));
        var result = UpdateCheck.Compute(state, Catalogue);
        Assert.Equal(StateFixtures.Text(state), StateFixtures.Text(result.State));
        Assert.Empty(result.Updates);
        Assert.Empty(result.Orphans);
        Assert.Empty(result.Undecidable);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void After_an_app_update_an_item_whose_catalogue_pin_changed_becomes_update_available_with_old_new_and_size()
    {
        var newer = CatalogueFixtures.Change(CatalogueFixtures.Change(Catalogue, "A1", i => i with { Pin = NewCommit }), "T1", i => i with { Pin = "3.14.9" });
        var result = UpdateCheck.Compute(Installed(("A1", OldCommit), ("T1", "3.14.8"), ("T2", "24.21.0")), newer);
        Assert.Equal(StateFixtures.Rec("A1", ItemState.UpdateAvailable, OldCommit, NewCommit), result.State.Get("A1"));
        Assert.Equal(StateFixtures.Rec("T1", ItemState.UpdateAvailable, "3.14.8", "3.14.9"), result.State.Get("T1"));
        Assert.Equal(ItemState.Installed, result.State.Get("T2").State);
        Assert.Equal([new UpdateInfo("A1", OldCommit, NewCommit, 8.1), new UpdateInfo("T1", "3.14.8", "3.14.9", 12.6)], result.Updates);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void The_check_is_repeatable_and_the_offer_follows_the_catalogue_when_it_moves_again_or_goes_back()
    {
        var v2 = CatalogueFixtures.Change(Catalogue, "T1", i => i with { Pin = "3.14.9" });
        var first = UpdateCheck.Compute(Installed(("T1", "3.14.8")), v2);
        var again = UpdateCheck.Compute(first.State, v2);
        Assert.Equal(StateFixtures.Text(first.State), StateFixtures.Text(again.State));
        var v3 = CatalogueFixtures.Change(Catalogue, "T1", i => i with { Pin = "3.15.0" });
        Assert.Equal(StateFixtures.Rec("T1", ItemState.UpdateAvailable, "3.14.8", "3.15.0"), UpdateCheck.Compute(first.State, v3).State.Get("T1"));
        var back = UpdateCheck.Compute(first.State, Catalogue);
        Assert.Equal(StateFixtures.Rec("T1", ItemState.Installed, "3.14.8"), back.State.Get("T1"));
        Assert.Empty(back.Updates);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Items_that_are_not_installed_or_are_busy_damaged_or_being_removed_are_left_alone()
    {
        var newer = CatalogueFixtures.Change(Catalogue, "T1", i => i with { Pin = "3.14.9" });
        var newer2 = CatalogueFixtures.Change(CatalogueFixtures.Change(CatalogueFixtures.Change(CatalogueFixtures.Change(CatalogueFixtures.Change(newer,
            "T2", i => i with { Pin = "25.0.0" }), "T3", i => i with { Pin = "26.8.1" }), "T4", i => i with { Pin = "26.10.0" }), "T5", i => i with { Pin = "12.5.0" }), "T6", i => i with { Pin = "3.13" });
        var state = AddonsState.Restore(
        [
            StateFixtures.Rec("T2", ItemState.Downloading, "24.21.0", "24.22.0"),
            StateFixtures.Rec("T3", ItemState.Damaged, "26.2.6"),
            StateFixtures.Rec("T4", ItemState.Removing, "26.09.0"),
            StateFixtures.Rec("T5", ItemState.Failed, "12.4.2", "12.4.3", "boom"),
            StateFixtures.Rec("T6", ItemState.Checking, null, "3.12"),
        ], [], [], []);
        var result = UpdateCheck.Compute(state, newer2);
        Assert.Equal(StateFixtures.Text(state), StateFixtures.Text(result.State));
        Assert.Equal(ItemState.Available, result.State.Get("T1").State);
        Assert.Empty(result.Updates);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void An_item_the_catalogue_no_longer_has_is_an_orphan_and_one_without_a_pin_cannot_be_decided_and_neither_is_changed()
    {
        var state = Installed(("A6", "x"), ("T1", "3.14.8"), ("Z9", "1"));
        var result = UpdateCheck.Compute(state, Catalogue);
        Assert.Equal(["Z9"], result.Orphans);
        Assert.Equal(["A6"], result.Undecidable);
        Assert.Equal(StateFixtures.Text(state), StateFixtures.Text(result.State));
        var offered = AddonsState.Restore([StateFixtures.Rec("A6", ItemState.UpdateAvailable, "x", "y")], [], [], []);
        var kept = UpdateCheck.Compute(offered, Catalogue);
        Assert.Equal(["A6"], kept.Undecidable);
        Assert.Equal(StateFixtures.Rec("A6", ItemState.UpdateAvailable, "x", "y"), kept.State.Get("A6"));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void A_set_pinned_per_skill_is_compared_by_the_whole_list_of_its_pins()
    {
        var o3 = CatalogueFixtures.Item(Catalogue, "O3");
        Assert.Equal("doc=45d05d75363abf13f99d09e899d61e07b8010685;pdf=49f948faa9258a0c61caceaf225e179651397431;slides=e6afb0d74cc75d220df2faf3dd6c635c2dc6a108;spreadsheet=e6afb0d74cc75d220df2faf3dd6c635c2dc6a108",
            UpdateCheck.CataloguePin(o3));
        Assert.Equal("3.14.8", UpdateCheck.CataloguePin(CatalogueFixtures.Item(Catalogue, "T1")));
        Assert.Equal(OldCommit, UpdateCheck.CataloguePin(CatalogueFixtures.Item(Catalogue, "A1")));
        Assert.Null(UpdateCheck.CataloguePin(CatalogueFixtures.Item(Catalogue, "A6")));
        var partial = o3 with { Skills = o3.Skills.Select(s => s.Folder == "pdf" ? s with { Pin = null } : s).ToList() };
        Assert.Null(UpdateCheck.CataloguePin(partial));
        var current = UpdateCheck.CataloguePin(o3)!;
        Assert.Equal(ItemState.Installed, UpdateCheck.Compute(Installed(("O3", current)), Catalogue).State.Get("O3").State);
        var moved = CatalogueFixtures.Change(Catalogue, "O3", i => i with { Skills = i.Skills.Select(s => s.Folder == "doc" ? s with { Pin = NewCommit } : s).ToList() });
        var result = UpdateCheck.Compute(Installed(("O3", current)), moved);
        Assert.Equal(ItemState.UpdateAvailable, result.State.Get("O3").State);
        Assert.Equal(current, result.Updates.Single().FromPin);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void The_comparison_is_plain_text_equality_with_no_version_ordering()
    {
        // A pin that reads lower is still a different pin: the plan says a pin changes only with the owner's approval of its report (ONDEMAND-PLAN.md 2.1), never by order.
        var older = CatalogueFixtures.Change(Catalogue, "T1", i => i with { Pin = "3.14.7" });
        var result = UpdateCheck.Compute(Installed(("T1", "3.14.8")), older);
        Assert.Equal(StateFixtures.Rec("T1", ItemState.UpdateAvailable, "3.14.8", "3.14.7"), result.State.Get("T1"));
    }
}
