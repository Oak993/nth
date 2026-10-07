using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.Testing;
using Marrow.UI.Port;
namespace Marrow.App.Tests.Port.Voice;

[Collection("Headless")]
[Trait("Decision", "ED-292")]
public sealed class DictationAddConfirmationTests(HeadlessSession headless)
{
    [Fact]
    public void Add_visually_confirms_its_predecessor_without_committing_the_transcribers_tentative_tail()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            var editor = new ComposerNSTextView { Width = 200, FontSize = 14 };
            var layer = new DictationLayer(editor, null) { Width = 200 };
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var window = new Avalonia.Controls.Window { Width = 200, Height = 180, Content = new Grid { Children = { editor, layer } } };
            window.Show(); window.UpdateLayout();
            layer.Begin(new DraftSelection("", 0, 0));
            layer.Apply(new(TranscriptUpdateKind.Live, [new(TimeSpan.Zero, WordOp.Add(0, "first", true)), new(TimeSpan.FromSeconds(.1), WordOp.Add(1, "second", true))], []));
            runtime.Advance(TimeSpan.FromSeconds(1));
            frames.Advance(TimeSpan.FromSeconds(1));
            window.UpdateLayout();
            Assert.True(layer.Words.Words[0].Tentative);
            Assert.Equal(0, layer.Words.Look(layer.Words.Words[0], SwiftClock.Now).Tentative);
            layer.Abandon(); window.Close();
        });
    }
}
