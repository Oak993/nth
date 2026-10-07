using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The panel's background (App/Views/Chat/HydraPanel.swift:99-121): one regular glass surface in a 22-point continuous rounded
/// rectangle, washed with Chrome.glassTint at 0.22 in dark and 0.16 in light, clipped to the shape with no hairline, over a
/// separate plain scrim (black 0.3 in dark, white 0.34 in light) that alone carries the shadow (black 1.0 in dark, 0.65 in
/// light, radius 28, 10 points down). The same recipe is on the helper panel (SubagentPanel.swift:333-355).
/// </summary>
[Collection("Headless")]
public sealed class HydraPanelSurfaceTests(HeadlessSession headless)
{
    private void Run(ThemeVariant theme, bool poppedOut, Action<HydraPanelContext> test) => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var context = new HydraPanelContext(visible: true, poppedOut: poppedOut, theme: theme);
        context.Tick(1);
        test(context);
    });

    private static Border Glass(HydraPanelContext c) =>
        Assert.Single(c.Find<Border>(), b => SwiftGlass.GetEffect(b) is { } effect && effect.Shape == GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous) && b.Bounds.Size == new Size(420, 280));

    // ED-358: the scrim's shadow is drawn by its SwiftShadow.Caster (the fill's own coverage at the fill's alpha, under it), not a BoxShadow.
    private static SwiftShadow.Caster Caster(HydraPanelContext c) => Assert.Single(c.Panel.GetLogicalDescendants().OfType<SwiftShadow.Caster>());
    private static Border Scrim(HydraPanelContext c) => Caster(c).Source;

    public static TheoryData<bool, bool> Cases => new() { { false, false }, { true, false }, { false, true }, { true, true } };

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Req", "PR-HYDRA-229")] // HydraPanel.swift:103-108 shape.fill(.clear).glassEffect(.regular, in: shape).overlay { shape.fill(Chrome.glassTint.opacity(isDark ? 0.22 : 0.16)) }
    public void One_regular_glass_surface_in_a_22_point_continuous_shape_washed_with_the_theme_tint(bool dark, bool poppedOut)
    {
        var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Run(theme, poppedOut, c =>
        {
            Assert.Equal(theme, c.Panel.ActualThemeVariant);
            var glass = Glass(c);
            Assert.Equal(GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous)), SwiftGlass.GetEffect(glass));
            Assert.Equal(new CornerRadius(22), glass.CornerRadius);
            Assert.True(glass.ClipToBounds);
            var wash = Assert.IsAssignableFrom<ISolidColorBrush>(glass.Background);
            Assert.Equal(Chrome.GlassTint.Opacity(dark ? 0.22 : 0.16).Resolve(theme), wash.Color);
            // The one glass surface of the panel (the strip's capsules carry their own, not the panel's shape).
            Assert.Single(c.Find<Border>(), b => SwiftGlass.GetEffect(b)?.Shape == GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous));
        });
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Req", "PR-HYDRA-229")] // HydraPanel.swift:112 .clipShape(shape); :110-111 glass supplies the edge, a hairline on top of it read as a second ring
    public void The_panel_is_clipped_to_the_shape_and_has_no_hairline_border(bool dark, bool poppedOut) =>
        Run(dark ? ThemeVariant.Dark : ThemeVariant.Light, poppedOut, c =>
        {
            // The whole panel (the strip over the stage, and the finish wave) is inside one 22-point clip, as well as the glass.
            var strip = c.WithTip("Drag to move");
            Assert.Contains(strip.GetLogicalAncestors().OfType<Border>(), b => b.CornerRadius == new CornerRadius(22) && b.ClipToBounds && b.Bounds.Size == new Size(420, 280));
            // No border of the panel's own size draws a stroke.
            foreach (var border in c.Find<Border>().Where(b => b.Bounds.Size == new Size(420, 280)))
            {
                Assert.Equal(default, border.BorderThickness);
                Assert.Null(border.BorderBrush);
            }
        });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Req", "PR-HYDRA-229")] // HydraPanel.swift:116-120 shape.fill((isDark ? Color.black : Color.white).opacity(isDark ? 0.3 : 0.34)).shadow(color: .black.opacity(isDark ? 1 : 0.65), radius: 28, y: 10)
    // ED-358 (numbered by the lead); the existing Req trait below/above names the source.
    public void A_separate_scrim_under_the_glass_carries_the_one_shadow(bool dark, bool poppedOut)
    {
        var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Run(theme, poppedOut, c =>
        {
            var caster = Caster(c);
            var scrim = Scrim(c);
            Assert.Equal(new CornerRadius(22), scrim.CornerRadius);
            Assert.Equal(new Size(420, 280), scrim.Bounds.Size);
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(scrim.Background).Color;
            Assert.Equal(dark ? Color.FromArgb(77, 0, 0, 0) : Color.FromArgb(87, 255, 255, 255), fill); // 0.3 and 0.34 of 255
            // ED-358 (was the scrim's BoxShadow of Blur (28 - 0.5) / 0.288675 at the colour's full alpha, outside the box only): radius 28
            // (sigma 28, ED-327), y 10, the Swift's colour, drawn at the colour's alpha times the fill's (1.0 x 0.3 = 77; 0.65 x 0.34 = 57), under it.
            Assert.Equal((28d, 0d, 10d), (caster.Radius, caster.X, caster.Y));
            Assert.Equal(dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(166, 0, 0, 0), caster.Color); // 1.0 and 0.65 of 255
            Assert.Equal(dark ? Color.FromArgb(77, 0, 0, 0) : Color.FromArgb(57, 0, 0, 0), caster.DrawnColor);
            // Under the glass, not over it, its shadow under it, and nothing carries a box shadow.
            var host = Assert.IsType<Grid>(scrim.Parent);
            Assert.Same(host, caster.Parent);
            Assert.Equal(host.Children.IndexOf(scrim) - 1, host.Children.IndexOf(caster));
            Assert.True(host.Children.IndexOf(scrim) < host.Children.IndexOf(Assert.IsType<Border>(host.Children.Single(x => x != scrim && x != caster))));
            Assert.DoesNotContain(c.Find<Border>(), b => b.BoxShadow.Count > 0);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-229")] // HydraPanel.swift:102, :117 isDark is read from the environment on every draw: the surface follows a theme change
    public void The_wash_the_scrim_and_the_shadow_follow_a_theme_change() => Run(ThemeVariant.Light, false, c =>
    {
        Assert.Equal(Color.FromArgb(87, 255, 255, 255), Assert.IsAssignableFrom<ISolidColorBrush>(Scrim(c).Background).Color);
        c.Window.RequestedThemeVariant = ThemeVariant.Dark;
        c.Drain();
        Assert.Equal(ThemeVariant.Dark, c.Panel.ActualThemeVariant);
        Assert.Equal(Color.FromArgb(77, 0, 0, 0), Assert.IsAssignableFrom<ISolidColorBrush>(Scrim(c).Background).Color);
        Assert.Equal(Color.FromArgb(255, 0, 0, 0), Caster(c).Color);
        Assert.Equal(Color.FromArgb(77, 0, 0, 0), Caster(c).DrawnColor);
        Assert.Equal(Chrome.GlassTint.Opacity(0.22).Resolve(ThemeVariant.Dark), Assert.IsAssignableFrom<ISolidColorBrush>(Glass(c).Background).Color);
    });
}
