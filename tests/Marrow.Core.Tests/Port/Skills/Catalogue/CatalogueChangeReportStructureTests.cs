using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-2: the rest of what a new pin run can change in an item, so the owner's report is complete: what it needs and provides, its skills and plugin folders and
// their counts. (ONDEMAND-PLAN.md 3.7: the report lists what a release carries; 2.3: needs and provides are catalogue fields.) None of these is invented: each is a
// field of SK-1's schema, compared as it is.
[Trait("Spec", "DL-2")]
public sealed class CatalogueChangeReportStructureTests
{
    private static CatalogueChangeReport Compare(Func<CatalogueItem, CatalogueItem> change, string id = "X3") =>
        CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), id, change));

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Needs_added_and_removed_are_listed_one_by_one()
    {
        var report = Compare(i => i with { Needs = ["X2"] });
        Assert.Equal(["needs: needs: removed X1", "needs: needs: added X2"], ChangeReportFixtures.Lines(report, "X3"));
        var removed = report.ChangesFor("X3").First(c => c.Category == ChangeCategory.Needs);
        Assert.Equal("X1", removed.Old);
        Assert.Null(removed.New);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Provides_added_and_removed_are_listed_with_their_kind()
    {
        var report = Compare(i => i with { Provides = [new CatalogueProvide { Kind = ProvideKind.Exe, Name = "other" }] }, "X1");
        Assert.Equal(["provides: provides: removed exe:fixturetool", "provides: provides: added exe:other"], ChangeReportFixtures.Lines(report, "X1"));
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Skills_added_removed_and_renamed_and_the_skill_count_are_listed()
    {
        var report = Compare(i => i with
        {
            SkillCount = 2,
            Skills = [i.Skills[0] with { Name = null, Path = "skills/renamed/alpha-skill", Needs = [] }, new CatalogueSkill { Folder = "gamma", Name = "gamma", Path = "skills/gamma", Needs = ["X1"] }],
        });
        var lines = ChangeReportFixtures.Lines(report, "X3");
        Assert.Contains("skills: skills[alpha-skill].name: alpha-skill -> (none)", lines);
        Assert.Contains("skills: skills[alpha-skill].path: skills/alpha-skill -> skills/renamed/alpha-skill", lines);
        Assert.Contains("skills: skills[alpha-skill].needs: removed X1", lines);
        Assert.Contains("skills: skills[beta]: removed", lines);
        Assert.Contains("skills: skills[gamma]: added", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("skills: skillCount", StringComparison.Ordinal));
        var counted = Compare(i => i with { SkillCount = 3 });
        Assert.Equal(["skills: skillCount: 2 -> 3"], ChangeReportFixtures.Lines(counted, "X3"));
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Plugin_folders_added_removed_and_recounted_are_listed_and_a_skills_listed_flip_is_too()
    {
        var previous = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X3", i => i with
        {
            Kind = ItemKind.Plugin, SkillsListed = false, Skills = [], SkillCount = 5, Folders = [new CatalogueFolder { Name = "sales", SkillCount = 3 }, new CatalogueFolder { Name = "legal", SkillCount = 2 }],
        });
        var next = ChangeReportFixtures.Mutate(previous, "X3", i => i with
        {
            SkillCount = 6, Folders = [new CatalogueFolder { Name = "sales", SkillCount = 4 }, new CatalogueFolder { Name = "design", SkillCount = 2 }], SkillsListed = true,
        });
        var lines = ChangeReportFixtures.Lines(CatalogueChangeReport.Compare(previous, next), "X3");
        Assert.Contains("skills: folders[sales]: 3 -> 4", lines);
        Assert.Contains("skills: folders[legal]: removed (was 2)", lines);
        Assert.Contains("skills: folders[design]: added (2)", lines);
        Assert.Contains("skills: skillCount: 5 -> 6", lines);
        Assert.Contains("skills: skillsListed: false -> true", lines);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_package_whose_licence_changes_is_a_licence_change_and_the_title_summary_and_vendor_are_text()
    {
        var report = Compare(i => i with
        {
            Packages = [new CataloguePackage { Name = "fixturemod", Version = "1.0", Licence = "BSD-3" }], Title = "T", Summary = "S.", Vendor = "V",
        }, "X2");
        Assert.Equal("packages[fixturemod].licence: MIT -> BSD-3", ChangeReportFixtures.Only(report, "X2", ChangeCategory.Licence, "packages[fixturemod].licence").Message);
        Assert.Equal(["text: title: Fixture set -> T", "text: summary: A fixture package set for the gate tests. -> S.", "text: vendor: Fixture -> V"],
            ChangeReportFixtures.Lines(report, "X2").Where(l => l.StartsWith("text:", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_item_set_covers_every_field_of_the_schema_so_no_change_can_slip_by()
    {
        // One change per field of an item and its parts; each must show up as at least one change of that item. A field added to the schema later without a line here
        // fails the first assertion, which names the fields this test knows.
        var known = new[]
        {
            "Id", "Kind", "Title", "Summary", "Vendor", "CountState", "PinState", "Pin", "PinHint", "SourceUrl", "LicenceName", "LicenceUrl", "LicenceFile", "LicenceNotes", "TermsClass",
            "ApproxSizeMb", "Needs", "Provides", "Artifacts", "Files", "SkillCount", "SkillsListed", "Skills", "Folders", "Packages", "Install",
        };
        Assert.Equal(known.OrderBy(k => k, StringComparer.Ordinal), typeof(CatalogueItem).GetProperties().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(["Folder", "Name", "Needs", "Path", "Pin", "TermsClass"], typeof(CatalogueSkill).GetProperties().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(["Name", "Sha256", "Sha256Hint", "Size", "Url"], typeof(CatalogueArtifact).GetProperties().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal));
        var changes = new Dictionary<string, Func<CatalogueItem, CatalogueItem>>
        {
            ["Kind"] = i => i with { Kind = ItemKind.Plugin }, ["Title"] = i => i with { Title = "t" }, ["Summary"] = i => i with { Summary = "s" }, ["Vendor"] = i => i with { Vendor = "v" },
            ["CountState"] = i => i with { CountState = CountState.Added }, ["PinState"] = i => i with { PinState = PinState.Unpinned }, ["Pin"] = i => i with { Pin = ChangeReportFixtures.Commit2 },
            ["SourceUrl"] = i => i with { SourceUrl = "https://github.com/example/x" }, ["LicenceName"] = i => i with { LicenceName = "BSD" }, ["LicenceUrl"] = i => i with { LicenceUrl = "https://github.com/example/y" },
            ["LicenceFile"] = i => i with { LicenceFile = "COPYING" }, ["LicenceNotes"] = i => i with { LicenceNotes = "n" }, ["TermsClass"] = i => i with { TermsClass = TermsClass.None },
            ["ApproxSizeMb"] = i => i with { ApproxSizeMb = 9 }, ["Needs"] = i => i with { Needs = [] }, ["Provides"] = i => i with { Provides = [new CatalogueProvide { Kind = ProvideKind.Exe, Name = "zz" }] },
            ["Files"] = i => i with { Files = [] }, ["SkillCount"] = i => i with { SkillCount = 9 }, ["SkillsListed"] = i => i with { SkillsListed = false }, ["Skills"] = i => i with { Skills = [] },
            ["Folders"] = i => i with { Folders = [new CatalogueFolder { Name = "f", SkillCount = 1 }] },
        };
        foreach (var (field, change) in changes)
            Assert.Contains(Compare(change).ChangesFor("X3"), c => c.Category != ChangeCategory.Item || field == "Kind");
        var tool = new Dictionary<string, Func<CatalogueItem, CatalogueItem>>
        {
            ["PinHint"] = i => i with { PinHint = "abcdef0" }, ["Artifacts"] = i => i with { Artifacts = [] },
            ["Packages"] = i => i with { Packages = [new CataloguePackage { Name = "p", Version = "1", Licence = null }] },
            ["Install"] = i => i with { Install = new CatalogueInstall { Kind = InstallKind.Pyset, PythonPackages = true } },
        };
        foreach (var (field, change) in tool)
            Assert.True(Compare(change, "X2").ChangesFor("X2").Count > 0, field);
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Rule", "R-TEST-4")]
    public void A_change_of_installer_parameters_is_an_install_change_named_by_the_installer_kind_with_both_texts()
    {
        // ED-NEW-dl-7: the report names every change of how an item is installed, so an owner reading a pin run's report sees a new shim, export or archive folder too.
        CatalogueInstall Zip(string? prefix) => new() { Kind = InstallKind.Zip, Archives = [new CatalogueInstallArchive { Artifact = "tool.zip", Prefix = prefix }] };
        SkillsCatalogue With(CatalogueInstall? install) => ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X2", i => i with { Install = install });
        CatalogueChange Install(CatalogueInstall? from, CatalogueInstall? to) =>
            CatalogueChangeReport.Compare(With(from), With(to)).ChangesFor("X2").Single(c => c.Category == ChangeCategory.Install);
        var added = Install(null, Zip(null));
        Assert.Equal(("install: added (zip)", (string?)null, SkillsCatalogueWriter.InstallText(Zip(null))), (added.Message, added.Old, added.New));
        var changed = Install(Zip(null), Zip("tool"));
        Assert.Equal(("install: the zip parameters changed", SkillsCatalogueWriter.InstallText(Zip(null)), SkillsCatalogueWriter.InstallText(Zip("tool"))), (changed.Message, changed.Old, changed.New));
        Assert.Contains("\"prefix\": \"tool\"", changed.New, StringComparison.Ordinal);
        Assert.Equal("install: zip -> pyset", Install(Zip(null), new CatalogueInstall { Kind = InstallKind.Pyset, PythonPackages = true }).Message);
        Assert.Equal("install: removed (was zip)", Install(Zip(null), null).Message);
        Assert.Empty(CatalogueChangeReport.Compare(With(Zip("tool")), With(Zip("tool"))).ChangesFor("X2"));
    }
}
