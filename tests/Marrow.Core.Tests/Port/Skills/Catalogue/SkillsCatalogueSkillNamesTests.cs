using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-1: skill folder names. skills-PLAN.md 2.1 "every skill folder name equals its frontmatter name" (WU-1), and the name rule of the
// Agent Skills specification (agentskills.io/specification, the plan's source 6) as skills-research-loading.md section 1 quotes it for OpenCode:
// "1-64 lowercase letters, digits and single hyphens, and the name must equal the folder name".
// UNVERIFIED: that a name may not start or end with a hyphen is the lead's reading of the specification; agentskills.io was not reachable from
// the build machine (egress proxy), so this rule is recorded as an ED and the rest is the plan's own wording.
[Trait("Spec", "DL-1")]
public sealed class SkillsCatalogueSkillNamesTests
{
    [Theory]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("a")]
    [InlineData("docx")]
    [InlineData("doc-coauthoring")]
    [InlineData("web-artifacts-builder")]
    [InlineData("a1")]
    [InlineData("3d-viewer")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijkl")]
    public void These_names_are_valid(string name) => Assert.True(SkillName.IsValid(name));

    [Theory]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Docx")]
    [InlineData("DOCX")]
    [InlineData("-docx")]
    [InlineData("docx-")]
    [InlineData("doc--x")]
    [InlineData("doc_x")]
    [InlineData("doc x")]
    [InlineData("doc.x")]
    [InlineData("doc/x")]
    [InlineData("doc\\x")]
    [InlineData(".hidden")]
    [InlineData("dök")]
    [InlineData("文档")]
    [InlineData("doc\u00A0x")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklm")]
    public void These_names_are_refused(string name) => Assert.False(SkillName.IsValid(name));

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_name_limit_is_sixty_four_characters_exactly()
    {
        Assert.True(SkillName.IsValid(new string('a', 64)));
        Assert.False(SkillName.IsValid(new string('a', 65)));
        Assert.Equal(64, SkillName.MaxLength);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_listed_skill_folder_in_the_catalogue_follows_the_rule_and_is_unique_inside_its_item()
    {
        var all = CatalogueFixtures.Real.Items.SelectMany(i => i.Skills.Select(s => (i.Id, s))).ToList();
        // ED-NEW-dl-11: pin run 3 lists every skill of the pinned sets, not only the ones the plan names (A1 13, A2 5, A5 6, O1 4, O2 30, O3 4, O6 8).
        // ED-NEW-dl-13: pin run 4 lists the skills the five plugin sets offer (A3 212, A4 63, A6 25, O4 64, O5 117); the others are listed as left out, with their reasons.
        Assert.Equal([("A1", 13), ("A2", 5), ("A3", 212), ("A4", 63), ("A5", 6), ("A6", 25), ("O1", 4), ("O2", 30), ("O3", 4), ("O4", 64), ("O5", 117), ("O6", 8)],
            CatalogueFixtures.Real.Items.Where(i => i.Skills.Count > 0).Select(i => (i.Id, i.Skills.Count)));
        Assert.Equal(551, all.Count);
        Assert.All(all, t => Assert.True(SkillName.IsValid(t.s.Folder), $"{t.Id}/{t.s.Folder}"));
        Assert.All(CatalogueFixtures.Real.Items, i => Assert.Equal(i.Skills.Count, i.Skills.Select(s => s.Folder).Distinct(StringComparer.Ordinal).Count()));
        // A plugin's skill is loaded by its frontmatter name, which may differ from its folder (ED-NEW-dl-13); every name follows the rule and none is used twice inside its item.
        Assert.All(all, t => Assert.True(SkillName.IsValid(t.s.Name!), $"{t.Id}/{t.s.Name}"));
        Assert.All(CatalogueFixtures.Real.Items, i => Assert.Equal(i.Skills.Count, i.Skills.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()));
        Assert.Equal([("A3", 9), ("A4", 2), ("A6", 1), ("O4", 2)],
            CatalogueFixtures.Real.Items.Select(i => (i.Id, i.Skills.Count(s => s.Name != s.Folder))).Where(t => t.Item2 > 0));
        Assert.All(all.Where(t => t.s.Name != t.s.Folder), t => Assert.Equal(ItemKind.Plugin, CatalogueFixtures.Item(CatalogueFixtures.Real, t.Id).Kind));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_folder_name_that_breaks_the_rule_is_refused_by_the_gate()
    {
        foreach (var bad in new[] { "Docx", "-x", "x--y", "x_y", "" })
            Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(CatalogueFixtures.Real, "A2", i => i with { Skills = i.Skills.Select(s => s.Folder == "docx" ? s with { Folder = bad } : s).ToList() })), CatalogueRules.SkillName, "A2"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_frontmatter_name_when_known_must_equal_the_folder_and_follow_the_rule()
    {
        var fixture = CatalogueFixtures.Pinned();
        Assert.Equal("", CatalogueFixtures.Rules(CatalogueGate.Check(fixture)));
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(fixture, "X3", i => i with { Skills = [i.Skills[0] with { Name = "alpha" }, i.Skills[1]] })), CatalogueRules.SkillName, "X3"));
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(fixture, "X3", i => i with { Skills = [i.Skills[0] with { Name = "Alpha-skill" }, i.Skills[1]] })), CatalogueRules.SkillName, "X3"));
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(fixture, "X3", i => i with { Skills = [i.Skills[0], i.Skills[0]] })), CatalogueRules.SkillName, "X3"));
        // Not read yet is allowed (the real catalogue holds null names until SK-2's re-read), and is reported as missing, not as a violation.
        var unread = CatalogueFixtures.Change(fixture, "X3", i => i with { Skills = [i.Skills[0] with { Name = null }, i.Skills[1]] });
        Assert.False(CatalogueFixtures.Fires(CatalogueGate.Check(unread), CatalogueRules.SkillName));
        Assert.Contains("skills[alpha-skill].name", CatalogueGate.Missing(CatalogueFixtures.Item(unread, "X3")));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_path_of_a_skill_ends_in_its_folder_name()
    {
        var fixture = CatalogueFixtures.Pinned();
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(fixture, "X3", i => i with { Skills = [i.Skills[0] with { Path = "skills/other" }, i.Skills[1]] })), CatalogueRules.SkillName, "X3"));
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(fixture, "X3", i => i with { Skills = [i.Skills[0] with { Path = "skills/../alpha-skill" }, i.Skills[1]] })), CatalogueRules.SkillName, "X3"));
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(fixture, "X3", i => i with { Skills = [i.Skills[0] with { Path = "/skills/alpha-skill" }, i.Skills[1]] })), CatalogueRules.SkillName, "X3"));
        // ED-NEW-dl-11: the gate's rule is the path's last segment; A5's skills sit at its repository's root (pin run 3), so their path is the folder itself.
        Assert.All(CatalogueFixtures.Real.Items.SelectMany(i => i.Skills).Where(s => s.Path is not null), s => Assert.Equal(s.Folder, s.Path!.Split('/')[^1]));
        Assert.Equal(6, CatalogueFixtures.Item(CatalogueFixtures.Real, "A5").Skills.Count(s => s.Path == s.Folder));
    }
}
