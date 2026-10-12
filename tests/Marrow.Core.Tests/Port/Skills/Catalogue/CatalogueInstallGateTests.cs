using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// RR-14, ED-NEW-dl-7: the gate checks every installer parameter against the item's kind, artifacts, needs and provides, so a catalogue that passes cannot ask an installer for a
// file it never downloads, publish a name the item does not provide, link a shim the needed item does not have, or set PATH. Marrow-made fixtures only.
[Trait("Feature", "RR-14")]
[Trait("Spec", "DL-1")]
[Trait("Rule", "R-TEST-4")]
public sealed class CatalogueInstallGateTests
{
    private static CatalogueItem Python(string id = "XP") => CatalogueFixtures.InstallableTool(id, "fixturepython") with
    {
        Artifacts =
        [
            new CatalogueArtifact { Name = "embed.zip", Url = "https://www.python.org/ftp/embed.zip", Size = 10, Sha256 = CatalogueFixtures.Sha("embed") },
            new CatalogueArtifact { Name = "pip-1.0-py3-none-any.whl", Url = "https://files.pythonhosted.org/pip.whl", Size = 11, Sha256 = CatalogueFixtures.Sha("pip") },
        ],
        Provides = [new CatalogueProvide { Kind = ProvideKind.Exe, Name = "fixturepython" }],
        Install = new CatalogueInstall
        {
            Kind = InstallKind.PythonEmbed, Archives = [new CatalogueInstallArchive { Artifact = "embed.zip" }],
            Python = new CataloguePythonEmbed { PipArtifact = "pip-1.0-py3-none-any.whl", PthFile = "python314._pth", StandardLibraryZip = "python314.zip", AgentVersion = "3.14" },
            Shims = [new CatalogueShim { Name = "fixturepython.exe", Target = "python.exe", Python = true }],
        },
    };

    private static CatalogueItem Pip(string id = "XQ") => CatalogueFixtures.InstallableTool(id, "fixturepip") with
    {
        Needs = ["XP"],
        Install = new CatalogueInstall
        {
            Kind = InstallKind.Zip, Archives = [new CatalogueInstallArchive { Artifact = "tool.zip" }],
            Shims = [new CatalogueShim { Name = "fixturepip.exe", Item = "XP", Shim = "fixturepython.exe", Arguments = ["-m", "pip"] }],
        },
    };

    private static SkillsCatalogue Catalogue() => CatalogueFixtures.Installable() with
    {
        Items = [.. CatalogueFixtures.Installable().Items, Python(), Pip()],
    };

    private static CatalogueGateReport Strict(SkillsCatalogue catalogue) => CatalogueGate.Check(catalogue, new CatalogueGateOptions { RequireAllPinned = true });

    private static CatalogueInstall With(string id, Func<CatalogueInstall, CatalogueInstall> change) => change(CatalogueFixtures.Item(Catalogue(), id).Install!);

    [Fact]
    public void A_catalogue_with_a_Python_embed_a_linked_pip_a_zip_tool_and_a_package_set_passes_the_release_gate()
    {
        var report = Strict(Catalogue());
        Assert.True(report.Violations.Count == 0, CatalogueFixtures.Rules(report));
        var text = SkillsCatalogueWriter.Serialize(Catalogue());
        var back = SkillsCatalogueLoader.Parse(text);
        Assert.Equal(text, SkillsCatalogueWriter.Serialize(back));
        Assert.Equal(CatalogueFixtures.Item(Catalogue(), "XQ").Install!.Shims.Single().Arguments, CatalogueFixtures.Item(back, "XQ").Install!.Shims.Single().Arguments);
        Assert.Equal(CatalogueFixtures.Item(Catalogue(), "XP").Install!.Python, CatalogueFixtures.Item(back, "XP").Install!.Python);
    }

