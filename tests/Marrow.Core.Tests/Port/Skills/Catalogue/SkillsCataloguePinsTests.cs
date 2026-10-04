using System.Text.RegularExpressions;
using Marrow.Core.Port.Skills.Catalogue;
using Xunit.Abstractions;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-1: nothing is invented. Every pin, version, size, URL and hash prefix the catalogue holds is the one ONDEMAND-PLAN.md 2.1, 2.2
// and 10 or skills-PLAN.md 6 prints (read on 2026-10-04 by the lead); every value the plan does not give is an explicit null, and the item
// is marked unpinned until SK-2's real pin run (or the lead's) fills it. This file says which items still lack real values.
public sealed partial class SkillsCataloguePinsTests(ITestOutputHelper output)
{
    [GeneratedRegex("^[0-9a-f]{40}$")] private static partial Regex Commit();

    private static readonly Dictionary<string, string> PlanPins = new()
    {
        ["A1"] = "8a1541c4a3ffa5a20a5a91de0dcf3f0bab1d1ef4",
        ["A2"] = "8a1541c4a3ffa5a20a5a91de0dcf3f0bab1d1ef4",
        ["A3"] = "8444efcd48f7012f09797778a36a33e73d0861f4",
        ["A4"] = "574ed3624aebd0418c7e96cd101262f30210ab26",
        ["A5"] = "e96556b637b56d6cc3a5ad33987009be9e60aa5c",
        ["O1"] = "2739e828581c3deee59a270cd740675dc278ed14",
        ["O2"] = "49f948faa9258a0c61caceaf225e179651397431",
        ["O4"] = "5fd93af4cd0c623e020d0cc7e9ce178b4ac1f70f",
        ["O5"] = "5fd93af4cd0c623e020d0cc7e9ce178b4ac1f70f",
        ["O6"] = "49f948faa9258a0c61caceaf225e179651397431",
    };

    private static readonly Dictionary<string, string> PlanSources = new()
    {
        ["A1"] = "https://github.com/anthropics/skills",
        ["A2"] = "https://github.com/anthropics/skills",
        ["A3"] = "https://github.com/anthropics/knowledge-work-plugins",
        ["A4"] = "https://github.com/anthropics/financial-services",
        ["A5"] = "https://github.com/anthropics/life-sciences",
        ["A6"] = "https://github.com/anthropics/claude-plugins-official",
        ["O1"] = "https://github.com/openai/codex",
        ["O2"] = "https://github.com/openai/skills",
        ["O3"] = "https://github.com/openai/skills",
        ["O4"] = "https://github.com/openai/plugins",
        ["O5"] = "https://github.com/openai/plugins",
        ["O6"] = "https://github.com/openai/skills",
    };

