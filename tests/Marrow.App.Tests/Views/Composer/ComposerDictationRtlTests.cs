using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-11, V-T81 (voice-SPEC V-8.9; RESEARCH 1.5: Urdu, Arabic, Persian): right-to-left words. The layer uses the editor's own bidi layout, so each word is drawn where that layout puts it (the
// first word at the right end of its run) and the words after a change glide to their new places along ez; the caret stands at the left end of the last word; the hand-over to the editor is
// the same pixels. The fixture is a short Urdu sentence ("this is a test"), the words said as a scripted speaker says them (no microphone, no engine).
[Collection("Headless")]
public sealed class ComposerDictationRtlTests(HeadlessSession headless)
{
    private const double Frame = 1.0 / 60;

    // یہ ایک ٹیسٹ ہے
    private static readonly string[] Urdu = ["یہ", "ایک", "ٹیسٹ", "ہے"];

    private static VoiceOrbSlot Slot(ComposerView view) => LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(view).Single();

    private static VoiceComposerRig Seen()
    {
        var rig = new VoiceComposerRig();
        rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
        return rig;
    }

    private static TranscriptUpdate Live(params WordOp[] ops) => new(TranscriptUpdateKind.Live, ops.Select(op => new ScheduledOp(TimeSpan.Zero, op)).ToList(), []);

    private static Point[] Places(ComposerNSTextView box, string text, int[] starts, int[] lengths, double width)
    {
        using var layout = new TextLayout(text, new Typeface(box.FontFamily, box.FontStyle, box.FontWeight), box.FontSize, Brushes.Black, box.TextAlignment, TextWrapping.Wrap, null, null,
            box.FlowDirection, width);
        return starts.Select((start, i) => layout.HitTestTextRange(start, lengths[i]).First().TopLeft).ToArray();
    }

    private static (int[] Starts, int[] Lengths) Spans(IReadOnlyList<string> words)
    {
        var starts = new int[words.Count];
        var at = 0;
        for (var i = 0; i < words.Count; i++)
        {
            starts[i] = at;
            at += words[i].Length + 1;
        }
        return (starts, words.Select(w => w.Length).ToArray());
    }

