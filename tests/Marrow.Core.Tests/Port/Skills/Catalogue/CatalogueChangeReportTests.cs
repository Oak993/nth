using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-2 (DL-2 of porting/first-release/skills-ONDEMAND-PLAN.md section 7, "Catalogue builder ... and its offline check"): the change report. DL-2: "a fixture
// 'previous' and 'next' catalogue produce a change report listing every version, hash, size and licence-text change; a licence change in a non-open item is
// flagged 'asks the terms again'"; 3.7: "The catalogue builder writes a change report (versions, hashes, licence changes, sizes) for the owner"; 2.1: "a pin changes
// only with the owner's approval of its change report". This class holds the frame: what is compared, in what order, and what counts as changed. Pure and offline.
[Trait("Spec", "DL-2")]
public sealed class CatalogueChangeReportTests
{
    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Two_equal_catalogues_have_no_change_and_every_item_is_unchanged()
    {
        var report = CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), ChangeReportFixtures.Previous());
        Assert.Empty(report.Items);
        Assert.Empty(report.Added);
        Assert.Empty(report.Removed);
        Assert.Empty(report.Attention);
        Assert.Equal(["X1", "X2", "X3"], report.Unchanged);
        Assert.Equal(3, report.PreviousItems);
        Assert.Equal(3, report.NextItems);
        Assert.Equal("2026-10-04", report.PreviousReadOn);
        Assert.Equal("2026-10-04", report.NextReadOn);
        Assert.Empty(report.ChangesFor("X1"));
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_embedded_catalogue_against_itself_has_no_change_and_every_non_open_item_without_a_licence_hash_needs_attention()
    {
        var report = CatalogueChangeReport.Compare(CatalogueFixtures.Real, CatalogueFixtures.Real);
        Assert.Empty(report.Items);
        Assert.Equal(CatalogueFixtures.Real.Items.Count, report.Unchanged.Count);
        // ONDEMAND-PLAN.md 2.5: a non-open item shows its terms first; with no hash of the licence file in the catalogue they cannot be accepted (ED-295).
        var expected = CatalogueFixtures.Real.Items.Where(i => i.TermsClass != TermsClass.Open && Marrow.Core.Port.Skills.State.ItemTerms.LicenceHash(i) is null).Select(i => i.Id);
        Assert.Equal(expected.OrderBy(i => i, StringComparer.Ordinal), report.Attention.Select(a => a.ItemId).OrderBy(i => i, StringComparer.Ordinal));
        Assert.All(report.Attention, a => Assert.Equal(ChangeFlag.LicenceTextNotRecorded, a.Flag));
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void An_added_and_a_removed_item_are_listed_once_each_with_no_field_by_field_noise()
    {
        var previous = ChangeReportFixtures.Previous();
        var next = CatalogueFixtures.Add(CatalogueFixtures.Without(previous, "X2"), CatalogueFixtures.PinnedTool("X9", "other"));
        var report = CatalogueChangeReport.Compare(previous, next);
        Assert.Equal(["X9"], report.Added);
        Assert.Equal(["X2"], report.Removed);
        Assert.Equal(["X1", "X3"], report.Unchanged);
        var added = Assert.Single(report.ChangesFor("X9"));
        Assert.Equal(ChangeCategory.Item, added.Category);
        Assert.Equal("added (tool)", added.Message);
        var removed = Assert.Single(report.ChangesFor("X2"));
        Assert.Equal(ChangeCategory.Item, removed.Category);
        Assert.Equal("removed (was pyset)", removed.Message);
        Assert.Equal(["X2", "X9"], report.Items.Select(i => i.ItemId));
        Assert.Equal(3, report.PreviousItems);
        Assert.Equal(3, report.NextItems);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_change_of_kind_is_listed()
    {
        var next = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X3", i => i with { Kind = ItemKind.Plugin });
        var change = Assert.Single(CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), next).ChangesFor("X3"));
        Assert.Equal(ChangeCategory.Item, change.Category);
        Assert.Equal("kind", change.Field);
        Assert.Equal("skillset", change.Old);
        Assert.Equal("plugin", change.New);
        Assert.Equal("kind: skillset -> plugin", change.Message);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Items_are_ordered_by_id_and_an_items_changes_by_category_then_in_a_fixed_order_whatever_the_order_in_the_files()
    {
        var previous = ChangeReportFixtures.Previous();
        var next = ChangeReportFixtures.Mutate(ChangeReportFixtures.Mutate(previous, "X3", i => i with { Title = "New title", Pin = ChangeReportFixtures.Commit2, ApproxSizeMb = 0.9 }),
            "X1", i => i with { Pin = "1.1.0" });
        var shuffled = next with { Items = next.Items.Reverse().ToList() };
        var report = CatalogueChangeReport.Compare(previous, shuffled);
        Assert.Equal(["X1", "X3"], report.Items.Select(i => i.ItemId));
        Assert.Equal(["version: pin: 0123456789ab -> 89abcdef0123", "size: approxSizeMb: 0.5 -> 0.9", "text: title: Fixture skills -> New title"], ChangeReportFixtures.Lines(report, "X3"));
        Assert.Equal(CatalogueChangeReport.Compare(previous, next).Items.SelectMany(i => i.Changes), report.Items.SelectMany(i => i.Changes));
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_report_carries_the_two_read_on_dates_and_the_item_counts()
    {
        var previous = ChangeReportFixtures.Previous();
        var next = CatalogueFixtures.Add(previous with { ReadOn = "2026-11-01" }, CatalogueFixtures.PinnedTool("X9", "other"));
        var report = CatalogueChangeReport.Compare(previous, next);
        Assert.Equal("2026-10-04", report.PreviousReadOn);
        Assert.Equal("2026-11-01", report.NextReadOn);
        Assert.Equal(3, report.PreviousItems);
        Assert.Equal(4, report.NextItems);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_null_catalogue_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => CatalogueChangeReport.Compare(null!, ChangeReportFixtures.Previous()));
        Assert.Throws<ArgumentNullException>(() => CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), null!));
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_pin_state_and_count_state_changes_are_listed_as_state()
    {
        var previous = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X1", i => i with { PinState = PinState.Unpinned, Pin = null });
        var next = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X3", i => i with { CountState = CountState.Added });
        var report = CatalogueChangeReport.Compare(previous, next);
        Assert.Equal(["state: pinState: unpinned -> pinned"], report.ChangesFor("X1").Where(c => c.Category == ChangeCategory.State).Select(c => c.Category.ToString().ToLowerInvariant() + ": " + c.Message));
        var counted = CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), next);
        Assert.Equal(["state: countState: plan -> added"], ChangeReportFixtures.Lines(counted, "X3"));
    }
}
