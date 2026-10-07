using Avalonia;
using Avalonia.Input;
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
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-11, V-T79 (voice-SPEC V-8.6, V-14.4): the withdraw on cancel. Esc (or the cross) makes this session's words leave from the end, one every 30 ms, each fading over .26 s on ease and
// moving 5 px down over .3 s on ez, the text that was there before staying; then the draft is what it was before the session, with its selection. Every frame is tested against the curves.
[Collection("Headless")]
public sealed class ComposerDictationWithdrawTests(HeadlessSession headless)
{
    private const double Frame = 1.0 / 60;

    private static VoiceOrbSlot Slot(ComposerView view) => LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(view).Single();

    private static VoiceComposerRig Seen()
    {
        var rig = new VoiceComposerRig();
        rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
        return rig;
    }

    private static (ComposerView View, AvaloniaWindow Window, DictationLayer Layer, ComposerNSTextView Box) Opened(VoiceComposerRig rig, string draft)
    {
        var (view, window) = rig.Open();
        if (draft.Length > 0)
        {
            rig.Main.OnMain(() => rig.Runtime.Draft = new ComposerDraft { Text = draft });
            rig.Elapse(window, 0.05);
        }
        rig.Main.OnMain(() => DictationProbe.Blur(view));
        return (view, window, rig.Main.OnMain(() => DictationProbe.Layer(view)), rig.Main.OnMain(() => DictationProbe.Box(view)));
    }

