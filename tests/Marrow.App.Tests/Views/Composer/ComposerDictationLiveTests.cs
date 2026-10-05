using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Marrow.App.Port.Voice;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-11, V-T76 (voice-SPEC V-7.5, V-8.1 to V-8.4): the live words in a real composer. A scripted session says "switch the theme please." and its words reach the draft as they are
// recognised (the composer in its typing state) while the editor keeps its own text, still and clear; the dictation layer draws the text over it, the words rising in on the design's
// curves (checked on the rendered pixels: a word's ink follows its opacity, the tentative word is drawn at 0.6 of the text colour and turns solid on ease), the words after a revised one
// following to their places along ez, the caret a pixel past the last word. No microphone, no engine: the session is scripted (R-TEST-10).
[Collection("Headless")]
public sealed class ComposerDictationLiveTests(HeadlessSession headless)
{
    private const double Frame = 1.0 / 60;

    private static VoiceOrbSlot Slot(ComposerView view) => LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(view).Single();

    private static VoiceComposerRig Seen()
    {
        var rig = new VoiceComposerRig();
        rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
        return rig;
    }

    private static TranscriptUpdate Live(params WordOp[] ops) => new(TranscriptUpdateKind.Live, ops.Select(op => new ScheduledOp(TimeSpan.Zero, op)).ToList(), []);

    // The place the layout (the editor's own face, size and width) gives the word that starts at `start`: an independent hit test.
    private static Point Where(ComposerNSTextView box, string text, int start, int length, double width)
    {
        using var layout = new TextLayout(text, new Typeface(box.FontFamily, box.FontStyle, box.FontWeight), box.FontSize, Brushes.Black, box.TextAlignment, TextWrapping.Wrap, null, null,
            box.FlowDirection, width);
        return layout.HitTestTextRange(start, length).Single().TopLeft;
    }

    // The pixels a word covers, in the window: its hit-test box in the editor's own kind of layout, inside the editor's padding and place; the caret (a pixel past its end) is outside it.
    private static PixelRect Pixels(ComposerNSTextView box, AvaloniaWindow window, string text, int start, int length, double width)
    {
        using var layout = new TextLayout(text, new Typeface(box.FontFamily, box.FontStyle, box.FontWeight), box.FontSize, Brushes.Black, box.TextAlignment, TextWrapping.Wrap, null, null,
            box.FlowDirection, width);
        var rect = layout.HitTestTextRange(start, length).Single();
        var origin = box.TranslatePoint(new Point(box.Padding.Left, box.Padding.Top), window)!.Value;
        var left = (int)Math.Floor(origin.X + rect.X);
        var right = (int)Math.Ceiling(origin.X + rect.Right);
        return new PixelRect(left, (int)Math.Floor(origin.Y + rect.Y) - 8, right - left, (int)Math.Ceiling(rect.Height) + 16);
    }

    private static (ComposerView View, AvaloniaWindow Window, DictationLayer Layer, ComposerNSTextView Box) Opened(VoiceComposerRig rig, string draft)
    {
        var (view, window) = rig.Open();
        if (draft.Length > 0)
        {
            rig.Main.OnMain(() => rig.Runtime.Draft = new ComposerDraft { Text = draft });
            rig.Elapse(window, 0.05);
        }
        rig.Main.OnMain(() => DictationProbe.Blur(view)); // no editor caret in the pictures
        return (view, window, rig.Main.OnMain(() => DictationProbe.Layer(view)), rig.Main.OnMain(() => DictationProbe.Box(view)));
    }

