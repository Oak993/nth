using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port.FirstRun;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.FirstRun;

// WV-1 (OD-0098 welcome-voice pick, Place A): the section in the Welcome page, its words per state, and OD-0076 (every visible change is transitioned, nothing cuts).
[Collection("Headless")]
public sealed class WelcomeVoiceSectionTests(HeadlessSession headless)
{
    private void OnWelcome(VoiceQuestionRig? rig, Action<Control, WelcomeView> check)
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = new WelcomeView(rig?.Question);
                var host = RootViewRig.Show(model, view, 900);
                try { check(host, view); }
                finally { RootViewRig.Close(host); }
            });
        });
    }

    private static void Settle(Control host)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            RootViewRig.Layout(host);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void The_Voice_typing_section_stands_above_Activity_list_and_below_the_add_project_stack() => OnWelcome(VoiceQuestionRig.Pc(), (host, _) =>
    {
        var voice = RootViewRig.In(RootViewRig.Text(host, "Voice typing"), host).Top;
        var activity = RootViewRig.In(RootViewRig.Text(host, "Activity list"), host).Top;
        var add = RootViewRig.In(RootViewRig.Text(host, "Or drop a folder anywhere in this window."), host).Top;
        Assert.True(add < voice, "after the drop hint");
        Assert.True(voice < activity, "above Activity list");
    });

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void Without_a_question_the_page_has_no_voice_section() => OnWelcome(null, (host, _) =>
        Assert.False(Shows(host, "Voice typing")));

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void The_ask_state_shows_its_heading_body_and_the_two_buttons_in_the_order_V2_draws_them()
    {
        var rig = VoiceQuestionRig.Pc();
        OnWelcome(rig, (host, _) =>
        {
            Assert.True(Shows(host, WelcomeVoiceWords.Heading(VoiceQuestionState.Ask)!));
            Assert.True(Shows(host, rig.Said(VoiceQuestionState.Ask)!));
            Assert.Contains("874 MB", rig.Said(VoiceQuestionState.Ask), StringComparison.Ordinal); // the owner's PC: large-v3-turbo-q8_0
            var buttons = RootViewRig.All<Button>(host).Select(RootViewRig.AllText).ToList();
            Assert.True(buttons.IndexOf("Maybe later") >= 0 && buttons.IndexOf("Maybe later") < buttons.IndexOf("Yes, set it up"));
        });
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void Choosing_no_shows_the_no_words_and_downloads_nothing()
    {
        var rig = VoiceQuestionRig.Pc();
        rig.Prep.Models.ThrowOnAnyTransfer = true;
        OnWelcome(rig, (host, _) =>
        {
            RootViewRig.All<WelcomeVoiceSection>(host).Single().Choose(0); // no, the first of the two buttons
            Settle(host);
            Assert.Equal(VoiceQuestionState.No, rig.Question.State);
            Assert.True(Shows(host, rig.Said(VoiceQuestionState.No)!));
            Assert.Empty(rig.Prep.Models.Installs);
            Assert.False(rig.Prep.Preparation.HasConsent);
        });
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void Choosing_yes_starts_the_preparation_and_shows_the_yes_words_then_the_ready_words()
    {
        var rig = VoiceQuestionRig.Pc();
        rig.Prep.Models.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        OnWelcome(rig, (host, _) =>
        {
            var section = RootViewRig.All<WelcomeVoiceSection>(host).Single();
            var run = Task.Run(() => section.Choose(1)); // yes, the second
            Assert.True(SpinWait.SpinUntil(() => rig.Question.State == VoiceQuestionState.Yes, TimeSpan.FromSeconds(5)));
            Settle(host);
            Assert.Equal(VoiceQuestionState.Yes, rig.Question.State);
            Assert.True(rig.Prep.Preparation.HasConsent);
            Assert.True(Shows(host, rig.Said(VoiceQuestionState.Yes)!));
            rig.Prep.Models.Gate.SetResult();
            Finish(run);
            Settle(host);
            Assert.True(Shows(host, rig.Said(VoiceQuestionState.Ready)!));
        });
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void Each_state_shows_its_words_and_the_slow_question_shows_its_note_and_choices()
    {
        var rig = VoiceQuestionRig.Slow();
        OnWelcome(rig, (host, _) =>
        {
            Finish(Task.Run(() => rig.Question.YesAsync(null, CancellationToken.None)));
            Settle(host);
            Assert.Equal(VoiceQuestionState.Slow, rig.Question.State);
            Assert.True(Shows(host, rig.Said(VoiceQuestionState.Slow)!));
            Assert.True(Shows(host, WelcomeVoiceWords.Foot(VoiceQuestionState.Slow)!));
            Assert.True(Shows(host, WelcomeVoiceWords.Details(VoiceQuestionState.Slow, rig.Question.SmallMb)[0]!));
            var section = RootViewRig.All<WelcomeVoiceSection>(host).Single();
            Finish(Task.Run(() => section.Choose(0)));
            Settle(host);
            Assert.Equal(VoiceQuestionState.SlowLive, rig.Question.State);
            Assert.True(Shows(host, rig.Said(VoiceQuestionState.SlowLive)!));
            Assert.False(Shows(host, rig.Said(VoiceQuestionState.Slow)!));
        });
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void A_change_of_state_crossfades_and_the_section_height_follows_it_with_no_cut()
    {
        var rig = VoiceQuestionRig.Slow();
        OnWelcome(rig, (host, _) =>
        {
            var manual = new ManualFrameSource();
            var time = new ManualTimeProvider();
            Finish(Task.Run(() => rig.Question.YesAsync(null, CancellationToken.None))); // to the slow question, with no frame clock bound: taken at once
            Settle(host);
            Assert.Equal(VoiceQuestionState.Slow, rig.Question.State);
            using var frames = FrameClock.Source.Override(manual);
            using var clock = SwiftClock.Provider.Override(time);
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false });
            Settle(host);
            var section = RootViewRig.All<WelcomeVoiceSection>(host).Single();
            var askHeight = section.Bounds.Height;
            var askText = rig.Said(VoiceQuestionState.Slow)!;
            var noText = rig.Said(VoiceQuestionState.SlowLive)!;
            var answer = rig.Question.AnswerAsync(LiveWordsChoice.Live, CancellationToken.None);
            Assert.True(answer.IsCompleted); // the fakes finish at once, so the change lands on this thread, in this flow
            Settle(host);
            // the frame the change lands: both layers are there, the old one still opaque, the new one clear, the height where it was
            Assert.True(Shows(host, askText));
            Assert.True(Shows(host, noText));
            Assert.Equal(askHeight, section.Bounds.Height, 1);
            var oldLayer = Layer(host, askText);
            var newLayer = Layer(host, noText);
            Assert.True(oldLayer.Opacity > 0.99, $"old layer opacity {oldLayer.Opacity}");
            Assert.True(newLayer.Opacity < 0.01, $"new layer opacity {newLayer.Opacity}");
            var step = TimeSpan.FromSeconds(1.0 / 60);
            var heights = new List<double>();
            var midway = false;
            for (var frame = 0; frame < 30; frame++)
            {
                time.Advance(step);
                manual.Advance(step);
                Settle(host);
                heights.Add(section.Bounds.Height);
                if (frame == 7 && Shows(host, askText)) midway = Layer(host, askText).Opacity is > 0 and < 1 && Layer(host, noText).Opacity is > 0 and < 1;
            }
            Assert.True(midway, "mid-transition both layers are partly visible");
            Assert.False(Shows(host, askText));
            Assert.True(Shows(host, noText));
            Assert.Equal(1.0, Layer(host, noText).Opacity, 3);
            Assert.True(heights.Distinct().Count() > 3, "the height moves over several frames, it does not jump");
            var before = askHeight;
            foreach (var height in heights)
            {
                Assert.True(Math.Abs(height - before) < 40, $"a jump from {before} to {height}");
                before = height;
            }
        });
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void Under_reduce_motion_a_state_is_taken_at_once()
    {
        var rig = VoiceQuestionRig.Pc();
        OnWelcome(rig, (host, _) =>
        {
            var manual = new ManualFrameSource();
            var time = new ManualTimeProvider();
            using var frames = FrameClock.Source.Override(manual);
            using var clock = SwiftClock.Provider.Override(time);
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
            Settle(host);
            var askText = rig.Said(VoiceQuestionState.Ask)!;
            rig.Question.No();
            Settle(host);
            Assert.False(Shows(host, askText));
            Assert.Equal(1.0, Layer(host, rig.Said(VoiceQuestionState.No)!).Opacity, 3);
        });
    }

    // The question's runs end on a pool thread (no context is captured); the view posts the change to its own thread, which Settle runs.
    private static void Finish(Task run) => Assert.True(SpinWait.SpinUntil(() => run.IsCompleted, TimeSpan.FromSeconds(5)), "the run did not finish");

    private static bool Shows(Control host, string text) => RootViewRig.AllText(host).Contains(text, StringComparison.Ordinal);

    private static Control Layer(Control host, string text) =>
        RootViewRig.All<WelcomeVoiceSection>(host).Single().Layers.Single(layer => RootViewRig.AllText(layer).Contains(text, StringComparison.Ordinal));
}