    public static TheoryData<string, string> Broken() => new()
    {
        { "XP", "a tool installed as a package set" },
        { "X1", "an archive that is not an artifact" },
        { "X1", "an artifact installed twice" },
        { "XP", "an artifact downloaded but never installed" },
        { "X1", "a shim that is not an exe or cmd" },
        { "X1", "a shim with both a target and a link" },
        { "X1", "a shim the item does not provide" },
        { "X1", "a Python shim without a Python embed" },
        { "XQ", "a link to an item that is not a need" },
        { "XQ", "a link to a shim the needed item does not publish" },
        { "X1", "an export of PATH" },
        { "X1", "a target outside the folder" },
        { "XP", "a pip artifact that is not a wheel" },
        { "XP", "a ._pth that is not a file name" },
        { "X2", "a package set off the Python path" },
        { "X1", "a tool on the Python path" },
        { "X1", "a marker outside the folder" },
        { "X1", "a shim argument with a control character" },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void A_broken_installer_parameter_is_a_gate_violation_of_its_item(string id, string what)
    {
        CatalogueInstall Change(CatalogueInstall i) => what switch
        {
            "a tool installed as a package set" => i with { Kind = InstallKind.Pyset },
            "an archive that is not an artifact" => i with { Archives = [new CatalogueInstallArchive { Artifact = "missing.zip" }] },
            "an artifact installed twice" => i with { Archives = [.. i.Archives, i.Archives[0]] },
            "an artifact downloaded but never installed" => i with { Python = i.Python! with { PipArtifact = "embed.zip" } },
            "a shim that is not an exe or cmd" => i with { Shims = [i.Shims[0] with { Name = "fixturetool.ps1" }] },
            "a shim with both a target and a link" => i with { Shims = [i.Shims[0] with { Item = "X2", Shim = "x.exe" }] },
            "a shim the item does not provide" => i with { Shims = [i.Shims[0] with { Name = "other.exe" }] },
            "a Python shim without a Python embed" => i with { Shims = [i.Shims[0] with { Python = true }] },
            "a link to an item that is not a need" => i with { Shims = [i.Shims[0] with { Item = "X1", Shim = "fixturetool.exe" }] },
            "a link to a shim the needed item does not publish" => i with { Shims = [i.Shims[0] with { Shim = "absent.exe" }] },
            "an export of PATH" => i with { Exports = [new CatalogueExport { Name = "PATH", Value = "{folder}/tool" }] },
            "a target outside the folder" => i with { Shims = [i.Shims[0] with { Target = "../fixturetool.exe" }] },
            "a pip artifact that is not a wheel" => i,
            "a ._pth that is not a file name" => i with { Python = i.Python! with { PthFile = "lib/python314._pth" } },
            "a package set off the Python path" => i with { PythonPackages = false },
            "a tool on the Python path" => i with { PythonPackages = true },
            "a marker outside the folder" => i with { Markers = ["../INSTALLATION_COMPLETE"] },
            "a shim argument with a control character" => i with { Shims = [i.Shims[0] with { Arguments = ["a\nb"] }] },
            _ => throw new ArgumentOutOfRangeException(nameof(what)),
        };
        var catalogue = CatalogueFixtures.Change(Catalogue(), id, item => what == "a pip artifact that is not a wheel"
            ? item with { Artifacts = [item.Artifacts[0], item.Artifacts[1] with { Name = "pip.zip" }], Install = item.Install! with { Python = item.Install.Python! with { PipArtifact = "pip.zip" } } }
            : item with { Install = Change(item.Install!) });
        var report = CatalogueGate.Check(catalogue);
        Assert.True(CatalogueFixtures.Fires(report, CatalogueRules.Install, id), what + ": " + CatalogueFixtures.Rules(report));
    }

    [Fact]
    public void Two_items_exporting_one_name_and_a_skill_set_or_extra_file_with_installer_parameters_are_refused()
    {
        var twice = CatalogueFixtures.Change(Catalogue(), "XQ", i => i with { Install = i.Install! with { Exports = [new CatalogueExport { Name = "FIXTURETOOL_HOME", Value = "1" }] } });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(twice), CatalogueRules.Install), CatalogueFixtures.Rules(CatalogueGate.Check(twice)));
        var skillset = CatalogueFixtures.Change(Catalogue(), "X3", i => i with { Install = new CatalogueInstall { Kind = InstallKind.Zip } });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(skillset), CatalogueRules.Kind, "X3"));
        var extra = CatalogueFixtures.Change(Catalogue(), "X1", i => i with { Files = [.. i.Files, new CatalogueFile { Path = "README", Size = 1, Sha256 = CatalogueFixtures.Sha("r") }] });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(extra), CatalogueRules.Kind, "X1"));
        var unpinnedLicence = CatalogueFixtures.Change(Catalogue(), "X1", i => i with { Files = [] });
        Assert.Equal("its licence file is not pinned in files", CatalogueGate.InstallMissing(CatalogueFixtures.Item(unpinnedLicence, "X1")));
        Assert.True(CatalogueFixtures.Fires(Strict(unpinnedLicence), CatalogueRules.Install, "X1"));
    }

    [Fact]
    public void A_null_element_inside_installer_parameters_is_refused_by_the_loader()
    {
        var text = SkillsCatalogueWriter.Serialize(Catalogue());
        var broken = text.Replace("\"-m\",", "null,", StringComparison.Ordinal);
        Assert.NotEqual(text, broken);
        Assert.Throws<CatalogueFormatException>(() => SkillsCatalogueLoader.Parse(broken));
    }
}
