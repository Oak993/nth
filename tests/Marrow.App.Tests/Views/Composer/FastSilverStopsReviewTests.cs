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

// REVIEW (blitz-effortslider2 scoped re-check of PR-COMPOSER-131, head 7790857e6): the fix makes the light-fill test follow the
// fill for Plain and Supercharged only. EffortSlider.swift:1010-1016: "Whether the fill under the knob is light" and "The passed
// stops: white on colour, dark on a white fill". In the source the fast fill below the top is the amber EffortPalette.fastFill
// (:992), a colour, so white stops are right there. OD-0073 (ED-248) draws the brand's fill and the brand's liquid under the fast
// look instead (EffortView.Fill's last arm, LiquidSpec.For ignores the kind), so a Codex/ChatGPT (Silver) slider in fast mode below
// its top effort (Codex models carry a fast service tier: CodexSession.swift:381, Provider.swift:255) draws Silver's white fill and
// white liquid under white 0.6 passed stops and light bubbles: the case the previous refutation named, still drawn.
// Expected to fail until the light-fill test follows the fill for the fast kind too (or the lead rules otherwise).
[Collection("Headless")]
public sealed class FastSilverStopsReviewTests(HeadlessSession headless)
{
    [Fact]
    public void REVIEW_A_fast_silver_track_on_its_white_fill_draws_its_passed_stops_dark() => headless.OnUiThread(() =>
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
}