    [Fact] // V-T79, V-8.6: the words leave from the end, 30 ms apart, on the withdraw curves; the text before them stays
    [Trait("Decision", "ED-292")]
    public void A_cancel_withdraws_the_words_from_the_end_30_ms_apart_on_the_designs_curves()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window, layer, box) = Opened(rig, "keep ");
            rig.Main.OnMain(() => box.SelectionStart = box.SelectionEnd = 5);
            rig.Elapse(window, 0.05);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 3.6); // every word has come and risen
            var count = rig.Main.OnMain(() => layer.Words.Count);
            Assert.True(count >= 3, "the words: " + count);
            Assert.False(rig.Main.OnMain(() => layer.Words.IsMoving(SwiftClock.Now)));
            var words = rig.Main.OnMain(() => layer.Words.Words.ToList());
            var draftBefore = rig.Main.OnMain(() => rig.Runtime.Draft.Text);
            Assert.StartsWith("keep ", draftBefore, StringComparison.Ordinal);

            var start = rig.Main.OnMain(() => SwiftClock.Now);
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape)));
            var sawText = new List<string>();
            for (var frame = 1; frame <= 70; frame++)
            {
                rig.Elapse(window, Frame);
                var t = rig.Main.OnMain(() => (SwiftClock.Now - start).TotalSeconds); // the clock's own time: a frame is a whole number of 100 ns ticks
                rig.Main.OnMain(() =>
                {
                    var now = SwiftClock.Now;
                    for (var k = 0; k < count; k++)
                    {
                        var word = words[count - 1 - k];
                        if (!layer.Words.Words.Contains(word)) continue;
                        var since = t - k * 0.03;
                        var look = layer.Words.Look(word, now);
                        var opacity = 1 - CssCurves.At(CssCurves.Ease, since, 0.26);
                        var rise = 5 * CssCurves.At(CssCurves.Ez, since, 0.3);
                        Assert.True(Math.Abs(look.Opacity - opacity) < 1e-5, $"word {count - 1 - k} opacity at {t:F3} s: {look.Opacity} against {opacity}");
                        Assert.True(Math.Abs(look.Rise - rise) < 1e-5, $"word {count - 1 - k} rise at {t:F3} s: {look.Rise} against {rise}");
                    }
                    if (layer.IsActive) Assert.StartsWith("keep ", layer.Text, StringComparison.Ordinal); // the text that was there before stays
                });
                sawText.Add(rig.Main.OnMain(() => rig.Runtime.Draft.Text));
            }
            Assert.True(rig.Main.OnMain(() => layer.HandedOver));
            // the draft shortened word by word, from the end, to what it was before the session
            Assert.Equal("keep ", sawText[^1]);
            Assert.True(sawText.Select(text => text.Length).Distinct().Count() > 2, "the draft shrank a word at a time");
            for (var i = 1; i < sawText.Count; i++) Assert.True(sawText[i].Length <= sawText[i - 1].Length, $"the draft only shrinks: {sawText[i - 1]} then {sawText[i]}");
            rig.Main.OnMain(() =>
            {
                Assert.Equal("keep ", box.Text);
                Assert.Equal((5, 5), (box.SelectionStart, box.SelectionEnd));
                Assert.False(box.TextHidden);
                Assert.False(box.IsReadOnly);
            });
            rig.Close(window);
        });
    }

    [Fact] // V-8.6: the draft returns to its pre-session text AND selection
    [Trait("Decision", "ED-292")]
    public void A_cancel_returns_the_draft_with_the_selection_it_had()
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
            rig.Elapse(window, 2.5);
            Assert.NotEqual("keep OLD tail", rig.Main.OnMain(() => rig.Runtime.Draft.Text)); // the selection was replaced as the words came
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape)));
            for (var frame = 0; frame < 120 && !rig.Main.OnMain(() => layer.HandedOver); frame++) rig.Elapse(window, Frame);
            rig.Elapse(window, 0.1);
            rig.Main.OnMain(() =>
            {
                Assert.Equal("keep OLD tail", rig.Runtime.Draft.Text);
                Assert.Equal("keep OLD tail", box.Text);
                Assert.Equal((5, 8), (Math.Min(box.SelectionStart, box.SelectionEnd), Math.Max(box.SelectionStart, box.SelectionEnd)));
                Assert.Equal(VoiceSessionState.Rest, view.Voice!.Session.State);
            });
            rig.Close(window);
        });
    }

    [Fact] // V-8.6 with V-3's pixel rule: after the withdraw the picture is the editor's with the original text, as before the session
    [Trait("Decision", "ED-292")]
    public void After_a_cancel_the_composer_looks_as_it_did_before_the_session()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window, layer, box) = Opened(rig, "keep this");
            var region = rig.Main.OnMain(() => DictationProbe.Region(box, window));
            var before = DictationProbe.Frame(window);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 2.5);
            rig.Main.OnMain(() => Press(view, Key.Escape));
            for (var frame = 0; frame < 120 && !rig.Main.OnMain(() => layer.HandedOver); frame++) rig.Elapse(window, Frame);
            rig.Elapse(window, 2.0);
            Assert.True(DictationProbe.Same(before, DictationProbe.Frame(window), region),
                $"the text area differs from before the session by {DictationProbe.Difference(before, DictationProbe.Frame(window), region):F4}");
            rig.Close(window);
        });
    }

    [Fact] // V-15 (an error ends the session with no landing and no withdraw): what was drawn leaves as a cancel's words do and the draft is what it was
    [Trait("Decision", "ED-292")]
    public void An_engine_failure_makes_the_drawn_words_leave_and_returns_the_draft()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window, layer, box) = Opened(rig, "keep ");
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            for (var frame = 0; frame < 400 && rig.Main.OnMain(() => layer.Words.Count) < 2; frame++) rig.Elapse(window, Frame);
            Assert.True(rig.Main.OnMain(() => layer.Words.Count) >= 2, "words were drawn");
            rig.Engine.FailNextDecode(new InvalidOperationException("the engine fell over"));
            for (var frame = 0; frame < 300 && view.Voice!.Session.State == VoiceSessionState.Listening; frame++) rig.Elapse(window, Frame);
            Assert.Equal(VoiceSessionState.Rest, view.Voice!.Session.State);
            for (var frame = 0; frame < 120 && !rig.Main.OnMain(() => layer.HandedOver); frame++) rig.Elapse(window, Frame);
            rig.Elapse(window, 0.1);
            rig.Main.OnMain(() =>
            {
                Assert.True(layer.HandedOver);
                Assert.Equal("keep ", rig.Runtime.Draft.Text);
                Assert.Equal("keep ", box.Text);
                Assert.False(box.TextHidden);
                Assert.False(box.IsReadOnly);
            });
            rig.Close(window);
        });
    }

    [Fact] // V-8.1, V-8.6, OD-0076: a cancel after a replaced selection brings the old text back by a fade (the mirror of its going), every frame on the .22 s ease; the hand-over is no jump
    [Trait("Decision", "ED-292")]
    public void A_cancel_after_a_replaced_selection_fades_the_old_text_back_in_and_the_hand_over_is_no_cut()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window, layer, box) = Opened(rig, "keep OLD tail");
            var unselectedBefore = DictationProbe.Frame(window);
            rig.Main.OnMain(() =>
            {
                box.SelectionStart = 5;
                box.SelectionEnd = 8;
            });
            rig.Elapse(window, 0.05);
            var selectedBefore = DictationProbe.Frame(window);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 2.5);
            var width = rig.Main.OnMain(() => layer.Bounds.Width);
            var area = rig.Main.OnMain(() => DictationProbe.Region(box, window));
            // the pixels of "OLD" in the original draft, less its first few columns (the caret stands at its left edge)
            using var layout = new TextLayout("keep OLD tail", new Typeface(box.FontFamily, box.FontStyle, box.FontWeight), box.FontSize, Brushes.Black, box.TextAlignment, TextWrapping.Wrap, null,
                null, box.FlowDirection, width);
            var old = layout.HitTestTextRange(5, 3).Single();
            var origin = rig.Main.OnMain(() => box.TranslatePoint(new Point(box.Padding.Left, box.Padding.Top), window)!.Value);
            var oldPixels = new PixelRect((int)(origin.X + old.X) + 5, area.Y, (int)Math.Ceiling(old.Width) - 5, area.Height);
            // The top of the selection is background only, above the glyph ink. Its expected pixels come from the editor before dictation, independently of the layer.
            var highlightPixels = new PixelRect(oldPixels.X, (int)Math.Floor(origin.Y + old.Y), oldPixels.Width, 2);
            Assert.True(DictationProbe.Difference(selectedBefore, unselectedBefore, highlightPixels) > 1, "the reference contains the original selection highlight");

            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape)));
            var frames = new List<(double T, (int Stride, byte[] Bytes, PixelSize Size) Frame)>();
            var diffs = new List<double>();
            double? endedAt = null;
            var start = rig.Main.OnMain(() => SwiftClock.Now);
            (int Stride, byte[] Bytes, PixelSize Size)? previous = null;
            double handOverJump = 0;
            for (var i = 0; i < 160; i++)
            {
                rig.Elapse(window, Frame);
                var (t, handed, count) = rig.Main.OnMain(() => ((SwiftClock.Now - start).TotalSeconds, layer.HandedOver, layer.Words.Words.Count));
                var frame = DictationProbe.Frame(window);
                if (previous is { } before)
                {
                    var jump = DictationProbe.Difference(before, frame, area);
                    if (handed) handOverJump = jump;
                    else diffs.Add(jump);
                }
                if (handed) break;
                if (endedAt is null && count == 0) endedAt = t;
                if (endedAt is { } ended) frames.Add((t - ended, frame));
                previous = frame;
            }
            Assert.NotNull(endedAt);
            Assert.True(rig.Main.OnMain(() => layer.HandedOver));
            // the old text's ink on each frame after the last word has gone follows the ease curve of the fade back in (the first frame has none: it has just begun)
            var first = frames[0].Frame;
            var total = DictationProbe.Difference(selectedBefore, first, oldPixels);
            Assert.True(total > 3, "the old text comes back: " + total);
            var highlightTotal = DictationProbe.Difference(selectedBefore, first, highlightPixels);
            Assert.True(highlightTotal > 1, "the selection highlight comes back");
            foreach (var (t, frame) in frames.Where(f => f.T > 0 && f.T <= 0.22))
            {
                var ink = DictationProbe.Difference(frame, first, oldPixels);
                var expected = CssCurves.At(CssCurves.Ease, t, 0.22);
                Assert.True(Math.Abs(ink / total - expected) < 0.06, $"the old text {t:F3} s after the words went: {ink / total:F3} of its ink, the .22 s ease says {expected:F3}");
                var highlight = DictationProbe.Difference(frame, first, highlightPixels);
                Assert.True(Math.Abs(highlight / highlightTotal - expected) < 0.06, $"the selection highlight at {t:F3} s: {highlight / highlightTotal:F3}, the .22 s ease says {expected:F3}");
            }
            Assert.True(DictationProbe.Same(selectedBefore, frames[^1].Frame, oldPixels), $"the old text and its selection match the editor's original selected image before the hand-over (difference {DictationProbe.Difference(selectedBefore, frames[^1].Frame, oldPixels):F4})");
            // and nothing shows whole at once: no frame to frame difference, the hand-over included, is larger than the fades' own peak
            var peak = diffs.Max();
            Assert.True(handOverJump <= peak + 1e-9, $"the hand-over frame differs from the one before by {handOverJump:F2}, the fades' peak is {peak:F2}");
            rig.Close(window);
        });
    }

    [Fact] // V-8.3, OD-0076: at a cancel the caret rides with the leaving words, then fades where they were (the design removes it after them); every frame against the curve
    [Trait("Decision", "ED-292")]
    public void A_cancel_keeps_the_caret_riding_and_fades_it_after_the_words_have_gone()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window, layer, box) = Opened(rig, "keep ");
            rig.Main.OnMain(() => box.SelectionStart = box.SelectionEnd = 5);
            rig.Elapse(window, 0.05);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 3.6);
            var before = rig.Main.OnMain(() => layer.CaretRect(SwiftClock.Now))!.Value;
            var start = rig.Main.OnMain(() => SwiftClock.Now);
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape)));
            double? endedAt = null;
            var previousX = before.X;
            for (var i = 0; i < 120; i++)
            {
                rig.Elapse(window, Frame);
                var (t, handed, count, caret, fade) = rig.Main.OnMain(() => ((SwiftClock.Now - start).TotalSeconds, layer.HandedOver, layer.Words.Words.Count, layer.CaretRect(SwiftClock.Now),
                    layer.CaretFade(SwiftClock.Now)));
                if (handed) break;
                Assert.True(caret is not null, $"the caret is there {t:F3} s after Esc (it rides with the words leaving)");
                if (i == 0) Assert.True(Math.Abs(caret!.Value.X - before.X) < 1.5, $"one frame after Esc the caret is where it was ({before.X}), not at {caret.Value.X}");
                Assert.True(caret!.Value.X <= previousX + 1e-6, $"the caret only moves toward the start: {previousX} then {caret.Value.X}");
                previousX = caret.Value.X;
                if (endedAt is null && count == 0) endedAt = t;
                var expectedFade = endedAt is { } e ? 1 - CssCurves.At(CssCurves.Ease, t - e, 0.18) : 1;
                Assert.True(Math.Abs(fade - expectedFade) < 1e-4, $"the caret's fade {t:F3} s after Esc is {fade}, the .18 s ease after the last word says {expectedFade}");
            }
            Assert.NotNull(endedAt);
            // where the words were: the end of the text that was there before them
            using var layout = new TextLayout("keep ", new Typeface(box.FontFamily, box.FontStyle, box.FontWeight), box.FontSize, Brushes.Black, box.TextAlignment, TextWrapping.Wrap, null, null,
                box.FlowDirection, 400);
            var insertionX = rig.Main.OnMain(() => box.Padding.Left) + layout.HitTestTextPosition(5).X + 1;
            Assert.True(Math.Abs(previousX - insertionX) < 3, $"the caret ends at the insertion point ({insertionX}), not at {previousX}");
            rig.Close(window);
        });
    }

    [Fact] // V-15 Listening -> Rest with nothing written: the layer was never drawing, and the composer is untouched
    [Trait("Decision", "ED-292")]
    public void A_cancel_before_any_word_leaves_the_editor_alone()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.QuietRoom();
            var (view, window, layer, box) = Opened(rig, "keep");
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            rig.Elapse(window, 0.5);
            rig.Main.OnMain(() =>
            {
                Assert.False(layer.IsActive);
                Assert.True(layer.IsOpen);
                Assert.False(box.TextHidden);
            });
            Assert.True(rig.Main.OnMain(() => Press(view, Key.Escape)));
            rig.Elapse(window, 0.5);
            rig.Main.OnMain(() =>
            {
                Assert.True(layer.HandedOver);
                Assert.Equal("keep", box.Text);
                Assert.Equal("keep", rig.Runtime.Draft.Text);
            });
            rig.Close(window);
        });
    }
}
