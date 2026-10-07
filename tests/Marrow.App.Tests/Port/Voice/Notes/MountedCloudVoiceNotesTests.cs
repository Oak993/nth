using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Port;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Voice.Notes;

// Uses the production factory and real controller/session/HTTP adapter, with in-memory seams.
[Collection("Headless")]
[Trait("Decision", "ED-351")]
public sealed class MountedCloudVoiceNotesTests(HeadlessSession headless)
{
    [Theory]
    [InlineData("missing", "CloudNoKey", "No OpenAI key yet. Add one in ", "Voice typing > OpenAI key", "voice:openAiKey")]
    [InlineData("401", "CloudKeyRefused", "OpenAI refused the key. Replace it in ", "Voice typing > OpenAI key", "voice:openAiKey")]
    [InlineData("429", "CloudOutOfQuota", "This key reached its OpenAI limit. Switch to this PC in ", "Voice typing > Engine", "voice:engine")]
    [InlineData("500", "EngineFailure", "Voice typing stopped on an error. Check it in ", "Settings > Voice typing", "voice:page")]
    [InlineData("network", "CloudNoNetwork", "Can’t reach OpenAI: no internet. Switch to this PC in ", "Voice typing > Engine", "voice:engine")]
    [InlineData("timeout", "CloudNoNetwork", "Can’t reach OpenAI: no internet. Switch to this PC in ", "Voice typing > Engine", "voice:engine")]
    public void The_real_cloud_path_mounts_the_picked_error_without_local_fallback(string error, string noteCase, string before, string link, string target)
    {
        headless.OnUiThread(() =>
        {
            var productionFactory = VoiceSessionFactory.Current.Current;
            using var rig = new VoiceComposerRig(typing: VoiceTypingSettings.Default with { Engine = VoiceEngine.Cloud });
            using var factory = VoiceSessionFactory.Current.Override(productionFactory);
            using var hub = VoiceSessionHub.Shared.Override(new VoiceSessionHub());
            using var secrets = SecretStore.Shared.Override(new SecretStore(new InMemoryCredentialBackend()));
            var keys = new VoiceKeyStore();
            if (error != "missing") Assert.True(keys.Save("sk-synthetic-mounted-voice"));
            using var handler = new InProcessHttpHandler
            {
                Answer = _ => error switch
                {
                    "network" => throw new HttpRequestException("scripted offline"),
                    "timeout" => throw new OperationCanceledException("scripted HTTP timeout"),
                    _ => InProcessHttpHandler.Reply(int.Parse(error, System.Globalization.CultureInfo.InvariantCulture), "{}"),
                },
            };
            using var engine = new OpenAiTranscriber(keys, handler);
            using var cloud = VoiceCloudEngine.Current.Override(engine);
            using var local = VoiceSeams.Engine.Override(new FailClosedSpeechEngine());
            using var capture = VoiceSeams.Capture.Override(rig.Capture);
            using var vad = VoiceSeams.Vad.Override(ScriptedVad.Speech((0, 400)));
            using var microphone = VoiceSeams.Microphone.Override(rig.Microphone);
            using var resampler = VoiceSeams.Resampler.Override(new FailClosedStreamingResampler());
            rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(true, true));
            var (view, window) = rig.Open(900);
            try
            {
                rig.Main.OnMain(() => view.Voice!.Activate());
                if (error == "missing")
                {
                    rig.Elapse(window, .7);
                    Assert.Equal(0, rig.Capture.StartCount);
                    Assert.Empty(handler.Received);
                }
                else
                {
                    Assert.Equal(1, rig.Capture.StartCount);
                    rig.Main.OnMain(() => rig.Capture.PushNow(_ => .25f, TimeSpan.FromSeconds(1)));
                    rig.Main.OnMain(() => view.Voice!.Activate());
                    rig.Elapse(window, .7);
                    var request = Assert.Single(handler.Received);
                    Assert.Equal(OpenAiTranscriber.Endpoint, request.Url);
                    Assert.Equal("POST", request.Method);
                    Assert.Equal("Bearer sk-synthetic-mounted-voice", request.Header("Authorization"));
                }
                var note = LogicalQuery.SelfAndDescendants<VoiceNoteLine>(view).Single(line => line.Content.Id == "voice.error." + noteCase && line.IsVisible);
                Assert.Equal(before, note.Content.Text);
                Assert.Equal(link, note.Content.LinkText);
                Assert.Equal(target, note.Content.LinkTarget);
                Assert.Equal(VoiceNotePlace.Field, note.Place);
                Assert.NotNull(note.Link);
                Assert.Equal(VoiceSessionState.Rest, view.Voice!.State);
                Assert.Equal(VoiceEngine.Cloud, rig.App.Settings.VoiceTyping.Engine);
                Assert.Equal(before + link.Replace(" > ", ", ", StringComparison.Ordinal) + ".", LogicalQuery.SelfAndDescendants<VoiceAnnouncer>(view).Single().Text);
                rig.Elapse(window, 5.3);
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<VoiceNoteLine>(view), line => line.Content.Id == "voice.error." + noteCase);
            }
            finally { rig.Close(window); }
        });
    }
}
