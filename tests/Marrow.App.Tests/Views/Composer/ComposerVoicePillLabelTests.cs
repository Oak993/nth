using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, V-T73 (voice-SPEC V-12.3, V-15 Listening row): the label's crossfade and the width's ease, each frame against the design's CSS curves (CssCurves solves ease and ez the way a browser does, not the way the
// port does), and the text sweep running only while the pill is open. The pill stands alone in a window on the rig's clocks, its inputs set on a frame of the test's choosing.
[Collection("Headless")]
public sealed class ComposerVoicePillLabelTests(HeadlessSession headless)
{
    private const string Listening = "Listening…";
    private const string Transcribing = "Transcribing…";

    private static VoiceComposerRig Rig() => new();

    [Fact] // V-T73: opening, every part against its curve
    [Trait("Decision", "ED-292")]
    public void Opening_runs_the_width_the_opacity_the_label_and_the_cross_on_the_design_curves()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Rig();
            var (pill, window) = Standalone(rig);
            rig.Main.OnMain(() => pill.Open = true);
            var seen = Frames(rig, window, 40, () => (Width: pill.ShownWidth, Opacity: pill.ShownOpacity, Label: pill.LabelOpacity(VoiceLabelState.Listening), Shift: pill.LabelShift(VoiceLabelState.Listening),
                Cross: pill.CrossOpacity, Scale: pill.CrossScale));
            var full = OpenWidth(40, Listening, 13.5);
            for (var k = 0; k < seen.Count; k++)
            {
                var t = k * FrameTime;
                Assert.Equal(40 + (full - 40) * CssCurves.At(CssCurves.Ez, t, 0.42), seen[k].Width, 1e-3); // left and width .42s ez
                Assert.Equal(CssCurves.At(CssCurves.Ease, t, 0.14), seen[k].Opacity, 1e-4); // opacity .14s ease
                Assert.Equal(CssCurves.At(CssCurves.Ease, t - 0.14, 0.26), seen[k].Label, 1e-4); // label in: .26s ease after .14s
                Assert.Equal(-4 + 4 * CssCurves.At(CssCurves.Ez, t - 0.1, 0.4), seen[k].Shift, 1e-4); // sliding 4px over .4s ez after .1s
                Assert.Equal(CssCurves.At(CssCurves.Ease, t - 0.16, 0.22), seen[k].Cross, 1e-4); // the cross disc in: .22s ease after .16s
                Assert.Equal(0.6 + 0.4 * CssCurves.At(CssCurves.Ez, t - 0.12, 0.38), seen[k].Scale, 1e-4); // scale .6 to 1 over .38s ez after .12s
            }
            rig.Close(window);
        });
    }

    [Fact] // V-T73: folding
    [Trait("Decision", "ED-292")]
    public void Folding_takes_the_width_to_H_then_fades_the_pill_and_takes_the_label_and_the_cross_out_on_their_curves()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Rig();
            var (pill, window) = Standalone(rig);
            rig.Main.OnMain(() => pill.Open = true);
            Run(rig, window, 1.2);
            var full = rig.Main.OnMain(() => pill.ShownWidth);
            rig.Main.OnMain(() => pill.Open = false);
            var seen = Frames(rig, window, 40, () => (Width: pill.ShownWidth, Opacity: pill.ShownOpacity, Label: pill.LabelOpacity(VoiceLabelState.Listening), Shift: pill.LabelShift(VoiceLabelState.Listening),
                Cross: pill.CrossOpacity, Scale: pill.CrossScale));
            for (var k = 0; k < seen.Count; k++)
            {
                var t = k * FrameTime;
                Assert.Equal(full - (full - 40) * CssCurves.At(CssCurves.Ez, t, 0.42), seen[k].Width, 1e-3); // width to H over .42s ez
                Assert.Equal(1 - CssCurves.At(CssCurves.Ease, t - 0.24, 0.18), seen[k].Opacity, 1e-4); // then opacity .18s ease after .24s
                Assert.Equal(1 - CssCurves.At(CssCurves.Ease, t, 0.2), seen[k].Label, 1e-4); // the label out: .2s ease
                Assert.Equal(-4 * CssCurves.At(CssCurves.Ez, t, 0.32), seen[k].Shift, 1e-4); // and 4px back over .32s ez
                Assert.Equal(1 - CssCurves.At(CssCurves.Ease, t, 0.16), seen[k].Cross, 1e-4); // the cross out: .16s ease
                Assert.Equal(1 - 0.4 * CssCurves.At(CssCurves.Ez, t, 0.3), seen[k].Scale, 1e-4); // scale back to .6 over .3s ez
            }
            Assert.Equal(40, seen[^1].Width, 1e-6);
            Assert.Equal(0, seen[^1].Opacity, 1e-6);
            rig.Close(window);
        });
    }

    [Fact] // V-T73, V-15 Listening row: a new label state crossfades and the width eases to the new words over 0.42 s ez
    [Trait("Decision", "ED-292")]
    public void A_new_label_state_crossfades_and_the_width_eases_to_the_new_words()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Rig();
            var (pill, window) = Standalone(rig);
            rig.Main.OnMain(() => pill.Open = true);
            Run(rig, window, 1.2);
            var from = rig.Main.OnMain(() => pill.ShownWidth);
            Assert.Equal(OpenWidth(40, Listening, 13.5), from, 0.01);
            rig.Main.OnMain(() => pill.State = VoiceLabelState.Transcribing);
            var to = OpenWidth(40, Transcribing, 13.5);
            Assert.NotEqual(from, to);
            var seen = Frames(rig, window, 40, () => (Width: pill.ShownWidth, Old: pill.LabelOpacity(VoiceLabelState.Listening), New: pill.LabelOpacity(VoiceLabelState.Transcribing),
                OldShift: pill.LabelShift(VoiceLabelState.Listening), NewShift: pill.LabelShift(VoiceLabelState.Transcribing)));
            for (var k = 0; k < seen.Count; k++)
            {
                var t = k * FrameTime;
                Assert.Equal(from + (to - from) * CssCurves.At(CssCurves.Ez, t, 0.42), seen[k].Width, 1e-3);
                Assert.Equal(1 - CssCurves.At(CssCurves.Ease, t, 0.2), seen[k].Old, 1e-4); // the old words out
                Assert.Equal(CssCurves.At(CssCurves.Ease, t - 0.14, 0.26), seen[k].New, 1e-4); // the new words in
                Assert.Equal(-4 * CssCurves.At(CssCurves.Ez, t, 0.32), seen[k].OldShift, 1e-4);
                Assert.Equal(-4 + 4 * CssCurves.At(CssCurves.Ez, t - 0.1, 0.4), seen[k].NewShift, 1e-4);
            }
            rig.Close(window);
        });
    }

    [Fact] // V-T73: the text sweep runs only while the pill is open
    [Trait("Decision", "ED-292")]
    public void The_text_sweep_runs_only_while_the_pill_is_open_on_the_design_keyframes()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Rig();
            var (pill, window) = Standalone(rig);
            Assert.False(rig.Main.OnMain(() => pill.SweepRunning));
            var closed = Frames(rig, window, 30, () => pill.SweepPosition);
            Assert.All(closed, position => Assert.Equal(1.35, position, 1e-9)); // held, nothing lit
            rig.Main.OnMain(() => pill.Open = true);
            Assert.True(rig.Main.OnMain(() => pill.SweepRunning));
            var seen = Frames(rig, window, 300, () => pill.SweepPosition); // 5 s: two cycles of 2.35 s
            double Expected(double t)
            {
                var f = t % 2.35 / 2.35; // 0%,12% 135%; 70%,100% -55%; ease-in-out between
                return f <= 0.12 ? 1.35 : f >= 0.70 ? -0.55 : 1.35 - 1.9 * FrameCurves.Bezier(FrameCurves.EaseInOut, (f - 0.12) / 0.58);
            }
            for (var k = 0; k < seen.Count; k++) if (Math.Abs(k * FrameTime % 2.35) > 1e-6 && Math.Abs(k * FrameTime % 2.35 - 2.35) > 1e-6) Assert.True(Math.Abs(Expected(k * FrameTime) - seen[k]) < 5e-3, $"frame {k}: sweep {seen[k]:F4}, the keyframes {Expected(k * FrameTime):F4}");
            Assert.Contains(seen, position => position < 0.5 && position > 0); // it moved through the text
            rig.Main.OnMain(() => pill.Open = false);
            Run(rig, window, 0.1);
            Assert.False(rig.Main.OnMain(() => pill.SweepRunning));
            var held = rig.Main.OnMain(() => pill.SweepPosition);
            Run(rig, window, 1.0);
            Assert.Equal(held, rig.Main.OnMain(() => pill.SweepPosition), 1e-9); // paused while closed
            rig.Close(window);
        });
    }

    [Fact] // V-T73, V-12.3: Reduce Motion stills the sweep and every transition lands at once
    [Trait("Decision", "ED-292")]
    public void Under_reduce_motion_the_pill_opens_at_once_and_the_sweep_stands_still()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
            using var rig = Rig();
            var (pill, window) = Standalone(rig);
            rig.Main.OnMain(() => pill.Open = true);
            rig.Elapse(window, FrameTime);
            rig.Main.OnMain(() =>
            {
                Assert.True(Math.Abs(OpenWidth(40, Listening, 13.5) - pill.ShownWidth) < 0.01, $"width {pill.ShownWidth} of {OpenWidth(40, Listening, 13.5)}");
                Assert.Equal(1, pill.ShownOpacity);
                Assert.Equal(1, pill.LabelOpacity(VoiceLabelState.Listening));
                Assert.Equal(1, pill.CrossOpacity);
                Assert.False(pill.SweepRunning);
            });
            Run(rig, window, 1);
            Assert.Equal(1.35, rig.Main.OnMain(() => pill.SweepPosition), 1e-9);
            rig.Close(window);
        });
    }
}
