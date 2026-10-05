using Avalonia;
using System.Globalization;
using Avalonia.Automation;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-11.1 (the Speech section), CZ-3.5.3 (where the pin lets go), CZ-T81, CZ-T82, CZ-T98; OD-0076 (every change is eased).
[Collection("Headless")]
[Trait("Decision", "ED-251")]
public sealed class CustomizationSpeechSectionTests(HeadlessSession headless)
{
    private const string AutoWords = "Auto-detect: Whisper works out the language";

    private static CzSection Section(CustomizationPageRig rig) => Assert.Single(rig.Page.Rest.Children.OfType<CzSection>(), s => s.Title == "Speech");
    private static CzRow Row(CustomizationPageRig rig, string title) => Section(rig).Rows.Slots.Select(s => s.Row).OfType<CzRow>().Single(r => r.Title.Text == title);

    [Fact]
    public void CZ_T81_Three_link_rows_in_order_each_with_a_Change_capsule() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var section = Section(rig);
        Assert.Equal(new[] { "Speech model", "Language", "Engine" }, section.Rows.Slots.Select(s => s.Row).OfType<CzRow>().Select(r => r.Title.Text));
        Assert.Equal(3, section.Rows.Slots.Count);
        foreach (var row in section.Rows.Slots.Select(s => s.Row).OfType<CzRow>())
        {
            var button = Assert.IsType<CzCapsuleButton>(row.Control);
            Assert.Equal("Change", AutomationProperties.GetName(button));
        }
    });

    [Fact]
    public void CZ_T81_Details_for_the_local_and_the_cloud_engine() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s => s.VoiceTyping = VoiceTypingSettings.Default with { Model = "large-v3-turbo-q8_0" });
        Assert.Equal("Large v3 Turbo, balanced 874 MB", Row(rig, "Speech model").Detail.Text);
        Assert.Equal("On this PC: your voice never leaves this computer", Row(rig, "Engine").Detail.Text);
        rig.Do(() => rig.App.Settings.VoiceTyping = rig.App.Settings.VoiceTyping with { Engine = VoiceEngine.Cloud });
        Assert.Equal("Not used while the cloud engine is on", Row(rig, "Speech model").Detail.Text);
        Assert.Equal("OpenAI cloud, with your own key", Row(rig, "Engine").Detail.Text);
        rig.Do(() => rig.App.Settings.VoiceTyping = rig.App.Settings.VoiceTyping with { Engine = VoiceEngine.Local, Model = "small.en-q5_1" });
        Assert.Equal("Small (English only), compact 190 MB", Row(rig, "Speech model").Detail.Text);
    });

    [Fact]
    public void CZ_T81_Language_follows_an_explicit_pick_and_CZ_T98_the_language_default_when_none_is_picked() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext(); // Windows' display language: fi-FI, not in the list
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var row = Row(rig, "Language");
        Assert.Equal(AutoWords, row.Detail.Text); // nothing picked, language default A
        rig.Do(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { LangMode = VoiceLangMode.Show });
        Assert.Equal("Auto-detect, named in the pill while you speak", row.Detail.Text);
        rig.Do(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { LangMode = VoiceLangMode.Windows });
        Assert.Equal(AutoWords, row.Detail.Text); // C with a display language outside the list is A's text
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("hi-IN");
            rig.Do(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { LangMode = VoiceLangMode.Auto });
            rig.Do(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { LangMode = VoiceLangMode.Windows });
            Assert.Equal("Hindi (from Windows)", row.Detail.Text);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
        rig.Do(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { LangMode = VoiceLangMode.Ask });
        Assert.Equal("Asked on first use", row.Detail.Text);
        // an explicit pick on the Voice typing page comes first, whatever the default says
        rig.Do(() => rig.App.Settings.VoiceTyping = rig.App.Settings.VoiceTyping with { Lang = "ur" });
        Assert.Equal("Urdu", row.Detail.Text);
        rig.Do(() => rig.App.Settings.VoiceTyping = rig.App.Settings.VoiceTyping with { Lang = "auto" });
        Assert.Equal(AutoWords, row.Detail.Text);
    });

    [Fact]
    public void CZ_T81_Every_Change_opens_Voice_typing() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig(reduceMotion: true);
        foreach (var title in new[] { "Speech model", "Language", "Engine" })
        {
            rig.Do(() => SettingsNavigation.Shared.RequestedPage = null);
            rig.Do(() => ((CzCapsuleButton)Row(rig, title).Control).Activate());
            Assert.Equal(SettingsPage.VoiceTyping, SettingsNavigation.Shared.RequestedPage);
        }
        rig.Do(() => SettingsNavigation.Shared.RequestedPage = null);
    });

    [Fact]
    public void CZ_T81_A_changed_detail_crossfades_on_the_frame_clock() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig();
        var row = Row(rig, "Engine");
        rig.Do(() => rig.App.Settings.VoiceTyping = rig.App.Settings.VoiceTyping with { Engine = VoiceEngine.Cloud });
        Assert.Equal(0, row.Detail.IncomingOpacity);
        rig.Advance(120);
        Assert.InRange(row.Detail.IncomingOpacity, .01, .99);
        rig.Advance(400);
        Assert.Equal(1, row.Detail.IncomingOpacity);
        Assert.Equal("OpenAI cloud, with your own key", row.Detail.Text);
    });

    [Fact]
    public void CZ_T82_Speech_follows_the_sticky_container_where_the_pin_lets_go() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var speech = Section(rig);
        Assert.DoesNotContain(rig.Page.Sticky.Children.OfType<CzSection>(), s => s.Title == "Speech");
        Assert.Same(speech, rig.Page.Rest.Children[0]);
        Assert.Same(rig.Page.Speech!.Section, speech);
        var dots = rig.Page.Dots!.Section;
        var dotsBottom = dots.TranslatePoint(new Avalonia.Point(0, dots.Bounds.Height), rig.Page)!.Value.Y;
        var speechTop = speech.TranslatePoint(default, rig.Page)!.Value.Y;
        Assert.True(speechTop >= dotsBottom - 1, $"Speech starts at {speechTop}, Working dots ends at {dotsBottom}");
    });
}
