using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Xunit;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-10 fix round (review of e2e29a02f): V-8.5 "focus is not stolen": a real pointer press on the orb takes the keyboard from the composer's editor (the orb's hit target is focusable,
// V-14.2), and nothing gives it back, so after the words land, Return starts a new voice session instead of sending them.
[Collection("Headless")]
public sealed class ComposerVoiceFocusTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Rule", "R-TEST-10")]
    public void A_pointer_click_on_the_orb_does_not_leave_the_keyboard_on_the_orb_when_the_editor_had_it()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            var orb = rig.Main.OnMain(() => LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(view).Single());
            var box = rig.Main.OnMain(() => One<ComposerNSTextView>(view));
            rig.Main.OnMain(() => Assert.True(box.Focus()));
            rig.PumpWindow(window);
            void Click()
            {
                rig.Main.OnMain(() =>
                {
                    var at = Frame(orb.HitTarget, window).Center;
                    window.MouseMove(at);
                    window.MouseDown(at, MouseButton.Left);
                    window.MouseUp(at, MouseButton.Left);
                });
            }
            Click();
            rig.Elapse(window, 3.0);
            Click();
            rig.Elapse(window, 10);
            Assert.Equal(VoiceSessionState.Rest, view.Voice!.Session.State);
            Assert.Equal(VoiceComposerRig.SwitchThemeText, rig.Runtime.Draft.Text);
            Assert.Same(box, rig.Main.OnMain(() => window.FocusManager!.GetFocusedElement())); // the editor, so Return sends the words
            rig.Close(window);
        });
    }
}
