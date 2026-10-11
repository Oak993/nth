using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-1: the counts the plan states, exactly. ONDEMAND-PLAN.md 0.2 and 2.1: "10 vendor skill sets holding 615 skills (Anthropic 388,
// OpenAI 227), about 49 MB", 2.2: "11 tools plus 6 package sets"; BUILD-PLAN.md 2.5 SK-1 and the OD-0099 amendment add A2 and O5 (already
// in the ten) and claude-plugins-official and the Figma folders (the two "added" items whose counts SK-2's re-read sets).
// The counts live in one place, SkillsCatalogueCounts; the first test pins that place to the plan, the others hold the catalogue to it.
// ED-NEW-dl-13: pin run 4 pinned the last five sets. A plugin set offers the skills nth can load and lists the others as left out, each with its reason, so its offered and left-out
// skills together are the plan's count (A4: 111 at the very commit the plan names, where the plan counts 112; run 3's resolver found the same 111).
[Trait("Spec", "DL-1")]
public sealed class SkillsCatalogueCountsTests
{
    private static readonly Dictionary<string, int> PlanSkills = new()
    {
        ["A1"] = 13, ["A2"] = 5, ["A3"] = 252, ["A4"] = 112, ["A5"] = 6,
        ["O1"] = 4, ["O2"] = 30, ["O3"] = 4, ["O4"] = 65, ["O5"] = 124,
    };

