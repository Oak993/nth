using Avalonia.Controls;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Testing;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-18 (DESIGN.md rev 5, 2.4 "a failed registration shows in Settings with Change", 15.6 Voice, 17 Q3): Settings, Assistant, Voice: the talk chord and the voice. The page stays its own
// class; the choices live in the one "dextra" key.
[Collection("Headless")]
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-10")]
public sealed class AssistantSettingsVoiceTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    [Fact]
    public void The_Voice_section_comes_after_Stop_and_holds_the_talk_chord_row()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var page = new AssistantSettingsPage(new DextraSettings(new InMemorySettingsStore()));
            using var stage = new HomeStage(new ScrollViewer { Content = page }, 800, 900);

            var texts = Texts(page).ToList();
            Assert.True(texts.IndexOf("Stop chord") < texts.IndexOf("Voice"));
            Assert.True(texts.IndexOf("Voice") < texts.IndexOf("Talk chord"));
            Assert.True(texts.IndexOf("Talk chord") < texts.IndexOf("Permissions"));
            Assert.Contains(AssistantSettingsWords.RowTalkDetail, texts);
            Assert.Null(page.VoiceMenu); // no voices were given: the row is not shown
        });
    }

    [Fact]
    public void The_talk_chord_menu_writes_the_choice_and_the_default_is_stored_as_none()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var settings = new DextraSettings(new InMemorySettingsStore());
            var page = new AssistantSettingsPage(settings);
            using var stage = new HomeStage(new ScrollViewer { Content = page }, 800, 900);

            Assert.Equal(AssistantSettingsWords.TalkChords, page.TalkMenu.Items.Select(i => i.Id));
            Assert.Equal("Ctrl+Alt+Enter", page.TalkMenu.SelectedId);
            page.TalkMenu.Select("Ctrl+Alt+Home");
            Assert.Equal("Ctrl+Alt+Home", settings.Value.TalkChord);
            Assert.Equal("Ctrl+Alt+Home", settings.TalkChord);
            page.TalkMenu.Select("Ctrl+Alt+Enter");
            Assert.Null(settings.Value.TalkChord);
            Assert.Equal("Ctrl+Alt+Enter", settings.TalkChord);
        });
    }

    [Fact]
    public void A_chord_that_is_not_registered_is_said_under_the_row_with_the_way_out_and_nothing_is_said_while_it_works()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var settings = new DextraSettings(new InMemorySettingsStore());
            var status = new VoiceChordStatus(false, "Ctrl+Alt+Enter", true, PcChordProblem.None);
            var page = new AssistantSettingsPage(settings, new AssistantSettingsParts { TalkStatus = () => status });
            using var stage = new HomeStage(new ScrollViewer { Content = page }, 800, 900);

            Assert.True(page.TalkStatusText.IsVisible);
            Assert.Equal("Ctrl+Alt+Enter is taken by another app. Change it.", page.TalkStatusText.Text);

            status = new VoiceChordStatus(true, "Ctrl+Alt+Enter", false, PcChordProblem.None);
            settings.Update(p => p with { TalkChord = "Ctrl+Alt+Home" }); // any change redraws the page
            stage.Layout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            stage.Layout();

            Assert.False(page.TalkStatusText.IsVisible);
        });
    }

    [Fact]
    public void The_Voice_row_lists_the_installed_voices_with_Automatic_first_and_stores_the_pick()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var settings = new DextraSettings(new InMemorySettingsStore());
            var page = new AssistantSettingsPage(settings, new AssistantSettingsParts { Voices = () => [("zira", "Microsoft Zira"), ("david", "Microsoft David")] });
            using var stage = new HomeStage(new ScrollViewer { Content = page }, 800, 900);

            var menu = page.VoiceMenu!;
            Assert.Equal(["", "zira", "david"], menu.Items.Select(i => i.Id));
            Assert.Equal("", menu.SelectedId);
            menu.Select("david");
            Assert.Equal("david", settings.Value.VoiceName);
            menu.Select("");
            Assert.Null(settings.Value.VoiceName);
        });
    }

    [Fact]
    public void The_two_new_choices_are_kept_only_when_set_and_a_file_without_them_loads_as_it_did()
    {
        var plain = DextraSettings.ToJson(DextraPreferences.Default);
        Assert.DoesNotContain("talkChord", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("voiceName", plain, StringComparison.Ordinal);
        var loaded = DextraSettings.FromJson(plain);
        Assert.Null(loaded.TalkChord);
        Assert.Null(loaded.VoiceName);

        var set = DextraSettings.ToJson(DextraPreferences.Default with { TalkChord = "Ctrl+Alt+Home", VoiceName = "Microsoft Zira" });
        var back = DextraSettings.FromJson(set);
        Assert.Equal("Ctrl+Alt+Home", back.TalkChord);
        Assert.Equal("Microsoft Zira", back.VoiceName);

        // an older file (no such keys) and a damaged one keep the defaults
        Assert.Null(DextraSettings.FromJson("{\"on\":true,\"firstRunDone\":true}").TalkChord);
        Assert.Null(DextraSettings.FromJson("{\"talkChord\":7,\"voiceName\":[]}").TalkChord);
        Assert.Null(DextraSettings.FromJson("not json").VoiceName);
        Assert.Equal("Ctrl+Alt+Enter", new DextraSettings(new InMemorySettingsStore()).TalkChord);
    }
}
