using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (blitz-effortslider2 adversarial review): DroppyCode/App/Views/Composer/EffortSlider.swift:1010-1016 states what the
// light-fill test is for: "Whether the fill under the knob is light" and "The passed stops: white on colour, dark on a white
// fill". In the source a white brand's fill is only drawn at maximum (.supercharged), so `kind == .supercharged && brand.isLight`
// is exactly "the fill is white". OD-0075 (this unit) makes the plain track Silver's white fill and white liquid too, but keeps
// the literal kind test, so a ChatGPT/Codex slider below its top effort draws its passed stops white at 0.6 on a white fill:
// the source never draws that. Expected to fail until the light-fill test follows the fill (or the lead rules otherwise).
[Collection("Headless")]
public sealed class PlainSilverStopsReviewTests(HeadlessSession headless)
{
    [Fact]
    public void REVIEW_A_plain_silver_track_on_its_white_fill_draws_its_passed_stops_dark() => headless.OnUiThread(() =>
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
}
