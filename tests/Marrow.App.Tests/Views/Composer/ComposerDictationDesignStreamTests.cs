using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// VT-11, V-T76 and V-14.8 (voice-SPEC V-7.5, V-7.6, V-8.7): the voice design's own live words. The fixture is what the design's SCRIPT, SYN (rng 91), STREAM (rng 7) and planFinal produce
// (tests/fixtures/voice/make-dictation-stream.js, run on the design's arithmetic): "Add a shortcut for the theme switch, and show it in the tooltip too." arriving word by word with the
// design's first guesses (theme -> team, switch, -> switch, tooltip -> tool, too. -> too), their revisions, the final pass a second click plans, and Landing's ink. The dictation layer is
// fed those events at those times; at every 60 Hz frame every word's look is compared with the design's curves worked out here from the events alone (ease and ez solved as a browser does).
[Collection("Headless")]
public sealed class ComposerDictationDesignStreamTests(HeadlessSession headless)
{
    private const double Frame = 1.0 / 60;

    private static double Seconds(long ticks) => ticks / (double)TimeSpan.TicksPerSecond;

    private static TimeSpan Span(double seconds) => TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));

    private static VoiceComposerRig Seen()
    {
        var rig = new VoiceComposerRig();
        rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
        return rig;
    }

    // What the design's applyEvent does with an event: an add writes the word tentative and confirms the word before it; a rev revises.
    private static void LiveOps(DictationStreamEvent e, List<(double T, WordOp Op)> into, double at)
    {
        if (e.Kind == "add")
        {
            into.Add((at, WordOp.Add(e.Index, e.Text, tentative: true)));
            if (e.Index > 0) into.Add((at, WordOp.Confirm(e.Index - 1)));
        }
        else into.Add((at, WordOp.Revise(e.Index, e.Text)));
    }

    // The look of word `word` at `now` that the design's CSS gives, from the operations alone.
    private static (bool Present, double Opacity, double Rise, double Tentative, double TextOpacity, double TextRise, List<(string Text, double Opacity, double Rise)> Old) Expected(
        IReadOnlyList<(double T, WordOp Op)> events, int word, double now)
    {
        double? addAt = null;
        var tentative = false;
        double? confirmAt = null;
        var text = "";
        var olds = new List<(string Text, double Start, double OpacityFrom, double RiseFrom)>();
        double? reviseAt = null;
        double textOpacityFrom = 1, textRiseFrom = 0;
        double TextOpacity(double t) => reviseAt is null ? 1 : textOpacityFrom + (1 - textOpacityFrom) * CssCurves.At(CssCurves.Ease, t - reviseAt.Value, 0.22);
        double TextRise(double t) => reviseAt is null ? 0 : textRiseFrom + (0 - textRiseFrom) * CssCurves.At(CssCurves.Ez, t - reviseAt.Value, 0.3);
        foreach (var (t, op) in events)
        {
            if (t > now) continue;
            // Locked applyEvent confirms the previous visual span for EVERY add, including
            // planFinal's adds. The logical tentative flag still belongs to the transcriber.
            if (op.Kind == WordOpKind.Add && op.Index == word + 1 && confirmAt is null && addAt is not null)
                confirmAt = t;
            if (op.Index != word) continue;
            switch (op.Kind)
            {
                case WordOpKind.Add:
                    addAt = t;
                    tentative = op.Tentative;
                    text = op.Text!;
                    break;
                case WordOpKind.Confirm when confirmAt is null && addAt is not null:
                    confirmAt = t;
                    break;
                case WordOpKind.Revise when op.Text != text:
                    olds.Add((text, t, TextOpacity(t), TextRise(t)));
                    reviseAt = t;
                    textOpacityFrom = 0;
                    textRiseFrom = DictationMotion.RiseFrom;
                    text = op.Text!;
                    break;
            }
        }
        if (addAt is null) return (false, 0, 0, 0, 0, 0, []);
        var since = now - addAt.Value;
        var mix = !tentative ? 0 : confirmAt is null ? 1 : 1 - CssCurves.At(CssCurves.Ease, now - confirmAt.Value, 0.45);
        var old = olds.Where(o => now < o.Start + 0.34).Select(o => (o.Text, o.OpacityFrom * (1 - CssCurves.At(CssCurves.Ease, now - o.Start, 0.22)),
            o.RiseFrom + (-6 - o.RiseFrom) * CssCurves.At(CssCurves.Ez, now - o.Start, 0.3))).ToList();
        return (true, CssCurves.At(CssCurves.Ease, since, 0.3), 6 * (1 - CssCurves.At(CssCurves.Ez, since, 0.4)), mix, TextOpacity(now), TextRise(now), old);
    }

    private static void Compare(DictationLayer layer, IReadOnlyList<(double T, WordOp Op)> events, double now)
    {
        var seen = layer.Words.Words;
        var instant = SwiftClock.Now;
        var present = 0;
        for (var i = 0; i < 40; i++)
        {
            var expected = Expected(events, i, now);
            if (!expected.Present) break;
            present++;
            Assert.True(i < seen.Count, $"word {i} should be there at {now:F3} s");
            var look = layer.Words.Look(seen[i], instant);
            bool Near(double a, double b) => Math.Abs(a - b) < 1e-5;
            Assert.True(Near(expected.Opacity, look.Opacity), $"word {i} opacity at {now:F3} s: {look.Opacity} against the design's {expected.Opacity}");
            Assert.True(Near(expected.Rise, look.Rise), $"word {i} rise at {now:F3} s: {look.Rise} against {expected.Rise}");
            Assert.True(Near(expected.Tentative, look.Tentative), $"word {i} colour at {now:F3} s: {look.Tentative} against {expected.Tentative}");
            Assert.True(Near(expected.TextOpacity, look.TextOpacity), $"word {i} new text opacity at {now:F3} s: {look.TextOpacity} against {expected.TextOpacity}");
            Assert.True(Near(expected.TextRise, look.TextRise), $"word {i} new text rise at {now:F3} s: {look.TextRise} against {expected.TextRise}");
            Assert.Equal(expected.Old.Count, look.Old.Count);
            for (var k = 0; k < expected.Old.Count; k++)
            {
                Assert.Equal(expected.Old[k].Text, look.Old[k].Text);
                Assert.True(Near(expected.Old[k].Opacity, look.Old[k].Opacity), $"word {i} old text opacity at {now:F3} s: {look.Old[k].Opacity} against {expected.Old[k].Opacity}");
                Assert.True(Near(expected.Old[k].Rise, look.Old[k].Rise), $"word {i} old text rise at {now:F3} s: {look.Old[k].Rise} against {expected.Old[k].Rise}");
            }
        }
        Assert.Equal(present, seen.Count);
    }

    [Fact] // V-T76: the design's live stream, every word every frame, against the design's curves
    [Trait("Decision", "ED-292")]
    public void The_designs_live_stream_is_written_on_the_designs_curves_at_every_frame()
    {
        headless.OnUiThread(() =>
        {
            var fixture = DictationStreamFixture.Load();
            using var rig = Seen();
            var (view, window) = rig.Open();
            var layer = rig.Main.OnMain(() => DictationProbe.Layer(view));
            rig.Main.OnMain(() => layer.Begin(new DraftSelection("", 0, 0)));

            var events = new List<(double T, WordOp Op)>();
            var scheduled = new List<ScheduledOp>();
            foreach (var e in fixture.Stream)
            {
                var at = Seconds(Span(e.T).Ticks);
                var ops = new List<(double T, WordOp Op)>();
                LiveOps(e, ops, at);
                foreach (var (t, op) in ops) scheduled.Add(new ScheduledOp(Span(t), op));
                events.AddRange(ops);
            }
            rig.Main.OnMain(() => layer.Apply(new TranscriptUpdate(TranscriptUpdateKind.Live, scheduled, [])));
            var start = rig.Main.OnMain(() => SwiftClock.Now);

            var frames = (int)Math.Ceiling((fixture.Stream[^1].T + 1.0) / Frame);
            for (var frame = 1; frame <= frames; frame++)
            {
                rig.Elapse(window, Frame);
                var now = rig.Main.OnMain(() => Seconds((SwiftClock.Now - start).Ticks));
                rig.Main.OnMain(() => Compare(layer, events, now));
            }
            // the words as the design ends the live part: its last guesses are revised, "too." only waits for the final pass
            rig.Main.OnMain(() => Assert.Equal("Add a shortcut for the theme switch, and show it in the tooltip too", string.Join(' ', layer.Words.Logical.Select(w => w.Text))));
            rig.Close(window);
        });
    }

    [Fact] // V-7.6, V-8.7: the second click once everything is said, the design's final pass (its adds 0.07 s apart, revisions 0.12 s, from 0.18 s), then Landing's ink 25 ms apart
    [Trait("Decision", "ED-292")]
    public void The_designs_final_pass_and_the_landing_ink_are_written_on_the_designs_curves_at_every_frame()
    {
        headless.OnUiThread(() =>
        {
            var fixture = DictationStreamFixture.Load();
            var click = fixture.Clicks.First(c => c.Final.Any(e => e.Kind == "add"));
            using var rig = Seen();
            var (view, window) = rig.Open();
            var layer = rig.Main.OnMain(() => DictationProbe.Layer(view));
            rig.Main.OnMain(() => layer.Begin(new DraftSelection("", 0, 0)));

            var events = new List<(double T, WordOp Op)>();
            var scheduled = new List<ScheduledOp>();
            foreach (var e in fixture.Stream.Where(e => e.T <= click.Tl))
            {
                var ops = new List<(double T, WordOp Op)>();
                LiveOps(e, ops, Seconds(Span(e.T).Ticks));
                foreach (var (t, op) in ops) scheduled.Add(new ScheduledOp(Span(t), op));
                events.AddRange(ops);
            }
            rig.Main.OnMain(() => layer.Apply(new TranscriptUpdate(TranscriptUpdateKind.Live, scheduled, [])));
            var start = rig.Main.OnMain(() => SwiftClock.Now);

            // the live part in one long step (a window that was not drawn for seconds catches up by the operations' own times), then every frame from the click on
            rig.Elapse(window, click.Tl);
            var clickTime = rig.Main.OnMain(() => Seconds((SwiftClock.Now - start).Ticks));
            rig.Main.OnMain(() => Compare(layer, events, clickTime));

            var finalOps = new List<ScheduledOp>();
            foreach (var e in click.Final)
            {
                var after = Span(e.T - click.Tl);
                var op = e.Kind == "add" ? WordOp.Add(e.Index, e.Text, tentative: true) : WordOp.Revise(e.Index, e.Text);
                finalOps.Add(new ScheduledOp(after, op));
                events.Add((clickTime + Seconds(after.Ticks), op));
            }
            rig.Main.OnMain(() => layer.Apply(new TranscriptUpdate(TranscriptUpdateKind.Final, finalOps, [])));
            double Run(double until)
            {
                var now = clickTime;
                while (now < until)
                {
                    rig.Elapse(window, Frame);
                    now = rig.Main.OnMain(() => Seconds((SwiftClock.Now - start).Ticks));
                    rig.Main.OnMain(() => Compare(layer, events, now));
                }
                return now;
            }
            var processingEnd = Run(clickTime + Math.Max(0.75, click.Duration + 0.3)); // V-7.6: the Processing state lasts so long from the click
            Assert.Equal("Add a shortcut for the theme switch, and show it in the tooltip too.", rig.Main.OnMain(() => string.Join(' ', layer.Words.Logical.Select(w => w.Text))));

            // Landing: every tentative word inks, 25 ms apart (the plan of V-8.7)
            var tentative = rig.Main.OnMain(() => layer.Words.Logical.Select(w => new TranscriptWord(w.Text, w.Tentative)).ToList());
            var landing = TranscriptOps.PlanLanding(tentative);
            Assert.True(landing.Count >= 2, "the last live word and the final pass's word are tentative: " + landing.Count);
            foreach (var ink in landing) events.Add((processingEnd + Seconds(ink.At.Ticks), ink.Op));
            rig.Main.OnMain(() => layer.Apply(new TranscriptUpdate(TranscriptUpdateKind.Landing, landing, tentative.Select(w => w with { Tentative = false }).ToList())));
            Run(processingEnd + 0.4); // the last ink ends 25 ms x (n - 1) + .45 s after the first; the layer hands over a frame after everything has settled
            rig.Close(window);
        });
    }
}
