// PORT-ONLY: SK-15 (DL-17). A catalogue of Marrow-made items for the library controller's tests: no vendor value, no network.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Tests.Fixtures;

internal static class LibraryFixtures
{
    public const string Pin = "0123456789abcdef0123456789abcdef01234567";
    public const string LicenceHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    private static CatalogueItem Set(string id, ItemKind kind, double? mb, TermsClass terms, string vendor, params string[] skills) => new()
    {
        Id = id, Kind = kind, Title = "Fixture " + id, Summary = "Marrow-made summary of " + id, Vendor = vendor, CountState = CountState.Plan, PinState = PinState.Unpinned, Pin = Pin,
        SourceUrl = "https://github.com/example/" + id.ToLowerInvariant(), LicenceName = terms == TermsClass.Open ? "Apache-2.0" : "Proprietary", LicenceUrl = null,
        LicenceFile = terms == TermsClass.Open ? null : "LICENSE.txt", TermsClass = terms, ApproxSizeMb = mb, Needs = [], Provides = [], Artifacts = [],
        Files = terms == TermsClass.Open ? [] : [new CatalogueFile { Path = "LICENSE.txt", Size = 5, Sha256 = LicenceHash }],
        SkillCount = skills.Length, SkillsListed = kind == ItemKind.Skillset && skills.Length > 0,
        Skills = kind == ItemKind.Skillset ? skills.Select(s => new CatalogueSkill { Folder = s, Name = null, Path = "skills/" + s, Needs = [] }).ToList() : [],
        Folders = kind == ItemKind.Plugin ? skills.Select(s => new CatalogueFolder { Name = s, SkillCount = 3 }).ToList() : [],
    };

    /// <summary>Skills page: S1 3.5 MB, S2 8.1 MB, S3 2 MB; S4 under Anthropic's terms; S5 with no size yet. Plugins page: P1 8.1 MB with two folders, P2 under OpenAI's terms.</summary>
    public static SkillsCatalogue Catalogue() => new()
    {
        Schema = 1, ReadOn = "2026-10-05",
        Items =
        [
            Set("P1", ItemKind.Plugin, 8.1, TermsClass.Open, "Anthropic", "sales", "legal"),
            Set("P2", ItemKind.Plugin, 18.2, TermsClass.Proprietary, "OpenAI", "security"),
            Set("S1", ItemKind.Skillset, 3.5, TermsClass.Open, "OpenAI", "docx", "xlsx"),
            Set("S2", ItemKind.Skillset, 8.1, TermsClass.Open, "Anthropic", "alpha"),
            Set("S3", ItemKind.Skillset, 2.0, TermsClass.Open, "OpenAI", "beta"),
            Set("S4", ItemKind.Skillset, 3.5, TermsClass.Proprietary, "Anthropic", "pdf"),
            Set("S5", ItemKind.Skillset, null, TermsClass.Open, "OpenAI", "gamma"),
        ],
    };

    public static SkillsCatalogue WithSize(SkillsCatalogue catalogue, string id, double? mb) =>
        catalogue with { Items = catalogue.Items.Select(i => i.Id == id ? i with { ApproxSizeMb = mb } : i).ToList() };
}
