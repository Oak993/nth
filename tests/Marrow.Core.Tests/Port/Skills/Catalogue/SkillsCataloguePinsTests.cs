using System.Text.RegularExpressions;
using Marrow.Core.Port.Skills.Catalogue;
using Xunit.Abstractions;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Pin run 3 (2026-10-11, OD for the owner's approval of exact versions; ED-NEW-dl-11). The embedded catalogue holds the facts the ED-544 generator read from each item's official
// source and the resolver verified (signatures, published digests): every pin, artifact URL, exact size and full SHA-256, and every licence file with its own size and SHA-256.
// The six items with no official source or no licence file covering them stay unpinned with the values they lack; nothing is filled in for them. These assertions replace the
// interim ones that held the plan's 2026-10-04 transcription (ED-NEW-dl-11 quotes them).
public sealed partial class SkillsCataloguePinsTests(ITestOutputHelper output)
{
    [GeneratedRegex("^[0-9a-f]{40}$")] private static partial Regex Commit();

    private static readonly string[] Blocked = ["A3", "A4", "A6", "O4", "O5", "T4"];

    private static readonly Dictionary<string, string> Pins = new()
    {
        ["A1"] = "8a1541c4a3ffa5a20a5a91de0dcf3f0bab1d1ef4", ["A2"] = "8a1541c4a3ffa5a20a5a91de0dcf3f0bab1d1ef4", ["A5"] = "e96556b637b56d6cc3a5ad33987009be9e60aa5c",
        ["O1"] = "2739e828581c3deee59a270cd740675dc278ed14", ["O2"] = "49f948faa9258a0c61caceaf225e179651397431", ["O6"] = "49f948faa9258a0c61caceaf225e179651397431",
        ["T1"] = "3.14.8", ["T1p"] = "26.2.1", ["T2"] = "24.21.0", ["T3"] = "26.8.1", ["T5"] = "12.4.2", ["T6"] = "3.12.1", ["T7"] = "2.56.0.2", ["T8"] = "2.102.0", ["T9"] = "0.13.0",
        ["T10"] = "153.0.8010.12",
        ["P1"] = "lock-538e7cbf0fcfcb2b", ["P2"] = "lock-aa3d89398a7bb84e", ["P3"] = "lock-f6ec0d1e8d733197", ["P4"] = "lock-a7605968006c29e1", ["P5"] = "lock-6381065e41bed01f",
        ["P6"] = "lock-3fbbdecaebfda9c0",
    };

    private static readonly (string Id, string Name, string Url, long Size, string Sha256)[] ToolArtifacts =
    [
        ("T1", "python-3.14.8-embed-amd64.zip", "https://www.python.org/ftp/python/3.14.8/python-3.14.8-embed-amd64.zip", 12601679L, "a93abe456ab01bd96d7a085b3cdb6566b3063f4241360d114142fbdb07f0a310"),
        ("T1", "pip-26.2.1-py3-none-any.whl", "https://files.pythonhosted.org/packages/f3/6e/1736e5b4ae2b778ef2f81c47d797de9f891d4d8acb047a24ca37a60294dd/pip-26.2.1-py3-none-any.whl", 1816632L, "71138adf1f4ca900cdb7d289c21b7494329f2332b6d85f0e1c42108c0384ed3e"),
        ("T1p", "pip-26.2.1-py3-none-any.whl", "https://files.pythonhosted.org/packages/f3/6e/1736e5b4ae2b778ef2f81c47d797de9f891d4d8acb047a24ca37a60294dd/pip-26.2.1-py3-none-any.whl", 1816632L, "71138adf1f4ca900cdb7d289c21b7494329f2332b6d85f0e1c42108c0384ed3e"),
        ("T2", "node-v24.21.0-win-x64.zip", "https://nodejs.org/dist/v24.21.0/node-v24.21.0-win-x64.zip", 37618919L, "158f7685b44de51f6c0df1d153526cbcd3e1bc739a8dfc607721cef75de9e541"),
        ("T3", "LibreOffice_26.8.1_Win_x86-64.msi", "https://download.documentfoundation.org/libreoffice/stable/26.8.1/win/x86_64/LibreOffice_26.8.1_Win_x86-64.msi", 374870016L, "c6298b10cfa1748bcbedad94c62ce771e240e5021bfd6d7a967cd571b04b5312"),
        ("T5", "qpdf-12.4.2-msvc64.zip", "https://github.com/qpdf/qpdf/releases/download/v12.4.2/qpdf-12.4.2-msvc64.zip", 28183147L, "db87077e683630c1217e0e8f9a20a9749d952ab676e881c3689187763a5de25d"),
        ("T6", "pandoc-3.12.1-windows-x86_64.zip", "https://github.com/jgm/pandoc/releases/download/3.12.1/pandoc-3.12.1-windows-x86_64.zip", 42154914L, "6ef431cb20b2c24a0371fc19200dc49d452be54b1b5c9239f3462841ef53e587"),
        ("T7", "PortableGit-2.56.0.2-64-bit.7z.exe", "https://github.com/git-for-windows/git/releases/download/v2.56.0.windows.2/PortableGit-2.56.0.2-64-bit.7z.exe", 60027568L, "075e158ef8e1f0ab80b347e245405d3eca735c2dc88fd8e032e137d0ca61f61b"),
        ("T8", "gh_2.102.0_windows_amd64.zip", "https://github.com/cli/cli/releases/download/v2.102.0/gh_2.102.0_windows_amd64.zip", 15512013L, "ae64e556ecc240b200f7eba60d550e4bb60d78e860e69dd88c449405b86067f4"),
        ("T9", "uv-x86_64-pc-windows-msvc.zip", "https://github.com/astral-sh/uv/releases/download/0.13.0/uv-x86_64-pc-windows-msvc.zip", 15722003L, "088962f9e7b7bd9ea740c04c650b2a21c8928c345bd99ac24350dc924dba656c"),
        ("T10", "chrome-win64.zip", "https://cdn.playwright.dev/builds/cft/153.0.8010.12/win64/chrome-win64.zip", 205123748L, "415968b02065d4a9e2c10b85f0ae9f489b8fba500e94d9d0a7b7c4852a7234c1"),
        ("T10", "chrome-headless-shell-win64.zip", "https://cdn.playwright.dev/builds/cft/153.0.8010.12/win64/chrome-headless-shell-win64.zip", 120200717L, "7aec872f3090e639c4237467624ea863c20fe2878914c93a6556bdbb52aa6c4c"),
    ];

    private static readonly (string Id, string Name, string File, string Url, long Size, string Sha256)[] Licences =
    [
        ("A1", "Apache-2.0", "skills/academy-guide/LICENSE.txt", "https://raw.githubusercontent.com/anthropics/skills/8a1541c4a3ffa5a20a5a91de0dcf3f0bab1d1ef4/skills/academy-guide/LICENSE.txt", 11345L, "bc6b3af2f331cbc7fb0da1344efb2cbe5877a31498b4d70dbc7000f3405a1362"),
        ("A2", "Proprietary", "skills/docx/LICENSE.txt", "https://raw.githubusercontent.com/anthropics/skills/8a1541c4a3ffa5a20a5a91de0dcf3f0bab1d1ef4/skills/docx/LICENSE.txt", 1467L, "79f6d8f5b427252fa3b1c11ecdbdb6bf610b944f7530b4de78f770f38741cfaa"),
        ("A5", "Apache-2.0", "clinical-trial-protocol-skill/LICENSE.txt", "https://raw.githubusercontent.com/anthropics/life-sciences/e96556b637b56d6cc3a5ad33987009be9e60aa5c/clinical-trial-protocol-skill/LICENSE.txt", 10254L, "7505b489cc8ad7f16ba08343184320f1583303cfacdb9121b9b756bc073df1ab"),
        ("O1", "Apache-2.0", "LICENSE", "https://raw.githubusercontent.com/openai/codex/2739e828581c3deee59a270cd740675dc278ed14/LICENSE", 10926L, "d17f227e4df5da1600391338865ce0f3055211760a36688f816941d58232d8dc"),
        ("O2", "Apache-2.0, MIT", "skills/.curated/cli-creator/LICENSE.txt", "https://raw.githubusercontent.com/openai/skills/49f948faa9258a0c61caceaf225e179651397431/skills/.curated/cli-creator/LICENSE.txt", 10776L, "4dd13869245e356246a5b770723247bbb80a8f07a181d1d3d873a1734297cdb9"),
        ("O3", "Apache-2.0", "skills/.curated/doc/LICENSE.txt", "https://raw.githubusercontent.com/openai/skills/45d05d75363abf13f99d09e899d61e07b8010685/skills/.curated/doc/LICENSE.txt", 10776L, "4dd13869245e356246a5b770723247bbb80a8f07a181d1d3d873a1734297cdb9"),
        ("O6", "Figma Developer Terms", "skills/.curated/figma-code-connect-components/LICENSE.TXT", "https://raw.githubusercontent.com/openai/skills/49f948faa9258a0c61caceaf225e179651397431/skills/.curated/figma-code-connect-components/LICENSE.TXT", 440L, "36bdad8e8a43a282ba3a5af4ac022d8b117c5826b8b473fe6a32622e83856ae3"),
        ("T1", "PSF-2.0", "LICENSE", "https://raw.githubusercontent.com/python/cpython/v3.14.8/LICENSE", 13804L, "b0e25a78cffb43f4d92de8b61ccfa1f1f98ecbc22330b54b5251e7b6ba010231"),
        ("T1p", "MIT", "LICENSE.txt", "https://raw.githubusercontent.com/pypa/pip/26.2.1/LICENSE.txt", 1093L, "634300a669d49aeae65b12c6c48c924c51a4cdf3d1ff086dc3456dc8bcaa2104"),
        ("T2", "MIT", "LICENSE", "https://raw.githubusercontent.com/nodejs/node/v24.21.0/LICENSE", 157609L, "5888dbb9a1d2b18f2c3e6c5f6af1b39de658372b402a0577b002777f14c62ace"),
        ("T3", "MPL-2.0", "COPYING.MPL", "https://raw.githubusercontent.com/LibreOffice/core/libreoffice-26.8.1.1/COPYING.MPL", 16725L, "1f256ecad192880510e84ad60474eab7589218784b9a50bc7ceee34c2b91f1d5"),
        ("T5", "Apache-2.0", "LICENSE.txt", "https://raw.githubusercontent.com/qpdf/qpdf/v12.4.2/LICENSE.txt", 11358L, "cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30"),
        ("T6", "GPL-2.0-or-later", "COPYING.md", "https://raw.githubusercontent.com/jgm/pandoc/3.12.1/COPYING.md", 17763L, "3ebfbf9235b049d384cdba5a66a1a3434bb544391e720b76faa8e1b3ee61a4e5"),
        ("T7", "GPL-2.0-only", "COPYING", "https://raw.githubusercontent.com/git-for-windows/git/v2.56.0.windows.2/COPYING", 18765L, "5b2198d1645f767585e8a88ac0499b04472164c0d2da22e75ecf97ef443ab32e"),
        ("T8", "MIT", "LICENSE", "https://raw.githubusercontent.com/cli/cli/v2.102.0/LICENSE", 1068L, "6da4adc42392c8485e40b4251c7e332fc3352df1947c9ffade71dd60b14a7a4f"),
        ("T9", "MIT OR Apache-2.0", "LICENSE-APACHE", "https://raw.githubusercontent.com/astral-sh/uv/0.13.0/LICENSE-APACHE", 11357L, "c71d239df91726fc519c6eb72d318ec65820627232b2f796219e87dcf35d0ab4"),
        ("T10", "BSD-3-Clause", "LICENSE", "https://raw.githubusercontent.com/chromium/chromium/153.0.8010.12/LICENSE", 1536L, "368cca1106be99d39ecd32a38d8305585d802a475effb66380b91ffc9bcf709b"),
        ("P1", "MIT, BSD-3-Clause, Apache-2.0, BSD-2-Clause and others", "LICENSE", "https://raw.githubusercontent.com/python-openxml/python-docx/v1.2.0/LICENSE", 1104L, "7652f271e46d0d533e9dc463f3b5fcbdcacf4d6a9c8d6b554d15efd0f37f6132"),
        ("P2", "MIT, BSD-3-Clause, Apache-2.0, MIT-0 and others", "LICENSE", "https://raw.githubusercontent.com/py-pdf/pypdf/6.20.0/LICENSE", 1605L, "a97ac230e5f33ef10a5367a850eb01f91f1a0b064e34742c7794d2294557f524"),
        ("P3", "MIT, Apache-2.0, BSD-3-Clause, MIT-CMU and others", "LICENSE", "https://raw.githubusercontent.com/python-pillow/Pillow/12.3.0/LICENSE", 1457L, "15181e7363dca9aed78b79bebebc7fde7f1814b8bd311ea3b87ae8ccadfc185b"),
        ("P4", "MIT, Apache-2.0, PSF-2.0", "LICENSE", "https://raw.githubusercontent.com/microsoft/playwright-python/v1.63.0/LICENSE", 11399L, "7fab1461b41970ff376f1c9303a637076bfaaeb71cd12dd3a1c44aaf59a1a2b9"),
        ("P5", "BSD-2-Clause, BSD-3-Clause, MIT-CMU", "LICENSE", "https://raw.githubusercontent.com/imageio/imageio/v2.38.1/LICENSE", 1307L, "c453cf1368bb9e009e4d7e5334fc78511e665316a612ef54ee4fe1245678fe2c"),
        ("P6", "MIT, Apache-2.0, ISC, 0BSD and others", "LICENSE", "https://raw.githubusercontent.com/dolanmiu/docx/9.10.0/LICENSE", 1072L, "b5b2364c0c95a26b2a2399d7e706a61acac680be1d5b180b00592ee85830bffb"),
    ];

    private static CatalogueItem Item(string id) => CatalogueFixtures.Item(CatalogueFixtures.Real, id);

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_approved_item_carries_the_pin_run_s_exact_pin_and_the_six_blocked_items_stay_unpinned()
    {
        foreach (var (id, pin) in Pins)
        {
            Assert.Equal(pin, Item(id).Pin);
            Assert.Equal(PinState.Pinned, Item(id).PinState);
        }
        foreach (var id in new[] { "A1", "A2", "A5", "O1", "O2", "O6" }) Assert.Matches(Commit(), Item(id).Pin!);
        var o3 = Item("O3");
        Assert.Null(o3.Pin);
        Assert.Equal(PinState.Pinned, o3.PinState);
        Assert.Equal("45d05d75363abf13f99d09e899d61e07b8010685", o3.Skills.Single(s => s.Folder == "doc").Pin);
        Assert.Equal("e6afb0d74cc75d220df2faf3dd6c635c2dc6a108", o3.Skills.Single(s => s.Folder == "spreadsheet").Pin);
        Assert.Equal("e6afb0d74cc75d220df2faf3dd6c635c2dc6a108", o3.Skills.Single(s => s.Folder == "slides").Pin);
        Assert.Equal(Pins["O2"], o3.Skills.Single(s => s.Folder == "pdf").Pin);
        Assert.Equal(Blocked, CatalogueFixtures.Real.Items.Where(i => i.PinState == PinState.Unpinned).Select(i => i.Id).ToArray());
        Assert.Equal(29, CatalogueFixtures.Real.Items.Count);
        Assert.Equal("2026-10-11", CatalogueFixtures.Real.ReadOn);
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_tool_artifact_is_the_exact_file_the_pin_run_hashed_at_its_official_url()
    {
        var actual = CatalogueFixtures.Real.Items.Where(i => i.Kind == ItemKind.Tool && i.PinState == PinState.Pinned)
            .SelectMany(i => i.Artifacts.Select(a => (i.Id, a.Name, a.Url!, a.Size!.Value, a.Sha256!))).ToArray();
        Assert.Equal(ToolArtifacts, actual);
        Assert.All(CatalogueFixtures.Real.Items.Where(i => i.PinState == PinState.Pinned).SelectMany(i => i.Artifacts), a => Assert.True(CatalogueHosts.IsAllowed(a.Url), a.Url));
        // ED-NEW-dl-11 (5): where the run pinned the very file the plan printed a digest prefix for (same release, same name), the prefix stays as the hint the gate checks it against.
        Assert.Equal([("T5", "db87077e"), ("T8", "ae64e556")], CatalogueFixtures.Real.Items.SelectMany(i => i.Artifacts.Where(a => a.Sha256Hint is not null).Select(a => (i.Id, a.Sha256Hint!))));
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_pinned_item_names_its_licence_by_SPDX_and_pins_its_licence_file_with_the_bytes_read_at_the_source()
    {
        var actual = CatalogueFixtures.Real.Items.Where(i => i.PinState == PinState.Pinned).Select(i =>
        {
            var file = i.Files.Single(f => f.Path == i.LicenceFile);
            return (i.Id, i.LicenceName!, i.LicenceFile!, i.LicenceUrl!, file.Size, file.Sha256);
        }).ToArray();
        Assert.Equal(Licences, actual);
        Assert.All(CatalogueFixtures.Real.Items.Where(i => i.PinState == PinState.Pinned && i.Kind is ItemKind.Tool or ItemKind.Pyset or ItemKind.Nodeset),
            i => Assert.Equal([i.LicenceFile], i.Files.Select(f => f.Path)));
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_package_set_pins_each_wheel_or_tarball_with_its_version_and_its_own_licence_metadata()
    {
        foreach (var (id, count) in new[] { ("P1", 27), ("P2", 11), ("P3", 13), ("P4", 4), ("P5", 4), ("P6", 40) })
        {
            var set = Item(id);
            Assert.Equal(count, set.Artifacts.Count);
            Assert.Equal(count, set.Packages.Count);
            Assert.All(set.Artifacts, a => Assert.Matches("^[0-9a-f]{64}$", a.Sha256!));
            Assert.All(set.Packages, p => Assert.False(string.IsNullOrEmpty(p.Version), p.Name));
        }
        Assert.Equal("1.2.0", Item("P1").Packages.Single(p => p.Name == "python-docx").Version);
        Assert.Equal("MIT", Item("P1").Packages.Single(p => p.Name == "python-docx").Licence);
        Assert.Equal("1.63.0", Item("P4").Packages.Single(p => p.Name == "playwright").Version);
        Assert.Equal("Apache-2.0 AND LGPL-3.0-or-later", Item("P6").Packages.Single(p => p.Name == "@img/sharp-win32-x64").Licence);
        Assert.Null(Item("P3").Packages.Single(p => p.Name == "matplotlib").Licence); // its metadata states a full licence text, not a name; the notes say so
        Assert.Contains("matplotlib", Item("P3").LicenceNotes, StringComparison.Ordinal);
        Assert.All(Item("P6").Install!.NodeTree, n => Assert.Matches("^sha512-[A-Za-z0-9+/]{86}==$", n.Integrity));
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Terms_classes_stay_the_plan_s_and_every_runnable_item_is_open()
    {
        var real = CatalogueFixtures.Real;
        foreach (var id in new[] { "A1", "A3", "A4", "A5", "O1", "O2", "O3", "O4" }) Assert.Equal(TermsClass.Open, Item(id).TermsClass);
        foreach (var id in new[] { "A2", "O5", "O6", "A6" }) Assert.Equal(TermsClass.Proprietary, Item(id).TermsClass);
        Assert.Equal(TermsClass.None, Item("A2").Skills.Single(s => s.Folder == "doc-coauthoring").TermsClass);
        Assert.All(Item("A2").Skills.Where(s => s.Folder != "doc-coauthoring"), s => Assert.Equal(TermsClass.Proprietary, s.TermsClass));
        Assert.All(real.Items.Where(i => i.Kind is ItemKind.Tool or ItemKind.Pyset or ItemKind.Nodeset), i => Assert.Equal(TermsClass.Open, i.TermsClass));
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_gate_finds_no_violation_and_only_the_blocked_items_lack_values_each_with_what_it_lacks()
    {
        var report = CatalogueGate.Check(CatalogueFixtures.Real);
        Assert.Empty(report.Violations);
        foreach (var item in CatalogueFixtures.Real.Items) Assert.Equal(CatalogueGate.Missing(item).Count == 0 ? PinState.Pinned : PinState.Unpinned, item.PinState);
        Assert.Equal(Blocked, report.Unpinned.Select(u => u.ItemId).ToArray());
        IReadOnlyList<string> Of(string id) => report.Unpinned.Single(u => u.ItemId == id).Missing;
        Assert.Equal(["licenceFile", "licenceUrl", "files", "skills"], Of("A3"));
        Assert.Equal(["licenceFile", "licenceUrl", "files", "skills"], Of("A4"));
        Assert.Equal(["pin", "licenceName", "licenceFile", "licenceUrl", "files", "skillCount", "skills"], Of("A6"));
        Assert.Equal(["licenceFile", "licenceUrl", "files", "skills"], Of("O4"));
        Assert.Equal(["licenceFile", "licenceUrl", "files", "skills"], Of("O5"));
        Assert.Equal(["licenceFile", "licenceUrl", "artifacts[0].url", "artifacts[0].size", "artifacts[0].sha256"], Of("T4"));
        foreach (var unpinned in report.Unpinned) output.WriteLine($"UNPINNED {unpinned.ItemId}: lacks {string.Join(", ", unpinned.Missing)}");
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_release_gate_fails_only_on_the_blocked_items_and_every_pinned_runnable_item_carries_its_installer()
    {
        var strict = CatalogueGate.Check(CatalogueFixtures.Real, new CatalogueGateOptions { RequireAllPinned = true });
        Assert.All(strict.Violations, v => Assert.Equal(CatalogueRules.Unpinned, v.Rule));
        Assert.Equal(Blocked, strict.Violations.Select(v => v.ItemId!).ToArray());
        Assert.All(CatalogueFixtures.Real.Items.Where(i => i.PinState == PinState.Pinned), i => Assert.Null(CatalogueGate.InstallMissing(i)));
        Assert.All(CatalogueFixtures.Real.Items.Where(i => i.Kind is ItemKind.Skillset or ItemKind.Plugin), i => Assert.Null(i.Install));
        Assert.Empty(CatalogueGate.Check(CatalogueFixtures.Installable(), new CatalogueGateOptions { RequireAllPinned = true }).Violations);
        var bare = CatalogueFixtures.Change(CatalogueFixtures.Installable(), "X1", i => i with { Install = null });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(bare, new CatalogueGateOptions { RequireAllPinned = true }), CatalogueRules.Install, "X1"));
        Assert.Empty(CatalogueGate.Check(bare).Violations); // only the release gate asks for installers
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-4")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_runnable_item_installs_by_its_kind_and_publishes_exactly_the_shims_links_and_exports_the_plan_names()
    {
        string Shims(string id) => string.Join("; ", Item(id).Install!.Shims.Select(s =>
            s.Name + "=" + (s.Target ?? s.Item + ":" + s.Shim) + (s.Arguments.Count > 0 ? " " + string.Join(' ', s.Arguments) : "") + (s.Python ? " [python]" : "") + (s.LibreOffice ? " [libreoffice]" : "")));
        Assert.Equal(InstallKind.PythonEmbed, Item("T1").Install!.Kind);
        Assert.Equal(new CataloguePythonEmbed { PipArtifact = "pip-26.2.1-py3-none-any.whl", PthFile = "python314._pth", StandardLibraryZip = "python314.zip", AgentVersion = "3.14" }, Item("T1").Install!.Python);
        Assert.Equal("python.exe=python.exe [python]; python3.exe=python.exe [python]; py.exe=python.exe [python]", Shims("T1"));
        Assert.Equal("pip.exe=T1:python.exe -m pip", Shims("T1p"));
        Assert.Equal("node.exe=node-v24.21.0-win-x64/node.exe; npm.cmd=node-v24.21.0-win-x64/node.exe {folder}/node-v24.21.0-win-x64/node_modules/npm/bin/npm-cli.js; " +
            "npx.cmd=node-v24.21.0-win-x64/node.exe {folder}/node-v24.21.0-win-x64/node_modules/npm/bin/npx-cli.js", Shims("T2"));
        Assert.Equal((InstallKind.MsiAdmin, "soffice.exe=program/soffice.exe [libreoffice]"), (Item("T3").Install!.Kind, Shims("T3")));
        Assert.Equal("qpdf.exe=qpdf-12.4.2-msvc64/bin/qpdf.exe", Shims("T5"));
        Assert.Equal("pandoc.exe=pandoc-3.12.1/pandoc.exe", Shims("T6"));
        Assert.Equal((InstallKind.Sfx7z, "bash.exe=bin/bash.exe; sh.exe=bin/sh.exe"), (Item("T7").Install!.Kind, Shims("T7")));
        Assert.Equal(("gh", "gh.exe=gh/bin/gh.exe"), (Item("T8").Install!.Archives.Single().Prefix, Shims("T8")));
        Assert.Equal("uv.exe=uv.exe", Shims("T9"));
        var t10 = Item("T10").Install!;
        Assert.Equal(["chrome-win64.zip=chromium-1243", "chrome-headless-shell-win64.zip=chromium_headless_shell-1243"], t10.Archives.Select(a => a.Artifact + "=" + a.Prefix));
        Assert.Equal(["chromium-1243/INSTALLATION_COMPLETE", "chromium_headless_shell-1243/INSTALLATION_COMPLETE"], t10.Markers);
        Assert.Equal(["PLAYWRIGHT_BROWSERS_PATH={folder}", "PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS=1"], t10.Exports.Select(e => e.Name + "=" + e.Value));
        foreach (var id in new[] { "P1", "P2", "P3", "P4", "P5" }) Assert.Equal((InstallKind.Pyset, true), (Item(id).Install!.Kind, Item(id).Install!.PythonPackages));
        var p6 = Item("P6").Install!;
        Assert.Equal(InstallKind.Nodeset, p6.Kind);
        Assert.Equal(Item("P6").Artifacts.Select(a => a.Name).Order(StringComparer.Ordinal), p6.NodeTree.Select(n => n.Artifact).Order(StringComparer.Ordinal));
        Assert.Contains(p6.NodeTree, n => n.Path == "node_modules/docx" && n.Artifact == "docx-9.10.0.tgz");
        Assert.Contains(p6.NodeTree, n => n.Path == "node_modules/pptxgenjs/node_modules/@types/node" && n.Artifact == "node-22.20.5.tgz");
        Assert.Equal(["NODE_PATH={folder}/node_modules"], p6.Exports.Select(e => e.Name + "=" + e.Value));
    }
}