    [Fact] // V-T76, V-8.2: the draft follows the words live; the composer is in its typing state; the editor keeps its text
    [Trait("Decision", "ED-292")]
    public void The_draft_follows_the_words_live_while_the_editor_keeps_its_own_text()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window, layer, box) = Opened(rig, "");
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.1);
            Assert.Equal(VoiceSessionState.Listening, view.Voice!.Session.State);
            Assert.True(rig.Main.OnMain(() => rig.Runtime.DraftIsEmpty));

            var drafts = new List<string>();
            var first = -1;
            for (var frame = 0; frame < 175; frame++) // 2.9 s of listening in all
            {
                rig.Elapse(window, Frame);
                var draft = rig.Main.OnMain(() => rig.Runtime.Draft.Text);
                if (draft.Length == 0) continue;
                if (first < 0) first = frame;
                if (drafts.Count == 0 || drafts[^1] != draft) drafts.Add(draft);
                rig.Main.OnMain(() =>
                {
                    Assert.False(rig.Runtime.DraftIsEmpty); // the composer's typing state: the light, the send button, the height
                    Assert.Equal("", box.Text); // the editor keeps what it had
                    Assert.True(box.TextHidden);
                    Assert.True(box.IsReadOnly);
                    Assert.True(layer.IsActive);
                    Assert.Equal(draft, layer.Text);
                });
            }
            Assert.True(first > 0, "words arrived while listening, not all at the end");
            Assert.True(drafts.Count >= 3, "the draft grew a word at a time: " + string.Join(" | ", drafts));
            Assert.StartsWith("switch", drafts[0], StringComparison.Ordinal);
            Assert.Contains("the", drafts[^1], StringComparison.Ordinal);
            Assert.True(view.Voice.Session.State == VoiceSessionState.Listening, "still listening");

            rig.Main.OnMain(() => Click(Slot(view).HitTarget)); // the second click
            for (var frame = 0; frame < 600 && !rig.Main.OnMain(() => layer.HandedOver); frame++) rig.Elapse(window, Frame);
            Assert.Equal(VoiceComposerRig.SwitchThemeText, rig.Main.OnMain(() => rig.Runtime.Draft.Text));
            Assert.Equal(VoiceComposerRig.SwitchThemeText, rig.Main.OnMain(() => box.Text));
            Assert.False(rig.Main.OnMain(() => box.TextHidden));
            Assert.False(rig.Main.OnMain(() => box.IsReadOnly));
            rig.Close(window);
        });
    }

    [Fact] // V-8.1: a selection is replaced when the first word arrives; the text before and after it stays; the caret ends after the words
    [Trait("Decision", "ED-292")]
    public void A_selection_is_replaced_by_the_first_word_and_the_text_around_it_stays()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window, layer, box) = Opened(rig, "keep OLD tail");
            rig.Main.OnMain(() =>
            {
                box.SelectionStart = 5;
                box.SelectionEnd = 8;
            });
            rig.Elapse(window, 0.05);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.1);
            for (var frame = 0; frame < 240 && rig.Main.OnMain(() => rig.Runtime.Draft.Text) == "keep OLD tail"; frame++) rig.Elapse(window, Frame);
            rig.Main.OnMain(() =>
            {
                var draft = rig.Runtime.Draft.Text;
                Assert.StartsWith("keep switch", draft, StringComparison.Ordinal);
                Assert.EndsWith(" tail", draft, StringComparison.Ordinal);
                Assert.DoesNotContain("OLD", draft, StringComparison.Ordinal);
                Assert.True(box.Text == "keep OLD tail", $"the editor still holds the draft the session began with, selection and all: '{box.Text}' (the draft is '{draft}', the layer '{layer.Text}')");
                Assert.Equal((5, 8), (Math.Min(box.SelectionStart, box.SelectionEnd), Math.Max(box.SelectionStart, box.SelectionEnd)));
            });
            rig.Elapse(window, 1.6); // on to three seconds of listening: the whole sentence has been said before the second click
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            for (var frame = 0; frame < 600 && !rig.Main.OnMain(() => layer.HandedOver); frame++) rig.Elapse(window, Frame);
            rig.Main.OnMain(() =>
            {
                Assert.True(box.Text == "keep switch the theme please. tail", $"the editor's text after the landing: '{box.Text}'");
                Assert.True(rig.Runtime.Draft.Text == "keep switch the theme please. tail", $"the draft after the landing: '{rig.Runtime.Draft.Text}'");
                Assert.Equal("keep switch the theme please.".Length, box.SelectionStart); // the caret ends after the inserted text
            });
            rig.Close(window);
        });
    }

    [Fact] // V-7.5 Add: a word's ink on the rendered frames follows the opacity curve (.3 s ease), at 0.6 of the text colour while it is tentative
    [Trait("Decision", "ED-292")]
    public void A_rising_words_ink_follows_the_opacity_curve_frame_by_frame_on_the_pixels()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            var (_, window, layer, box) = Opened(rig, "A");
            rig.Main.OnMain(() => layer.Begin(new DraftSelection("A", 1, 0)));
            rig.Main.OnMain(() => layer.Apply(Live(WordOp.Add(0, "xx", false))));
            rig.Elapse(window, 0.8);
            var width = rig.Main.OnMain(() => layer.Bounds.Width);
            var region = rig.Main.OnMain(() => Pixels(box, window, "A xx elephants", 5, 9, width));
            var baseline = DictationProbe.Frame(window);

            rig.Main.OnMain(() => layer.Apply(Live(WordOp.Add(1, "elephants", true))));
            var inks = new List<(double Seconds, double Ink)>();
            for (var frame = 1; frame <= 40; frame++)
            {
                rig.Elapse(window, Frame);
                inks.Add((frame * Frame, DictationProbe.Difference(DictationProbe.Frame(window), baseline, region)));
            }
            var settled = inks[^1].Ink;
            Assert.True(settled > 5, "the word is drawn: " + settled);
            foreach (var (seconds, ink) in inks)
            {
                var expected = CssCurves.At(CssCurves.Ease, seconds, 0.3);
                Assert.True(Math.Abs(ink / settled - expected) < 0.04, $"at {seconds:F3} s: ink {ink / settled:F3} of the settled against the opacity curve's {expected:F3}");
            }
            rig.Close(window);
        });
    }

    [Fact] // V-8.4, V-7.5 Confirm: tentative is the text colour at 0.6; solid after .45 s on ease
    [Trait("Decision", "ED-292")]
    public void The_tentative_word_is_drawn_at_0_6_of_the_text_colour_and_turns_solid_on_the_ease_curve()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            var (_, window, layer, box) = Opened(rig, "A");
            rig.Main.OnMain(() => layer.Begin(new DraftSelection("A", 1, 0)));
            rig.Main.OnMain(() => layer.Apply(Live(WordOp.Add(0, "xx", false))));
            rig.Elapse(window, 0.8);
            var width = rig.Main.OnMain(() => layer.Bounds.Width);
            var region = rig.Main.OnMain(() => Pixels(box, window, "A xx elephants", 5, 9, width));
            var baseline = DictationProbe.Frame(window);
            rig.Main.OnMain(() => layer.Apply(Live(WordOp.Add(1, "elephants", true))));
            rig.Elapse(window, 0.8); // risen, still tentative
            var tentative = DictationProbe.Difference(DictationProbe.Frame(window), baseline, region);

            rig.Main.OnMain(() => layer.Apply(Live(WordOp.Confirm(1))));
            var inks = new List<(double Seconds, double Ink)>();
            for (var frame = 1; frame <= 40; frame++)
            {
                rig.Elapse(window, Frame);
                inks.Add((frame * Frame, DictationProbe.Difference(DictationProbe.Frame(window), baseline, region)));
            }
            var solid = inks[^1].Ink;
            Assert.True(Math.Abs(tentative / solid - 0.6) < 0.03, $"tentative ink {tentative / solid:F3} of the solid, expected 0.6 (the text colour's 0.6, V-8.4)");
            foreach (var (seconds, ink) in inks)
            {
                var expected = 0.6 + 0.4 * CssCurves.At(CssCurves.Ease, seconds, 0.45);
                Assert.True(Math.Abs(ink / solid - expected) < 0.03, $"at {seconds:F3} s: ink {ink / solid:F3} of the solid against the colour curve's {expected:F3}");
            }
            rig.Close(window);
        });
    }

    [Fact] // V-7.5 FLIP: the words after a revised one follow their new places along ez over .3 s, from where they were
    [Trait("Decision", "ED-292")]
    public void The_words_after_a_revised_one_follow_to_their_new_places_by_FLIP_frame_by_frame()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            var (_, window, layer, box) = Opened(rig, "A");
            rig.Main.OnMain(() => layer.Begin(new DraftSelection("A", 1, 0)));
            rig.Main.OnMain(() => layer.Apply(Live(WordOp.Add(0, "alpha", false), WordOp.Add(1, "beta", false), WordOp.Add(2, "gamma", false))));
            rig.Elapse(window, 1.0);
            var width = rig.Main.OnMain(() => layer.Bounds.Width);
            var before = Where(box, "A alpha beta gamma", 13, 5, width);
            Assert.Equal(before, rig.Main.OnMain(() => layer.DisplayedPlace(2, SwiftClock.Now)));

            var longer = new string('B', 15);
            rig.Main.OnMain(() => layer.Apply(Live(WordOp.Revise(1, longer))));
            var after = Where(box, "A alpha " + longer + " gamma", 8 + 15 + 1, 5, width);
            Assert.True(after.X - before.X > 40, "the revision moved gamma to the right");
            for (var frame = 1; frame <= 25; frame++)
            {
                rig.Elapse(window, Frame);
                var t = frame * Frame;
                var e = CssCurves.At(CssCurves.Ez, t, 0.3);
                var expected = new Point(before.X + (after.X - before.X) * e, before.Y + (after.Y - before.Y) * e);
                var drawn = rig.Main.OnMain(() => layer.DisplayedPlace(2, SwiftClock.Now));
                Assert.True(Math.Abs(drawn.X - expected.X) < 1e-3 && Math.Abs(drawn.Y - expected.Y) < 1e-3, $"at {t:F3} s: gamma at {drawn} against ez's {expected}");
            }
            rig.Close(window);
        });
    }

    [Fact] // V-8.10: when the text passes the field's height the end is brought into view on the design's ez, never a jump
    [Trait("Decision", "ED-292")]
    public void A_long_dictation_scrolls_to_keep_its_end_in_view_with_an_ease_never_a_jump()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            var (view, window, layer, box) = Opened(rig, "");
            var scroll = rig.Main.OnMain(() => Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.ScrollViewer>(layer)!);
            rig.Main.OnMain(() => layer.Begin(new DraftSelection("", 0, 0)));
            var ops = Enumerable.Range(0, 120).Select(i => WordOp.Add(i, "word" + i, false)).ToArray();
            rig.Main.OnMain(() => layer.Apply(Live(ops)));
            var offsets = new List<double>();
            for (var frame = 0; frame < 90; frame++)
            {
                rig.Elapse(window, Frame);
                offsets.Add(rig.Main.OnMain(() => scroll.Offset.Y));
            }
            var (final, viewport, extent) = rig.Main.OnMain(() => (scroll.Offset.Y, scroll.Viewport.Height, scroll.Extent.Height));
            Assert.True(extent > viewport + 20, $"the text is taller than the field: {extent} against {viewport}");
            var layerWidth = rig.Main.OnMain(() => layer.Bounds.Width);
            var bottom = Where(box, string.Join(' ', Enumerable.Range(0, 120).Select(i => "word" + i)), 0, 1, layerWidth).Y; // the first line's top: 0
            using var all = new TextLayout(string.Join(' ', Enumerable.Range(0, 120).Select(i => "word" + i)), new Typeface(box.FontFamily, box.FontStyle, box.FontWeight), box.FontSize,
                Brushes.Black, box.TextAlignment, TextWrapping.Wrap, null, null, box.FlowDirection, layerWidth);
            Assert.True(Math.Abs(all.Height + 4 - extent) < 1.5, $"the field's extent {extent} is the text's height {all.Height + 4} at the layer's width {layerWidth} (editor {rig.Main.OnMain(() => box.Bounds.Width)}, first line {bottom})");
            Assert.True(final > 0 && Math.Abs(final - (extent - viewport)) < 2, $"the end is in view: offset {final} of at most {extent - viewport}");
            for (var i = 1; i < offsets.Count; i++) Assert.True(offsets[i] >= offsets[i - 1] - 1e-9, $"the view only moves toward the end: {offsets[i - 1]} then {offsets[i]}");
            // every frame is the design's .tin curve (ez over .36 s) of the travel, the move starting on the frame after the words arrive or one or two later (the first layout pass of the
            // taller text has to land before the view can go that far)
            var fits = Enumerable.Range(0, 4).Where(slip => offsets.Select((offset, i) => Math.Abs(offset - final * CssCurves.At(CssCurves.Ez, (i + 1 - slip) * Frame, 0.36))).All(error => error < 1.5)).ToList();
            Assert.True(fits.Count > 0, "the scroll follows ez over .36 s: " + string.Join(", ", offsets.Take(30).Select(o => o.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))));
            rig.Close(window);
        });
    }

    [Fact] // OD-0076 and the design's reduced motion: under Reduce Motion every transition of the words lands at once and the caret stands still
    [Trait("Decision", "ED-292")]
    public void Under_reduce_motion_the_words_land_at_once()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
            using var rig = Seen();
            var (_, window, layer, _) = Opened(rig, "");
            rig.Main.OnMain(() => layer.Begin(new DraftSelection("", 0, 0)));
            rig.Main.OnMain(() => layer.Apply(Live(WordOp.Add(0, "team", true))));
            rig.Elapse(window, Frame);
            rig.Main.OnMain(() =>
            {
                var look = layer.Words.Look(layer.Words.Words[0], SwiftClock.Now);
                Assert.Equal(1, look.Opacity);
                Assert.Equal(0, look.Rise);
            });
            rig.Main.OnMain(() => layer.Apply(Live(WordOp.Revise(0, "theme"), WordOp.Confirm(0))));
            rig.Elapse(window, Frame);
            rig.Main.OnMain(() =>
            {
                var look = layer.Words.Look(layer.Words.Words[0], SwiftClock.Now);
                Assert.Empty(look.Old);
                Assert.Equal(1, look.TextOpacity);
                Assert.Equal(0, look.TextRise);
                Assert.Equal(0, look.Tentative);
                Assert.False(layer.Words.IsMoving(SwiftClock.Now));
            });
            rig.Main.OnMain(() => layer.Apply(Live(WordOp.Remove(0))));
            rig.Elapse(window, Frame);
            rig.Main.OnMain(() => Assert.Empty(layer.Words.Words)); // gone at once
            rig.Close(window);
        });
    }

    [Fact] // V-8.3: the caret is 1.5 x 17, a pixel past the last word, and follows it
    [Trait("Decision", "ED-292")]
    public void The_caret_stands_a_pixel_past_the_last_word()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            var (_, window, layer, box) = Opened(rig, "");
            rig.Main.OnMain(() => layer.Begin(new DraftSelection("", 0, 0)));
            Assert.Null(rig.Main.OnMain(() => layer.CaretRect(SwiftClock.Now))); // no word, no caret
            rig.Main.OnMain(() => layer.Apply(Live(WordOp.Add(0, "switch", false), WordOp.Add(1, "the", true))));
            rig.Elapse(window, 1.0);
            var width = rig.Main.OnMain(() => layer.Bounds.Width);
            using var layout = new TextLayout("switch the", new Typeface(box.FontFamily, box.FontStyle, box.FontWeight), box.FontSize, Brushes.Black, box.TextAlignment, TextWrapping.Wrap, null, null,
                box.FlowDirection, width);
            var theRect = layout.HitTestTextRange(7, 3).Single();
            var caret = rig.Main.OnMain(() => layer.CaretRect(SwiftClock.Now))!.Value;
            Assert.Equal(1.5, caret.Width);
            Assert.Equal(17, caret.Height);
            var padding = rig.Main.OnMain(() => box.Padding);
            Assert.True(Math.Abs(caret.X - (padding.Left + theRect.Right + 1)) < 0.01, $"caret at {caret.X}, the word ends at {theRect.Right}");
            Assert.True(Math.Abs(caret.Y - (padding.Top + theRect.Y + (theRect.Height - 17) / 2)) < 0.01);
            rig.Close(window);
        });
    }
}
