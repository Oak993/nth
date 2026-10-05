using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;

namespace Marrow.App.Tests.Port.Settings;

// voice-SPEC V-7.14 with customization-SPEC CZ-11.1 (CZ-8 review, the Not done item): the rows that name Windows' display language follow it while the page is open, through the display-language change source.
[Collection("Headless")]
[Trait("Decision", "ED-251")]
public sealed class SpeechDisplayLanguageChangeTests(HeadlessSession headless)
{
    [Fact]
    public void The_Speech_Language_row_follows_a_change_of_Windows_display_language() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        var windows = "fi-FI"; // not in the list
        using var language = WindowsDisplayLanguage.Source.Override(() => windows);
        Action? changed = null;
        using var source = WindowsDisplayLanguage.Changes.Override(callback => { changed = callback; return new CallbackDisposable(() => changed = null); });
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s => s.VoiceOrb = s.VoiceOrb with { LangMode = VoiceLangMode.Windows });
        var row = rig.Page.Speech!.Section.Rows.Slots.Select(s => s.Row).OfType<Marrow.App.Port.Settings.CzRow>().Single(r => r.Title.Text == "Language");
        Assert.Equal("Auto-detect: Whisper works out the language", row.Detail.Text);
        Assert.NotNull(changed);
        windows = "hi-IN";
        rig.Do(() => changed!());
        Assert.Equal("Hindi (from Windows)", row.Detail.Text);
        windows = "fi-FI";
        rig.Do(() => changed!());
        Assert.Equal("Auto-detect: Whisper works out the language", row.Detail.Text);
    });

    [Fact]
    public void The_page_stops_listening_when_it_leaves_the_window() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        var live = 0;
        using var source = WindowsDisplayLanguage.Changes.Override(_ => { live++; return new CallbackDisposable(() => live--); });
        var rig = new CustomizationPageRig(reduceMotion: true);
        Assert.Equal(1, live);
        rig.Dispose();
        Assert.Equal(0, live);
    });

    [Fact]
    public void The_Voice_typing_Language_row_follows_the_same_change() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var windows = "en-US";
        using var language = WindowsDisplayLanguage.Source.Override(() => windows);
        scene.Model.Settings.VoiceOrb = scene.Model.Settings.VoiceOrb with { LangMode = VoiceLangMode.Windows };
        VoiceTypingScene.Sync();
        Assert.Equal("English (from Windows)", scene.Page.LanguageMenu.ValueLabel.Text);
        windows = "hi-IN";
        WindowsDisplayLanguage.Notify();
        VoiceTypingScene.Sync();
        Assert.Equal("Hindi (from Windows)", scene.Page.LanguageMenu.ValueLabel.Text);
    }, VoiceTypingPageRig.TwoMicrophones());
}
