using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;
using Marrow.Core.Port.Voice.Session;
using Xunit;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-11 (voice-SPEC V-5.5, V-11.4, V-2.2 item 4; the VT-10 gap): the orb follows the voice. The composer reads the session's published level once per frame on the frame clock, smooths
// each band (attack 0.07 s, release 0.24 s, values under 1e-4 to 0) and hands the orb the result; the orb's own motion takes the voice value from it. Every frame is tested against the
// follower formula itself, written out here, not against a bound on how far it may move. Sessions are scripted: no microphone, no engine.
[Collection("Headless")]
public sealed class ComposerVoiceOrbLevelTests(HeadlessSession headless)
{
    private const double Frame = 0.016;

    private static VoiceOrbSlot Slot(ComposerView view) => LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(view).Single();

    // The design's follow(x, target, dt, up, down): x + (target - x)(1 - exp(-dt / tau)), tau = up when rising, down when falling; a band under 1e-4 is 0.
    private static double Follow(double x, double target, double dt)
    {
        var next = x + (target - x) * (1 - Math.Exp(-dt / Math.Max(1e-3, target > x ? 0.07 : 0.24)));
        return next < 1e-4 ? 0 : next;
    }

    private static VoiceComposerRig Seen()
    {
        var rig = new VoiceComposerRig();
        rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
        return rig;
    }

    [Fact] // V-5.5, V-11.4: the orb's bands are the followers of the session's level, frame by frame
    [Trait("Decision", "ED-292")]
    public void The_orb_takes_the_smoothed_voice_each_frame_on_the_followers_curve()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.1);
            var slot = rig.Main.OnMain(() => Slot(view));
            var session = view.Voice!.Session;
            Assert.Equal(VoiceSessionState.Listening, session.State);

            var (all, low, mid, high) = rig.Main.OnMain(() => (slot.Orb.Bands.All, slot.Orb.Bands.Low, slot.Orb.Bands.Mid, slot.Orb.Bands.High));
            var loudest = 0.0;
            var framesMoving = 0;
            for (var frame = 0; frame < 170; frame++) // 2.7 s: the room, "switch the theme please." (0.5 to 2.5 s), the room again
            {
                rig.Elapse(window, Frame);
                var raw = session.AudioLevel;
                all = Follow(all, raw.All, Frame);
                low = Follow(low, raw.Low, Frame);
                mid = Follow(mid, raw.Mid, Frame);
                high = Follow(high, raw.High, Frame);
                var shown = rig.Main.OnMain(() => slot.Orb.Bands);
                Assert.True(Math.Abs(shown.All - all) < 1e-9, $"frame {frame}: all {shown.All} against the curve {all}");
                Assert.True(Math.Abs(shown.Low - low) < 1e-9, $"frame {frame}: low {shown.Low} against the curve {low}");
                Assert.True(Math.Abs(shown.Mid - mid) < 1e-9, $"frame {frame}: mid {shown.Mid} against the curve {mid}");
                Assert.True(Math.Abs(shown.High - high) < 1e-9, $"frame {frame}: high {shown.High} against the curve {high}");
                loudest = Math.Max(loudest, shown.All);
                if (shown.All > 0 && shown.All < raw.All) framesMoving += 1;
            }
            Assert.True(loudest > 0.2, "the orb heard the voice: " + loudest);
            Assert.True(framesMoving > 3, "the level was approached over several frames, not taken at once");
            rig.Close(window);
        });
    }

    [Fact] // V-5.5: a pause lets the bands fall on the release; they do not snap to zero when listening ends
    [Trait("Decision", "ED-292")]
    public void The_bands_fall_on_the_release_after_listening_ends_and_then_no_frames_are_asked_for()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            var baseline = rig.Frames.Pending;
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 1.5); // "theme" is being said
            var slot = rig.Main.OnMain(() => Slot(view));
            var before = rig.Main.OnMain(() => slot.Orb.Bands);
            Assert.True(before.All > 0.2, "speaking: " + before.All);

            rig.Main.OnMain(() => Click(slot.HitTarget)); // the second click: listening ends, the session's level reads zero
            var previous = before.All;
            rig.Elapse(window, Frame);
            var first = rig.Main.OnMain(() => slot.Orb.Bands.All);
            Assert.True(view.Voice!.Session.State == VoiceSessionState.Processing, "state after the click: " + view.Voice.Session.State);
            Assert.True(first > 0, "the bands did not snap to zero");
            Assert.True(Math.Abs(first - Follow(previous, 0, Frame)) < 1e-9, $"first frame after the click: {first} against the release {Follow(previous, 0, Frame)}");
            previous = first;
            for (var frame = 0; frame < 400 && previous > 0; frame++) // the release is 0.24 s: from a half to under 1e-4 takes about two seconds
            {
                rig.Elapse(window, Frame);
                var shown = rig.Main.OnMain(() => slot.Orb.Bands.All);
                Assert.True(Math.Abs(shown - Follow(previous, 0, Frame)) < 1e-9, $"release frame {frame}: {shown} against {Follow(previous, 0, Frame)}");
                previous = shown;
            }
            Assert.True(previous == 0, "the release ended at " + previous);
            Assert.True(rig.Main.OnMain(() => slot.Orb.Bands.Low) == 0, "low");

            rig.Elapse(window, 6); // the final pass, Landing, Rest
            Assert.True(view.Voice.Session.State == VoiceSessionState.Rest, "state after the final pass: " + view.Voice.Session.State);
            Assert.True(rig.Frames.Pending <= baseline, $"frames still asked for at rest: {rig.Frames.Pending} against {baseline}");
            rig.Close(window);
        });
    }

    [Fact] // V-11.4: at rest and in Hover the orb has no level; a session that heard nothing never moves the bands
    [Trait("Decision", "ED-292")]
    public void A_quiet_room_leaves_the_bands_at_zero()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.QuietRoom();
            var (view, window) = rig.Open();
            var slot = rig.Main.OnMain(() => Slot(view));
            Assert.Equal(default, rig.Main.OnMain(() => slot.Orb.Bands));
            rig.Main.OnMain(() => Click(slot.HitTarget));
            for (var frame = 0; frame < 60; frame++)
            {
                rig.Elapse(window, Frame);
                Assert.Equal(default, rig.Main.OnMain(() => slot.Orb.Bands));
            }
            rig.Close(window);
        });
    }
}
