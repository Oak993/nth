using Avalonia.Controls;
using Marrow.App.Port.Voice;
using Marrow.App.Views.Composer;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, V-T88 (voice-SPEC V-15; OD-0076: nothing cuts): the transition audit of section 15 on the frame clock, over a whole session of each listening style (the click, the words, the second click, the final pass,
// Landing and the fold). Every visible quantity of the pill, the cross and the text field's edge is sampled on each 1/60 s frame; none steps in one frame by more than its own CSS curve steps at its steepest (the
// lead's ruling on the criterion: each frame against the curve, not against a share of the travel; the curves themselves are held to in the per-transition tests of V-T72 to V-T75, V-T125 and V-T128), and
// every part that is drawn at the start of the session has left again at its end. The glow's and the light's own eases are VT-9's (ED-336) and are held there.
[Collection("Headless")]
public sealed class ComposerVoiceTransitionAuditTests(HeadlessSession headless)
{
    // the steepest one-frame step of a curve over a duration, as a fraction of its travel
    private static double SteepestStep(Func<double, double> curve, double duration)
    {
        var frames = (int)Math.Ceiling(duration / FrameTime) + 1;
        return Enumerable.Range(0, frames).Max(k => Math.Abs(CssCurves.At(curve, (k + 1) * FrameTime, duration) - CssCurves.At(curve, k * FrameTime, duration)));
    }

    private static VoiceScript Said() => new(
    [
        new ScriptedWord("switch", 0.5, 0.9),
        new ScriptedWord("the", 1.0, 1.2),
        new ScriptedWord("theme", 1.3, 1.8),
        new ScriptedWord("please.", 2.0, 2.5),
    ]);

    private static void NoFrameCuts(string name, IReadOnlyList<double> series, double steepest)
    {
        var amplitude = series.Max() - series.Min();
        if (amplitude < 1e-9) return;
        for (var k = 1; k < series.Count; k++)
            Assert.True(Math.Abs(series[k] - series[k - 1]) <= amplitude * steepest * 1.02 + 1e-3 * amplitude, $"{name}: frame {k} steps {Math.Abs(series[k] - series[k - 1]) / amplitude:P1} of its travel, the curve's steepest is {steepest:P1}");
    }

    [Fact] // V-T88: the inline pill's whole session
    [Trait("Decision", "ED-292")]
    public void A_whole_inline_session_never_cuts_the_pill_the_label_the_cross_or_the_text_edge()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
            var script = Said();
            rig.Speak(script);
            rig.Engine = script.CreateEngine(TimeSpan.FromSeconds(1));
            var (view, window) = rig.Open(900);
            var column = One<ComposerTextColumn>(view);
            (double Width, double Opacity, double Listening, double Transcribing, double Finishing, double Cross, double Inset) Read() => rig.Main.OnMain(() =>
            {
                var pill = Pill(view);
                return (pill.ShownWidth, pill.ShownOpacity, pill.LabelOpacity(VoiceLabelState.Listening), pill.LabelOpacity(VoiceLabelState.Transcribing), pill.LabelOpacity(VoiceLabelState.Finishing),
                    pill.CrossOpacity, column.Margin.Right);
            });
            var series = new List<(double Width, double Opacity, double Listening, double Transcribing, double Finishing, double Cross, double Inset)> { Read() };
            void Step(int frames)
            {
                for (var k = 0; k < frames; k++)
                {
                    rig.Elapse(window, FrameTime);
                    series.Add(Read());
                }
            }
            Step(30);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Step(220); // listening: the label goes to Transcribing and the width eases with it
            rig.Main.OnMain(() => Click(Slot(view).HitTarget)); // the second click: Finishing
            Step(60);
            for (var k = 0; k < 600 && rig.Main.OnMain(() => view.Voice!.State) != VoiceSessionState.Rest; k++) Step(1); // the final pass, Landing
            Step(90); // the fold
            Assert.True(series.Max(s => s.Width) > 100, "the pill opened");
            NoFrameCuts("width", series.Select(s => s.Width).ToList(), SteepestStep(CssCurves.Ez, 0.42));
            NoFrameCuts("opacity", series.Select(s => s.Opacity).ToList(), SteepestStep(CssCurves.Ease, 0.14));
            NoFrameCuts("listening label", series.Select(s => s.Listening).ToList(), SteepestStep(CssCurves.Ease, 0.14)); // out .14 s when the words change, the fastest label fade
            NoFrameCuts("transcribing label", series.Select(s => s.Transcribing).ToList(), SteepestStep(CssCurves.Ease, 0.14));
            NoFrameCuts("finishing label", series.Select(s => s.Finishing).ToList(), SteepestStep(CssCurves.Ease, 0.14));
            NoFrameCuts("cross", series.Select(s => s.Cross).ToList(), SteepestStep(CssCurves.Ease, 0.16));
            NoFrameCuts("text edge", series.Select(s => s.Inset).ToList(), SteepestStep(CssCurves.Ez, 0.42));
            var end = series[^1];
            Assert.Equal((40.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0), (end.Width, end.Opacity, end.Listening, end.Transcribing, end.Finishing, end.Cross, end.Inset)); // everything has left again
            Assert.Contains(series, s => s.Transcribing > 0.99); // the label really went through Transcribing
            Assert.Contains(series, s => s.Finishing > 0.99);
            rig.Close(window);
        });
    }

    [Fact] // V-T88: the Cross style's session
    [Trait("Decision", "ED-292")]
    public void A_whole_cross_session_never_cuts_the_cross_or_the_text_edge()
    {
        headless.OnUiThread(() =>
        {
            using var rig = ComposerVoicePillGeometryTests.Quiet(VoiceOrbSettings.Default with { ListeningStyle = VoiceListeningStyle.Cross });
            var (view, window) = rig.Open(900);
            var column = One<ComposerTextColumn>(view);
            var series = new List<(double Slide, double Opacity, double Inset)>();
            void Step(int frames)
            {
                for (var k = 0; k < frames; k++)
                {
                    rig.Elapse(window, FrameTime);
                    series.Add(rig.Main.OnMain(() => (Slot(view).CrossSlide, Slot(view).CrossOpacity, column.Margin.Right)));
                }
            }
            Step(10);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Step(60);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Step(300);
            NoFrameCuts("slide", series.Select(s => s.Slide).ToList(), SteepestStep(CssCurves.Ez, 0.42));
            NoFrameCuts("opacity", series.Select(s => s.Opacity).ToList(), SteepestStep(CssCurves.Ease, 0.2));
            NoFrameCuts("text edge", series.Select(s => s.Inset).ToList(), SteepestStep(CssCurves.Ez, 0.42));
            Assert.Equal((0.0, 0.0, 0.0), (series[^1].Slide, series[^1].Opacity, series[^1].Inset));
            Assert.True(series.Max(s => s.Slide) > 0.99);
            rig.Close(window);
        });
    }

    [Fact] // V-T88: the floating pill's session: the orb lifts and settles on a spring, never cut
    [Trait("Decision", "ED-292")]
    public void A_whole_floating_session_lifts_the_orb_and_settles_it_back_without_a_cut()
    {
        headless.OnUiThread(() =>
        {
            using var rig = ComposerVoicePillGeometryTests.Quiet(VoiceOrbSettings.Default with { ListeningStyle = VoiceListeningStyle.Float });
            var (view, window) = rig.Open(900);
            var series = new List<(double Lift, double Width, double Opacity)>();
            void Step(int frames)
            {
                for (var k = 0; k < frames; k++)
                {
                    rig.Elapse(window, FrameTime);
                    series.Add(rig.Main.OnMain(() => (Slot(view).Lift, Pill(view).ShownWidth, Pill(view).ShownOpacity)));
                }
            }
            Step(10);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Step(90);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Step(300);
            var lift = series.Select(s => s.Lift).ToList();
            for (var k = 1; k < lift.Count; k++) Assert.True(Math.Abs(lift[k] - lift[k - 1]) <= 0.2, $"the lift jumps at frame {k}: {lift[k - 1]:F3} to {lift[k]:F3}");
            Assert.True(lift.Max() > 0.99 && Math.Abs(lift[^1]) < 1e-3, "lifted, then settled back");
            NoFrameCuts("width", series.Select(s => s.Width).ToList(), SteepestStep(CssCurves.Ez, 0.42));
            NoFrameCuts("opacity", series.Select(s => s.Opacity).ToList(), SteepestStep(CssCurves.Ease, 0.14));
            rig.Close(window);
        });
    }
}
