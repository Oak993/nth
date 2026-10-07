using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Voice.Cloud;

// voice-SPEC V-18.1/V-18.2: one complete recording after stop, with SSE operations in the session's Processing state.
public sealed class OpenAiCloudSessionTests
{
    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-16")]
    [Trait("Spec", "V-T113")]
    public void Without_a_key_the_cloud_note_is_raised_before_the_microphone_records()
    {
        var (keys, _) = CloudFixtures.KeyStore(key: null);
        var handler = CloudFixtures.Answering(_ => throw new InvalidOperationException("HTTP must not be reached"));
        using var rig = new CloudSessionRig(keys, handler);
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        Assert.Equal(CloudNote.KeyMissing, Assert.IsType<OpenAiTranscriptionException>(rig.Session.FailureCause).Note);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal(0, rig.Capture.StartCount);
        Assert.Empty(handler.Received);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-16")]
    [Trait("Spec", "V-T110")]
    public void Listening_sends_nothing_and_stop_sends_the_complete_recording_in_one_request()
    {
        var (keys, _) = CloudFixtures.KeyStore();
        var handler = CloudFixtures.Answering(_ => InProcessHttpHandler.Reply(200, "{\"text\":\"whole recording\"}"));
        using var rig = new CloudSessionRig(keys, handler);
        rig.Speak(40); // exceeds the local 30 s window; no cloud truncation or intermediate request is allowed
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        Assert.Empty(handler.Received);
        Assert.Empty(rig.Updates);
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        var sent = Assert.Single(handler.Received);
        var file = CloudFixtures.Multipart(sent).Single(p => p.Name == "file");
        Assert.Equal(44 + 40 * 16000 * 2, file.Data.Length);
        Assert.Equal(["whole", "recording"], rig.Session.Words.Select(w => w.Text));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-16")]
    [Trait("Spec", "V-T111")]
    public void SSE_deltas_reach_the_session_during_Processing_and_the_done_text_corrects_them()
    {
        var (keys, _) = CloudFixtures.KeyStore();
        var body = CloudFixtures.Sse(CloudFixtures.Delta("Hello wurld"), CloudFixtures.Done("Hello world"));
        using var rig = new CloudSessionRig(keys, CloudFixtures.Answering(_ => InProcessHttpHandler.Reply(200, body, "text/event-stream")));
        var states = new List<VoiceSessionState>();
        rig.Session.TranscriptChanged += _ => states.Add(rig.Session.State);
        rig.Speak(1);
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        var live = Assert.Single(rig.Updates, u => u.Kind == TranscriptUpdateKind.Live && u.Words.LastOrDefault().Text == "wurld");
        Assert.Equal(["Hello", "wurld"], live.Words.Select(w => w.Text));
        Assert.All(states, state => Assert.Equal(VoiceSessionState.Processing, state));
        Assert.Equal(["Hello", "world"], rig.Session.Words.Select(w => w.Text));
        rig.Runtime.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.Zero, rig.Session.BufferedAudio);
    }

    [Theory]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-16")]
    [Trait("Spec", "V-T112")]
    [InlineData(401, CloudNote.KeyRefused)]
    [InlineData(429, CloudNote.QuotaOrRate)]
    [InlineData(503, CloudNote.EngineFailure)]
    public void A_cloud_failure_ends_the_session_with_its_cause_and_no_second_request(int status, CloudNote note)
    {
        var (keys, _) = CloudFixtures.KeyStore();
        var handler = CloudFixtures.Answering(_ => InProcessHttpHandler.Reply(status, "refused"));
        using var rig = new CloudSessionRig(keys, handler);
        rig.Speak(1);
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal(note, Assert.IsType<OpenAiTranscriptionException>(rig.Session.FailureCause).Note);
        Assert.Single(handler.Received);
        Assert.Equal(TimeSpan.Zero, rig.Session.BufferedAudio);
    }
}
