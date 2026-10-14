using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Views.Chat;

// The veil the timeline's scroll drives (DroppyCode/UI/Chrome/Chrome.swift:581-629, ChatView.swift:143-145), rendered over a white and over a black backdrop, so the share of the
// backdrop it hides at each row of pixels is read off the pixels themselves (a pixel is backdrop * (1 - s) + veil * s, so a white and a black backdrop differ by 1 - s), and set
// beside the profile measured on the reference runtime.
[Collection("Headless")]
public sealed class ThreadTimelineVeilTests(HeadlessSession headless)
{
    // porting reference capture: E:\Droppy\work\reference-video\frames\gap-R2-02\scrim_profile.json (2-chat-use.mov 8.382 without the veil, 8.403 with it; the window's top edge at video y 30;
    // x 2000-2300; the share of the backdrop hidden at each video y). The theme, the transparency and the wallpaper are the reference recording's own.
    private static readonly (int VideoY, double Share)[] Captured =
    [
        (32, 0.452), (36, 0.463), (40, 0.461), (44, 0.461), (48, 0.469), (52, 0.433), (56, 0.403), (60, 0.371), (64, 0.324), (68, 0.281),
        (72, 0.236), (76, 0.199), (80, 0.173), (84, 0.145), (88, 0.097), (92, 0.061), (96, 0.029), (100, -0.008), (104, -0.028), (108, -0.028),
    ];

    // The share of the backdrop the veil hides at each of the first 80 rows of pixels, at a scroll travel, in a theme.
    private double[] Shares(ThemeVariant theme, double travel)
    {
        var shares = new double[80];
        headless.OnUiThread(() =>
        {
            double[] Levels(Color backdrop)
            {
                var model = new ChromeScrollModel(); model.Update(travel);
                var host = new Grid { Background = new SolidColorBrush(backdrop), Children = { new PaneTopVeil(model) { VerticalAlignment = VerticalAlignment.Top } } };
                var window = new Avalonia.Controls.Window { Width = 200, Height = 120, Content = host, RequestedThemeVariant = theme };
                try
                {
                    window.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
                    return FramePixels.Levels(window, Enumerable.Range(0, 80).Select(y => (100, y)).ToArray());
                }
                finally { window.Close(); }
            }
            var white = Levels(Colors.White); var black = Levels(Colors.Black);
            for (var y = 0; y < shares.Length; y++) shares[y] = 1 - (white[y] - black[y]);
        });
        return shares;
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-159 (capture comparison at full travel, dark: the falloff of the veil over its 72 points matches 2-chat-use.mov 8.403; the level itself is the reference recording's theme's and is not compared); Chrome.swift:581-629
    public void The_veil_at_full_travel_falls_off_over_its_seventy_two_points_as_it_does_on_the_reference_runtime() => ThemeShapeIsTheCaptures(ThemeVariant.Dark);

    private void ThemeShapeIsTheCaptures(ThemeVariant theme)
    {
        var shares = Shares(theme, travel: 44);
        var plateau = Enumerable.Range(2, 17).Select(y => shares[y]).Average(); // the first 28 percent (20 points) hold the colour
        Assert.True(plateau > 0.2, $"the veil hides {plateau} of the backdrop at its top");
        var capturedPlateau = Captured.Where(sample => sample.VideoY <= 48).Average(sample => sample.Share);
        foreach (var (videoY, share) in Captured)
        {
            var y = videoY - 30; // the window's top edge is at video y 30
            Assert.InRange(shares[y] / plateau, Math.Max(0, share / capturedPlateau) - 0.08, Math.Max(0, share / capturedPlateau) + 0.08);
        }
        // Nothing of it is left at its bottom edge, and nothing is drawn below it.
        Assert.InRange(shares[71] / plateau, -0.03, 0.03);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-159 (the veil is drawn only once content is under the chrome, and fades in with the travel); Chrome.swift:572-575,587-590,622
    public void The_veil_is_not_drawn_at_rest_and_deepens_with_the_travel()
    {
        var at = new Dictionary<double, double>();
        foreach (var travel in new[] { 0.0, 10.0, 20.0, 30.0, 44.0 }) { var shares = Shares(ThemeVariant.Dark, travel); at[travel] = Enumerable.Range(2, 17).Select(y => shares[y]).Average(); }
        // Under 10 points of travel there is nothing yet; from there it grows to its full level at 44.
        Assert.InRange(at[0], -0.02, 0.02); Assert.InRange(at[10], -0.02, 0.02);
        Assert.True(at[20] > 0.02 && at[20] < at[30] && at[30] < at[44], string.Join(", ", at.Select(pair => pair.Key + ": " + pair.Value)));
        // How much of it: SwiftUI's `.opacity` on the veil's stack (:622, 0.08 + 0.92 * progress) multiplies the opacity of each layer, with no compositing group, over a black scrim of
        // 0.14 + 0.18 * progress (:587) and the theme's tint (:589), so the share hidden at the top is 1 - (1 - scrim * opacity) * (1 - tint * opacity). The tint's own share is what the
        // theme's surface makes of it: the dark tint is the surface's colour at 0.30 of its own alpha (:589).
        var tint = 0.30 * Marrow.UI.Chrome.Chrome.GlassTint.Resolve(ThemeVariant.Dark).A / 255.0;
        foreach (var travel in new[] { 20.0, 30.0, 44.0 })
        {
            var progress = Math.Round(Math.Clamp((travel - 10) / 34, 0, 1) / 0.04) * 0.04; var opacity = 0.08 + 0.92 * progress;
            var expected = 1 - (1 - (0.14 + 0.18 * progress) * opacity) * (1 - tint * opacity);
            Assert.True(Math.Abs(expected - at[travel]) < 0.012, $"travel {travel}: expected {expected}, drawn {at[travel]}; tint {tint}");
        }
    }
}
