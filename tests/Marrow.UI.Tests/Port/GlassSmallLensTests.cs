using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Scene = Marrow.UI.Tests.Fixtures.GlassScene;

namespace Marrow.UI.Tests.Port;

// GlassMaterial on a small lens: the effort knob (EffortSlider.swift:780-792, a 28 pt
// .glassEffect(.regular.tint(Color.white.opacity(0.32))) over a blurred disc of the fill colour) reads in the
// reference video as that colour lightened (2-chat-use.mov 41.3 s: a peach disc over the terracotta fill). With the
// panel-sized refracting band, blur and shade the whole small disc bent in its dark surroundings instead.
[Collection("Headless")]
public sealed class GlassSmallLensTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-180")] // the knob's tinted glass lens (the port's glass material, OD-0005)
    public void A_small_tinted_lens_shows_the_colour_under_it_lightened_not_its_surroundings() => headless.OnUiThread(() =>
    {
        var under = Color.FromRgb(204, 107, 71); // Claude's fill (EffortSlider.swift:913)
        var backdrop = new Panel { Background = new SolidColorBrush(Color.FromRgb(30, 30, 32)) };
        backdrop.Children.Add(new Ellipse { Width = 30, Height = 30, Fill = new SolidColorBrush(under),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var lens = new Border { Background = Brushes.Transparent, Width = 28, Height = 28,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // OD-0147: the release material's small lens (GlassLook.Release, which a piece that names it still draws)
        SwiftGlass.SetEffect(lens, GlassEffect.Regular(GlassShape.Circle).Tint(SwiftColor.White, 0.32).WithLook(GlassLook.Release));
        using var scene = new Scene(backdrop, lens);
        var pixels = scene.Pixels();
        var centre = pixels(128, 80);
        var expected = Color.FromRgb((byte)(under.R * 0.68 + 255 * 0.32), (byte)(under.G * 0.68 + 255 * 0.32), (byte)(under.B * 0.68 + 255 * 0.32));
        Assert.True(Math.Abs(centre.R - expected.R) <= 24 && Math.Abs(centre.G - expected.G) <= 24 && Math.Abs(centre.B - expected.B) <= 24,
            $"centre {centre} should be the colour under the lens lightened, about {expected}");
        // Inside the rim, too: a panel-sized band would bend this point's sample out past the disc into the dark.
        var inner = pixels(128 + 9, 80);
        Assert.True(Math.Abs(inner.R - expected.R) <= 40 && Math.Abs(inner.G - expected.G) <= 40 && Math.Abs(inner.B - expected.B) <= 40,
            $"9 pt from the centre {inner} should still be the lightened colour, about {expected}");
    });
}
