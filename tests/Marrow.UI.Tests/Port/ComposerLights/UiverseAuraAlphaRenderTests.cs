using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.ComposerLights;

// Unit CZ-2 (OD-0099 Look Picks, Q-CZ-3 B): the composer light draws the alpha of a Custom colour. Rendered on the CPU raster the playground's
// way (ComposerLightRig: the composer on a flat card, the light over it, device scale 1). The halo's colour (--au-c1) is rgba(colour, alpha):
// over a flat background it adds alpha times what the opaque colour adds.
public sealed class UiverseAuraAlphaRenderTests
{
    private static ComposerLightSettings Halo(string c1) => ComposerLightSettings.Default with
    {
        Style = ComposerLightStyle.Aura,
        Aura = UiverseAuraOptions.Default with { Scheme = AuraScheme.Custom, Color1 = c1, Color2 = c1, Rest = 100, Rim = false, Dots = false },
    };

    // how far the red channel rises over the field, a few px left of the composer's middle where the halo is its first colour
    private static double Rise(string c1)
    {
        var light = ComposerLightRig.Light(Halo(c1), dark: true);
        light.Still(0.35, 0.35, 0, 0, 0);
        using var frame = ComposerLightRig.Render(light, dark: true);
        return frame.GetPixel(ComposerLightRig.Margin - 5, ComposerLightRig.Margin + 26).Red - 0x17;
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_halo_is_drawn_at_the_alpha_of_a_custom_colour()
    {
        var opaque = Rise("#FF0000FF");
        var six = Rise("#FF0000");
        var half = Rise("#FF000080");
        var none = Rise("#FF000000");
        Assert.True(opaque > 20, $"the opaque halo should be plainly there, it raised red by {opaque}");
        Assert.Equal(opaque, six);                                  // a stored #RRGGBB reads as opaque
        Assert.Equal(0, none);                                      // alpha 0 draws nothing of it
        Assert.InRange(half / opaque, 0.45, 0.56);                  // alpha 80 / FF: 128 / 255 of the rise
    }
}
