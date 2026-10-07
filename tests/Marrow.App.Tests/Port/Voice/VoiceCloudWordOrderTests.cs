using Marrow.App.Port.Voice;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.Services.Port;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Voice;

// V-T111: replay the session's actual schedule in DictationWords, in the due-time order used by DictationLayer.Queue.
public sealed class VoiceCloudWordOrderTests
{
    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-16")]
    [Trait("Spec", "V-T111")]
    public void A_done_correction_cannot_run_before_the_word_it_corrects_has_arrived()
    {
        using var runtime = new TestRuntime();
        var keys = new VoiceKeyStore(new SecretStore(new InMemoryCredentialBackend()));
        Assert.True(keys.Save("sk-synthetic-word-order"));
        var handler = new InProcessHttpHandler
        {
            Answer = _ => InProcessHttpHandler.Reply(200,
                "data: {\"type\":\"transcript.text.delta\",\"delta\":\"Hello wurld\"}\n\n" +
                "data: {\"type\":\"transcript.text.done\",\"text\":\"Hello world\"}\n\n", "text/event-stream"),
        };
        using var engine = new OpenAiTranscriber(keys, handler);
        var capture = new ScriptedCapture();
        var session = new VoiceSession(() => new(VoiceOrbSettings.Default, VoiceTypingSettings.Default with { Engine = VoiceEngine.Cloud }, "en-US", new("gpt-transcribe", null)),
            _ => { }, VoiceSeamSet.Of(capture, ScriptedVad.Speech((0, 300)), engine, new ScriptedMicrophone()), new VoiceSessionHub());
        var scheduled = new List<(ContinuousInstant Due, WordOp Op)>();
        session.TranscriptChanged += update => scheduled.AddRange(update.Ops.Select(op => (SwiftClock.Now + op.At, op.Op)));
        session.Activate();
        runtime.RunUntilIdle();
        capture.PushNow(_ => .25f, TimeSpan.FromSeconds(1));
        session.Activate();
        runtime.RunUntilIdle();
        runtime.Advance(TimeSpan.FromSeconds(2));
        var words = new DictationWords();
        foreach (var op in scheduled.OrderBy(op => op.Due)) words.Apply(op.Op, op.Due);
        Assert.Equal(["Hello", "world"], words.Logical.Select(word => word.Text));
        Assert.All(words.Logical, word => Assert.False(word.Tentative));
    }
}
