using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// VT-16: manual capture, VAD and clock; the real cloud adapter over an HTTP handler fake and an in-memory secret store.
internal sealed class CloudSessionRig : IDisposable
{
    public TestRuntime Runtime { get; } = new();
    public ScriptedCapture Capture { get; } = new();
    public VoiceSession Session { get; }
    public OpenAiTranscriber Engine { get; }
    public VoiceTypingSettings Typing { get; set; } = VoiceTypingSettings.Default with { Engine = VoiceEngine.Cloud };
    public List<TranscriptUpdate> Updates { get; } = [];

    public CloudSessionRig(VoiceKeyStore keys, HttpMessageHandler handler)
    {
        Engine = new OpenAiTranscriber(keys, handler);
        var seams = VoiceSeamSet.Of(Capture, ScriptedVad.Speech((0, 300)), Engine, new ScriptedMicrophone());
        Session = new VoiceSession(() => new(VoiceOrbSettings.Default, Typing, "en-US", new SpeechModel("gpt-transcribe", null)),
            value => Typing = value, seams, new VoiceSessionHub());
        Session.TranscriptChanged += Updates.Add;
    }

    public void Speak(int seconds)
    {
        Session.Activate();
        Runtime.RunUntilIdle();
        // Nonzero samples avoid the blocked-microphone detector; the session splits the block into VAD frames.
        Capture.PushNow(_ => .25f, TimeSpan.FromSeconds(seconds));
        Runtime.RunUntilIdle();
    }

    public void Dispose()
    {
        Session.Cancel();
        Runtime.RunUntilIdle();
        Engine.Dispose();
        Runtime.Dispose();
    }
}
