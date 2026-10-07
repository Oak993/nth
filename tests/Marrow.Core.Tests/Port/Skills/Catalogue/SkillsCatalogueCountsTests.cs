using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-1: the counts the plan states, exactly. ONDEMAND-PLAN.md 0.2 and 2.1: "10 vendor skill sets holding 615 skills (Anthropic 388,
// OpenAI 227), about 49 MB", 2.2: "11 tools plus 6 package sets"; BUILD-PLAN.md 2.5 SK-1 and the OD-0099 amendment add A2 and O5 (already
// in the ten) and claude-plugins-official and the Figma folders (the two "added" items whose counts SK-2's re-read sets).
// The counts live in one place, SkillsCatalogueCounts; the first test pins that place to the plan, the others hold the catalogue to it.
[Trait("Spec", "DL-1")]
public sealed class SkillsCatalogueCountsTests
{
    private static readonly Dictionary<string, int> PlanSkills = new()
    {
        ["A1"] = 13, ["A2"] = 5, ["A3"] = 252, ["A4"] = 112, ["A5"] = 6,
        ["O1"] = 4, ["O2"] = 30, ["O3"] = 4, ["O4"] = 65, ["O5"] = 124,
    };

    private static IEnumerable<CatalogueItem> PlanSets => CatalogueFixtures.Real.Items.Where(i => i.Kind is ItemKind.Skillset or ItemKind.Plugin && i.CountState == CountState.Plan);

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_one_place_for_the_counts_says_what_the_plan_says()
    {
        Assert.Equal(1, SkillsCatalogueCounts.SchemaVersion);
        Assert.Equal(10, SkillsCatalogueCounts.VendorSets);
        Assert.Equal(615, SkillsCatalogueCounts.Skills);
        Assert.Equal(388, SkillsCatalogueCounts.AnthropicSkills);
        Assert.Equal(227, SkillsCatalogueCounts.OpenAiSkills);
        Assert.Equal(11, SkillsCatalogueCounts.Tools);
        Assert.Equal(6, SkillsCatalogueCounts.PackageSets);
        Assert.Equal(2, SkillsCatalogueCounts.AddedSets);
        Assert.Equal(SkillsCatalogueCounts.Skills, SkillsCatalogueCounts.AnthropicSkills + SkillsCatalogueCounts.OpenAiSkills);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Ten_vendor_sets_hold_615_skills_388_Anthropic_and_227_OpenAI()
    {
        var sets = PlanSets.ToList();
        Assert.Equal(SkillsCatalogueCounts.VendorSets, sets.Count);
        Assert.Equal(PlanSkills.Keys.OrderBy(k => k, StringComparer.Ordinal), sets.Select(s => s.Id).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(SkillsCatalogueCounts.Skills, sets.Sum(s => s.SkillCount!.Value));
        Assert.Equal(SkillsCatalogueCounts.AnthropicSkills, sets.Where(s => s.Vendor == "Anthropic").Sum(s => s.SkillCount!.Value));
        Assert.Equal(SkillsCatalogueCounts.OpenAiSkills, sets.Where(s => s.Vendor == "OpenAI").Sum(s => s.SkillCount!.Value));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Each_set_has_the_skill_count_its_row_in_ONDEMAND_2_1_states()
    {
        foreach (var (id, count) in PlanSkills) Assert.Equal(count, CatalogueFixtures.Item(CatalogueFixtures.Real, id).SkillCount);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_plugin_folders_of_A3_O4_and_O5_add_up_to_their_set_and_have_the_names_and_counts_the_plan_lists()
    {
        var a3 = CatalogueFixtures.Item(CatalogueFixtures.Real, "A3");
        Assert.Equal(18, a3.Folders.Count);
        Assert.Equal(252, a3.Folders.Sum(f => f.SkillCount));
        Assert.Equal(36, a3.Folders.Single(f => f.Name == "sales").SkillCount);
        Assert.Equal(44, a3.Folders.Single(f => f.Name == "small-business").SkillCount);
        Assert.Equal(71, a3.Folders.Single(f => f.Name == "partner-built").SkillCount);
        Assert.Equal(1, a3.Folders.Single(f => f.Name == "pdf-viewer").SkillCount);
        var o4 = CatalogueFixtures.Item(CatalogueFixtures.Real, "O4");
        Assert.Equal(9, o4.Folders.Count);
        Assert.Equal(65, o4.Folders.Sum(f => f.SkillCount));
        Assert.Equal(18, o4.Folders.Single(f => f.Name == "ngs-analysis").SkillCount);
        var o5 = CatalogueFixtures.Item(CatalogueFixtures.Real, "O5");
        Assert.Equal(8, o5.Folders.Count);
        Assert.Equal(124, o5.Folders.Sum(f => f.SkillCount));
        Assert.Equal(50, o5.Folders.Single(f => f.Name == "life-science-research").SkillCount);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_skills_the_plan_names_one_by_one_are_listed_in_full()
    {
        string[] Folders(string id) => CatalogueFixtures.Item(CatalogueFixtures.Real, id).Skills.Select(s => s.Folder).ToArray();
        Assert.Equal(["academy-guide", "algorithmic-art", "canvas-design", "claude-api", "discernment-nudge", "frontend-design", "internal-comms",
            "mcp-builder", "skill-creator", "slack-gif-creator", "theme-factory", "web-artifacts-builder", "webapp-testing"], Folders("A1"));
        Assert.DoesNotContain("brand-guidelines", Folders("A1"));
        Assert.Equal(["docx", "xlsx", "pptx", "pdf", "doc-coauthoring"], Folders("A2"));
        Assert.Equal(["imagegen", "openai-docs", "review-agent", "skill-creator"], Folders("O1"));
        Assert.DoesNotContain("skill-installer", Folders("O1"));
        Assert.Equal(["doc", "spreadsheet", "slides", "pdf"], Folders("O3"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Eleven_tools_and_six_package_sets_with_the_ids_the_plan_gives()
    {
        var tools = CatalogueFixtures.Real.Items.Where(i => i.Kind == ItemKind.Tool).Select(i => i.Id).ToList();
        Assert.Equal(SkillsCatalogueCounts.Tools, tools.Count);
        Assert.Equal(["T1", "T10", "T1p", "T2", "T3", "T4", "T5", "T6", "T7", "T8", "T9"], tools.OrderBy(i => i, StringComparer.Ordinal));
        var sets = CatalogueFixtures.Real.Items.Where(i => i.Kind is ItemKind.Pyset or ItemKind.Nodeset).ToList();
        Assert.Equal(SkillsCatalogueCounts.PackageSets, sets.Count);
        Assert.Equal(["P1", "P2", "P3", "P4", "P5", "P6"], sets.Select(i => i.Id).OrderBy(i => i, StringComparer.Ordinal));
        Assert.Equal(5, sets.Count(i => i.Kind == ItemKind.Pyset));
        Assert.Equal(ItemKind.Nodeset, CatalogueFixtures.Item(CatalogueFixtures.Real, "P6").Kind);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_two_added_sets_are_marked_added_and_stay_out_of_the_plan_totals()
    {
        var added = CatalogueFixtures.Real.Items.Where(i => i.CountState == CountState.Added).Select(i => i.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
        Assert.Equal(["A6", "O6"], added);
        Assert.Equal(SkillsCatalogueCounts.AddedSets, added.Count);
        Assert.Null(CatalogueFixtures.Item(CatalogueFixtures.Real, "A6").SkillCount);
        Assert.Equal(8, CatalogueFixtures.Item(CatalogueFixtures.Real, "O6").SkillCount);
        Assert.Equal(SkillsCatalogueCounts.Tools + SkillsCatalogueCounts.PackageSets + SkillsCatalogueCounts.VendorSets + SkillsCatalogueCounts.AddedSets,
            CatalogueFixtures.Real.Items.Count);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Plugins_and_skills_split_as_BUILD_PLAN_sections_C_page_table_says()
    {
        string[] Ids(ItemKind kind) => CatalogueFixtures.Real.Items.Where(i => i.Kind == kind && i.Id[0] is 'A' or 'O').Select(i => i.Id).OrderBy(i => i, StringComparer.Ordinal).ToArray();
        Assert.Equal(["A3", "A4", "A6", "O4", "O5"], Ids(ItemKind.Plugin));
        Assert.Equal(["A1", "A2", "A5", "O1", "O2", "O3", "O6"], Ids(ItemKind.Skillset));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_sizes_transcribed_from_the_plan_add_up_to_the_totals_the_plan_prints()
    {
        // ONDEMAND-PLAN.md 2.1: "about 49 MB in all". 2.2: "The common document case (T1, T1p, P1, P2, P3, T3, T4) is about 480 MB."
        Assert.Equal(49, Math.Round(PlanSets.Sum(s => s.ApproxSizeMb!.Value), MidpointRounding.AwayFromZero));
        double Mb(string id) => CatalogueFixtures.Item(CatalogueFixtures.Real, id).ApproxSizeMb!.Value;
        Assert.Equal(480, Math.Round(new[] { "T1", "T1p", "P1", "P2", "P3", "T3", "T4" }.Sum(Mb), MidpointRounding.AwayFromZero));
        Assert.Equal(356, Mb("T3"));
        Assert.Equal(325.3, Mb("T10"), 6);
        Assert.Equal(12.6, Mb("T1"), 6);
        Assert.Null(CatalogueFixtures.Item(CatalogueFixtures.Real, "P6").ApproxSizeMb);
    }
}