    private static readonly Dictionary<string, string> PlanVersions = new()
    {
        ["T1"] = "3.14.8", ["T1p"] = "26.2.1", ["T2"] = "24.21.0", ["T3"] = "26.2.6", ["T4"] = "26.09.0", ["T5"] = "12.4.2",
        ["T6"] = "3.12", ["T7"] = "2.56.0", ["T8"] = "2.102.0", ["T9"] = "0.12.23", ["T10"] = "153.0.8010.12",
    };

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_vendor_pin_is_the_commit_the_plan_prints()
    {
        foreach (var (id, pin) in PlanPins) Assert.Equal(pin, CatalogueFixtures.Item(CatalogueFixtures.Real, id).Pin);
        foreach (var id in new[] { "A1", "A2", "A3", "A4", "A5", "O1", "O2", "O4", "O5", "O6" })
            Assert.Matches(Commit(), CatalogueFixtures.Item(CatalogueFixtures.Real, id).Pin!);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void O3_carries_its_four_pins_one_per_skill_and_claude_plugins_official_only_the_short_pin_the_plan_has()
    {
        var o3 = CatalogueFixtures.Item(CatalogueFixtures.Real, "O3");
        Assert.Null(o3.Pin);
        Assert.Equal("45d05d75363abf13f99d09e899d61e07b8010685", o3.Skills.Single(s => s.Folder == "doc").Pin);
        Assert.Equal("e6afb0d74cc75d220df2faf3dd6c635c2dc6a108", o3.Skills.Single(s => s.Folder == "spreadsheet").Pin);
        Assert.Equal("e6afb0d74cc75d220df2faf3dd6c635c2dc6a108", o3.Skills.Single(s => s.Folder == "slides").Pin);
        // skills-PLAN.md 6 prints "pdf @ 49f948f"; the one openai/skills commit that starts so is O2's full pin.
        Assert.Equal(PlanPins["O2"], o3.Skills.Single(s => s.Folder == "pdf").Pin);
        var a6 = CatalogueFixtures.Item(CatalogueFixtures.Real, "A6");
        Assert.Null(a6.Pin);
        Assert.Equal("ab024cdc", a6.PinHint);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_source_url_is_the_repository_or_listing_the_plan_names()
    {
        foreach (var (id, url) in PlanSources) Assert.Equal(url, CatalogueFixtures.Item(CatalogueFixtures.Real, id).SourceUrl);
        Assert.Equal("https://www.python.org/ftp/python/3.14.8/", CatalogueFixtures.Item(CatalogueFixtures.Real, "T1").SourceUrl);
        Assert.Equal("https://github.com/jgm/pandoc/releases", CatalogueFixtures.Item(CatalogueFixtures.Real, "T6").SourceUrl);
        Assert.Null(CatalogueFixtures.Item(CatalogueFixtures.Real, "T1p").SourceUrl);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_tool_version_is_the_one_ONDEMAND_2_2_prints()
    {
        foreach (var (id, version) in PlanVersions) Assert.Equal(version, CatalogueFixtures.Item(CatalogueFixtures.Real, id).Pin);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_exact_sizes_and_urls_the_plan_prints_are_there_and_no_other_size_is_claimed()
    {
        var t1 = CatalogueFixtures.Item(CatalogueFixtures.Real, "T1").Artifacts.Single();
        Assert.Equal("python-3.14.8-embed-amd64.zip", t1.Name);
        Assert.Equal("https://www.python.org/ftp/python/3.14.8/python-3.14.8-embed-amd64.zip", t1.Url);
        Assert.Equal(12_601_679L, t1.Size);
        var t10 = CatalogueFixtures.Item(CatalogueFixtures.Real, "T10").Artifacts;
        Assert.Equal(["chrome-win64.zip", "chrome-headless-shell-win64.zip"], t10.Select(a => a.Name));
        Assert.Equal([205_123_748L, 120_200_717L], t10.Select(a => a.Size));
        Assert.Equal("https://cdn.playwright.dev/builds/cft/153.0.8010.12/win64/chrome-win64.zip", t10[0].Url);
        Assert.Equal("https://cdn.playwright.dev/builds/cft/153.0.8010.12/win64/chrome-headless-shell-win64.zip", t10[1].Url);
        Assert.Equal("https://nodejs.org/dist/v24.21.0/node-v24.21.0-win-x64.zip", CatalogueFixtures.Item(CatalogueFixtures.Real, "T2").Artifacts.Single().Url);
        Assert.Equal("https://download.documentfoundation.org/libreoffice/stable/26.2.6/win/x86_64/LibreOffice_26.2.6_Win_x86-64.msi",
            CatalogueFixtures.Item(CatalogueFixtures.Real, "T3").Artifacts.Single().Url);
        var sized = CatalogueFixtures.Real.Items.SelectMany(i => i.Artifacts.Where(a => a.Size is not null).Select(a => i.Id + "/" + a.Name)).OrderBy(s => s, StringComparer.Ordinal);
        Assert.Equal(["T1/python-3.14.8-embed-amd64.zip", "T10/chrome-headless-shell-win64.zip", "T10/chrome-win64.zip"], sized);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_hash_prefixes_the_plan_prints_are_kept_as_hints_and_no_full_hash_is_claimed_without_a_pin_run()
    {
        var hints = CatalogueFixtures.Real.Items.SelectMany(i => i.Artifacts.Where(a => a.Sha256Hint is not null).Select(a => i.Id + "=" + a.Sha256Hint)).OrderBy(s => s, StringComparer.Ordinal);
        Assert.Equal(["T5=db87077e", "T6=2a77ebc2", "T7=eceb5e06", "T8=ae64e556", "T9=75d05de6"], hints);
        Assert.All(CatalogueFixtures.Real.Items, i =>
        {
            Assert.All(i.Artifacts, a => Assert.Null(a.Sha256));
            Assert.Empty(i.Files);
        });
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Licence_names_and_terms_classes_are_the_plan_s_for_the_items_it_classifies()
    {
        var real = CatalogueFixtures.Real;
        foreach (var id in new[] { "A1", "A3", "A4", "A5", "O1", "O2", "O3", "O4" })
            Assert.Equal(TermsClass.Open, CatalogueFixtures.Item(real, id).TermsClass);
        foreach (var id in new[] { "A2", "O5", "O6", "A6" }) Assert.Equal(TermsClass.Proprietary, CatalogueFixtures.Item(real, id).TermsClass);
        Assert.Equal("Proprietary", CatalogueFixtures.Item(real, "A2").LicenceName);
        Assert.Equal("Proprietary", CatalogueFixtures.Item(real, "O5").LicenceName);
        Assert.Equal("Figma Developer Terms", CatalogueFixtures.Item(real, "O6").LicenceName);
        Assert.Equal("Apache-2.0", CatalogueFixtures.Item(real, "A1").LicenceName);
        Assert.Equal("MIT", CatalogueFixtures.Item(real, "O4").LicenceName);
        Assert.Null(CatalogueFixtures.Item(real, "A6").LicenceName);
        Assert.Equal(TermsClass.None, CatalogueFixtures.Item(real, "A2").Skills.Single(s => s.Folder == "doc-coauthoring").TermsClass);
        Assert.All(CatalogueFixtures.Item(real, "A2").Skills.Where(s => s.Folder != "doc-coauthoring"), s => Assert.Equal(TermsClass.Proprietary, s.TermsClass));
        Assert.All(real.Items.Where(i => i.Kind is ItemKind.Tool or ItemKind.Pyset or ItemKind.Nodeset), i => Assert.Equal(TermsClass.Open, i.TermsClass));
        Assert.Equal("PSF-2.0", CatalogueFixtures.Item(real, "T1").LicenceName);
        Assert.Equal("MPL-2.0", CatalogueFixtures.Item(real, "T3").LicenceName);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void No_item_claims_to_be_pinned_while_a_value_is_still_missing_and_the_report_says_which_items_lack_real_values()
    {
        var report = CatalogueGate.Check(CatalogueFixtures.Real);
        Assert.Empty(report.Violations);
        foreach (var item in CatalogueFixtures.Real.Items)
        {
            var missing = CatalogueGate.Missing(item);
            Assert.Equal(missing.Count == 0 ? PinState.Pinned : PinState.Unpinned, item.PinState);
        }
        Assert.Equal(CatalogueFixtures.Real.Items.Where(i => CatalogueGate.Missing(i).Count > 0).Select(i => i.Id), report.Unpinned.Select(u => u.ItemId));
        Assert.NotEmpty(report.Unpinned);
        foreach (var unpinned in report.Unpinned) output.WriteLine($"UNPINNED {unpinned.ItemId}: lacks {string.Join(", ", unpinned.Missing)}");
        output.WriteLine($"{report.Unpinned.Count} of {CatalogueFixtures.Real.Items.Count} items still lack real values (SK-2's pin run fills them).");
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_missing_lists_name_the_fields_the_plan_does_not_give()
    {
        var report = CatalogueGate.Check(CatalogueFixtures.Real);
        IReadOnlyList<string> Of(string id) => report.Unpinned.Single(u => u.ItemId == id).Missing;
        Assert.Contains("artifacts[0].sha256", Of("T1"));
        Assert.DoesNotContain("artifacts[0].size", Of("T1"));
        Assert.DoesNotContain("artifacts[0].url", Of("T1"));
        Assert.Contains("artifacts[0].url", Of("T6"));
        Assert.Contains("artifacts[0].size", Of("T6"));
        Assert.Contains("sourceUrl", Of("T1p"));
        Assert.Contains("pin", Of("A6"));
        Assert.Contains("licenceName", Of("A6"));
        Assert.Contains("files", Of("A1"));
        Assert.Contains("skills[skill-creator].name", Of("A1"));
        Assert.DoesNotContain("pin", Of("A1"));
        Assert.DoesNotContain("pin", Of("O3"));
        Assert.Contains("skills[doc].path", Of("O3"));
        Assert.Contains("skillCount", Of("A6"));
        Assert.DoesNotContain("skillCount", Of("O6"));
        Assert.Contains("artifacts", Of("P1"));
        Assert.Contains("packages[et-xmlfile].version", Of("P1"));
        Assert.All(report.Unpinned.Where(u => u.ItemId is not "A6"), u => Assert.Contains("licenceUrl", u.Missing));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_strict_gate_a_release_will_use_fails_while_any_value_is_missing_and_names_each_item()
    {
        var strict = CatalogueGate.Check(CatalogueFixtures.Real, new CatalogueGateOptions { RequireAllPinned = true });
        var unpinned = CatalogueGate.Check(CatalogueFixtures.Real).Unpinned.Select(u => u.ItemId).ToList();
        Assert.Equal(unpinned.OrderBy(i => i, StringComparer.Ordinal), strict.Violations.Where(v => v.Rule == CatalogueRules.Unpinned).Select(v => v.ItemId!).OrderBy(i => i, StringComparer.Ordinal));
        Assert.Empty(CatalogueGate.Check(CatalogueFixtures.Pinned(), new CatalogueGateOptions { RequireAllPinned = true }).Violations);
    }
}
