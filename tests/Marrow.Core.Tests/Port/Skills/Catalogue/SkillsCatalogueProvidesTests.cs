using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-1: the provides index. ONDEMAND-PLAN.md 2.3 lists what makes an item findable when missing (executables, Python modules, Node
// modules, Playwright's missing-browser message) and 5.3 "Lookup": "the token goes through the catalogue's provides index to one item (for a
// module, the smallest set that provides it). No hit, no offer." DL-1: "every provides name maps to exactly one tool or the smallest package set".
[Trait("Spec", "DL-1")]
public sealed class SkillsCatalogueProvidesTests
{
    private static readonly Dictionary<string, string[]> PlanExecutables = new()
    {
        ["T1"] = ["python", "python3", "py"], ["T1p"] = ["pip"], ["T2"] = ["node", "npm", "npx"], ["T3"] = ["soffice"],
        ["T4"] = ["pdftoppm", "pdftotext"], ["T5"] = ["qpdf"], ["T6"] = ["pandoc"], ["T7"] = ["bash", "sh"], ["T8"] = ["gh"], ["T9"] = ["uv"],
    };

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_sixteen_executables_of_the_plan_each_resolve_to_the_tool_ONDEMAND_2_2_gives_them()
    {
        var index = ProvidesIndex.Build(CatalogueFixtures.Real);
        var count = 0;
        foreach (var (tool, names) in PlanExecutables)
            foreach (var name in names)
            {
                Assert.Equal(tool, index.Resolve(ProvideKind.Exe, name)?.Id);
                count++;
            }
        Assert.Equal(16, count);
        Assert.Empty(index.Problems);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Executable_names_match_without_regard_to_case_and_extension_is_the_detector_s_job_not_the_index_s()
    {
        var index = ProvidesIndex.Build(CatalogueFixtures.Real);
        Assert.Equal("T3", index.Resolve(ProvideKind.Exe, "SOFFICE")?.Id);
        Assert.Null(index.Resolve(ProvideKind.Exe, "soffice.exe"));
        Assert.Null(index.Resolve(ProvideKind.Exe, "rm"));
        Assert.Null(index.Resolve(ProvideKind.Exe, ""));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_modules_the_plan_names_resolve_to_a_package_set_in_the_right_language()
    {
        var index = ProvidesIndex.Build(CatalogueFixtures.Real);
        foreach (var name in new[] { "docx", "openpyxl", "pptx" }) Assert.Equal("P1", index.Resolve(ProvideKind.PyModule, name)?.Id);
        foreach (var name in new[] { "pypdf", "pdfplumber", "reportlab", "pdf2image" }) Assert.Equal("P2", index.Resolve(ProvideKind.PyModule, name)?.Id);
        foreach (var name in new[] { "PIL", "pandas" }) Assert.Equal("P3", index.Resolve(ProvideKind.PyModule, name)?.Id);
        Assert.Equal("P4", index.Resolve(ProvideKind.PyModule, "playwright")?.Id);
        Assert.Equal("P5", index.Resolve(ProvideKind.PyModule, "imageio")?.Id);
        foreach (var name in new[] { "docx", "pptxgenjs", "sharp", "react-icons" }) Assert.Equal("P6", index.Resolve(ProvideKind.NodeModule, name)?.Id);
        Assert.Null(index.Resolve(ProvideKind.NodeModule, "openpyxl"));
        Assert.Null(index.Resolve(ProvideKind.PyModule, "pptxgenjs"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Module_names_are_case_sensitive_as_Python_and_Node_are()
    {
        var index = ProvidesIndex.Build(CatalogueFixtures.Real);
        Assert.Equal("P3", index.Resolve(ProvideKind.PyModule, "PIL")?.Id);
        Assert.Null(index.Resolve(ProvideKind.PyModule, "pil"));
        Assert.Null(index.Resolve(ProvideKind.NodeModule, "Sharp"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Playwright_s_missing_browser_message_resolves_to_Chromium()
    {
        var index = ProvidesIndex.Build(CatalogueFixtures.Real);
        Assert.Equal("T10", index.Resolve(ProvideKind.Message, "Executable doesn't exist at")?.Id);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Only_tools_provide_programs_and_messages_and_only_package_sets_provide_modules()
    {
        foreach (var item in CatalogueFixtures.Real.Items)
        {
            foreach (var provide in item.Provides)
            {
                var allowed = provide.Kind switch
                {
                    ProvideKind.Exe or ProvideKind.Message => item.Kind == ItemKind.Tool,
                    ProvideKind.PyModule => item.Kind == ItemKind.Pyset,
                    ProvideKind.NodeModule => item.Kind == ItemKind.Nodeset,
                };
                Assert.True(allowed, $"{item.Id} provides {provide.Kind} {provide.Name}");
            }
            if (item.Kind is ItemKind.Skillset or ItemKind.Plugin) Assert.Empty(item.Provides);
        }
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Two_tools_cannot_share_a_program_name()
    {
        var clash = CatalogueFixtures.Change(CatalogueFixtures.Real, "T9", i => i with { Provides = [new CatalogueProvide { Kind = ProvideKind.Exe, Name = "qpdf" }] });
        Assert.NotEmpty(ProvidesIndex.Build(clash).Problems);
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(clash), CatalogueRules.Provides));
        Assert.Null(ProvidesIndex.Build(clash).Resolve(ProvideKind.Exe, "qpdf"));
        var caseClash = CatalogueFixtures.Change(CatalogueFixtures.Real, "T9", i => i with { Provides = [new CatalogueProvide { Kind = ProvideKind.Exe, Name = "QPDF" }] });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(caseClash), CatalogueRules.Provides));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_module_in_two_sets_goes_to_the_smaller_set_and_a_tie_is_refused()
    {
        var small = CatalogueFixtures.PinnedPyset("S1", "shared", 1.0);
        var large = CatalogueFixtures.PinnedPyset("S2", "shared", 9.0);
        var catalogue = CatalogueFixtures.Add(CatalogueFixtures.Add(CatalogueFixtures.Pinned(), small), large);
        catalogue = CatalogueFixtures.Without(catalogue, "X2");
        var index = ProvidesIndex.Build(catalogue);
        Assert.Equal("S1", index.Resolve(ProvideKind.PyModule, "shared")?.Id);
        Assert.Empty(index.Problems);
        Assert.False(CatalogueFixtures.Fires(CatalogueGate.Check(catalogue), CatalogueRules.Provides));
        var tie = CatalogueFixtures.Change(catalogue, "S2", i => i with { ApproxSizeMb = 1.0 });
        Assert.Null(ProvidesIndex.Build(tie).Resolve(ProvideKind.PyModule, "shared"));
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(tie), CatalogueRules.Provides));
        var unknown = CatalogueFixtures.Change(CatalogueFixtures.Change(catalogue, "S1", i => i with { ApproxSizeMb = null }), "S2", i => i with { ApproxSizeMb = null });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(unknown), CatalogueRules.Provides));
        var oneUnknown = CatalogueFixtures.Change(catalogue, "S1", i => i with { ApproxSizeMb = null });
        Assert.Equal("S2", ProvidesIndex.Build(oneUnknown).Resolve(ProvideKind.PyModule, "shared")?.Id);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_blank_or_repeated_name_inside_one_item_is_refused()
    {
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(CatalogueFixtures.Real, "T9", i => i with { Provides = [new CatalogueProvide { Kind = ProvideKind.Exe, Name = " " }] })), CatalogueRules.Provides, "T9"));
        var twice = new CatalogueProvide { Kind = ProvideKind.Exe, Name = "uv" };
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(CatalogueFixtures.Real, "T9", i => i with { Provides = [twice, twice] })), CatalogueRules.Provides, "T9"));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_index_covers_every_provided_name_exactly_once()
    {
        var index = ProvidesIndex.Build(CatalogueFixtures.Real);
        var provided = CatalogueFixtures.Real.Items.SelectMany(i => i.Provides.Select(p => (p.Kind, Name: p.Kind == ProvideKind.Exe ? p.Name.ToLowerInvariant() : p.Name))).Distinct().Count();
        Assert.Equal(provided, index.Count);
        Assert.Equal(CatalogueFixtures.Real.Items.Sum(i => i.Provides.Count), index.Count);
    }
}
