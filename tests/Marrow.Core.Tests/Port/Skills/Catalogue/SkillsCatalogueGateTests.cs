using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-1: the gate. ONDEMAND-PLAN.md section 7 DL-1: every item has id, kind, pin, URLs, sizes, SHA-256s, licence name and URL, terms
// class, needs, provides; ids are unique; needs form no cycle and name existing ids; hosts are on the allow-list; plus BUILD-PLAN.md 2.5
// SK-1: every item names its licence file. Each rule has a control (the real catalogue passes) and a mutation that must make exactly that
// rule fire, so a rule that stopped checking would fail here (the negative control of R-TEST-4).
public sealed class SkillsCatalogueGateTests
{
    private static SkillsCatalogue Real => CatalogueFixtures.Real;

    private static CatalogueGateReport Check(SkillsCatalogue catalogue) => CatalogueGate.Check(catalogue);

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_embedded_catalogue_and_the_fully_pinned_fixture_both_pass_the_gate()
    {
        Assert.Equal("", CatalogueFixtures.Rules(Check(Real)));
        var pinned = Check(CatalogueFixtures.Pinned());
        Assert.Equal("", CatalogueFixtures.Rules(pinned));
        Assert.Empty(pinned.Unpinned);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_schema_version_must_be_the_one_this_code_reads()
    {
        Assert.True(CatalogueFixtures.Fires(Check(Real with { Schema = 2 }), CatalogueRules.Schema));
        Assert.True(CatalogueFixtures.Fires(Check(Real with { ReadOn = "" }), CatalogueRules.Schema));
        Assert.True(CatalogueFixtures.Fires(Check(Real with { Items = [] }), CatalogueRules.Schema));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Ids_are_unique_and_well_formed()
    {
        var duplicate = CatalogueFixtures.Add(Real, CatalogueFixtures.Item(Real, "T5"));
        Assert.True(CatalogueFixtures.Fires(Check(duplicate), CatalogueRules.Id, "T5"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T5", i => i with { Id = "T 5" })), CatalogueRules.Id));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T5", i => i with { Id = "" })), CatalogueRules.Id));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_fields_each_kind_may_and_may_not_carry_are_enforced()
    {
        var tool = CatalogueFixtures.Item(Real, "T2");
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T2", i => i with { Artifacts = [] })), CatalogueRules.Kind, "T2"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T2", i => i with { SkillCount = 3 })), CatalogueRules.Kind, "T2"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "P1", i => i with { Packages = [] })), CatalogueRules.Kind, "P1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { Artifacts = tool.Artifacts })), CatalogueRules.Kind, "A1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { SkillCount = null })), CatalogueRules.Kind, "A1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { Provides = tool.Provides })), CatalogueRules.Provides, "A1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T2", i => i with { Folders = [new CatalogueFolder { Name = "x", SkillCount = 1 }] })), CatalogueRules.Kind, "T2"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { Folders = [new CatalogueFolder { Name = "x", SkillCount = 13 }] })), CatalogueRules.Kind, "A1"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Text_fields_are_present_and_a_summary_is_one_line()
    {
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { Title = " " })), CatalogueRules.Text, "A1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { Summary = "" })), CatalogueRules.Text, "A1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { Summary = "Two\nlines." })), CatalogueRules.Text, "A1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { Vendor = "" })), CatalogueRules.Text, "A1"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Pins_are_full_commits_for_repositories_and_never_blank_where_given()
    {
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { Pin = "8a1541c" })), CatalogueRules.PinFormat, "A1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { Pin = "8A1541C4A3FFA5A20A5A91DE0DCF3F0BAB1D1EF4" })), CatalogueRules.PinFormat, "A1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T1", i => i with { Pin = "" })), CatalogueRules.PinFormat, "T1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T1", i => i with { Pin = "3 14" })), CatalogueRules.PinFormat, "T1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "O3", i => i with { Skills = i.Skills.Select(s => s.Folder == "doc" ? s with { Pin = "45d05d7" } : s).ToList() })), CatalogueRules.PinFormat, "O3"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A6", i => i with { PinHint = "XYZ" })), CatalogueRules.PinFormat, "A6"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { PinHint = "ffffffff" })), CatalogueRules.PinFormat, "A1"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Sizes_and_hashes_must_be_well_formed_and_a_hash_must_match_its_hint()
    {
        CatalogueItem WithArtifact(CatalogueItem item, Func<CatalogueArtifact, CatalogueArtifact> change) => item with { Artifacts = item.Artifacts.Select(change).ToList() };
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T1", i => WithArtifact(i, a => a with { Size = 0 }))), CatalogueRules.ValueFormat, "T1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T1", i => WithArtifact(i, a => a with { Size = -5 }))), CatalogueRules.ValueFormat, "T1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T1", i => WithArtifact(i, a => a with { Sha256 = "abc" }))), CatalogueRules.ValueFormat, "T1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T1", i => WithArtifact(i, a => a with { Sha256 = CatalogueFixtures.Sha("x").ToUpperInvariant() }))), CatalogueRules.ValueFormat, "T1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T5", i => WithArtifact(i, a => a with { Sha256 = CatalogueFixtures.Sha("other") }))), CatalogueRules.ValueFormat, "T5"));
        Assert.False(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T1", i => WithArtifact(i, a => a with { Sha256 = CatalogueFixtures.Sha("x") }))), CatalogueRules.ValueFormat));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { ApproxSizeMb = -1 })), CatalogueRules.ValueFormat, "A1"));
        var file = new CatalogueFile { Path = "skills/x/SKILL.md", Size = 5, Sha256 = CatalogueFixtures.Sha("x") };
        Assert.False(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { Files = [file] })), CatalogueRules.ValueFormat));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { Files = [file, file] })), CatalogueRules.ValueFormat, "A1"));
        foreach (var bad in new[] { "/abs/SKILL.md", "skills/../SKILL.md", "skills\\x\\SKILL.md", "" })
            Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { Files = [file with { Path = bad }] })), CatalogueRules.ValueFormat, "A1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { Files = [file with { Sha256 = "zz" }] })), CatalogueRules.ValueFormat, "A1"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_licence_name_file_and_url_are_checked_for_shape_and_for_agreeing_with_the_pin()
    {
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { LicenceName = "" })), CatalogueRules.Licence, "A1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { LicenceFile = "../LICENSE" })), CatalogueRules.Licence, "A1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { LicenceFile = "/LICENSE" })), CatalogueRules.Licence, "A1"));
        var fixture = CatalogueFixtures.Pinned();
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(fixture, "X3", i => i with { LicenceUrl = "https://raw.githubusercontent.com/example/fixtureskills/main/LICENSE" })), CatalogueRules.Licence, "X3"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(fixture, "X3", i => i with { LicenceFile = "COPYING" })), CatalogueRules.Licence, "X3"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_declared_pin_state_must_equal_the_one_the_values_give()
    {
        var fixture = CatalogueFixtures.Pinned();
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(fixture, "X1", i => i with { PinState = PinState.Unpinned })), CatalogueRules.PinState, "X1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(fixture, "X1", i => i with { Artifacts = i.Artifacts.Select(a => a with { Sha256 = null }).ToList() })), CatalogueRules.PinState, "X1"));
        // ED-NEW-dl-11: pin run 3 pinned T1; a pinned item declared unpinned is refused. ED-NEW-dl-13: pin run 4 pinned T4 and the plugin sets, so an item that loses a value it pins
        // while still declared pinned is refused: T4 without its artifact's digest, A3 without its files, O5 with a plugin's licence removed.
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T4", i => i with { Artifacts = i.Artifacts.Select(a => a with { Sha256 = null }).ToList() })), CatalogueRules.PinState, "T4"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A3", i => i with { Files = [] })), CatalogueRules.PinState, "A3"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "O5", i => i with { Folders = i.Folders.Select((f, n) => n == 0 ? f with { LicenceName = null, LicenceFile = null } : f).ToList() })), CatalogueRules.PinState, "O5"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "T1", i => i with { PinState = PinState.Unpinned })), CatalogueRules.PinState, "T1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(fixture, "X3", i => i with { Files = [] })), CatalogueRules.PinState, "X3"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(fixture, "X3", i => i with { LicenceFile = null })), CatalogueRules.PinState, "X3"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(fixture, "X2", i => i with { Packages = [new CataloguePackage { Name = "fixturemod", Version = null, Licence = "MIT" }] })), CatalogueRules.PinState, "X2"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_set_with_per_skill_pins_is_pinned_without_an_item_pin_and_only_when_every_skill_has_one()
    {
        var fixture = CatalogueFixtures.Pinned();
        var perSkill = CatalogueFixtures.Change(fixture, "X3", i => i with
        {
            Pin = null,
            Skills = i.Skills.Select(s => s with { Pin = CatalogueFixtures.FixturePin }).ToList(),
        });
        Assert.Equal("", CatalogueFixtures.Rules(Check(perSkill)));
        var oneShort = CatalogueFixtures.Change(perSkill, "X3", i => i with { Skills = [i.Skills[0], i.Skills[1] with { Pin = null }] });
        Assert.True(CatalogueFixtures.Fires(Check(oneShort), CatalogueRules.PinState, "X3"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Skill_counts_folder_sums_and_listed_skills_must_agree()
    {
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A3", i => i with { SkillCount = 251 })), CatalogueRules.Count, "A3"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { SkillCount = 14 })), CatalogueRules.Count, "A1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { Skills = i.Skills.Skip(1).ToList() })), CatalogueRules.Count, "A1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A1", i => i with { SkillCount = -1 })), CatalogueRules.Count, "A1"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A4", i => i with { Skills = CatalogueFixtures.Item(Real, "A1").Skills.Take(1).ToList() })), CatalogueRules.Count, "A4"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A3", i => i with { Folders = i.Folders.Append(i.Folders[0]).ToList() })), CatalogueRules.Count, "A3"));
        Assert.True(CatalogueFixtures.Fires(Check(CatalogueFixtures.Change(Real, "A3", i => i with { Folders = i.Folders.Select(f => f.Name == "sales" ? f with { SkillCount = -1 } : f).ToList() })), CatalogueRules.Count, "A3"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_strict_option_off_lets_unpinned_items_through_and_on_it_does_not()
    {
        // ED-NEW-dl-13: every real item is pinned now, so both pass on the real catalogue; with T4 taken back to unpinned only the strict option refuses it.
        var strict = new CatalogueGateOptions { RequireAllPinned = true };
        Assert.Empty(Check(Real).Violations);
        Assert.Empty(CatalogueGate.Check(Real, strict).Violations);
        var unpinned = CatalogueFixtures.Change(Real, "T4", i => i with { PinState = PinState.Unpinned, Artifacts = i.Artifacts.Select(a => a with { Sha256 = null }).ToList() });
        Assert.Empty(Check(unpinned).Violations);
        Assert.Equal([(CatalogueRules.Unpinned, "T4")], CatalogueGate.Check(unpinned, strict).Violations.Select(v => (v.Rule, v.ItemId!)));
    }
}
