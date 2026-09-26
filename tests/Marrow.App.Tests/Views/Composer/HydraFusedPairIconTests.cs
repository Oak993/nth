using Avalonia.Styling;
using Xunit;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/HydraFusedPairIcon.swift (B2 1fbf81e), :66-89.
public sealed class HydraFusedPairIconTests
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-139")] // App/Views/Composer/HydraFusedPairIcon.swift:66-74
    public void Different_providers_hold_each_brand_color_on_its_half_and_blend_only_between_the_halves()
    {
        var lead = EffortBrand.Create(ProviderKind.Claude).TitleColor;
        var heads = EffortBrand.Create(ProviderKind.Deepseek).TitleColor;

        var stops = HydraFusedPairIcon.Gradient(ProviderKind.Claude, ProviderKind.Deepseek);

        Assert.Equal(4, stops.Count);
        Assert.Equal(0, stops[0].Location);
        Assert.Equal(0.42, stops[1].Location);
        Assert.Equal(0.58, stops[2].Location);
        Assert.Equal(1, stops[3].Location);
        Assert.Equal(lead.Resolve(ThemeVariant.Light), stops[0].Color.Resolve(ThemeVariant.Light));
        Assert.Equal(lead.Resolve(ThemeVariant.Light), stops[1].Color.Resolve(ThemeVariant.Light));
        Assert.Equal(heads.Resolve(ThemeVariant.Light), stops[2].Color.Resolve(ThemeVariant.Light));
        Assert.Equal(heads.Resolve(ThemeVariant.Light), stops[3].Color.Resolve(ThemeVariant.Light));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-139")] // App/Views/Composer/HydraFusedPairIcon.swift:66-73
    public void An_antigravity_brand_spreads_its_three_Gemini_colors_across_its_side()
    {
        var gemini = EffortBrand.Gemini;
        var stops = HydraFusedPairIcon.Gradient(ProviderKind.Antigravity, ProviderKind.Claude);

        Assert.Equal(5, stops.Count);
        Assert.Equal(0, stops[0].Location);
        Assert.Equal(0.21, stops[1].Location);
        Assert.Equal(0.42, stops[2].Location);
        Assert.Equal(0.58, stops[3].Location);
        Assert.Equal(1, stops[4].Location);
        for (var index = 0; index < gemini.Count; index++)
            Assert.Equal(gemini[index].Resolve(ThemeVariant.Light), stops[index].Color.Resolve(ThemeVariant.Light));

        var heads = EffortBrand.Create(ProviderKind.Claude).TitleColor;
        Assert.Equal(heads.Resolve(ThemeVariant.Light), stops[3].Color.Resolve(ThemeVariant.Light));
        Assert.Equal(heads.Resolve(ThemeVariant.Light), stops[4].Color.Resolve(ThemeVariant.Light));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-139")] // App/Views/Composer/HydraFusedPairIcon.swift:68-73
    public void Equal_lead_and_heads_use_a_lighter_shade_of_the_lead_brand()
    {
        var brand = EffortBrand.Create(ProviderKind.Claude);
        var expectedHead = brand.IsLight
            ? brand.TitleColor.Opacity(0.55)
            : brand.TitleColor.Mix(SwiftColor.White, 0.4);

        var stops = HydraFusedPairIcon.Gradient(ProviderKind.Claude, ProviderKind.Claude);

        Assert.Equal(4, stops.Count);
        Assert.Equal(0, stops[0].Location);
        Assert.Equal(0.42, stops[1].Location);
        Assert.Equal(0.58, stops[2].Location);
        Assert.Equal(1, stops[3].Location);
        Assert.Equal(brand.TitleColor.Resolve(ThemeVariant.Light), stops[0].Color.Resolve(ThemeVariant.Light));
        Assert.Equal(brand.TitleColor.Resolve(ThemeVariant.Light), stops[1].Color.Resolve(ThemeVariant.Light));
        Assert.Equal(expectedHead.Resolve(ThemeVariant.Light), stops[2].Color.Resolve(ThemeVariant.Light));
        Assert.Equal(expectedHead.Resolve(ThemeVariant.Light), stops[3].Color.Resolve(ThemeVariant.Light));
    }
}
