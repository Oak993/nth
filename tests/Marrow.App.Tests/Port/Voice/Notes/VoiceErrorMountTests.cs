using Avalonia.Threading;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
namespace Marrow.App.Tests.Port.Voice.Notes;

[Collection("Headless")]
[Trait("Decision", "ED-351")]
public sealed class VoiceErrorMountTests(HeadlessSession headless)
{
    [Fact]
    public void The_mounted_error_survives_generic_draft_updates_moves_to_the_notice_and_expires()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            using var rig = new VoiceComposerRig();
            rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(true, true));
            rig.Microphone.Current = MicrophoneAccessStatus.NoDevice;
            var (view, window) = rig.Open();
            try
            {
                rig.Main.OnMain(() => view.Voice!.Session.Activate());
                rig.Elapse(window, .7);
                var line = LogicalQuery.SelfAndDescendants<VoiceNoteLine>(view).Single(l => l.Content.Id == "voice.error.NoMicrophone" && l.IsVisible);
                Assert.Equal("No microphone found. Set one up in ", line.Content.Text);
                Assert.Equal(VoiceNotePlace.Field, line.Place);
                var content = line.Content;
                rig.Main.OnMain(() => rig.Runtime.Draft = rig.Runtime.Draft with { Text = "draft text" });
                rig.Elapse(window, .7);
                Dispatcher.UIThread.RunJobs();
                var notice = LogicalQuery.SelfAndDescendants<VoiceNoteLine>(view).Single(l => l.Content.Id == content.Id && l.IsVisible);
                Assert.Equal(VoiceNotePlace.Notice, notice.Place);
                Assert.Same(content, notice.Content);
                Assert.Equal("draft text", rig.Main.OnMain(() => rig.Runtime.Draft.Text));
                rig.Elapse(window, 6);
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<VoiceNoteLine>(view), l => l.Content.Id == content.Id);
            }
            finally { rig.Close(window); }
        });
    }
}
