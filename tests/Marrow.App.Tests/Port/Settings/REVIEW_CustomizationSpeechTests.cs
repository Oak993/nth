using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// Independent CZ-8 review: customization-SPEC CZ-11.1, CZ-14.1; voice-SPEC V-7.14;
// OD-0076. Expected values come from those contracts, not the current culture or current paints.
[Collection("Headless")]
[Trait("Decision", "ED-251")]
[Trait("Feature", "CZ-8")]
[Trait("Review", "CZ-8")]
public sealed class REVIEW_CustomizationSpeechTests(HeadlessSession headless)
{
    private static CzRow Row(CustomizationPageRig rig, string title) =>
        rig.Page.Speech!.Section.Rows.Slots.Select(s => s.Row).OfType<CzRow>().Single(r => r.Title.Text == title);

    [Theory]
    [InlineData("hi-IN", "Hindi (from Windows)")]
    [InlineData("en-US", "English (from Windows)")]
    [Trait("Spec", "CZ-T98")]
    public void REVIEW_Windows_default_uses_the_system_display_language(string displayLanguage, string expected) => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext(); // managed UI culture fi-FI deliberately differs from Windows
        using var systemLanguage = WindowsDisplayLanguage.Source.Override(() => displayLanguage);
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s =>
            s.VoiceOrb = s.VoiceOrb with { LangMode = VoiceLangMode.Windows });
        Assert.Equal(expected, Row(rig, "Language").Detail.Text);
    });

    [Fact]
    [Trait("Spec", "CZ-T81")]
    public void REVIEW_Enabling_Reduce_Motion_settles_an_active_Speech_detail_fade() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig();
        var detail = Row(rig, "Engine").Detail;
        rig.Do(() => rig.App.Settings.VoiceTyping = rig.App.Settings.VoiceTyping with { Engine = VoiceEngine.Cloud });
        rig.Advance(120);
        Assert.InRange(detail.IncomingOpacity, .01, .99); // establish that the actual fade is running
        rig.Do(() => EnvironmentValues.SetAccessibilityReduceMotion(rig.Host, true));
        rig.Advance(1000.0 / 60); // the next frame must honor the new system preference
        Assert.Equal(1, detail.IncomingOpacity);
        Assert.Equal(0, detail.OutgoingOpacity);
    });

    [Theory]
    [InlineData("Speech model", "")]
    [InlineData("Language", "Auto-detect: Whisper works out the language")]
    [InlineData("Engine", "On this PC: your voice never leaves this computer")]
    [Trait("Spec", "CZ-T81")]
    public void REVIEW_Speech_link_is_named_for_its_row_and_exposes_its_detail(string title, string detail) => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var button = Assert.IsType<CzCapsuleButton>(Row(rig, title).Control);
        Assert.Equal(title, AutomationProperties.GetName(button)); // CZ-15: the control's name is its row title
        Assert.Equal(detail, AutomationProperties.GetHelpText(button));
    });

    [Fact]
    [Trait("Spec", "CZ-T81")]
    public void REVIEW_Speech_link_help_text_follows_the_engine_summary() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var button = Row(rig, "Engine").Control;
        Assert.Equal("On this PC: your voice never leaves this computer", AutomationProperties.GetHelpText(button));
        rig.Do(() => rig.App.Settings.VoiceTyping = rig.App.Settings.VoiceTyping with { Engine = VoiceEngine.Cloud });
        Assert.Equal("OpenAI cloud, with your own key", AutomationProperties.GetHelpText(button));
    });

    [Fact]
    [Trait("Spec", "CZ-T81")]
    public void REVIEW_Speech_capsule_participates_in_Settings_theme_turnover() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig();
        // Match Settings' inherited turnover flag. Mark the already drawn host so this
        // control test does not depend on the native top-level's painted-frame callback.
        rig.Do(() => AppearanceTurn.SetTurnsOver(rig.Host, true));
        var button = Assert.IsType<CzCapsuleButton>(Row(rig, "Engine").Control);
        Assert.True(AppearanceTurn.Eases(button));
        var dark = Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color;
        var light = Color.FromArgb(140, 255, 255, 255); // CZ-12 cap: rgba(255,255,255,.55)
        Assert.NotEqual(dark, light);
        rig.Do(() => rig.Window.RequestedThemeVariant = ThemeVariant.Light);
        Assert.Equal(dark, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
        rig.Advance(1000.0 / 60);
        var next = Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color;
        Assert.NotEqual(dark, next);
        Assert.NotEqual(light, next);
        rig.Advance(500);
        Assert.Equal(light, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
    });

    [Fact]
    [Trait("Spec", "CZ-T81")]
    public void REVIEW_Speech_capsule_hover_fill_takes_the_designs_point_12_second_transition() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig();
        var section = rig.Page.Speech!.Section;
        var button = Assert.IsType<CzCapsuleButton>(Row(rig, "Engine").Control);
        // Show the actual Speech section alone in the existing window so a real pointer
        // can reach it without relying on CZ-6's missing scroll/pin implementation.
        rig.Do(() => { rig.Page.Rest.Children.Remove(section); rig.Host.Child = section; });
        rig.Do(() => rig.Window.MouseMove(new Point(650, 850)));
        Assert.False(button.IsPointerOver);
        var idle = Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color;
        var hover = Color.FromArgb(77, 0, 0, 0); // gen.py .czbtn:hover: caph rgba(0,0,0,.3)
        var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), rig.Window)!.Value;
        rig.Do(() => rig.Window.MouseMove(centre));
        Assert.True(button.IsPointerOver);
        Assert.Equal(idle, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
        rig.Advance(60);
        var middle = Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color;
        Assert.NotEqual(idle, middle);
        Assert.NotEqual(hover, middle);
        rig.Advance(100);
        Assert.Equal(hover, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
    });
}
