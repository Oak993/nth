using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Views.Composer;

// PORT-ONLY, no Swift twin (OD-0049, OD-0050, OD-0060, ED-234): the slider brand of OpenRouter. OD-0050's automatic colour
// is not built yet, so OpenRouter takes the source's no-provider default (EffortSlider.swift:869, EffortBrand.purple) and
// the colour is worked out later; this test pins that holding value, so the day the automatic colour replaces it the test
// is the one that has to change.
public sealed class EffortBrandOpenRouterTests
{
    [Fact]
    [Trait("Decision", "ED-234")]
    public void OpenRouter_has_the_no_provider_default_brand_until_the_automatic_colour_is_built()
    {
        Assert.Equal(EffortBrand.Purple, EffortBrand.Create(ProviderKind.OpenRouter));
        Assert.Equal(EffortBrand.Create(null), EffortBrand.Create(ProviderKind.OpenRouter));
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_brand_of_every_provider_is_still_a_brand_and_the_other_fourteen_keep_the_ones_OD_0047_gave_them()
    {
        // Adding a case must not move any existing mapping: the fourteen source providers keep their own brand,
        // OD-0114 / ED-410: Local also uses the existing no-provider fallback; source mappings stay intact.
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases.Where(kind => kind is not (ProviderKind.OpenRouter or ProviderKind.Local)))
            Assert.NotEqual(EffortBrand.Purple, EffortBrand.Create(provider));
        Assert.Equal(16, SwiftEnum<ProviderKind>.AllCases.Count);
        Assert.Equal(EffortBrand.Create(null), EffortBrand.Create(ProviderKind.Local));
    }
}
