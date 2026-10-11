using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// ED-NEW-dl-13 (the lead's 2026-10-11 decision): a plugin set's licences may be recorded per plugin, nth shows each plugin's own, and the set is offered when every plugin in it has
// one; a skill with no clear licence, a broken name or a name another skill already uses is left out, with its reason. The gate refuses first what a generation would refuse
// (GenerationItem: plugin roots, undeclared SKILL.md files, names), so a pinned item can always be generated. All values are Marrow-made fixtures.
public sealed class PluginLicencesGateTests
{
    private static SkillsCatalogue With(CatalogueItem plugin) => CatalogueFixtures.Add(CatalogueFixtures.Pinned(), plugin);

    private static CatalogueGateReport Check(Func<CatalogueItem, CatalogueItem> change) => CatalogueGate.Check(With(change(CatalogueFixtures.PinnedPlugin())));

    private static string[] Messages(CatalogueGateReport report, string rule) => report.Violations.Where(v => v.Rule == rule && v.ItemId == "X4").Select(v => v.Message).ToArray();

    private static CatalogueItem Files(CatalogueItem item, Func<IEnumerable<CatalogueFile>, IEnumerable<CatalogueFile>> change) => item with { Files = change(item.Files).ToList() };

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_plugin_set_with_a_licence_for_every_plugin_is_pinned_without_an_item_licence_file_and_passes_the_release_gate()
    {
        var plugin = CatalogueFixtures.PinnedPlugin();
        Assert.True(PluginLicences.PerPlugin(plugin));
        Assert.Empty(CatalogueGate.Missing(plugin));
        Assert.Empty(CatalogueGate.Check(With(plugin)).Violations);
        Assert.Empty(CatalogueGate.Check(CatalogueFixtures.Add(CatalogueFixtures.Installable(), plugin), new CatalogueGateOptions { RequireAllPinned = true }).Violations);
        Assert.Null(CatalogueGate.InstallMissing(plugin));
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_plugin_without_its_licence_leaves_the_set_unpinned_and_each_named_licence_file_must_be_pinned()
    {
        var oneMissing = CatalogueFixtures.PinnedPlugin() with { Folders = [CatalogueFixtures.PinnedPlugin().Folders[0], CatalogueFixtures.PinnedPlugin().Folders[1] with { LicenceName = null, LicenceFile = null }] };
        Assert.False(PluginLicences.PerPlugin(oneMissing));
        Assert.Equal(["licenceFile", "licenceUrl"], CatalogueGate.Missing(oneMissing));
        var report = CatalogueGate.Check(With(oneMissing));
        Assert.Contains("licences recorded per plugin name one for every plugin folder", Messages(report, CatalogueRules.Licence));
        Assert.True(CatalogueFixtures.Fires(report, CatalogueRules.PinState, "X4"));
        Assert.Equal(["X4"], CatalogueGate.Check(With(oneMissing with { PinState = PinState.Unpinned }), new CatalogueGateOptions { RequireAllPinned = true }).Violations
            .Where(v => v.Rule == CatalogueRules.Unpinned).Select(v => v.ItemId!));

        var unpinnedLicence = Files(CatalogueFixtures.PinnedPlugin(), f => f.Where(x => x.Path != "plugins/two/LICENSE"));
        Assert.Equal(["files[plugins/two/LICENSE]"], CatalogueGate.Missing(unpinnedLicence));
        Assert.Contains("plugin plugins/two: the licence file 'plugins/two/LICENSE' is not in files", Messages(CatalogueGate.Check(With(unpinnedLicence)), CatalogueRules.Licence));
        var unpinnedSkillLicence = Files(CatalogueFixtures.PinnedPlugin(), f => f.Where(x => x.Path != "plugins/one/skills/beta-folder/LICENSE.txt"));
        Assert.Equal(["files[plugins/one/skills/beta-folder/LICENSE.txt]"], CatalogueGate.Missing(unpinnedSkillLicence));
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Licences_per_plugin_are_refused_anywhere_but_whole_in_a_plugin_item_with_no_item_licence()
    {
        Assert.Contains("an item with licences per plugin has no item licence file or URL",
            Messages(Check(i => Files(i with { LicenceFile = "LICENSE", LicenceUrl = "https://raw.githubusercontent.com/example/fixtureplugins/" + CatalogueFixtures.FixturePin + "/LICENSE" },
                f => f.Append(new CatalogueFile { Path = "LICENSE", Size = 3, Sha256 = CatalogueFixtures.Sha("MIT") }))), CatalogueRules.Licence));
        Assert.Contains("an item with licences per plugin has no item licence file or URL", Messages(Check(i => i with { LicenceUrl = "https://github.com/example/fixtureplugins" }), CatalogueRules.Licence));
        Assert.Contains("plugin plugins/one: a licence names both its name and its file",
            Messages(Check(i => i with { Folders = [i.Folders[0] with { LicenceFile = null }, i.Folders[1]] }), CatalogueRules.Licence));
        Assert.Contains("plugin plugins/one: the licence name is blank or not one line",
            Messages(Check(i => i with { Folders = [i.Folders[0] with { LicenceName = "MIT\nor not" }, i.Folders[1]] }), CatalogueRules.Licence));
        Assert.Contains("plugin plugins/one: the licence name is blank or not one line",
            Messages(Check(i => i with { Folders = [i.Folders[0] with { LicenceName = " " }, i.Folders[1]] }), CatalogueRules.Licence));
        Assert.Contains("plugin plugins/one: the licence file '../LICENSE' is not a plain relative path",
            Messages(Check(i => i with { Folders = [i.Folders[0] with { LicenceFile = "../LICENSE" }, i.Folders[1]] }), CatalogueRules.Licence));
        Assert.Contains("skill plugins/one/skills/beta-folder: a licence names both its name and its file",
            Messages(Check(i => i with { Skills = [i.Skills[0], i.Skills[1] with { LicenceName = null }, i.Skills[2]] }), CatalogueRules.Licence));
        Assert.Contains("a skill names its own licence only where every plugin names one",
            Messages(Check(i => i with { Folders = i.Folders.Select(f => f with { LicenceName = null, LicenceFile = null }).ToList() }), CatalogueRules.Licence));

        var skillset = CatalogueFixtures.PinnedSkillset();
        var licensedSkill = skillset with { Skills = [skillset.Skills[0] with { LicenceName = "MIT", LicenceFile = "LICENSE" }, skillset.Skills[1]] };
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(CatalogueFixtures.Pinned(), "X3", _ => licensedSkill)), CatalogueRules.Licence, "X3"));
        Assert.False(PluginLicences.PerPlugin(licensedSkill));
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_plugin_skill_keeps_its_frontmatter_name_but_no_two_skills_share_one_and_a_skill_set_still_names_the_folder()
    {
        // The fixture's "beta" lives in the folder "beta-folder": allowed in a plugin, where the plugin's own tools load a skill by its frontmatter name.
        Assert.Empty(Messages(CatalogueGate.Check(With(CatalogueFixtures.PinnedPlugin())), CatalogueRules.SkillName));
        Assert.Contains("skill 'gamma': the frontmatter name 'beta' is used by another skill of the item",
            Messages(Check(i => i with { Skills = [i.Skills[0], i.Skills[1], i.Skills[2] with { Name = "beta" }] }), CatalogueRules.SkillName));
        Assert.Contains("skill 'gamma': the frontmatter name 'Bad_Name' is not the folder name in the name rule's form",
            Messages(Check(i => i with { Skills = [i.Skills[0], i.Skills[1], i.Skills[2] with { Name = "Bad_Name" }] }), CatalogueRules.SkillName));
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(CatalogueFixtures.Pinned(), "X3", i => i with { Skills = [i.Skills[0], i.Skills[1] with { Name = "other" }] })),
            CatalogueRules.SkillName, "X3"));
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_pinned_SKILL_md_is_a_listed_skill_s_own_so_a_nested_or_left_out_skill_is_never_fetched()
    {
        Assert.Contains("the file plugins/one/skills/alpha/inner/SKILL.md is a skill the item does not list",
            Messages(Check(i => Files(i, f => f.Append(new CatalogueFile { Path = "plugins/one/skills/alpha/inner/SKILL.md", Size = 1, Sha256 = CatalogueFixtures.Sha("x") }))), CatalogueRules.SkillName));
        Assert.Contains("the file SKILL.md is a skill the item does not list",
            Messages(Check(i => Files(i, f => f.Append(new CatalogueFile { Path = "SKILL.md", Size = 1, Sha256 = CatalogueFixtures.Sha("x") }))), CatalogueRules.SkillName));
        var skillset = CatalogueFixtures.PinnedSkillset();
        var undeclared = skillset with { Files = skillset.Files.Append(new CatalogueFile { Path = "skills/gamma/skill.md", Size = 1, Sha256 = CatalogueFixtures.Sha("g") }).ToList() };
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(CatalogueFixtures.Pinned(), "X3", _ => undeclared)), CatalogueRules.SkillName, "X3"));
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_plugin_folders_are_exactly_the_plugin_roots_and_each_skill_lies_in_one_and_is_counted_there()
    {
        // The folders are the keys a plugin's switch is stored under (SkillSwitches.PluginEnabled(id, root)), so each one must be a root a generation finds.
        Assert.Contains("plugin folder plugins/one holds no plugin.json",
            Messages(Check(i => Files(i, f => f.Where(x => x.Path != "plugins/one/.claude-plugin/plugin.json"))), CatalogueRules.Count));
        Assert.Contains("the plugin at plugins/three is not listed as a plugin folder",
            Messages(Check(i => Files(i, f => f.Append(new CatalogueFile { Path = "plugins/three/.cursor-plugin/plugin.json", Size = 1, Sha256 = CatalogueFixtures.Sha("3") }))), CatalogueRules.Count));
        Assert.Contains("skill plugins/one/skills/alpha lies in 2 plugin folders, not one",
            Messages(Check(i => Files(i with
            {
                SkillCount = 3,
                Folders = [i.Folders[0] with { SkillCount = 1 }, i.Folders[1] with { SkillCount = 1 }, new CatalogueFolder { Name = "plugins/one/skills", SkillCount = 1, LicenceName = "MIT", LicenceFile = "plugins/one/LICENSE" }],
            }, f => f.Append(new CatalogueFile { Path = "plugins/one/skills/.claude-plugin/plugin.json", Size = 1, Sha256 = CatalogueFixtures.Sha("n") }))), CatalogueRules.Count));
        Assert.Contains("plugin folder plugins/one counts 1 skills but holds 2",
            Messages(Check(i => i with { Folders = [i.Folders[0] with { SkillCount = 1 }, i.Folders[1] with { SkillCount = 2 }] }), CatalogueRules.Count));
        Assert.Contains("skill other/skills/x lies in 0 plugin folders, not one",
            Messages(Check(i => Files(i with
            {
                SkillCount = 4, Folders = [i.Folders[0], i.Folders[1] with { SkillCount = 2 }],
                Skills = i.Skills.Append(new CatalogueSkill { Folder = "x", Name = "x", Path = "other/skills/x", Needs = [] }).ToList(),
            }, f => f.Append(new CatalogueFile { Path = "other/skills/x/SKILL.md", Size = 1, Sha256 = CatalogueFixtures.Sha("x") }))), CatalogueRules.Count));
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_skill_left_out_has_a_plain_path_a_one_line_reason_no_listed_twin_and_none_of_its_files_pinned()
    {
        CatalogueSkipped Skip(string path, string reason = "No clear licence of its own") => new() { Path = path, Name = "delta", Reason = reason };
        Assert.Contains("skipped skill plugins/two/skills/delta is listed twice",
            Messages(Check(i => i with { Skipped = [Skip("plugins/two/skills/delta"), Skip("plugins/two/skills/delta")] }), CatalogueRules.SkillName));
        Assert.Contains("skipped skill plugins/two/skills/gamma is also listed as a skill",
            Messages(Check(i => i with { Skipped = [Skip("plugins/two/skills/gamma")] }), CatalogueRules.SkillName));
        Assert.Contains("skipped skill '/abs' is not a plain relative path", Messages(Check(i => i with { Skipped = [Skip("/abs")] }), CatalogueRules.SkillName));
        Assert.Contains("skipped skill plugins/two/skills/delta: the reason is blank or not one line",
            Messages(Check(i => i with { Skipped = [Skip("plugins/two/skills/delta", "two\nlines")] }), CatalogueRules.Text));
        Assert.Contains("skipped skill plugins/two/skills/delta: the reason is blank or not one line",
            Messages(Check(i => i with { Skipped = [Skip("plugins/two/skills/delta", " ")] }), CatalogueRules.Text));
        Assert.Contains("skipped skill plugins/two/skills/delta: its files are pinned, so it would be fetched",
            Messages(Check(i => Files(i, f => f.Append(new CatalogueFile { Path = "plugins/two/skills/delta/notes.md", Size = 1, Sha256 = CatalogueFixtures.Sha("d") }))), CatalogueRules.SkillName));
        var tool = CatalogueFixtures.PinnedTool() with { Skipped = [Skip("x")] };
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(CatalogueFixtures.Pinned(), "X1", _ => tool)), CatalogueRules.Kind, "X1"));
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "SK-13")]
    [Trait("Rule", "R-TEST-4")]
    public void The_terms_key_of_a_set_with_licences_per_plugin_covers_every_licence_file_by_path_and_hash()
    {
        var plugin = CatalogueFixtures.PinnedPlugin();
        Assert.Equal(["plugins/one/LICENSE", "plugins/one/skills/beta-folder/LICENSE.txt", "plugins/two/LICENSE"], PluginLicences.Files(plugin));
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            "plugins/one/LICENSE\0" + CatalogueFixtures.Sha("MIT") + "\n" +
            "plugins/one/skills/beta-folder/LICENSE.txt\0" + CatalogueFixtures.Sha("Apache") + "\n" +
            "plugins/two/LICENSE\0" + CatalogueFixtures.Sha("Apache") + "\n")));
        Assert.Equal(expected, PluginLicences.Hash(plugin));
        Assert.Equal(expected, ItemTerms.LicenceHash(plugin));
        // A change to any one licence file's bytes, or a licence moved to another file, is a new key, so accepted terms are asked again.
        var changed = plugin with { Files = plugin.Files.Select(f => f.Path == "plugins/two/LICENSE" ? f with { Sha256 = CatalogueFixtures.Sha("Apache 2") } : f).ToList() };
        Assert.NotEqual(expected, PluginLicences.Hash(changed));
        var moved = plugin with { Folders = [plugin.Folders[0], plugin.Folders[1] with { LicenceFile = "plugins/one/LICENSE" }] };
        Assert.NotEqual(expected, PluginLicences.Hash(moved));
        Assert.Null(PluginLicences.Hash(plugin with { Files = plugin.Files.Where(f => f.Path != "plugins/one/LICENSE").ToList() }));
        Assert.Null(PluginLicences.Hash(CatalogueFixtures.PinnedSkillset()));
        Assert.Null(PluginLicences.Hash(plugin with { Folders = [plugin.Folders[0], plugin.Folders[1] with { LicenceName = null, LicenceFile = null }] }));
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Each_skill_is_under_its_own_licence_or_its_plugin_s_and_a_plugin_line_names_its_own_first()
    {
        var plugin = CatalogueFixtures.PinnedPlugin();
        Assert.Equal(["MIT", "Apache-2.0", "Apache-2.0"], plugin.Skills.Select(s => PluginLicences.NameFor(plugin, s)));
        Assert.Equal(["MIT", "Apache-2.0"], PluginLicences.NamesOf(plugin, plugin.Folders[0]));
        Assert.Equal(["Apache-2.0"], PluginLicences.NamesOf(plugin, plugin.Folders[1]));
        var nested = plugin with { Folders = [.. plugin.Folders, new CatalogueFolder { Name = "plugins/one/skills", SkillCount = 0, LicenceName = "X", LicenceFile = "plugins/one/LICENSE" }] };
        Assert.Equal("plugins/one/skills", PluginLicences.FolderOf(nested, "plugins/one/skills/alpha")!.Name);
        Assert.Equal("plugins/one", PluginLicences.FolderOf(nested, "plugins/one/README.md")!.Name);
        Assert.Null(PluginLicences.FolderOf(nested, "plugins/oneself/x"));
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_plugin_fixture_round_trips_through_the_writer_with_its_licences_and_skipped_skills()
    {
        var catalogue = With(CatalogueFixtures.PinnedPlugin());
        var text = SkillsCatalogueWriter.Serialize(catalogue);
        var back = SkillsCatalogueLoader.Parse(text);
        Assert.Empty(CatalogueChangeReport.Compare(catalogue, back).Items);
        Assert.Equal(text, SkillsCatalogueWriter.Serialize(back));
        var item = CatalogueFixtures.Item(back, "X4");
        Assert.Equal(CatalogueFixtures.PinnedPlugin().Folders, item.Folders);
        Assert.Equal(CatalogueFixtures.PinnedPlugin().Skipped, item.Skipped);
        Assert.Equal(("Apache-2.0", "plugins/one/skills/beta-folder/LICENSE.txt"), (item.Skills[1].LicenceName, item.Skills[1].LicenceFile));
        Assert.Contains("\"skipped\": [", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"licenceName\": null", text, StringComparison.Ordinal); // optional on skills and folders, so left out where empty
    }
}
