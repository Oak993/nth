using System.Text.Json.Nodes;
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// REVIEW (unit/integ-sk, the Windows verification of SK-1 = DL-1): findings of the adversarial review, each a test that failed on the merged
// branch before its fix.
// (1) DL-1 asks that every skill folder name equal its frontmatter name. The gate checks that only for skills the item lists, and an item whose
// skills are not listed (A3, A4, A5, O2, O4, O5, O6 and A6 today) could be declared pinned and pass the release gate with no skill name checked.
// (2) ED-294 (10): "null where none is allowed ... fail closed as CatalogueFormatException". A null inside a list passed the loader and
// then crashed the gate with ArgumentNullException or NullReferenceException.
public sealed class SkillsCatalogueReviewTests
{
    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_skill_set_whose_skills_are_not_listed_lacks_its_skills_and_the_release_gate_refuses_it()
    {
        var unlisted = CatalogueFixtures.Change(CatalogueFixtures.Pinned(), "X3", i => i with { SkillsListed = false, Skills = [], PinState = PinState.Unpinned });

        var report = CatalogueGate.Check(unlisted);
        var strict = CatalogueGate.Check(unlisted, new CatalogueGateOptions { RequireAllPinned = true });

        Assert.Contains("skills", report.Unpinned.Single(u => u.ItemId == "X3").Missing);
        Assert.Empty(report.Violations);
        Assert.True(CatalogueFixtures.Fires(strict, CatalogueRules.Unpinned, "X3"), CatalogueFixtures.Rules(strict));
        var claimed = CatalogueFixtures.Change(unlisted, "X3", i => i with { PinState = PinState.Pinned });
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(claimed), CatalogueRules.PinState, "X3"));
    }

    [Theory]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("needs")]
    [InlineData("provides")]
    [InlineData("artifacts")]
    [InlineData("files")]
    [InlineData("skills")]
    [InlineData("items")]
    public void A_null_inside_a_list_is_a_format_error_not_a_crash_of_the_gate(string list)
    {
        var root = JsonNode.Parse(CatalogueFixtures.RawJson())!;
        var target = list == "items" ? root : root["items"]!.AsArray()[0]!;
        target[list]!.AsArray().Add(null);

        Assert.Throws<CatalogueFormatException>(() => SkillsCatalogueLoader.Parse(root.ToJsonString()));
    }
}
