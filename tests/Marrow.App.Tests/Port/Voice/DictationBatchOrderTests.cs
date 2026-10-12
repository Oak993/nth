using Avalonia.Controls;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
namespace Marrow.App.Tests.Port.Voice;

// ED-NEW-voicefix-ui-4 (owner 2026-10-11, 125%: an earlier live word "but" stayed drawn over the new Devanagari live text while the pill said Transcribing).
// Every live batch is the difference from the words after all the operations sent before it; the session paces its adds 60 ms apart (V-7.5, PaceLive), so
// the next batch's revises, removes and confirms (due at once) used to run before the earlier batch's later adds and were lost.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-voicefix-ui-4")]
public sealed class DictationBatchOrderTests(HeadlessSession headless)
{
    private static readonly TimeSpan Pace = TranscriptOps.MinAddSpacing;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_later_batch_never_runs_ahead_of_an_earlier_batchs_paced_adds(bool remove)
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            var editor = new ComposerNSTextView { Width = 320, FontSize = 14 };
            var layer = new DictationLayer(editor, null) { Width = 320 };
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var window = new Avalonia.Controls.Window { Width = 320, Height = 180, Content = new Grid { Children = { editor, layer } } };
            window.Show(); window.UpdateLayout();
            void Step(double seconds) { runtime.Advance(TimeSpan.FromSeconds(seconds)); frames.Advance(TimeSpan.FromSeconds(seconds)); window.UpdateLayout(); }
            try
            {
                layer.Begin(new DraftSelection("", 0, 0));
                // the session's first batch: two words, the second paced one add later
                layer.Apply(new(TranscriptUpdateKind.Live, [new(TimeSpan.Zero, WordOp.Add(0, "so", true)), new(Pace, WordOp.Add(1, "but", true))], []));
                Step(1.0 / 60);
                // the next batch arrives before "but" is due: it revises (or removes) the word the session already counts as shown
                WordOp[] next = remove ? [WordOp.Revise(0, "देव"), WordOp.Remove(1)] : [WordOp.Revise(1, "देव")];
                layer.Apply(new(TranscriptUpdateKind.Live, next.Select(op => new ScheduledOp(TimeSpan.Zero, op)).ToArray(), []));
                for (var k = 0; k < 180; k++) Step(1.0 / 60);
                var expected = remove ? new[] { "देव" } : new[] { "so", "देव" };
                Assert.Equal(expected, layer.Words.Logical.Select(word => word.Text).ToArray());
                // and nothing of the stale word is still drawn (a revised or removed word's leaving picture is gone by now)
                Assert.DoesNotContain(layer.Words.Words, word => word.Text == "but");
                Assert.DoesNotContain(layer.Words.Words.SelectMany(word => word.Old), old => old.Text == "but" && old.GoneAt > SwiftClock.Now);
            }
            finally { layer.Abandon(); window.Close(); }
        });
    }

    [Fact]
    public void Adds_keep_their_own_pace_when_nothing_is_waiting()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            var editor = new ComposerNSTextView { Width = 320, FontSize = 14 };
            var layer = new DictationLayer(editor, null) { Width = 320 };
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var window = new Avalonia.Controls.Window { Width = 320, Height = 180, Content = new Grid { Children = { editor, layer } } };
            window.Show(); window.UpdateLayout();
            void Step(double seconds) { runtime.Advance(TimeSpan.FromSeconds(seconds)); frames.Advance(TimeSpan.FromSeconds(seconds)); window.UpdateLayout(); }
            try
            {
                layer.Begin(new DraftSelection("", 0, 0));
                layer.Apply(new(TranscriptUpdateKind.Live, [new(TimeSpan.Zero, WordOp.Add(0, "one", true)), new(Pace, WordOp.Add(1, "two", true)), new(Pace * 2, WordOp.Add(2, "three", true))], []));
                Step(1.0 / 60);
                Assert.Equal(["one"], layer.Words.Logical.Select(word => word.Text).ToArray());
                Step(Pace.TotalSeconds);
                Assert.Equal(["one", "two"], layer.Words.Logical.Select(word => word.Text).ToArray());
                Step(Pace.TotalSeconds);
                Assert.Equal(["one", "two", "three"], layer.Words.Logical.Select(word => word.Text).ToArray());
            }
            finally { layer.Abandon(); window.Close(); }
        });
    }
}