    [Fact] // V-T81: each word is where the editor's own bidi layout puts it: the first word at the right end of the run
    [Trait("Decision", "ED-292")]
    public void Urdu_words_are_drawn_where_the_bidi_layout_puts_them_and_the_caret_stands_at_the_left_of_the_last()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => DictationProbe.Blur(view));
            var layer = rig.Main.OnMain(() => DictationProbe.Layer(view));
            var box = rig.Main.OnMain(() => DictationProbe.Box(view));
            rig.Main.OnMain(() => layer.Begin(new DraftSelection("", 0, 0)));
            rig.Main.OnMain(() => layer.Apply(Live(WordOp.Add(0, Urdu[0], false), WordOp.Add(1, Urdu[1], false), WordOp.Add(2, Urdu[2], false), WordOp.Add(3, Urdu[3], true))));
            rig.Elapse(window, 1.5);
            var width = rig.Main.OnMain(() => layer.Bounds.Width);
            var (starts, lengths) = Spans(Urdu);
            var expected = Places(box, string.Join(' ', Urdu), starts, lengths, width);
            rig.Main.OnMain(() =>
            {
                var now = SwiftClock.Now;
                for (var i = 0; i < Urdu.Length; i++)
                    Assert.True(Math.Abs(layer.DisplayedPlace(i, now).X - expected[i].X) < 1e-6 && Math.Abs(layer.DisplayedPlace(i, now).Y - expected[i].Y) < 1e-6,
                        $"word {i} is drawn at {layer.DisplayedPlace(i, now)}, the bidi layout puts it at {expected[i]}");
                Assert.Equal(string.Join(' ', Urdu), layer.Text);
                for (var i = 1; i < Urdu.Length; i++) Assert.True(expected[i].X < expected[i - 1].X, "in a right-to-left run each word comes to the left of the one before it");
                var caret = layer.CaretRect(now)!.Value;
                Assert.True(caret.Right <= layer.DisplayedPlace(3, now).X + box.Padding.Left, $"the caret ({caret}) stands at the left of the last word ({layer.DisplayedPlace(3, now)})");
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T81: a word added to a right-to-left run comes in at its own place while the words before it glide to theirs along ez
    [Trait("Decision", "ED-292")]
    public void A_word_added_to_an_Urdu_run_rises_in_its_place_while_the_others_glide_frame_by_frame()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => DictationProbe.Blur(view));
            var layer = rig.Main.OnMain(() => DictationProbe.Layer(view));
            var box = rig.Main.OnMain(() => DictationProbe.Box(view));
            rig.Main.OnMain(() => layer.Begin(new DraftSelection("", 0, 0)));
            rig.Main.OnMain(() => layer.Apply(Live(WordOp.Add(0, Urdu[0], false), WordOp.Add(1, Urdu[1], false), WordOp.Add(2, Urdu[2], false))));
            rig.Elapse(window, 1.5);
            var width = rig.Main.OnMain(() => layer.Bounds.Width);
            var (threeStarts, threeLengths) = Spans(Urdu.Take(3).ToArray());
            var (fourStarts, fourLengths) = Spans(Urdu);
            var before = Places(box, string.Join(' ', Urdu.Take(3)), threeStarts, threeLengths, width);
            var after = Places(box, string.Join(' ', Urdu), fourStarts, fourLengths, width);
            Assert.True(Math.Abs(after[0].X - before[0].X) > 5, "the first word moves when a word is added at the left end of the run");

            rig.Main.OnMain(() => layer.Apply(Live(WordOp.Add(3, Urdu[3], true))));
            for (var frame = 1; frame <= 25; frame++)
            {
                rig.Elapse(window, Frame);
                var e = CssCurves.At(CssCurves.Ez, frame * Frame, 0.3);
                rig.Main.OnMain(() =>
                {
                    var now = SwiftClock.Now;
                    for (var i = 0; i < 3; i++)
                    {
                        var expected = new Point(before[i].X + (after[i].X - before[i].X) * e, before[i].Y + (after[i].Y - before[i].Y) * e);
                        var drawn = layer.DisplayedPlace(i, now);
                        Assert.True(Math.Abs(drawn.X - expected.X) < 1e-3 && Math.Abs(drawn.Y - expected.Y) < 1e-3, $"word {i} at {frame * Frame:F3} s: {drawn} against ez's {expected}");
                    }
                    Assert.Equal(after[3], layer.DisplayedPlace(3, now)); // the new word is at its own place from the start, and only rises
                });
            }
            rig.Close(window);
        });
    }

    [Fact] // V-T81: the whole session on the Urdu fixture: the words reach the draft live and the hand-over to the editor is the same pixels
    [Trait("Decision", "ED-292")]
    public void An_Urdu_session_lands_with_a_pixel_identical_hand_over()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(new VoiceScript(
            [
                new ScriptedWord(Urdu[0], 0.5, 0.9),
                new ScriptedWord(Urdu[1], 1.0, 1.2),
                new ScriptedWord(Urdu[2], 1.3, 1.8),
                new ScriptedWord(Urdu[3], 2.0, 2.5),
            ], "ur"));
            var (view, window) = rig.Open();
            rig.Main.OnMain(() => DictationProbe.Blur(view));
            var layer = rig.Main.OnMain(() => DictationProbe.Layer(view));
            var box = rig.Main.OnMain(() => DictationProbe.Box(view));
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 3.0);
            Assert.Equal(VoiceSessionState.Listening, view.Voice!.Session.State);
            Assert.Contains(Urdu[0], rig.Main.OnMain(() => rig.Runtime.Draft.Text), StringComparison.Ordinal); // live
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));

            var (drawn, handed) = DictationProbe.HandOver(rig, window, layer);
            Assert.NotNull(drawn);
            Assert.NotNull(handed);
            var region = rig.Main.OnMain(() => DictationProbe.Region(box, window));
            Assert.Equal(string.Join(' ', Urdu), rig.Main.OnMain(() => box.Text));
            Assert.Equal(string.Join(' ', Urdu), rig.Main.OnMain(() => rig.Runtime.Draft.Text));
            Assert.True(DictationProbe.Same(drawn!.Value, handed!.Value, region),
                $"the last frame the layer drew and the first the editor drew differ by {DictationProbe.Difference(drawn.Value, handed.Value, region):F4}");
            rig.Close(window);
        });
    }
}
