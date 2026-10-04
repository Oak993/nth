using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// DroppyCode/App/Views/Composer/EffortSlider.swift:1010-1016: "Whether the fill under the knob is light" and "The passed stops: white on
// colour, dark on a white fill". In the source the fast fill below the top is the amber EffortPalette.fastFill (:992), a colour, so
// white stops are right there. OD-0073 (ED-248) draws the brand's fill and the brand's liquid under the fast look instead
// (EffortView.Fill's last arm, LiquidSpec.For ignores the kind), so a Codex/ChatGPT (Silver) slider in fast mode below its top effort
// (Codex models carry a fast service tier: CodexSession.swift:381, Provider.swift:255) draws Silver's white fill and white liquid. Lead
// ruling R1 (ED-NEW-effortslider2 (6)): the port computes isLightFill from the fill it draws, so its passed stops are dark there. The
// fusion look (fast mode at the top) keeps the source's rule: the second test reads its stops white.
[Collection("Headless")]
public sealed class FastSilverStopsReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:1010-1016 isLightFill and stopColor, as amended by OD-0075 and OD-0073 (lead ruling R1): the drawn passed stops of a fast Silver track below its top
    public void A_fast_silver_track_on_its_white_fill_draws_its_passed_stops_dark() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slider = new EffortSlider(3, new Binding<int>(() => 1, _ => { }), "Medium", fastMode: true, brand: EffortBrand.Silver);
        var host = new Border { Child = slider, Width = 330, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Avalonia.Controls.Window { Content = host, Width = 330, Height = 300 };
        window.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        try
        {
            var theme = slider.ActualThemeVariant;
            // the premise: below the top in fast mode the fill is Silver's white gradient (OD-0073), not the source's amber
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

    // lead ruling R1: the fusion look (fast mode at the top) keeps the source's rule (:1012 `kind == .supercharged`), so its passed stops stay white
    [Fact]
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:1010-1016 isLightFill and stopColor, as amended by OD-0075 and OD-0073 (lead ruling R1): the fusion look keeps the source's rule
    public void A_silver_fusion_track_keeps_the_sources_white_passed_stops() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slider = new EffortSlider(3, new Binding<int>(() => 2, _ => { }), "High", fastMode: true, brand: EffortBrand.Silver);
        var host = new Border { Child = slider, Width = 330, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Avalonia.Controls.Window { Content = host, Width = 330, Height = 300 };
        window.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        try
        {
            var theme = slider.ActualThemeVariant;
            Assert.Equal(TrackLook.Kind.Fusion, Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>(), e => TrackEffect.IsFast(e.Look)).Look.TrackLookKind);
            var passed = slider.GetVisualDescendants().OfType<Ellipse>().Where(e => e.Width == 5).OrderBy(Canvas.GetLeft).ToArray();
            Assert.Equal(3, passed.Length); // the knob is at the top: all three stops are passed
            Assert.All(passed, dot => Assert.Equal(SwiftColor.White.Opacity(0.6).Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(dot.Fill).Color));
        }
        finally { window.Close(); }
    });
}
