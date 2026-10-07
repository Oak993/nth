using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// GAP-2, DL-1: the acceptance list requires actual embedded release pins, not only a fully pinned synthetic fixture.
public sealed class GapCatalogueAcceptanceTests
{
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Feature", "SK-1")]
    [Trait("Spec", "DL-1")]
    public void GAP_The_embedded_catalogue_has_every_required_release_pin_and_passes_the_strict_gate()
    {
        var report = CatalogueGate.Check(CatalogueFixtures.Real, new CatalogueGateOptions { RequireAllPinned = true });
        Assert.True(report.Unpinned.Count == 0 && report.Violations.Count == 0,
            string.Join("\n", report.Unpinned.Select(i => i.ItemId + ": " + string.Join(", ", i.Missing))
                .Concat(report.Violations.Select(v => v.ItemId + ": " + v.Message))));
    }
}
