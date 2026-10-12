using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Xunit;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-lean-7 item 4, review 2026-10-11: the accepted change, guarded. While no window of the app can be seen, the production
// frame source parks draw loops' requests; DictationLayer's display link is one. Its tick publishes the live draft (V-8.2)
// and hands over when the landing's motion ends, so while nothing can be seen the live draft waits, and the first frame once a
// window can be seen catches it up with every word that arrived meanwhile. The landing's one edit (V-8.7) needs no frame: the
// draft holds the whole dictation at once, unseen or not, and only the hand-over waits for the window. ManualFrameSource.Unseen
// parks as AvaloniaFrameSource does (FrameClockHiddenHostBudgetTests, FrameClockProductionSourceTests prove the production one).
[Collection("Headless")]
public sealed class ComposerDictationUnseenTests(HeadlessSession headless)
{
    private const double Frame = 1.0 / 60;

    private static VoiceOrbSlot Slot(ComposerView view) => LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(view).Single();

    [Fact] // V-8.2, V-8.7; ED-NEW-lean-7
    [Trait("Decision", "ED-NEW-lean-7")]
    public void While_no_window_can_be_seen_the_live_draft_waits_and_the_first_frame_after_catches_it_up_and_the_landing_is_one_edit()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => DictationProbe.Blur(view));
            var layer = rig.Main.OnMain(() => DictationProbe.Layer(view));
            var box = rig.Main.OnMain(() => DictationProbe.Box(view));
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.1);
            Assert.Equal(VoiceSessionState.Listening, view.Voice!.Session.State);

            string Draft() => rig.Main.OnMain(() => rig.Runtime.Draft.Text);
            for (var frame = 0; frame < 175 && Draft().Length == 0; frame++) rig.Elapse(window, Frame);
            var seen = Draft();
            Assert.StartsWith("switch", seen, StringComparison.Ordinal); // the first word, live

            rig.Frames.Unseen = true; // every window minimised, or on another virtual desktop
            for (var frame = 0; frame < 90; frame++)
            {
                rig.Elapse(window, Frame);
                Assert.Equal(seen, Draft()); // accepted: the live draft waits while nothing can be seen
            }
            Assert.True(rig.Frames.Pending > 0, "the layer's request waits, it is not dropped");
            Assert.True(rig.Main.OnMain(() => layer.IsActive));

            rig.Frames.Unseen = false;
            rig.Elapse(window, Frame);
            var caughtUp = Draft();
            Assert.NotEqual(seen, caughtUp); // words arrived meanwhile: the first frame after publishes them all
            Assert.StartsWith(seen, caughtUp, StringComparison.Ordinal);
            Assert.Equal(caughtUp, rig.Main.OnMain(() => layer.Text));

            rig.Frames.Unseen = true;
            rig.Main.OnMain(() => Click(Slot(view).HitTarget)); // the second click: the words land
            for (var frame = 0; frame < 120 && Draft() != VoiceComposerRig.SwitchThemeText; frame++) rig.Elapse(window, Frame);
            Assert.Equal(VoiceComposerRig.SwitchThemeText, Draft()); // the landing's one edit, with no frame
            Assert.False(rig.Main.OnMain(() => layer.HandedOver)); // its motion and the hand-over wait for the window

            rig.Frames.Unseen = false;
            for (var frame = 0; frame < 600 && !rig.Main.OnMain(() => layer.HandedOver); frame++) rig.Elapse(window, Frame);
            Assert.True(rig.Main.OnMain(() => layer.HandedOver));
            Assert.Equal(VoiceComposerRig.SwitchThemeText, Draft());
            Assert.Equal(VoiceComposerRig.SwitchThemeText, rig.Main.OnMain(() => box.Text));
            Assert.False(rig.Main.OnMain(() => box.TextHidden));
            rig.Close(window);
        });
    }
}
