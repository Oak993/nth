using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// DroppyCode/App/Views/Composer/EffortSlider.swift:1010-1016 states what the light-fill test is for: "Whether the fill under the knob is
// light" and "The passed stops: white on colour, dark on a white fill". In the source a white brand's fill is only drawn at maximum
// (.supercharged), so `kind == .supercharged && brand.isLight` is exactly "the fill is white" there. OD-0075 draws Silver's white
// fill and white liquid on the plain track too, and OD-0073 (ED-248) under the fast look; lead ruling R1 (ED-NEW-effortslider2 (6)):
// the port computes isLightFill from the fill it draws under the knob, so a Silver track below its top effort draws its passed stops
// dark, and the bubbles' light or dark look follows the same test. The fusion look keeps the source's rule (not light). These tests
// read the stops a Silver plain slider draws and the light-fill test of every brand's plain, fast, supercharged and fusion look.
[Collection("Headless")]
public sealed class PlainSilverStopsReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:1010-1016 isLightFill and stopColor, as amended by OD-0075 and OD-0073 (lead ruling R1): the drawn passed stops of a plain Silver track
    public void A_plain_silver_track_on_its_white_fill_draws_its_passed_stops_dark() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slider = new EffortSlider(3, new Binding<int>(() => 1, _ => { }), "Medium", brand: EffortBrand.Silver);
        var host = new Border { Child = slider, Width = 330, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Avalonia.Controls.Window { Content = host, Width = 330, Height = 300 };
        window.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        try
        {
            var theme = slider.ActualThemeVariant;
            // the premise: below the top the fill is Silver's white gradient now (OD-0075), white into (0.88, 0.88, 0.91)
            var fill = slider.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "EffortFill");
            var stops = Assert.IsType<LinearGradientBrush>(fill.Background).GradientStops;
            Assert.Equal(Colors.White, stops[0].Color);
            var passed = slider.GetVisualDescendants().OfType<Ellipse>().Where(e => e.Width == 5).OrderBy(Canvas.GetLeft).Take(2).ToArray();
            Assert.Equal(2, passed.Length);
            // :1015 on a white fill the passed stops are black at 35 percent
            Assert.All(passed, dot => Assert.Equal(SwiftColor.Black.Opacity(0.35).Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(dot.Fill).Color));
        }
        finally { window.Close(); }
    });

    // The light-fill test follows the fill (:1010-1011, lead ruling R1): Silver's fill is white wherever the port draws it (the plain, fast and
    // supercharged looks), every other brand's fill is a colour, and the fusion look keeps the source's rule, not light.
    [Fact]
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:1010-1016 isLightFill and stopColor, as amended by OD-0075 and OD-0073 (lead ruling R1): the light-fill test, the stop colour and the bubbles' rim of every brand's looks
    public void The_light_fill_test_follows_the_fill_for_the_plain_fast_and_supercharged_look_of_every_brand_and_not_the_fusion_look()
    {
        foreach (var brand in Enum.GetValues<EffortBrand>().Where(b => b != EffortBrand.Silver))
            foreach (var kind in new[] { TrackLook.Kind.Plain, TrackLook.Kind.Fast, TrackLook.Kind.Supercharged, TrackLook.Kind.Fusion })
            {
                var look = new TrackLook(kind, brand);
                Assert.False(look.IsLightFill, $"{kind} {brand}");
                Assert.Equal(SwiftColor.White.Opacity(0.6).Resolve(Avalonia.Styling.ThemeVariant.Light), look.StopColor.Resolve(Avalonia.Styling.ThemeVariant.Light));
                Assert.False(TrackEffect.DarkBubbles(look), $"{kind} {brand}");
            }
        foreach (var kind in new[] { TrackLook.Kind.Plain, TrackLook.Kind.Fast, TrackLook.Kind.Supercharged })
        {
            var look = new TrackLook(kind, EffortBrand.Silver);
            Assert.True(look.IsLightFill, $"{kind} Silver");
            Assert.Equal(SwiftColor.Black.Opacity(0.35).Resolve(Avalonia.Styling.ThemeVariant.Light), look.StopColor.Resolve(Avalonia.Styling.ThemeVariant.Light)); // :1015
            Assert.True(TrackEffect.DarkBubbles(look), $"{kind} Silver"); // the white liquid's bubbles are dark-rimmed
        }
        // lead ruling R1: the fusion look keeps the source's rule (:1012 `kind == .supercharged`): not light, white stops, light bubbles
        var fusion = new TrackLook(TrackLook.Kind.Fusion, EffortBrand.Silver);
        Assert.False(fusion.IsLightFill);
        Assert.Equal(SwiftColor.White.Opacity(0.6).Resolve(Avalonia.Styling.ThemeVariant.Light), fusion.StopColor.Resolve(Avalonia.Styling.ThemeVariant.Light));
        Assert.False(TrackEffect.DarkBubbles(fusion));
        // a pair's light test is the pair's, and its liquid is coloured segments: no dark bubbles
        var silverPair = new EffortPairBrand(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex });
        var paired = new TrackLook(TrackLook.Kind.Plain, EffortBrand.Silver, silverPair);
        Assert.True(paired.IsLightFill); Assert.False(TrackEffect.DarkBubbles(paired));
    }
}
