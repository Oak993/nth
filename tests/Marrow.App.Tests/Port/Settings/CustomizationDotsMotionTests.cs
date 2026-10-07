using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Settings;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Settings;

// OD-0076 (nothing cuts), customization-SPEC CZ-10.1, CZ-14.1: what changes on the Working dots section is eased. The swatches wear the theme's hex, so a change of theme
// blends each swatch's fill over 0.32 s on the CSS ease, frame by frame, as every other use of the accent does (SPEC 6.4); the rows' details crossfade and the Custom row folds
// (CzRow and CzRowSlot, held by CustomizationDotsRowsTests and the CZ-1 control tests).
[Collection("Headless")]
public sealed class CustomizationDotsMotionTests(HeadlessSession headless)
{
    private static double Ease(double seconds) => CssCurve.At(CssCurve.Ease, seconds * 1000, 320);

    private static int Distance(Color a, Color b) => new[] { Math.Abs(a.R - b.R), Math.Abs(a.G - b.G), Math.Abs(a.B - b.B), Math.Abs(a.A - b.A) }.Max();

    [Fact]
    [Trait("Decision", "ED-251")] // OD-0076, CZ-14.1: a theme change blends the swatch fills from the dark hex to the light one on the 0.32 s ease in every frame, and the picked ring does not move
    public void A_change_of_theme_blends_the_swatch_fills_over_point_32_seconds() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: false, seed: s => s.WorkingDots = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Custom });
        rig.Advance(500);
        var dots = rig.Page.Dots!;
        var teal = dots.Swatches.Classic.Buttons.Single(b => b.Id == "teal");
        var coral = dots.Swatches.Signature.Buttons.Single(b => b.Id == "coral");
        Color Fill(Marrow.UI.Port.Settings.CzRadioButton b) => Assert.IsAssignableFrom<ISolidColorBrush>(b.SwatchFill).Color;
        var (darkTeal, lightTeal) = (Color.Parse("#40C8E0"), Color.Parse("#007D91"));
        Assert.Equal(darkTeal, Fill(teal));
        rig.Do(() => rig.Window.RequestedThemeVariant = ThemeVariant.Light);
        Assert.Equal(darkTeal, Fill(teal)); // the change's own frame
        var tested = 0;
        for (var frame = 1; frame <= 19; frame++)
        {
            rig.Advance(1000.0 / 60);
            var expected = SwiftColor.PremultipliedMix(darkTeal, lightTeal, Ease(frame / 60.0));
            Assert.True(Distance(expected, Fill(teal)) <= 2, $"{frame / 60.0:F3} s: {Fill(teal)}, the curve {expected}");
            tested++;
        }
        Assert.Equal(19, tested);
        rig.Advance(100);
        Assert.Equal((lightTeal, Color.Parse("#9C5345")), (Fill(teal), Fill(coral)));
    });

    [Fact]
    [Trait("Decision", "ED-251")] // OD-0076: under Reduce Motion the swatches take the theme's hex at once
    public void Under_reduce_motion_the_swatches_take_the_new_theme_at_once() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var teal = rig.Page.Dots!.Swatches.Classic.Buttons.Single(b => b.Id == "teal");
        rig.Do(() => rig.Window.RequestedThemeVariant = ThemeVariant.Light);
        Assert.Equal(Color.Parse("#007D91"), Assert.IsAssignableFrom<ISolidColorBrush>(teal.SwatchFill).Color);
    });
}
