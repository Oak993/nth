using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;
using Marrow.Testing;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;

namespace Marrow.App.Tests.Port.Voice.Notes;

[Collection("Headless")]
[Trait("Decision", "ED-351")]
public sealed class MountedVoiceDownloadNoteTests(HeadlessSession headless)
{
    [Fact]
    public void The_real_notes_adapter_suppresses_download_errors_only_while_the_capsule_shows_the_transfer()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Marrow.App.Tests.Views.Composer.ComposerVoicePillGeometryTests.Quiet();
            var models = new HeldVoicePreparationModels();
            var clock = new ManualTimeProvider();
            var preparation = new VoicePreparation(new ScriptedProbe(ScriptedMachines.OwnersDesktop), models,
                () => new VoicePrepEngine(clock, VoiceQuestionRig.PcSeconds), new AppSettingsVoiceAccess(rig.App.Settings),
                () => new float[160320], VoicePrepRig.App, VoicePrepRig.Runtime, clock);
            var question = new VoiceSpeedQuestion(preparation);
            question.No(); question.CloseWelcome();
            VoiceFirstRun.Bind(rig.App.Settings, new VoiceFirstRun(preparation, question, models));
            var backend = new ScriptedSpeechModelBackend();
            using var store = SpeechModelSeams.Backend.Override(backend);
            var file = ModelCatalog.Find("large-v3-turbo-q8_0")!;
            rig.Main.OnMain(() => { rig.App.Settings.VoiceTyping = rig.App.Settings.VoiceTyping with { Model = file.Id }; SpeechModelController.For(rig.App.Settings).Download(file); });
            rig.Engine.FailLoads(new InvalidOperationException("scripted model load failure"));
            var (view, window) = rig.Open(1000);
            try
            {
                rig.Main.OnMain(() => view.Voice!.Activate());
                rig.Main.Loop.RunUntil(() => view.Voice!.PreparationProgress?.Id == VoicePreparation.DownloadId);
                rig.PumpWindow(window); rig.Elapse(window, .8);
                Assert.True(Pill(view).ShowsDownload);
                // A late load error arrives at the real session, while the actual preparation capsule still owns that information.
                rig.Main.OnMain(() => view.Voice!.Session.Activate());
                rig.PumpWindow(window); rig.Elapse(window, .7);
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<VoiceNoteLine>(view), line => line.Content.Id == "voice.error.ModelStillDownloading");
                rig.Main.OnMain(() => view.Voice!.Cancel());
                rig.Main.Loop.RunUntil(() => models.CancellationObserved);
                rig.PumpWindow(window); rig.Elapse(window, .8);
                Assert.False(Pill(view).ShowsDownload);
                rig.Main.OnMain(() => view.Voice!.Session.Activate());
                rig.PumpWindow(window); rig.Elapse(window, .7);
                var note = LogicalQuery.SelfAndDescendants<VoiceNoteLine>(view).Single(line => line.Content.Id == "voice.error.ModelStillDownloading" && line.IsVisible);
                Assert.Equal("voice:speechModel", note.Content.LinkTarget);
                Assert.Equal(TimeSpan.FromSeconds(5.2), note.Content.Length);
                Assert.NotNull(note.Link);
            }
            finally { rig.Main.OnMain(() => SpeechModelController.For(rig.App.Settings).Cancel(file)); rig.Close(window); }
        });
    }
}
