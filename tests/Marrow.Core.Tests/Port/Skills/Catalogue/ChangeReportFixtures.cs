// PORT-ONLY: shared helpers of the SK-2 tests (first-release feature unit SK-2, DL-2 of porting/first-release/skills-ONDEMAND-PLAN.md section 7).
// "Previous" and "next" catalogues are built in memory from SK-1's Marrow-made fixtures; nothing here reads a vendor file or the network.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

internal static class ChangeReportFixtures
{
    public static SkillsCatalogue Previous() => CatalogueFixtures.Pinned();

    // The licence file as one more file of the item, so ItemTerms.LicenceHash finds it (SK-7).
    public static CatalogueItem WithLicenceText(CatalogueItem item, string text) =>
        item with
        {
            Files = item.Files.Where(f => f.Path != item.LicenceFile).Append(new CatalogueFile { Path = item.LicenceFile!, Size = text.Length, Sha256 = CatalogueFixtures.Sha(text) }).ToList(),
        };

    public static SkillsCatalogue WithLicencedX3(SkillsCatalogue catalogue, string text) =>
        Mutate(catalogue, "X3", i => WithLicenceText(i with { TermsClass = TermsClass.Proprietary }, text));

    public static SkillsCatalogue Mutate(SkillsCatalogue catalogue, string id, Func<CatalogueItem, CatalogueItem> change) =>
        CatalogueFixtures.Change(catalogue, id, change);

    public static CatalogueItem ChangeFile(CatalogueItem item, string path, Func<CatalogueFile, CatalogueFile> change) =>
        item with { Files = item.Files.Select(f => f.Path == path ? change(f) : f).ToList() };

    public static CatalogueItem ChangeArtifact(CatalogueItem item, string name, Func<CatalogueArtifact, CatalogueArtifact> change) =>
        item with { Artifacts = item.Artifacts.Select(a => a.Name == name ? change(a) : a).ToList() };

    public static string[] Lines(CatalogueChangeReport report, string id) =>
        report.ChangesFor(id).Select(c => c.Category.ToString().ToLowerInvariant() + ": " + c.Message).ToArray();

    public static CatalogueChange Only(CatalogueChangeReport report, string id, ChangeCategory category, string field) =>
        report.ChangesFor(id).Single(c => c.Category == category && c.Field == field);

    public const string Commit1 = "0123456789abcdef0123456789abcdef01234567";
    public const string Commit2 = "89abcdef0123456789abcdef0123456789abcdef";
}
