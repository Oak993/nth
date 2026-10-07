using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Voice.Cloud;

// V-T111 and V-T113: controlled streaming lifetime and the protected key after a cached model load. No socket or wall-clock sequencing.
public sealed class OpenAiCloudLifetimeTests
{
    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-16")]
    [Trait("Spec", "V-T113")]
    public void Removing_the_key_after_a_successful_session_prevents_the_next_capture_even_with_a_cached_load()
    {
        var (keys, _) = CloudFixtures.KeyStore();
        var handler = CloudFixtures.Answering(_ => InProcessHttpHandler.Reply(200, "{\"text\":\"first\"}"));
        using var rig = new CloudSessionRig(keys, handler);
        rig.Speak(1);
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        rig.Runtime.Advance(TimeSpan.FromSeconds(2));
        Assert.True(keys.Remove());
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        Assert.Equal(CloudNote.KeyMissing, Assert.IsType<OpenAiTranscriptionException>(rig.Session.FailureCause).Note);
        Assert.Equal(1, rig.Capture.StartCount);
        Assert.Single(handler.Received);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-16")]
    [Trait("Spec", "V-T111")]
    public void A_delta_is_visible_before_done_and_cancelling_the_read_cannot_change_the_next_session()
    {
        var (keys, _) = CloudFixtures.KeyStore();
        using var stream = new CloudReplyStream();
        var handler = CloudFixtures.Answering(_ =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentType = new("text/event-stream");
            return response;
        });
        using var rig = new CloudSessionRig(keys, handler);
        rig.Speak(1);
        rig.Session.Activate();
        rig.Runtime.Loop.RunUntil(() => stream.Waiting.Task.IsCompleted);
        stream.Send(CloudFixtures.Sse(CloudFixtures.Delta("old words")));
        rig.Runtime.Loop.RunUntil(() => rig.Session.Words.Count == 2);
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State);
        Assert.Equal(["old", "words"], rig.Session.Words.Select(word => word.Text));
        rig.Session.Cancel();
        rig.Runtime.Loop.RunUntil(() => stream.Cancelled.Task.IsCompleted);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal(TimeSpan.Zero, rig.Session.BufferedAudio);
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        stream.Send(CloudFixtures.Sse(CloudFixtures.Done("late obsolete words")));
        stream.Complete();
        rig.Runtime.RunUntilIdle();
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        Assert.Empty(rig.Session.Words);
        Assert.Single(handler.Received);
    }
}
