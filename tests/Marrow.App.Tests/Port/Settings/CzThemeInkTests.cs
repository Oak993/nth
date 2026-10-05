using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// OD-0076 (nothing cuts), customization-SPEC CZ-14.1: the text colours of the Cz controls take Settings' 0.32 s turnover on a theme change, as their fills do (CZ-8 review finding 3, the part it named but did not test).
[Collection("Headless")]
[Trait("Decision", "ED-251")]
public sealed class CzThemeInkTests(HeadlessSession headless)
{
    private static readonly Color DarkPrimary = Color.FromArgb(255, 236, 236, 236), LightPrimary = Color.FromArgb(255, 28, 28, 30);
    private static readonly Color DarkSecondary = Color.FromArgb(179, 236, 236, 236), LightSecondary = Color.FromArgb(173, 28, 28, 30);

    private static CzRow Row(CustomizationPageRig rig, string title) =>
        rig.Page.Speech!.Section.Rows.Slots.Select(s => s.Row).OfType<CzRow>().Single(r => r.Title.Text == title);

    private static Color Ink(TextBlock text) => Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color;
    private static TextBlock Label(CzCapsuleButton button) => LogicalQuery.SelfAndDescendants<TextBlock>(button).Single(t => t.Text == "Change");
    private static TextBlock Shown(CzCrossfadeText text) => text.Children.OfType<TextBlock>().Single(t => t.Opacity > 0);

    [Fact]
    public void The_capsule_label_eases_to_the_new_theme_over_the_turnover() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig();
        rig.Do(() => AppearanceTurn.SetTurnsOver(rig.Host, true));
        var label = Label(Assert.IsType<CzCapsuleButton>(Row(rig, "Engine").Control));
        Assert.Equal(DarkPrimary, Ink(label));
        rig.Do(() => rig.Window.RequestedThemeVariant = ThemeVariant.Light);
        Assert.Equal(DarkPrimary, Ink(label)); // the frame of the change draws what was shown
        rig.Advance(1000.0 / 60);
        var middle = Ink(label);
        Assert.NotEqual(DarkPrimary, middle);
        Assert.NotEqual(LightPrimary, middle);
        rig.Advance(500);
        Assert.Equal(LightPrimary, Ink(label));
    });

    [Fact]
    public void The_detail_text_eases_to_the_new_theme_over_the_turnover() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig();
        rig.Do(() => AppearanceTurn.SetTurnsOver(rig.Host, true));
        var detail = Row(rig, "Engine").Detail;
        Assert.Equal(DarkSecondary, Ink(Shown(detail)));
        rig.Do(() => rig.Window.RequestedThemeVariant = ThemeVariant.Light);
        rig.Advance(1000.0 / 60);
        var middle = Ink(Shown(detail));
        Assert.NotEqual(DarkSecondary, middle);
        Assert.NotEqual(LightSecondary, middle);
        rig.Advance(500);
        Assert.Equal(LightSecondary, Ink(Shown(detail)));
    });

    [Fact]
    public void A_detail_that_changes_mid_turnover_wears_the_colour_the_turnover_has_reached() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig();
        rig.Do(() => AppearanceTurn.SetTurnsOver(rig.Host, true));
        var row = Row(rig, "Engine");
        rig.Do(() => rig.Window.RequestedThemeVariant = ThemeVariant.Light);
        rig.Advance(1000.0 / 60);
        rig.Do(() => row.SetDetail("A new sentence"));
        var incoming = Ink(Assert.Single(row.Detail.Children.OfType<TextBlock>(), t => t.Text == "A new sentence"));
        Assert.NotEqual(DarkSecondary, incoming);
        Assert.NotEqual(LightSecondary, incoming);
        rig.Advance(500);
        Assert.Equal(LightSecondary, Ink(Shown(row.Detail)));
    });

    [Fact]
    public void Without_a_turnover_or_under_Reduce_Motion_the_text_takes_the_theme_at_once() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig(reduceMotion: true);
        rig.Do(() => AppearanceTurn.SetTurnsOver(rig.Host, true));
        var label = Label(Assert.IsType<CzCapsuleButton>(Row(rig, "Engine").Control));
        var detail = Row(rig, "Engine").Detail;
        rig.Do(() => rig.Window.RequestedThemeVariant = ThemeVariant.Light);
        Assert.Equal(LightPrimary, Ink(label));
        Assert.Equal(LightSecondary, Ink(Shown(detail)));
    });
}
