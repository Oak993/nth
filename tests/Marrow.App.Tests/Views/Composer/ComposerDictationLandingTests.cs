using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
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

// VT-11, V-T77 and V-T78 (voice-SPEC V-8.3, V-8.7): the hand-over and the landing. When the words have inked and settled the layer gives the text back to the editor in one frame, and
// the two frames are the same pixels; the whole insertion is one edit of the editor's own text, so one undo removes it and the history before it is still there. Sessions are scripted.
[Collection("Headless")]
public sealed class ComposerDictationLandingTests(HeadlessSession headless)
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

    // Listens for `seconds` of the script, then the second click.
    private static void SayAndStop(VoiceComposerRig rig, ComposerView view, AvaloniaWindow window, double seconds)
    {
        rig.Main.OnMain(() => Click(Slot(view).HitTarget));
        rig.Elapse(window, seconds);
        Assert.Equal(VoiceSessionState.Listening, view.Voice!.Session.State);
        rig.Main.OnMain(() => Click(Slot(view).HitTarget));
    }

    [Fact] // V-T77, V-8.3: pixel-identical hand-over
    [Trait("Decision", "ED-292")]
    public void The_hand_over_from_the_layer_to_the_editor_is_pixel_identical()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window, layer, box) = Opened(rig, "");
            var baselineFrames = rig.Frames.Pending;
            SayAndStop(rig, view, window, 3.0);

            var (drawn, handed) = DictationProbe.HandOver(rig, window, layer);
            Assert.NotNull(drawn);
            Assert.NotNull(handed);
            var region = rig.Main.OnMain(() => DictationProbe.Region(box, window));
            Assert.Equal(VoiceComposerRig.SwitchThemeText, rig.Main.OnMain(() => box.Text));
            Assert.False(rig.Main.OnMain(() => box.TextHidden));
            Assert.True(DictationProbe.Same(drawn!.Value, handed!.Value, region),
                $"the last frame the layer drew and the first the editor drew differ by {DictationProbe.Difference(drawn.Value, handed.Value, region):F4}");
            rig.Elapse(window, 3);
            Assert.True(rig.Frames.Pending <= baselineFrames, $"frames still asked for after the hand-over: {rig.Frames.Pending} against {baselineFrames}");
            rig.Close(window);
        });
    }

    [Fact] // V-T77: the hand-over is the same frame for a text that began in a draft (the words after it, the text before it)
    [Trait("Decision", "ED-292")]
    public void The_hand_over_is_pixel_identical_inside_an_existing_draft()
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
            SayAndStop(rig, view, window, 3.0);
            var (drawn, handed) = DictationProbe.HandOver(rig, window, layer);
            Assert.NotNull(drawn);
            Assert.NotNull(handed);
            var region = rig.Main.OnMain(() => DictationProbe.Region(box, window));
            Assert.Equal("keep switch the theme please. tail", rig.Main.OnMain(() => box.Text));
            Assert.True(DictationProbe.Same(drawn!.Value, handed!.Value, region),
                $"the last frame the layer drew and the first the editor drew differ by {DictationProbe.Difference(drawn.Value, handed.Value, region):F4}");
            rig.Close(window);
        });
    }

    [Fact] // V-T78, V-8.7: one undo step covers the whole insertion, and the history before it is kept
    [Trait("Decision", "ED-292")]
    public void One_undo_removes_the_whole_dictation_and_the_edits_before_it_are_still_there()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window, layer, box) = Opened(rig, "A");
            rig.Main.OnMain(() =>
            {
                box.Focus();
                box.SelectionStart = box.SelectionEnd = 1;
                box.SelectedText = " B"; // the user's own edit, one step of the editor's undo history
                box.SelectionStart = box.SelectionEnd = box.Text!.Length;
            });
            rig.Elapse(window, 0.05);
            rig.Main.OnMain(() => DictationProbe.Blur(view));
            Assert.Equal("A B", rig.Main.OnMain(() => box.Text));

            SayAndStop(rig, view, window, 3.0);
            DictationProbe.HandOver(rig, window, layer);
            Assert.Equal("A B switch the theme please.", rig.Main.OnMain(() => box.Text));
            Assert.Equal("A B switch the theme please.", rig.Main.OnMain(() => rig.Runtime.Draft.Text));

            rig.Main.OnMain(() => box.Undo());
            rig.Elapse(window, 0.05);
            Assert.Equal("A B", rig.Main.OnMain(() => box.Text)); // the whole dictation, one step
            Assert.Equal("A B", rig.Main.OnMain(() => rig.Runtime.Draft.Text));
            rig.Main.OnMain(() => box.Redo());
            Assert.Equal("A B switch the theme please.", rig.Main.OnMain(() => box.Text));
            rig.Main.OnMain(() =>
            {
                box.Undo();
                box.Undo();
            });
            Assert.Equal("A", rig.Main.OnMain(() => box.Text)); // the edit before the dictation is the next step back
            rig.Close(window);
        });
    }

    [Fact] // V-8.7: the caret stays after the text; the editor is the user's again (typing works)
    [Trait("Decision", "ED-292")]
    public void After_the_landing_the_caret_is_after_the_text_and_the_editor_takes_typing_again()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Seen();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window, layer, box) = Opened(rig, "");
            SayAndStop(rig, view, window, 3.0);
            DictationProbe.HandOver(rig, window, layer);
            rig.Main.OnMain(() =>
            {
                Assert.Equal(VoiceComposerRig.SwitchThemeText.Length, box.SelectionStart);
                Assert.Equal(box.SelectionStart, box.SelectionEnd);
                Assert.False(box.IsReadOnly);
                box.Focus();
            });
            rig.Main.OnMain(() => window.KeyTextInput(" ok"));
            rig.Elapse(window, 0.05);
            Assert.Equal(VoiceComposerRig.SwitchThemeText + " ok", rig.Main.OnMain(() => box.Text));
            Assert.Equal(VoiceComposerRig.SwitchThemeText + " ok", rig.Main.OnMain(() => rig.Runtime.Draft.Text));
            rig.Close(window);
        });
    }

    [Fact] // V-8.8 with VT-11: "send" sends the landed text 0.35 s after Landing starts, while the ink may still be settling; the composer ends clean
    [Trait("Decision", "ED-292")]
    public void A_send_after_landing_leaves_no_layer_and_no_text_behind()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig(VoiceOrbSettings.Default with { AfterVoice = VoiceAfterVoice.Send });
            rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window, layer, box) = Opened(rig, "");
            SayAndStop(rig, view, window, 3.0);
            for (var frame = 0; frame < 600 && !rig.Main.OnMain(() => layer.HandedOver); frame++) rig.Elapse(window, Frame);
            rig.Elapse(window, 1);
            Assert.True(rig.Main.OnMain(() => layer.HandedOver));
            Assert.Equal("", rig.Main.OnMain(() => rig.Runtime.Draft.Text));
            Assert.Equal("", rig.Main.OnMain(() => box.Text));
            Assert.False(rig.Main.OnMain(() => box.TextHidden));
            Assert.Equal([VoiceComposerRig.SwitchThemeText], rig.Main.OnMain(() => rig.Runtime.SentPrompts.ToList()));
            rig.Close(window);
        });
    }
}
