// PORT-ONLY: shared helpers of the SK-1 tests (first-release feature unit SK-1, DL-1 of porting/first-release/skills-ONDEMAND-PLAN.md section 7).
// Nothing here touches the network, a real CLI, a download or the user's data folder: the real catalogue is read from the embedded
// resource and every other catalogue is built in memory from Marrow-made values.
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

internal static class CatalogueFixtures
{
    public static SkillsCatalogue Real { get; } = SkillsCatalogueLoader.LoadEmbedded();

    public static string RawJson()
    {
        using var stream = typeof(SkillsCatalogue).Assembly.GetManifestResourceStream(SkillsCatalogueLoader.ResourceName)
            ?? throw new InvalidOperationException("The embedded catalogue is missing: " + SkillsCatalogueLoader.ResourceName);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static CatalogueItem Item(SkillsCatalogue catalogue, string id) =>
        catalogue.Items.Single(i => i.Id == id);

    public static SkillsCatalogue Change(SkillsCatalogue catalogue, string id, Func<CatalogueItem, CatalogueItem> change) =>
        catalogue with { Items = catalogue.Items.Select(i => i.Id == id ? change(i) : i).ToList() };

    public static SkillsCatalogue Add(SkillsCatalogue catalogue, CatalogueItem item) =>
        catalogue with { Items = catalogue.Items.Append(item).ToList() };

    public static SkillsCatalogue Without(SkillsCatalogue catalogue, string id) =>
        catalogue with { Items = catalogue.Items.Where(i => i.Id != id).ToList() };

    public static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static string Rules(CatalogueGateReport report) =>
        string.Join(", ", report.Violations.Select(v => v.Rule + "@" + (v.ItemId ?? "-")).OrderBy(s => s, StringComparer.Ordinal));

    public static bool Fires(CatalogueGateReport report, string rule, string? itemId = null) =>
        report.Violations.Any(v => v.Rule == rule && (itemId is null || v.ItemId == itemId));

    // A tiny catalogue in which every item carries every real value, so the pinned path of the gate can be proved.
    // All values are Marrow-made fixtures (hashes are the SHA-256 of fixture words), none is a vendor value.
    public const string FixturePin = "0123456789abcdef0123456789abcdef01234567";

    public static CatalogueItem PinnedTool(string id = "X1", string exeName = "fixturetool") => new()
    {
        Id = id, Kind = ItemKind.Tool, Title = "Fixture tool", Summary = "A fixture program for the gate tests.", Vendor = "Fixture",
        CountState = CountState.Plan, PinState = PinState.Pinned, Pin = "1.0.0", SourceUrl = "https://github.com/example/fixturetool/releases",
        LicenceName = "MIT", LicenceUrl = "https://github.com/example/fixturetool/blob/main/LICENSE", LicenceFile = "LICENSE",
        TermsClass = TermsClass.Open, ApproxSizeMb = 1.0, Needs = [], Provides = [new CatalogueProvide { Kind = ProvideKind.Exe, Name = exeName }],
        Artifacts = [new CatalogueArtifact { Name = "tool.zip", Url = "https://github.com/example/fixturetool/releases/download/1.0.0/tool.zip", Size = 1024, Sha256 = Sha("tool"), Sha256Hint = null }],
        Files = [],
    };

    public static CatalogueItem PinnedPyset(string id = "X2", string moduleName = "fixturemod", double mb = 2.0) => new()
    {
        Id = id, Kind = ItemKind.Pyset, Title = "Fixture set", Summary = "A fixture package set for the gate tests.", Vendor = "Fixture",
        CountState = CountState.Plan, PinState = PinState.Pinned, Pin = "lock-1", SourceUrl = "https://files.pythonhosted.org/",
        LicenceName = "MIT", LicenceUrl = "https://files.pythonhosted.org/licence", LicenceFile = "LICENSE",
        TermsClass = TermsClass.Open, ApproxSizeMb = mb, Needs = ["X1"], Provides = [new CatalogueProvide { Kind = ProvideKind.PyModule, Name = moduleName }],
        Artifacts = [new CatalogueArtifact { Name = "fixturemod-1.0-py3-none-any.whl", Url = "https://files.pythonhosted.org/fixturemod.whl", Size = 2048, Sha256 = Sha("wheel"), Sha256Hint = null }],
        Packages = [new CataloguePackage { Name = "fixturemod", Version = "1.0", Licence = "MIT" }],
        Files = [],
    };

    public static CatalogueItem PinnedSkillset(string id = "X3") => new()
    {
        Id = id, Kind = ItemKind.Skillset, Title = "Fixture skills", Summary = "Two fixture skills for the gate tests.", Vendor = "Fixture",
        CountState = CountState.Plan, PinState = PinState.Pinned, Pin = FixturePin, SourceUrl = "https://github.com/example/fixtureskills",
        LicenceName = "Apache-2.0", LicenceFile = "LICENSE", LicenceUrl = "https://raw.githubusercontent.com/example/fixtureskills/" + FixturePin + "/LICENSE",
        TermsClass = TermsClass.Open, ApproxSizeMb = 0.5, Needs = ["X1"], Provides = [], Artifacts = [],
        SkillCount = 2, SkillsListed = true,
        Skills =
        [
            new CatalogueSkill { Folder = "alpha-skill", Name = "alpha-skill", Path = "skills/alpha-skill", Needs = ["X1"] },
            new CatalogueSkill { Folder = "beta", Name = "beta", Path = "skills/beta", Needs = [] },
        ],
        Files =
        [
            new CatalogueFile { Path = "skills/alpha-skill/SKILL.md", Size = 10, Sha256 = Sha("alpha") },
            new CatalogueFile { Path = "skills/beta/SKILL.md", Size = 11, Sha256 = Sha("beta") },
        ],
    };

    public static SkillsCatalogue Pinned() => new()
    {
        Schema = SkillsCatalogueCounts.SchemaVersion, ReadOn = "2026-10-04",
        Items = [PinnedTool(), PinnedPyset(), PinnedSkillset()],
    };

    // ED-NEW-dl-7: the pinned fixture as the release gate wants it, with each runnable item's installer parameters and its licence file pinned in files.
    public static CatalogueItem InstallableTool(string id = "X1", string exeName = "fixturetool") => PinnedTool(id, exeName) with
    {
        Files = [new CatalogueFile { Path = "LICENSE", Size = 3, Sha256 = Sha("MIT") }],
        Install = new CatalogueInstall
        {
            Kind = InstallKind.Zip, Archives = [new CatalogueInstallArchive { Artifact = "tool.zip", Prefix = "tool" }],
            Shims = [new CatalogueShim { Name = exeName + ".exe", Target = "tool/bin/" + exeName + ".exe" }],
            Exports = [new CatalogueExport { Name = "FIXTURETOOL_HOME", Value = "{folder}/tool" }],
        },
    };

    public static CatalogueItem InstallablePyset(string id = "X2", string moduleName = "fixturemod") => PinnedPyset(id, moduleName) with
    {
        Files = [new CatalogueFile { Path = "LICENSE", Size = 3, Sha256 = Sha("MIT") }],
        Install = new CatalogueInstall { Kind = InstallKind.Pyset, PythonPackages = true },
    };

    public static SkillsCatalogue Installable() => Pinned() with { Items = [InstallableTool(), InstallablePyset(), PinnedSkillset()] };
}
