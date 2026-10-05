// SK-11f / DL-14 against the real embedded catalogue (SK-1): "refuses names outside the catalogue" means what the detector's own lookup means (ONDEMAND-PLAN.md
// 5.3 and SK-10's ModelRequest): an item id, or a program a catalogue item provides.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-11f")]
public sealed class ToolRequestCatalogueTests
{
    private static readonly SkillsCatalogue Catalogue = SkillsCatalogueLoader.LoadEmbedded();

    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData("T3")]
    [InlineData("soffice")]
    [InlineData("SOFFICE")]
    [InlineData("python")]
    [InlineData("pdftoppm")]
    [InlineData("A2")]
    public void A_catalogue_item_id_or_a_provided_program_is_in_the_catalogue(string name) =>
        Assert.True(ToolRequestCatalogue.Contains(Catalogue, name));

    [Theory, Trait("Rule", "R-TEST-10")]
    [InlineData("rm")]
    [InlineData("powershell")]
    [InlineData("curl")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("C:\\Program Files\\LibreOffice\\program\\soffice.exe")]
    [InlineData("t3 ")]
    public void Anything_else_is_not(string name) => Assert.False(ToolRequestCatalogue.Contains(Catalogue, name));
}
