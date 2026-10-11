using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Port;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Port.Voice.Notes;

[Collection("Headless")]
[Trait("Decision", "ED-351")]
public sealed class MountedVoiceKeyRetryTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Fact]
    public void The_missing_key_link_opens_the_actual_key_editor_and_its_saved_key_allows_a_cloud_retry() => WithManager((_, manager) =>
    {
        var productionFactory = VoiceSessionFactory.Current.Current;
        using var rig = new VoiceComposerRig(typing: VoiceTypingSettings.Default with { Engine = VoiceEngine.Cloud });
        using var factory = VoiceSessionFactory.Current.Override(productionFactory);
        using var hub = VoiceSessionHub.Shared.Override(new VoiceSessionHub());
        using var secrets = SecretStore.Shared.Override(new SecretStore(new InMemoryCredentialBackend()));
        var keys = new VoiceKeyStore();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, "{\"text\":\"cloud retry\"}") };
        using var engine = new OpenAiTranscriber(keys, handler);
        using var cloud = VoiceCloudEngine.Current.Override(engine);
        using var local = VoiceSeams.Engine.Override(new FailClosedSpeechEngine());
        using var capture = VoiceSeams.Capture.Override(rig.Capture);
        using var vad = VoiceSeams.Vad.Override(ScriptedVad.Speech((0, 400)));
        using var microphone = VoiceSeams.Microphone.Override(rig.Microphone);
        using var resampler = VoiceSeams.Resampler.Override(new FailClosedStreamingResampler());
        rig.Main.OnMain(() => { manager.Model.Settings.VoiceTyping = manager.Model.Settings.VoiceTyping with { Engine = VoiceEngine.Cloud }; rig.App.Settings.VoiceNotes = new VoiceNotesSettings(true, true); });
        var (view, window) = rig.Open(1000);
        try
        {
            rig.Main.OnMain(() => view.Voice!.Activate());
            rig.PumpWindow(window); rig.Elapse(window, .7);
            var note = LogicalQuery.SelfAndDescendants<VoiceNoteLine>(view).Single(line => line.Content.Id == "voice.error.CloudNoKey" && line.IsVisible);
            Assert.Empty(handler.Received); Assert.Equal(0, rig.Capture.StartCount);
            rig.Main.OnMain(() => Marrow.App.Tests.Views.Composer.ComposerViewTests.Click(note.Link!));
            var settings = Settings(manager);
            // OD-0140: Settings is a panel in the main window, laid out in the window's next layout pass rather than as a window is shown;
            // that pass builds the page, on the main actor as in the app.
            rig.Main.OnMain(() => Drain(settings));
            var page = LogicalQuery.SelfAndDescendants<VoiceTypingSettingsPage>(settings).Single();
            Assert.Same(page.KeyButton, settings.FocusManager!.GetFocusedElement());
            rig.Main.OnMain(() => page.KeyButton.Activate());
            rig.Main.OnMain(() => Drain(settings));
            var field = LogicalQuery.SelfAndDescendants<TextBox>(page.KeyFold).Single();
            Assert.Equal('\u2022', field.PasswordChar); Assert.Equal(200, field.Width);
            rig.Main.OnMain(() => field.Text = "sk-synthetic-editor-retry");
            rig.Main.OnMain(() => Drain(settings));
            Assert.Equal("sk-synthetic-editor-retry", keys.Read());
            rig.Main.OnMain(() => view.Voice!.Activate());
            rig.PumpWindow(window);
            Assert.Equal(1, rig.Capture.StartCount);
            rig.Elapse(window, .4); // OD-0076 retains the outgoing note through its .3-second fade.
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<VoiceNoteLine>(view), line => line.Content.Id == "voice.error.CloudNoKey" && line.IsVisible);
            rig.Main.OnMain(() => { rig.Capture.PushNow(_ => .25f, TimeSpan.FromSeconds(1)); view.Voice!.Activate(); });
            rig.Main.Loop.RunUntil(() => view.Voice!.Session.Words.Count == 2);
            rig.Elapse(window, 1.5);
            Assert.Equal("cloud retry", rig.Runtime.Draft.Text);
            var request = Assert.Single(handler.Received);
            Assert.Equal("Bearer sk-synthetic-editor-retry", request.Header("Authorization"));
            Assert.Equal(OpenAiTranscriber.Endpoint, request.Url);
            Assert.Equal(VoiceEngine.Cloud, rig.App.Settings.VoiceTyping.Engine);
        }
        finally { rig.Close(window); }
    });
}
