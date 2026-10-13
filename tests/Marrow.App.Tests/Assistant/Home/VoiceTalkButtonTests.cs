using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port.Armilla;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-18 (DESIGN.md rev 5, 2.4 "Hold the orb inside nth (a pointer hold): talk to the Assistant while held", "Dextra's look in voice ... 24 px on the button"; 16 "hold on the talk
// button"): the talk button of Dextra's home composer. A hold talks while the pointer is down, a tap leaves it listening, a press while Dextra talks cuts it; the button is Armilla at
// 24 px; only Dextra's own home thread has it (every other chat keeps the dictation orb).
[Collection("Headless")]
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-10")]
public sealed class VoiceTalkButtonTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static (VoiceTalkButton Button, VoiceRig Rig, ManualTimeProvider Clock) Make()
    {
        var rig = new VoiceRig();
        var clock = new ManualTimeProvider();
        return (new VoiceTalkButton(() => rig.Conversation, () => "Ctrl+Alt+Enter", clock.GetUtcNow), rig, clock);
    }

    [Fact]
    public void The_button_is_Armilla_at_24_px_with_a_name_a_tip_that_names_the_chord_and_it_is_off_while_no_voice_runs()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (button, rig, _) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(button, 200, 80);

            Assert.Equal(24, button.Mark.MarkSize);
            Assert.Equal(ArmillaKind.Row, button.Mark.Kind);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(button.Mark));
            Assert.Equal("Talk", AutomationProperties.GetName(button.Button));
            Assert.Equal("Talk (hold to talk, or Ctrl+Alt+Enter anywhere)", ToolTip.GetTip(button.Button));
            Assert.True(button.Button.IsEnabled);

            var none = new VoiceTalkButton(() => null);
            Assert.False(none.Button.IsEnabled);
            none.Press(); // nothing happens, nothing throws
            none.Release();
        });
    }

    [Fact]
    public void A_press_that_is_held_listens_while_down_and_ends_the_request_when_let_go()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (button, rig, clock) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(button, 200, 80);
            rig.Heard.Enqueue("pause everything");

            button.Press();
            Assert.True(button.IsHeld);
            Assert.Equal(VoicePhase.Listening, rig.Conversation.Phase);
            Assert.Equal(ArmillaState.Listening, button.Mark.State);
            rig.Say(SyntheticSpeech.Voice(2, 1.0, 0.12));
            rig.Wait(3.0); // longer than the 1.2 s of silence that ends a toggled request: a held one waits
            Assert.Equal(VoicePhase.Listening, rig.Conversation.Phase);

            clock.Advance(TimeSpan.FromSeconds(5));
            button.Release();

            Assert.False(button.IsHeld);
            Assert.Equal(["PauseAll Voice: Pause everything"], rig.Controls.Calls);
        });
    }

    [Fact]
    public void A_tap_leaves_it_listening_and_the_request_ends_on_silence_like_the_chord()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (button, rig, clock) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(button, 200, 80);
            rig.Heard.Enqueue("resume everything");

            button.Press();
            clock.Advance(TimeSpan.FromMilliseconds(120));
            button.Release();
            Assert.Equal(VoicePhase.Listening, rig.Conversation.Phase);
            Assert.Equal(VoiceStart.Toggle, rig.Conversation.Mode);

            rig.Say(SyntheticSpeech.Voice(2, 1.0, 0.12));
            rig.Wait(1.5);

            Assert.Equal(["ResumeAll"], rig.Controls.Calls);
        });
    }

    [Fact]
    public void A_press_while_Dextra_talks_cuts_it_and_listens_and_is_not_a_hold()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (button, rig, clock) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(button, 200, 80);
            rig.Conversation.Start(VoiceStart.Toggle);
            rig.Ask("what is running");
            Assert.Equal(VoicePhase.Speaking, rig.Conversation.Phase);
            Assert.Equal(ArmillaState.Speaking, button.Mark.State);

            button.Press();

            Assert.False(button.IsHeld);
            Assert.True(rig.Player.StopCount >= 1);
            Assert.Equal(VoicePhase.Listening, rig.Conversation.Phase);
            clock.Advance(TimeSpan.FromSeconds(2));
            button.Release(); // nothing was held: nothing more happens
            Assert.Equal(VoicePhase.Listening, rig.Conversation.Phase);
        });
    }

    [Fact]
    public void Space_and_Enter_on_the_focused_button_toggle_the_conversation_so_the_keyboard_and_a_screen_reader_can_talk()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (button, rig, _) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(button, 200, 80);

            Click(button.Button);

            Assert.Equal(VoicePhase.Listening, rig.Conversation.Phase);
            Assert.False(button.IsHeld);
        });
    }

    [Fact]
    public void A_real_pointer_press_and_release_hold_and_let_go()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (button, rig, _) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(button, 200, 80);
            var box = stage.Box(button.Button);
            var middle = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);

            stage.Window.MouseDown(middle, MouseButton.Left);
            stage.Layout();
            Assert.True(button.IsHeld);
            Assert.Equal(VoicePhase.Listening, rig.Conversation.Phase);

            stage.Window.MouseUp(middle, MouseButton.Left);
            stage.Layout();
            Assert.False(button.IsHeld);
        });
    }

    [Fact]
    public void Only_Dextras_own_home_thread_gets_the_talk_button_and_every_other_chat_keeps_the_orb()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var project = AppModelSamples.Project("Project", h.NewFolder("project"));
            var chat = AppModelSamples.Thread(project);
            h.WriteLibrary([project], [chat]);
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                model.AssistantHost = DextraHosts.Make(model);
                var host = model.AssistantHost;
                host.Settings.Update(p => p with { FirstRunDone = true });
                var home = host.HomeThread(() => h.NewFolder("dextra-home"))!;
                var astur = home with { Id = Guid.NewGuid(), AssistantID = Guid.NewGuid() };
                var mission = home with { Id = Guid.NewGuid(), MissionID = Guid.NewGuid() };
                var head = home with { Id = Guid.NewGuid(), ParentThreadID = home.Id };

                Assert.IsType<VoiceTalkButton>(DextraTalkSlot.For(model.Runtime(home.Id), home));
                Assert.Null(DextraTalkSlot.For(model.Runtime(chat.Id), model.Thread(chat.Id)!));
                Assert.Null(DextraTalkSlot.For(model.Runtime(home.Id), astur));
                Assert.Null(DextraTalkSlot.For(model.Runtime(home.Id), mission));
                Assert.Null(DextraTalkSlot.For(model.Runtime(home.Id), head));
            });
        });
    }
}
