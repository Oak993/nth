using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// SA-1 (customization-SPEC CZ-2.1.1, CZ-T3; first-release Settings pages): the sidebar symbols of the pages the owner added
// that the B2 mapping (porting/icons/sf-symbols-to-lucide.json, 2753 names, embedded unmodified) does not hold.
[Collection("Headless")]
public sealed class PortAddedSymbolsTests(HeadlessSession headless)
{
    [Theory]
    [InlineData("sparkles", "sparkles")]
    [InlineData("waveform", "audio-waveform")]
    [InlineData("arrow.triangle.2.circlepath", "refresh-cw")]
    [InlineData("powerplug", "plug")]
    [InlineData("wand.and.sparkles", "wand-sparkles")]
    [Trait("Decision", "ED-179")] // CZ-2.1.1: the new pages' symbols resolve to an embedded Lucide icon, and the row draws it
    public void A_new_page_symbol_resolves_to_a_non_empty_Lucide_icon(string sf, string lucide) => headless.OnUiThread(() =>
    {
        var mapping = SymbolCatalogue.Mapping(sf);
        Assert.NotNull(mapping);
        Assert.Equal(lucide, mapping.Lucide);
        Assert.NotNull(SymbolCatalogue.Icon(lucide));
        var image = SwiftImage.SystemName(sf);
        Assert.False(image.IsEmpty, sf);
        Assert.Same(SymbolCatalogue.Icon(lucide), image.Icon);
    });

    [Fact]
    [Trait("Decision", "ED-179")] // the B2 table is not edited: its names and counts stay the ones ED-179 embeds unmodified
    public void The_added_names_are_not_part_of_the_B2_table() => headless.OnUiThread(() =>
    {
        Assert.Equal(2753, SymbolCatalogue.SymbolNames.Count);
        Assert.DoesNotContain("waveform", SymbolCatalogue.SymbolNames);
        Assert.DoesNotContain("powerplug", SymbolCatalogue.SymbolNames);
        Assert.DoesNotContain("arrow.triangle.2.circlepath", SymbolCatalogue.SymbolNames);
        Assert.Contains("sparkles", SymbolCatalogue.SymbolNames);
        Assert.Contains("wand.and.sparkles", SymbolCatalogue.SymbolNames);
    });

    [Fact]
    [Trait("Decision", "ED-179")] // a name in neither the table nor the added list still renders nothing
    public void A_name_in_neither_list_still_resolves_to_nothing() => headless.OnUiThread(() =>
    {
        Assert.Null(SymbolCatalogue.Mapping("waveform.nope"));
        Assert.True(SwiftImage.SystemName("waveform.nope").IsEmpty);
    });
}
