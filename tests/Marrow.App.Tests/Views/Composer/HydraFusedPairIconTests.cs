using Avalonia.Styling;
using Xunit;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.UI.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/HydraFusedPairIcon.swift (B2 1fbf81e), :66-89.
[Collection("Headless")]
public sealed class HydraFusedPairIconTests(HeadlessSession headless)
{
    // STOP App.Views.Composer.HydraFusedPairIcon #1
    // Kind: missing dependency outside assigned units
    // Swift: App/Views/Composer/HydraFusedPairIcon.swift:47-59 (B3 52bc792)
    // Records: PR-COMPOSER-139
    // Missing: SwiftImage.Named(provider.IconName) is empty: provider SVG resources and catalog entries are absent.
    // Needed: shared SwiftImage catalog and resource changes outside GROUP2, which may not be touched.
    // Geometry/template/timing tests below do not claim visible provider artwork or final rendered pixels.

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-COMPOSER-139")] // App/Views/Composer/HydraFusedPairIcon.swift:17-59 masks and resting/animated state
    public void Review_constructed_halves_use_template_masks_and_source_geometry(bool animates) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var icon = new HydraFusedPairIcon(ProviderKind.Claude, ProviderKind.Deepseek, size: 15, animates);
            var grid = Assert.IsType<Grid>(icon.Child); Assert.Equal(15, grid.Width); Assert.Equal(15, grid.Height);
            for (var i = 0; i < 2; i++)
            {
                var half = Assert.IsType<Border>(grid.Children[i]);
                Assert.Equal(new Rect(i == 0 ? 0 : 9, 0, 6, 15), Assert.IsType<RectangleGeometry>(half.Clip).Rect);
                var image = Assert.IsType<SwiftImage>(Assert.IsType<VisualBrush>(Assert.IsType<Rectangle>(half.Child).OpacityMask).Visual);
                Assert.Equal(i == 0 ? "provider-claude" : "provider-deepseek", image.AssetName);
                Assert.Equal(ImageRenderingMode.Template, image.EffectiveRenderingMode);
                Assert.Equal(ContentMode.Fit, image.AspectContentMode);
                Assert.Equal(animates ? 0 : 1, half.Opacity);
                Assert.Equal(animates ? (i == 0 ? -5.25 : 5.25) : 0, Assert.IsType<TranslateTransform>(half.RenderTransform).X);
            }
            var divider = Assert.IsType<Border>(grid.Children[2]);
            Assert.Equal(1, divider.Width); Assert.Equal(12.75, divider.Height); Assert.Equal(new CornerRadius(0.5), divider.CornerRadius);
            Assert.Equal(animates ? 0.15 : 1, Assert.IsType<ScaleTransform>(divider.RenderTransform).ScaleY);
        });
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-COMPOSER-139")] // App/Views/Composer/HydraFusedPairIcon.swift:37-42 30ms then spring(0.55,0.25), nil under reduced motion
    public void Review_fuse_waits_for_thirty_ms_then_obeys_reduced_motion(bool reduced) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); var frames = new ManualFrameSource(); using var seam = FrameClock.Source.Override(frames);
        HydraFusedPairIcon? icon = null; Avalonia.Controls.Window? window = null;
        runtime.OnMain(() =>
        {
            icon = new HydraFusedPairIcon(ProviderKind.Claude, ProviderKind.Deepseek);
            EnvironmentValues.SetAccessibilityReduceMotion(icon, reduced);
            window = new Avalonia.Controls.Window { Content = icon }; window.Show();
        });
        var half = Assert.IsType<Border>(Assert.IsType<Grid>(icon!.Child).Children[0]);
        runtime.AdvanceMilliseconds(28); runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(28)));
        Assert.Equal(0, half.Opacity);
        runtime.AdvanceMilliseconds(2); runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(2)));
        Assert.Equal(reduced ? 1 : 0, half.Opacity);
        if (!reduced)
        {
            runtime.AdvanceMilliseconds(275); runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(275)));
            Assert.Equal(Animation.Spring(0.55, 0.25).Progress(0.275), half.Opacity, 7);
            // ED-011: spring duration is its perceptual response; the shared engine's Duration is its settling span.
            var remaining = TimeSpan.FromSeconds(Animation.Spring(0.55, 0.25).Duration - 0.275);
            runtime.Advance(remaining); runtime.OnMain(() => frames.Advance(remaining));
            Assert.Equal(1, half.Opacity);
        }
        runtime.OnMain(() => { window!.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); });
    });
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
