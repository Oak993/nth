using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-2: licence changes and the terms flag. DL-2: "a licence change in a non-open item is flagged 'asks the terms again'". ONDEMAND-PLAN.md 2.5: "Items whose
// termsClass is not open (A2, O5) show the licence text before the download ... The acceptance is stored with the date and the licence file's hash, so a later pin with
// changed terms asks again"; 3.7: "A changed licence text in a non-open item asks for the terms again before updating". The licence text is the SHA-256 the catalogue
// holds for the item's licence file (SK-7's ItemTerms.LicenceHash), so a changed text is a changed hash. Cases the plan leaves open are in ED-298.
[Trait("Spec", "DL-2")]
public sealed class CatalogueChangeReportLicenceTests
{
    private static SkillsCatalogue Licensed(TermsClass terms, string text) =>
        ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X3", i => ChangeReportFixtures.WithLicenceText(i with { TermsClass = terms }, text));

    private static CatalogueChangeReport Compare(TermsClass before, string textBefore, TermsClass after, string textAfter) =>
        CatalogueChangeReport.Compare(Licensed(before, textBefore), Licensed(after, textAfter));

    private static string Short(string text) => CatalogueFixtures.Sha(text)[..12];

    [Theory]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData(TermsClass.Proprietary)]
    [InlineData(TermsClass.None)]
    public void A_changed_licence_text_in_a_non_open_item_is_flagged_as_asking_the_terms_again(TermsClass terms)
    {
        var report = Compare(terms, "old terms", terms, "new terms");
        var text = ChangeReportFixtures.Only(report, "X3", ChangeCategory.Licence, "licenceText");
        Assert.Equal(CatalogueFixtures.Sha("old terms"), text.Old);
        Assert.Equal(CatalogueFixtures.Sha("new terms"), text.New);
        Assert.Equal($"licenceText: {Short("old terms")} -> {Short("new terms")}", text.Message);
        Assert.Equal([ChangeFlag.AsksTermsAgain], report.Items.Single().Flags);
        var attention = Assert.Single(report.Attention);
        Assert.Equal(("X3", ChangeFlag.AsksTermsAgain), (attention.ItemId, attention.Flag));
        Assert.Equal($"the licence text changed: {Short("old terms")} -> {Short("new terms")}", attention.Reason);
        Assert.Equal(["X3"], report.AsksTermsAgain);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_licence_file_is_also_listed_as_a_changed_file_so_the_owner_sees_both()
    {
        var report = Compare(TermsClass.Proprietary, "old terms", TermsClass.Proprietary, "new terms");
        Assert.Equal($"files[LICENSE]: {Short("old terms")} -> {Short("new terms")}", ChangeReportFixtures.Only(report, "X3", ChangeCategory.Hash, "files[LICENSE]").Message);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_same_licence_text_asks_nothing_again_even_when_the_pin_moves()
    {
        var previous = Licensed(TermsClass.Proprietary, "same terms");
        var next = ChangeReportFixtures.Mutate(previous, "X3", i => i with { Pin = ChangeReportFixtures.Commit2 });
        var report = CatalogueChangeReport.Compare(previous, next);
        Assert.Empty(report.AsksTermsAgain);
        Assert.DoesNotContain(report.Attention, a => a.Flag == ChangeFlag.AsksTermsAgain);
        Assert.DoesNotContain(report.ChangesFor("X3"), c => c.Category == ChangeCategory.Licence);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_changed_licence_text_in_an_open_item_is_listed_but_asks_nothing()
    {
        var report = Compare(TermsClass.Open, "old terms", TermsClass.Open, "new terms");
        Assert.Equal($"licenceText: {Short("old terms")} -> {Short("new terms")}", ChangeReportFixtures.Only(report, "X3", ChangeCategory.Licence, "licenceText").Message);
        Assert.Empty(report.Items.Single().Flags);
        Assert.Empty(report.Attention);
        Assert.Empty(report.AsksTermsAgain);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void An_item_that_turns_from_open_to_non_open_needs_its_terms_now_and_one_that_turns_open_does_not()
    {
        var report = Compare(TermsClass.Open, "terms", TermsClass.Proprietary, "terms");
        var terms = ChangeReportFixtures.Only(report, "X3", ChangeCategory.Terms, "termsClass");
        Assert.Equal(("open", "proprietary", "termsClass: open -> proprietary"), (terms.Old, terms.New, terms.Message));
        Assert.Equal([ChangeFlag.TermsNowRequired], report.Items.Single().Flags);
        Assert.Equal("was open, now proprietary", Assert.Single(report.Attention).Reason);
        Assert.Empty(report.AsksTermsAgain);
        var relaxed = Compare(TermsClass.Proprietary, "terms", TermsClass.Open, "terms");
        Assert.Equal("termsClass: proprietary -> open", ChangeReportFixtures.Only(relaxed, "X3", ChangeCategory.Terms, "termsClass").Message);
        Assert.Empty(relaxed.Attention);
        Assert.Empty(relaxed.Items.Single().Flags);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_non_open_item_whose_licence_hash_is_not_recorded_needs_attention_changed_or_not()
    {
        var withoutHash = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X3", i => i with { TermsClass = TermsClass.Proprietary });
        var same = CatalogueChangeReport.Compare(withoutHash, withoutHash);
        var attention = Assert.Single(same.Attention);
        Assert.Equal(("X3", ChangeFlag.LicenceTextNotRecorded), (attention.ItemId, attention.Flag));
        Assert.Equal("the catalogue holds no hash of its licence file, so its terms cannot be accepted yet", attention.Reason);
        Assert.Empty(same.Items);
        // The hash arriving for the first time is a recorded licence text, not a changed one: nothing asks again (no acceptance can exist for a hash that was never known).
        var recorded = CatalogueChangeReport.Compare(withoutHash, Licensed(TermsClass.Proprietary, "terms"));
        Assert.Empty(recorded.AsksTermsAgain);
        Assert.Empty(recorded.Attention);
        Assert.Equal($"licenceText: (none) -> {Short("terms")}", ChangeReportFixtures.Only(recorded, "X3", ChangeCategory.Licence, "licenceText").Message);
        // And a hash that disappears is flagged as not recorded again.
        var lost = CatalogueChangeReport.Compare(Licensed(TermsClass.Proprietary, "terms"), withoutHash);
        Assert.Equal([ChangeFlag.LicenceTextNotRecorded], lost.Attention.Select(a => a.Flag));
        Assert.Empty(lost.AsksTermsAgain);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void An_open_item_never_needs_the_licence_attention_flags()
    {
        var report = CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), ChangeReportFixtures.Previous());
        Assert.Empty(report.Attention);
        Assert.Empty(CatalogueChangeReport.Compare(Licensed(TermsClass.Open, "a"), Licensed(TermsClass.Open, "b")).Attention);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_new_non_open_item_needs_its_licence_recorded_but_is_not_a_second_asking()
    {
        var added = CatalogueFixtures.PinnedSkillset("X8") with { TermsClass = TermsClass.Proprietary };
        var report = CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), CatalogueFixtures.Add(ChangeReportFixtures.Previous(), added));
        Assert.Equal([ChangeFlag.LicenceTextNotRecorded], report.Attention.Select(a => a.Flag));
        Assert.Equal([ChangeFlag.LicenceTextNotRecorded], report.Items.Single(i => i.ItemId == "X8").Flags);
        Assert.Empty(report.AsksTermsAgain);
        Assert.Equal(["X8"], report.Added);
        var hashed = CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), CatalogueFixtures.Add(ChangeReportFixtures.Previous(), ChangeReportFixtures.WithLicenceText(added, "terms")));
        Assert.Empty(hashed.Attention);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_licence_name_url_and_file_changes_are_listed_in_any_item_and_the_notes_are_text()
    {
        var report = CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X1",
            i => i with { LicenceName = "Apache-2.0", LicenceUrl = "https://github.com/example/fixturetool/blob/main/COPYING", LicenceFile = "COPYING", LicenceNotes = "now dual licensed" }));
        Assert.Equal(["licence: licenceName: MIT -> Apache-2.0", "licence: licenceUrl: https://github.com/example/fixturetool/blob/main/LICENSE -> https://github.com/example/fixturetool/blob/main/COPYING",
            "licence: licenceFile: LICENSE -> COPYING", "text: licenceNotes: (none) -> now dual licensed"], ChangeReportFixtures.Lines(report, "X1"));
        Assert.Empty(report.Attention);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_per_skill_terms_class_change_is_a_terms_change()
    {
        var next = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X3", i => i with { Skills = [i.Skills[0] with { TermsClass = TermsClass.Proprietary }, i.Skills[1]] });
        var report = CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), next);
        Assert.Equal("skills[alpha-skill].termsClass: (none) -> proprietary", ChangeReportFixtures.Only(report, "X3", ChangeCategory.Terms, "skills[alpha-skill].termsClass").Message);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_attention_list_is_in_item_order_and_holds_each_flag_of_each_item()
    {
        var previous = ChangeReportFixtures.Mutate(Licensed(TermsClass.Proprietary, "old"), "X1", i => i with { TermsClass = TermsClass.Open });
        var next = ChangeReportFixtures.Mutate(Licensed(TermsClass.Proprietary, "new"), "X1", i => i with { TermsClass = TermsClass.Proprietary });
        var report = CatalogueChangeReport.Compare(previous, next);
        Assert.Equal([("X1", ChangeFlag.TermsNowRequired), ("X1", ChangeFlag.LicenceTextNotRecorded), ("X3", ChangeFlag.AsksTermsAgain)], report.Attention.Select(a => (a.ItemId, a.Flag)));
    }
}