    // ED-NEW-dl-13: what each set offers after pin run 4, and how many of its skills it lists as left out.
    private static readonly Dictionary<string, (int Offered, int LeftOut)> Offered = new()
    {
        ["A1"] = (13, 0), ["A2"] = (5, 0), ["A3"] = (212, 40), ["A4"] = (63, 48), ["A5"] = (6, 0),
        ["O1"] = (4, 0), ["O2"] = (30, 0), ["O3"] = (4, 0), ["O4"] = (64, 1), ["O5"] = (117, 7),
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
    public void Ten_vendor_sets_hold_the_plan_s_615_skills_388_Anthropic_and_227_OpenAI_offered_or_left_out_but_for_the_one_A4_skill_its_pin_lacks()
    {
        var sets = PlanSets.ToList();
        Assert.Equal(SkillsCatalogueCounts.VendorSets, sets.Count);
        Assert.Equal(PlanSkills.Keys.OrderBy(k => k, StringComparer.Ordinal), sets.Select(s => s.Id).OrderBy(k => k, StringComparer.Ordinal));
        static int Held(CatalogueItem s) => s.SkillCount!.Value + s.Skipped.Count;
        Assert.Equal(SkillsCatalogueCounts.Skills - 1, sets.Sum(Held));
        Assert.Equal(SkillsCatalogueCounts.AnthropicSkills - 1, sets.Where(s => s.Vendor == "Anthropic").Sum(Held));
        Assert.Equal(SkillsCatalogueCounts.OpenAiSkills, sets.Where(s => s.Vendor == "OpenAI").Sum(Held));
        Assert.Equal([("A4", 112, 111)], sets.Where(s => Held(s) != PlanSkills[s.Id]).Select(s => (s.Id, PlanSkills[s.Id], Held(s))));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Each_set_offers_the_skills_pin_run_4_found_loadable_and_lists_the_rest_of_its_ONDEMAND_2_1_row_as_left_out()
    {
        Assert.Equal(PlanSkills.Keys.Order(StringComparer.Ordinal), Offered.Keys.Order(StringComparer.Ordinal));
        foreach (var (id, (offered, leftOut)) in Offered)
        {
            var set = CatalogueFixtures.Item(CatalogueFixtures.Real, id);
            Assert.Equal((id, offered, leftOut), (id, set.SkillCount!.Value, set.Skipped.Count));
            Assert.Equal(offered, set.Skills.Count);
            Assert.Equal(id == "A4" ? PlanSkills[id] - 1 : PlanSkills[id], offered + leftOut);
        }
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_plugin_folders_of_each_plugin_set_are_its_plugins_and_add_up_to_the_skills_it_offers()
    {
        // ED-NEW-dl-13: a plugin set's folders are its plugins, the folders its plugin.json files mark (the keys its switches are stored under), so the plan's one partner-built folder of
        // A3 (71 skills) is its five partner plugins; a plugin left with no skill nth can load is not offered (A4's fund-admin and operations, O4's zotero).
        (string, int)[] Folders(string id) => CatalogueFixtures.Item(CatalogueFixtures.Real, id).Folders.Select(f => (f.Name, f.SkillCount)).ToArray();
        Assert.Equal([("bio-research", 6), ("cowork-plugin-management", 2), ("customer-support", 5), ("data", 10), ("design", 7), ("engineering", 10), ("enterprise-search", 5),
            ("finance", 8), ("human-resources", 9), ("legal", 9), ("marketing", 8), ("operations", 9), ("partner-built/apollo", 3), ("partner-built/brand-voice", 3),
            ("partner-built/common-room", 5), ("partner-built/slack", 2), ("partner-built/zoom-plugin", 23), ("pdf-viewer", 1), ("product-management", 7), ("productivity", 3),
            ("sales", 34), ("small-business", 43)], Folders("A3"));
        Assert.Equal([("plugins/agent-plugins/earnings-reviewer", 6), ("plugins/agent-plugins/gl-reconciler", 2), ("plugins/agent-plugins/kyc-screener", 2),
            ("plugins/agent-plugins/market-researcher", 5), ("plugins/agent-plugins/meeting-prep-agent", 3), ("plugins/agent-plugins/model-builder", 3),
            ("plugins/agent-plugins/month-end-closer", 3), ("plugins/agent-plugins/pitch-agent", 3), ("plugins/agent-plugins/statement-auditor", 1),
            ("plugins/agent-plugins/valuation-reviewer", 3), ("plugins/partner-built/lseg", 8), ("plugins/partner-built/spglobal", 3), ("plugins/vertical-plugins/equity-research", 3),
            ("plugins/vertical-plugins/financial-analysis", 3), ("plugins/vertical-plugins/investment-banking", 8), ("plugins/vertical-plugins/private-equity", 7)], Folders("A4"));
        Assert.Equal([("plugins/claude-code-setup", 1), ("plugins/claude-md-management", 1), ("plugins/claude-security", 1), ("plugins/cwc-makers", 2), ("plugins/example-plugin", 2),
            ("plugins/frontend-design", 1), ("plugins/hookify", 1), ("plugins/math-olympiad", 1), ("plugins/math-proof", 2), ("plugins/mcp-server-dev", 3), ("plugins/playground", 1),
            ("plugins/plugin-dev", 7), ("plugins/project-artifact", 1), ("plugins/skill-creator", 1)], Folders("A6"));
        Assert.Equal([("plugins/build-web-apps", 6), ("plugins/build-web-data-visualization", 18), ("plugins/game-studio", 9), ("plugins/google-drive", 5), ("plugins/ngs-analysis", 18),
            ("plugins/plugin-eval", 5), ("plugins/sentry", 1), ("plugins/test-android-apps", 2)], Folders("O4"));
        Assert.Equal([("plugins/codex-security", 15), ("plugins/creative-production", 2), ("plugins/data-analytics", 14), ("plugins/life-science-research", 50),
            ("plugins/openai-ads-conversions", 1), ("plugins/openai-developers", 5), ("plugins/product-design", 9), ("plugins/public-equity-investing", 21)], Folders("O5"));
        Assert.All(CatalogueFixtures.Real.Items.Where(i => i.Kind == ItemKind.Plugin), i => Assert.Equal(i.SkillCount, i.Folders.Sum(f => f.SkillCount)));
        Assert.All(CatalogueFixtures.Real.Items.Where(i => i.Kind == ItemKind.Plugin), i => Assert.All(i.Folders, f => Assert.True(f.SkillCount > 0, f.Name)));
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
        // ED-NEW-dl-11: pin run 3 lists every pinned set's skills in the ordinal order of their paths (the plan wrote A2 and O3 in its own order; O2's skills/.system/plugin-creator comes after its curated folders).
        Assert.Equal(["doc-coauthoring", "docx", "pdf", "pptx", "xlsx"], Folders("A2"));
        Assert.Equal(["imagegen", "openai-docs", "review-agent", "skill-creator"], Folders("O1"));
        Assert.DoesNotContain("skill-installer", Folders("O1"));
        Assert.Equal(["doc", "pdf", "slides", "spreadsheet"], Folders("O3"));
        Assert.All(CatalogueFixtures.Real.Items.Where(i => i.PinState == PinState.Pinned), i => Assert.Equal(i.Skills.Select(s => s.Path!).Order(StringComparer.Ordinal), i.Skills.Select(s => s.Path!)));
        Assert.Equal("plugin-creator", Folders("O2")[^1]);
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
        var a6 = CatalogueFixtures.Item(CatalogueFixtures.Real, "A6");
        Assert.Equal((25, 2), (a6.SkillCount, a6.Skipped.Count)); // ED-NEW-dl-13: pin run 4 counted A6
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
    public void Every_pinned_size_is_the_one_the_pin_run_measured_and_the_plan_s_estimates_stay_only_where_nothing_was_measured()
    {
        // ONDEMAND-PLAN.md 2.1: "about 49 MB in all" for the sets the plan sizes; measured, the files the ten sets offer come to 45 MB (ED-NEW-dl-13: pin run 4 measured the last four,
        // without the skills they leave out).
        Assert.Equal(45, Math.Round(PlanSets.Sum(s => s.ApproxSizeMb!.Value), MidpointRounding.AwayFromZero));
        // ED-NEW-dl-11: pin run 3 measured every pinned item, a set by its files and a tool or package set by its downloads, in MB to two places (the plan's 356 MB T3, 12.6 MB T1,
        // 325.3 MB T10 and unsized P6 were estimates); the items it could not pin kept the plan's estimates until pin run 4 measured them (ED-NEW-dl-13).
        foreach (var item in CatalogueFixtures.Real.Items.Where(i => i.PinState == PinState.Pinned))
        {
            var bytes = item.Kind is ItemKind.Skillset or ItemKind.Plugin ? item.Files.Sum(f => f.Size) : item.Artifacts.Sum(a => a.Size!.Value);
            Assert.Equal(Math.Round(bytes / 1e6, 2), item.ApproxSizeMb!.Value, 6);
        }
        Assert.All(CatalogueFixtures.Real.Items, i => Assert.Equal((i.Id, PinState.Pinned), (i.Id, i.PinState)));
        Assert.Equal(new (string, double)[] { ("A3", 6.09), ("A4", 1.12), ("A6", 3.76), ("O4", 3.79), ("O5", 17.44), ("T4", 43.71) },
            new[] { "A3", "A4", "A6", "O4", "O5", "T4" }.Select(id => (id, CatalogueFixtures.Item(CatalogueFixtures.Real, id).ApproxSizeMb!.Value)));
        double Mb(string id) => CatalogueFixtures.Item(CatalogueFixtures.Real, id).ApproxSizeMb!.Value;
        // ONDEMAND-PLAN.md 2.2: "The common document case (T1, T1p, P1, P2, P3, T3, T4) is about 480 MB"; measured it is 557 MB (T4 measured by pin run 4, ED-NEW-dl-13).
        Assert.Equal(557, Math.Round(new[] { "T1", "T1p", "P1", "P2", "P3", "T3", "T4" }.Sum(Mb), MidpointRounding.AwayFromZero));
        Assert.Equal(374.87, Mb("T3"), 6);
        Assert.Equal(325.32, Mb("T10"), 6);
        Assert.Equal(14.42, Mb("T1"), 6);
        Assert.Equal(43.54, Mb("P6"), 6);
    }
}
