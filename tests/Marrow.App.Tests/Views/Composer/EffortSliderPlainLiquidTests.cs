using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// DEVIATION OD-0075 (owner, 2026-10-02, the choices canvas: "liquid at every effort level"; settles OD-0072) from
// DroppyCode/App/Views/Composer/EffortSlider.swift:760-767 (the effect is overlaid only when the kind is not plain or a pair exists)
// and :990 (the plain fill is Chrome.accent): the plain track draws the liquid like every other look, in its maker's liquid
// (the same palette every look draws), over the brand's fill (as OD-0073 left under the fast look). The per-model and per-effort
// shade of OD-0063 is the colour engine's (unbuilt; ED-NEW-effortslider2): until it lands every effort wears the maker's palette.
[Collection("Headless")]
public sealed class EffortSliderPlainLiquidTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Show(Control content)
    {
        var window = new Avalonia.Controls.Window { Width = 330, Height = 220, Content = content };
        window.Show(); Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static EffortSlider Slider(int index, EffortBrand brand, bool fast = false, EffortPairBrand? pair = null, int count = 5) =>
        new(count, new Binding<int>(() => index, _ => { }), "Title", fast, brand, pair);

    private static Border FillOf(EffortSlider slider) => slider.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "EffortFill");

    public static IEnumerable<object[]> Brands() => Enum.GetValues<EffortBrand>().Select(b => new object[] { b });

    [Theory]
    [Trait("Req", "PR-COMPOSER-127")] // DEVIATION OD-0075: the plain kind draws the liquid effect, as the other kinds do (EffortSlider.swift:729-738, :760-767)
    [Trait("Req", "PR-COMPOSER-128")] // DEVIATION OD-0075: the plain fill is the brand's fill (EffortSlider.swift:990-991, :889-909), for every brand
    [MemberData(nameof(Brands))]
    public void A_plain_solo_track_holds_one_liquid_effect_of_its_brand_over_the_brands_fill_at_every_stop_below_the_top(EffortBrand brand) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        for (var index = 0; index < 4; index++)
        {
            var slider = Slider(index, brand);
            var window = Show(slider);
            try
            {
                var effect = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
                Assert.Equal(TrackLook.Kind.Plain, effect.Look.TrackLookKind);
                Assert.Equal(brand, effect.Look.Brand);
                Assert.Null(effect.Look.Pair);
                Assert.True(TrackEffect.IsLiquid(effect.Look));
                Assert.False(TrackEffect.IsFast(effect.Look)); // plain draws the liquid, not Strike
                // the same liquid as the maximum look draws: one spec per maker
                var theme = slider.ActualThemeVariant;
                Assert.Equal(LiquidSpec.For(new TrackLook(TrackLook.Kind.Supercharged, brand)).Stops().Select(s => (s.Color.Resolve(theme), s.At)),
                    LiquidSpec.For(effect.Look).Stops().Select(s => (s.Color.Resolve(theme), s.At)));
                // and the fill under it is the brand's, the maximum look's, not the accent
                var expected = EffortView.Fill(new TrackLook(TrackLook.Kind.Supercharged, brand), theme);
                var shown = FillOf(slider).Background;
                if (expected is ISolidColorBrush solid) Assert.Equal(solid.Color, Assert.IsAssignableFrom<ISolidColorBrush>(shown).Color);
                else
                {
                    var want = ((LinearGradientBrush)expected).GradientStops; var got = Assert.IsType<LinearGradientBrush>(shown).GradientStops;
                    Assert.Equal(want.Select(s => (s.Color, s.Offset)), got.Select(s => (s.Color, s.Offset)));
                }
            }
            finally { window.Close(); }
        }
    });

    private static TrackEffect Mounted(TrackLook look, double width = 240, double height = 34)
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var effect = new TrackEffect(look) { TrackWidth = width };
        effect.Measure(new Size(width, height)); effect.Arrange(new Rect(0, 0, width, height));
        return effect;
    }

    private static SKBitmap FrameAt(TrackEffect effect, double seconds)
    {
        typeof(TrackEffect).GetField("time", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(effect, seconds);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)effect.Bounds.Width, (int)effect.Bounds.Height));
        using (var context = bitmap.CreateDrawingContext()) effect.Child!.Render(context);
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static (double R, double G, double B, double Opaque) Mean(SKBitmap bitmap)
    {
        double r = 0, g = 0, b = 0, opaque = 0; var n = 0;
        for (var y = 8; y < bitmap.Height - 8; y++)
            for (var x = 20; x < bitmap.Width - 20; x++)
            {
                var p = bitmap.GetPixel(x, y); n++;
                r += p.Red; g += p.Green; b += p.Blue; if (p.Alpha > 200) opaque++;
            }
        return (r / n, g / n, b / n, opaque / n);
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-127")] // DEVIATION OD-0075: the plain kind's effect paints the maker's liquid (EffortSlider.swift:760-767 draws none)
    [InlineData(EffortBrand.Claude)]
    [InlineData(EffortBrand.Deepseek)]
    [InlineData(EffortBrand.Silver)]
    [InlineData(EffortBrand.Cursor)]
    public void A_plain_track_paints_the_same_liquid_a_maximum_track_paints(EffortBrand brand) => headless.OnUiThread(() =>
    {
        // each track has its own bubbles, so the comparison is of the whole tube's mean colour at a moment: the currents and the
        // light are the same, the bubbles are a few small discs
        var plain = FrameAt(Mounted(new TrackLook(TrackLook.Kind.Plain, brand)), 1.7);
        var top = FrameAt(Mounted(new TrackLook(TrackLook.Kind.Supercharged, brand)), 1.7);
        var (pr, pg, pb, opaque) = Mean(plain);
        var (tr, tg, tb, _) = Mean(top);
        Assert.True(opaque > 0.95, $"the plain liquid paints the tube: {opaque:P0} of it is opaque");
        Assert.True(Math.Abs(pr - tr) < 6 && Math.Abs(pg - tg) < 6 && Math.Abs(pb - tb) < 6, $"plain ({pr:F0}, {pg:F0}, {pb:F0}) against maximum ({tr:F0}, {tg:F0}, {tb:F0})");
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-128")] // DEVIATION OD-0075: the plain fill is no longer Chrome.accent (EffortSlider.swift:990)
    public void A_plain_track_is_not_the_accent_it_was() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slider = Slider(0, EffortBrand.Claude);
        var window = Show(slider);
        try
        {
            var accent = Chrome_Accent(slider.ActualThemeVariant);
            Assert.NotEqual(accent, Assert.IsAssignableFrom<ISolidColorBrush>(FillOf(slider).Background).Color);
        }
        finally { window.Close(); }
    });

    private static Color Chrome_Accent(ThemeVariant theme) => Chrome.Accent.Resolve(theme);

    [Fact]
    [Trait("Req", "PR-COMPOSER-132")] // EffortSlider.swift:1020-1026 the identity is the whole look without a pair: a change that keeps the kind keeps the effect
    public void A_look_change_below_the_top_that_keeps_the_kind_does_not_restart_the_liquid() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        var index = 0;
        var slider = new EffortSlider(5, new Binding<int>(() => index, v => index = v), "Title", false, EffortBrand.Claude);
        var window = Show(slider);
        try
        {
            var effect = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            clock.Advance(TimeSpan.FromSeconds(2)); frames.Advance(TimeSpan.FromSeconds(2)); Dispatcher.UIThread.RunJobs();
            index = 2; slider.UpdateLook(false, "Medium", null); Dispatcher.UIThread.RunJobs(); // Low -> Medium: still the plain kind
            Assert.Same(effect, Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>()));
            Assert.Equal(2, effect.TimeAt(SwiftDate.Now), 3);
        }
        finally { window.Close(); }
    });
}
