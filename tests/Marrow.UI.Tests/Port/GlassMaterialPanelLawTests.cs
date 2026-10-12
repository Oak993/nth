using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

// OD-0107, Mac frame 18 (400 x 267 head card): a flat dark body, no bright rim or ten-point band.
// Small surfaces keep their rim (the task's ruling); frames 01/02 show the pill and title capsules.
[Collection("Headless")]
public sealed class GlassMaterialPanelLawTests(HeadlessSession headless)
{
    private static GlassScene Scene(double width, double height, double radius)
    {
        var piece = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(radius), Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        // OD-0147: the panel law is the release material's (GlassLook.Release, which a piece that names it still draws); the canvas's sheet and
        // popover are measured by GlassTuningTests
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.RoundedRectangle(radius, RoundedCornerStyle.Continuous)).WithLook(GlassLook.Release));
        return new GlassScene(new Panel { Background = new SolidColorBrush(Color.FromRgb(27, 27, 27)) }, piece, width + 120, height + 120);
    }

    private static Func<int, int, Color> Pixels(GlassScene scene, string tag) => scene.Pixels(
        Environment.GetEnvironmentVariable("MARROW_GLASS_LAW_EVIDENCE") is { Length: > 0 } folder
            ? Path.Combine(folder, tag + ".png") : null);

    [Fact]
    [Trait("Req", "PR-WIN-180")] // Chrome.swift:179-182; OD-0107, frame 18, ED-358 (4)
    public void A_large_dark_card_has_a_flat_body_without_a_lit_rim_or_inner_band() => headless.OnUiThread(() =>
    {
        using var scene = Scene(400, 267, 22);
        var pixels = Pixels(scene, "dark-card-400x267");
        // 27 under the scrim/shadow, about 44 before the site's 0.22 tint (glass-g3's video fit).
        Assert.InRange(pixels(260, 106).G, (byte)43, (byte)51);
        Assert.InRange(Math.Abs(pixels(260, 106).G - pixels(260, 266).G), 0, 2);
        var body = pixels(260, 106).G;
        for (var inset = 0; inset <= 10; inset++)
            Assert.InRange(Math.Abs(pixels(260, 60 + inset).G - body), 0, 3);
    });

    [Theory]
    [InlineData(32, 32, 16)]
    [InlineData(300, 46, 23)]
    [InlineData(90, 60, 16)]
    [Trait("Req", "PR-WIN-180")] // Chrome.swift:166-182; OD-0107, the task's small-surface rim ruling
    public void Small_capsules_pills_and_popovers_keep_a_lit_rim_even_on_a_dark_backdrop(double width, double height, double radius) => headless.OnUiThread(() =>
    {
        using var scene = Scene(width, height, radius);
        var pixels = Pixels(scene, $"dark-small-{width}x{height}");
        var x = 60 + (int)(width / 2);
        Assert.True(pixels(x, 60).G >= pixels(x, 66).G + 18,
            $"{width} x {height}: rim {pixels(x, 60)} against body {pixels(x, 66)}");
    });
}
