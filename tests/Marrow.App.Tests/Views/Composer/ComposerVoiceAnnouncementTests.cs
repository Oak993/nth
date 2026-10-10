using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Xunit;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-10, V-T84 (voice-SPEC V-14.5; DESIGN voice-composer.js say()): what the composer announces, as polite live-region text, at each step of a session.
[Collection("Headless")]
public sealed class ComposerVoiceAnnouncementTests(HeadlessSession headless)
{
    private static VoiceOrbSlot Slot(Control view) => LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(view).Single();

    private static VoiceComposerRig Seen()
    {
        var rig = new VoiceComposerRig();
        rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
        return rig;
    }

    [Fact] // V-T84: one polite live region in the composer
    [Trait("Rule", "R-TEST-10")]
    public void The_composer_has_one_polite_live_region_that_starts_empty()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            var (view, window) = rig.Open();
            rig.Main.OnMain(() =>
            {
                var region = Assert.Single(LogicalQuery.SelfAndDescendants<VoiceAnnouncer>(view));
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(region));
                Assert.Equal("", region.Text);
                Assert.Equal("", view.Voice!.Announcement);
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T84: start, stop, done (leave)
    [Trait("Rule", "R-TEST-10")]
    public void Start_stop_and_done_are_announced_with_the_designs_words_and_the_text_stays_for_the_user()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            string Said() => rig.Main.OnMain(() => LogicalQuery.SelfAndDescendants<VoiceAnnouncer>(view).Single().Text!);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.1);
            Assert.Equal("Listening. Your words are written into the composer as you speak. Click the orb again to finish, or press Escape to cancel.", Said());
            rig.Elapse(window, 3.0);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.01);
            Assert.Equal("Finishing the text.", Said());
            rig.Elapse(window, 10);
            Assert.Equal("Done: switch the theme please. The text is in the composer; send it when you are ready.", Said());
            Assert.Equal(Said(), rig.Main.OnMain(() => view.Voice!.Announcement));
            rig.Close(window);
        });
    }

    [Fact] // V-T84: done with "Send it as the prompt"
    [Trait("Rule", "R-TEST-10")]
    public void Done_with_send_on_says_Sending()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig(VoiceOrbSettings.Default with { AfterVoice = VoiceAfterVoice.Send });
            rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(true, true));
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 3.0);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 10);
            Assert.Equal("Done: switch the theme please. Sending.", rig.Main.OnMain(() => view.Voice!.Announcement));
            rig.Close(window);
        });
    }

    [Fact] // V-T84: cancel
    [Trait("Rule", "R-TEST-10")]
    public void Cancelling_is_announced()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 1.0);
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape)));
            rig.Elapse(window, 0.1);
            Assert.Equal("Voice input cancelled; the words were removed.", rig.Main.OnMain(() => LogicalQuery.SelfAndDescendants<VoiceAnnouncer>(view).Single().Text));
            rig.Close(window);
        });
    }

    [Fact] // V-T84: blocked
    [Trait("Rule", "R-TEST-10")]
    public void A_blocked_microphone_is_announced()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Microphone.Current = MicrophoneAccessStatus.Denied;
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.1);
            Assert.Equal("The microphone is blocked. Allow Nth in Windows Settings, Privacy and security, Microphone.",
                rig.Main.OnMain(() => LogicalQuery.SelfAndDescendants<VoiceAnnouncer>(view).Single().Text));
            rig.Close(window);
        });
    }
}
