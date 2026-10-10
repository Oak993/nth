// PORT-ONLY: shared helpers of the SK-15 Core tests (DL-17). Every item is a Marrow-made fixture in memory: no vendor value, no network, no file.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Tests.Port.Skills.Pages;

internal static class PageFixtures
{
    public const string Pin = "0123456789abcdef0123456789abcdef01234567";
    public const string LicenceHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    public static CatalogueItem Plugin(string id, string title, double? mb, TermsClass terms = TermsClass.Open, string vendor = "Anthropic", params (string Name, int Skills)[] folders) => new()
    {
        Id = id, Kind = ItemKind.Plugin, Title = title, Summary = "nth-made summary of " + title, Vendor = vendor, CountState = CountState.Plan, PinState = PinState.Unpinned,
        Pin = Pin, SourceUrl = "https://github.com/example/" + id.ToLowerInvariant(), LicenceName = terms == TermsClass.Open ? "Apache-2.0" : "Proprietary", LicenceUrl = null,
        LicenceFile = terms == TermsClass.Open ? null : "LICENSE.txt", TermsClass = terms, ApproxSizeMb = mb, Needs = [], Provides = [], Artifacts = [],
        Files = terms == TermsClass.Open ? [] : [new CatalogueFile { Path = "LICENSE.txt", Size = 5, Sha256 = LicenceHash }],
        SkillCount = folders.Sum(f => f.Skills), Folders = folders.Select(f => new CatalogueFolder { Name = f.Name, SkillCount = f.Skills }).ToList(),
    };

    public static CatalogueItem SkillSet(string id, string title, double? mb, string[]? needs = null, TermsClass terms = TermsClass.Open, string vendor = "OpenAI", params string[] skills) => new()
    {
        Id = id, Kind = ItemKind.Skillset, Title = title, Summary = "nth-made summary of " + title, Vendor = vendor, CountState = CountState.Plan, PinState = PinState.Unpinned,
        Pin = Pin, SourceUrl = "https://github.com/example/" + id.ToLowerInvariant(), LicenceName = terms == TermsClass.Open ? "Apache-2.0" : "Proprietary", LicenceUrl = null,
        LicenceFile = terms == TermsClass.Open ? null : "LICENSE.txt", TermsClass = terms, ApproxSizeMb = mb, Needs = needs ?? [], Provides = [], Artifacts = [],
        Files = terms == TermsClass.Open ? [] : [new CatalogueFile { Path = "LICENSE.txt", Size = 5, Sha256 = LicenceHash }],
        SkillCount = skills.Length, SkillsListed = skills.Length > 0,
        Skills = skills.Select(s => new CatalogueSkill { Folder = s, Name = null, Path = "skills/" + s, Needs = [] }).ToList(),
    };

    public static CatalogueItem Tool(string id, string title) => new()
    {
        Id = id, Kind = ItemKind.Tool, Title = title, Summary = "A fixture tool", Vendor = "Fixture", CountState = CountState.Plan, PinState = PinState.Unpinned, Pin = "1.0",
        SourceUrl = "https://example.org/" + id, LicenceName = "MIT", LicenceUrl = null, LicenceFile = null, TermsClass = TermsClass.Open, ApproxSizeMb = 12.6, Needs = [], Provides = [],
        Artifacts = [], Files = [],
    };

    /// <summary>Two plugins (one open, one under Anthropic's terms) and three skill sets (one open with a need, one with no size, one under OpenAI's terms), plus a tool they need.</summary>
    public static SkillsCatalogue Catalogue() => new()
    {
        Schema = 1, ReadOn = "2026-10-05",
        Items =
        [
            Plugin("PA", "Fixture plugins A", 8.1, folders: [("sales", 36), ("legal", 9)]),
            Plugin("PB", "Fixture plugins B", 18.2, TermsClass.Proprietary, "OpenAI", [("security", 15)]),
            SkillSet("SA", "Fixture skills A", 3.5, ["TP", "TQ"], skills: ["docx", "xlsx"]),
            SkillSet("SB", "Fixture skills B", null, skills: ["one"]),
            SkillSet("SC", "Fixture skills C", 0.28, null, TermsClass.Proprietary, "Anthropic", "pdf"),
            Tool("TP", "Python"),
            Tool("TQ", "LibreOffice"),
        ],
    };

    public static ItemRecord Rec(string id, ItemState state, string? installed = null, string? target = null, string? failure = null) => new(id, state, installed, target, failure);

    public static Func<string, ItemRecord> Records(params ItemRecord[] records) => id => records.FirstOrDefault(r => r.Id == id) ?? ItemRecord.Available(id);

    public static IReadOnlyList<LibraryRow> Rows(LibrarySection section, AddonsState? state = null, ISwitchReader? switches = null, params ItemRecord[] records) =>
        LibraryRows.Build(section, Catalogue(), state ?? AddonsState.Empty, Records(records), _ => null, switches ?? new Switches());
}
