using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.HomeSamples;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-18 (DESIGN.md rev 5, 2.4 the voice sheet: Armilla at 176 px follows the mic level while listening and the speech level while speaking, the reply is shown with
// the current word lit; 2.5 Reduce motion; 2.2 the thread's copy steps back; 16 "done when"): the sheet over the home, on a real conversation (VoiceRig) in a headless window.
// Frames are the test's: the sheet's per-frame step is called by hand.
[Collection("Headless")]
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-10")]
public sealed class VoiceSheetTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static (AssistantHomeView Home, VoiceRig Rig) Make(Func<AssistantHomeParts, AssistantHomeParts>? change = null, params MissionJob[] jobs)
    {
        var rig = new VoiceRig();
        rig.Jobs.Set(jobs);
        var parts = new AssistantHomeParts(rig.Jobs, rig.Decisions, rig.Controls) { Name = DextraHost.Name, Voice = rig.Conversation, Talk = rig.Conversation.Toggle, TalkChord = () => "Ctrl+Alt+Enter" };
        return (new AssistantHomeView(change?.Invoke(parts) ?? parts), rig);
    }

    [Fact]
    public void The_sheet_is_hidden_until_the_conversation_opens_and_then_covers_the_thread_which_steps_back()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (home, rig) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(home, 1200, 900);
            var sheet = home.VoiceSheet!;
            Assert.False(sheet.GetVisualParent<Control>()!.IsVisible);
            Assert.True(home.HeroShown);

            rig.Conversation.Start(VoiceStart.Toggle);
            stage.Layout();

            Assert.True(sheet.IsEffectivelyVisible);
            Assert.False(home.HeroShown); // only one presence shows (2.2)
            Assert.Equal("Listening", sheet.StateText);
            Assert.Equal(ArmillaState.Listening, sheet.Mark.State);
            Assert.Equal(176, sheet.Mark.MarkSize);
            Assert.Equal(ArmillaKind.Live, sheet.Mark.Kind);

            sheet.DoneButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            stage.Layout();

            Assert.False(sheet.IsEffectivelyVisible);
            Assert.True(home.HeroShown);
            Assert.Equal(VoicePhase.Closed, rig.Conversation.Phase);
        });
    }

    [Fact]
    public void Armilla_takes_the_pose_of_each_phase_and_the_buttons_follow()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (home, rig) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(home, 1200, 900);
            var sheet = home.VoiceSheet!;
            rig.Conversation.Start(VoiceStart.Toggle);
            stage.Layout();
            Assert.False(sheet.InterruptButton.IsEnabled); // nothing to interrupt while it listens

            rig.Ask("what is running");
            stage.Layout();

            Assert.Equal("Speaking", sheet.StateText);
            Assert.Equal(ArmillaState.Speaking, sheet.Mark.State);
            Assert.True(sheet.InterruptButton.IsEnabled);
            Assert.Equal("Nothing is running.", sheet.ReplyText);
            Assert.Equal("what", sheet.HeardText.Split(' ')[0]);

            rig.Conversation.Close();
            stage.Layout();
            Assert.Equal(ArmillaState.Idle, sheet.Mark.State);
        });
    }

    [Fact]
    public void The_mark_follows_the_microphone_while_listening_and_Dextras_speech_while_it_speaks_and_nothing_otherwise()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (home, rig) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(home, 1200, 900);
            var sheet = home.VoiceSheet!;
            var levels = new List<double>();
            sheet.LevelSink = levels.Add;
            rig.Conversation.Start(VoiceStart.Toggle);
            stage.Layout();

            sheet.Pump();
            Assert.Equal(0, levels[^1], 3);
            var voice = SyntheticSpeech.Voice(2, 0.5, 0.2);
            var heard = new List<double>();
            for (var at = 0; at < voice.Length; at += VoiceRig.Chunk)
            {
                rig.Step(voice.AsSpan(at, Math.Min(VoiceRig.Chunk, voice.Length - at)));
                sheet.Pump();
                heard.Add(levels[^1]);
                Assert.Equal(rig.Conversation.Level, levels[^1], 3);
            }
            Assert.InRange(heard.Max(), 0.2, 1.0);

            rig.Heard.Enqueue("what is running");
            rig.Say(SyntheticSpeech.Voice(2, 0.6, 0.12));
            rig.Wait(1.5);
            Assert.Equal(VoicePhase.Speaking, rig.Conversation.Phase);
            var spoken = new List<double>();
            for (var i = 0; i < 100; i++)
            {
                rig.Step();
                sheet.Pump();
                spoken.Add(levels[^1]);
            }
            Assert.Contains(spoken, level => level > 0.5);
            Assert.Equal(rig.Speech.Level, levels[^1], 3);

            rig.Conversation.Stop();
            sheet.Pump();
            Assert.Equal(0, levels[^1], 3);
        });
    }

    [Fact]
    public void With_reduce_motion_the_mark_is_calm_and_only_its_brightness_follows_the_voice_and_the_sheet_still_shows_every_state()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (home, rig) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(home, 1200, 900);
            var sheet = home.VoiceSheet!;
            rig.Conversation.Start(VoiceStart.Toggle);
            stage.Layout();

            Assert.False(sheet.Mark.Look.Reduce);
            EnvironmentValues.SetAccessibilityReduceMotion(stage.Window, true);
            stage.Layout();
            Assert.True(sheet.Mark.Look.Reduce); // the engine then moves nothing and lets the voice change the brightness only (ArmillaStillPoseTests)
            Assert.Equal(ArmillaState.Listening, sheet.Mark.State);
            rig.Ask("what is running");
            Assert.Equal(ArmillaState.Speaking, sheet.Mark.State);
            Assert.True(sheet.Mark.Look.Reduce);
        });
    }

    [Fact]
    public void The_word_being_spoken_is_lit_and_the_text_never_changes_so_nothing_reflows()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (home, rig) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(home, 1200, 900);
            var sheet = home.VoiceSheet!;
            rig.Conversation.Start(VoiceStart.Toggle);
            rig.Ask("what is running"); // "Nothing is running.": three words over the 10 s of the test voice
            stage.Layout();
            var box = sheet.Bounds;
            var before = sheet.ReplyText;

            Assert.Equal(0, sheet.LitWord);
            for (var i = 0; i < 400; i++) rig.Step(); // 4 s: past the second word's start at 3.3 s
            sheet.Pump();
            stage.Layout();

            Assert.Equal(1, sheet.LitWord);
            Assert.Equal(before, sheet.ReplyText);
            Assert.Equal(box, sheet.Bounds);
            for (var i = 0; i < 400; i++) rig.Step();
            sheet.Pump();
            Assert.Equal(2, sheet.LitWord);
            rig.Conversation.Close();
            stage.Layout();
        });
    }

    [Fact]
    public void Escape_closes_the_sheet_and_gives_the_keyboard_back_to_where_it_was()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (home, rig) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(home, 1200, 900);
            home.StopAllButton.Focus();
            stage.Layout();
            Assert.Same(home.StopAllButton, stage.Focused);

            rig.Conversation.Start(VoiceStart.Toggle);
            stage.Layout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            stage.Layout();
            Assert.Same(home.VoiceSheet!.DoneButton, stage.Focused);

            stage.Press(Key.Escape);

            Assert.Equal(VoicePhase.Closed, rig.Conversation.Phase);
            Assert.Same(home.StopAllButton, stage.Focused);
        });
    }

    [Fact]
    public void The_sheet_has_accessible_names_a_live_region_and_the_hint_names_the_chord()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (home, rig) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(home, 1200, 900);
            var sheet = home.VoiceSheet!;
            rig.Conversation.Start(VoiceStart.Toggle);
            stage.Layout();

            Assert.Equal("Voice", AutomationProperties.GetName(sheet));
            Assert.Equal("Interrupt Dextra", AutomationProperties.GetName(sheet.InterruptButton));
            Assert.Equal("Done talking", AutomationProperties.GetName(sheet.DoneButton));
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(sheet.Mark));
            Assert.Contains("Ctrl+Alt+Enter", sheet.HintText, StringComparison.Ordinal);
            Assert.Equal("Listening", sheet.Announced);

            rig.Ask("what is running");
            Assert.Equal("Speaking. Nothing is running.", sheet.Announced);
        });
    }

    [Fact]
    public void A_note_is_shown_under_the_words_and_a_long_reply_wraps_at_375_px_with_no_sideways_scroll()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (home, rig) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(home, 375, 700);
            var sheet = home.VoiceSheet!;
            rig.Model = null;
            rig.Conversation.Start(VoiceStart.Toggle); // no speech model: the sheet says why
            stage.Layout();
            Assert.Contains("speech model", sheet.NoteText, StringComparison.Ordinal);

            rig.Model = new Marrow.Core.Port.Voice.Seams.SpeechModel("test", null);
            rig.Brain.Reply = _ => new VoiceAnswer(string.Join(' ', Enumerable.Repeat("a long answer that keeps going and going", 6)) + ".");
            rig.Conversation.Close();
            rig.Conversation.Start(VoiceStart.Toggle);
            rig.Ask("tell me a story");
            stage.Layout();

            var scroll = sheet.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5, $"{scroll.Extent.Width} vs {scroll.Viewport.Width}");
            Assert.True(sheet.Bounds.Right <= stage.Window.Bounds.Width + 0.5);
            Assert.NotEqual("", sheet.ReplyText);
        });
    }

    [Fact]
    public void The_voice_marks_motion_is_given_before_every_list_and_a_home_without_a_voice_has_no_sheet()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (home, rig) = Make();
            using var disposeRig = rig;
            using var stage = new HomeStage(home, 1200, 900);
            var scope = MotionScope.For(stage.Window);
            Assert.Contains(scope.Lists, entry => entry.Rank == ArmillaListRank.Voice);
            Assert.True((int)ArmillaListRank.Voice < (int)ArmillaListRank.Board);

            var plain = new AssistantHomeView(new AssistantHomeParts(rig.Jobs, rig.Decisions, rig.Controls) { Name = "Dextra" });
            Assert.Null(plain.VoiceSheet);
        });
    }
}
