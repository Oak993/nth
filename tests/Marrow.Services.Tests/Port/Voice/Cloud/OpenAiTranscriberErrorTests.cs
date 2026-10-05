using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Voice.Cloud;

// VT-16, voice-SPEC V-18.4, V-17.6 and V-T112, V-T113: 401, 429, 5xx and a timeout map to the cloud notes and the call ends there: no
// retry, no fallback to the local engine; no key gives the cloud note before anything is sent; the key comes from the secret store only.
public sealed class OpenAiTranscriberErrorTests
{
    private static async Task<(OpenAiTranscriptionException Error, InProcessHttpHandler Handler)> Fail(int status, string body = "{\"error\":{\"message\":\"Incorrect API key provided: sk-test-01********cdef\"}}")
    {
        var (keys, _) = CloudFixtures.KeyStore();
        var handler = CloudFixtures.Answering(_ => InProcessHttpHandler.Reply(status, body));
        using var engine = new OpenAiTranscriber(keys, handler);
        var error = await Assert.ThrowsAsync<OpenAiTranscriptionException>(() => engine.TranscribeAsync(CloudFixtures.Audio(1), CloudFixtures.Request(), CancellationToken.None));
        return (error, handler);
    }

    [Theory]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData(401, CloudNote.KeyRefused)]
    [InlineData(429, CloudNote.QuotaOrRate)]
    [InlineData(500, CloudNote.EngineFailure)]
    [InlineData(502, CloudNote.EngineFailure)]
    [InlineData(503, CloudNote.EngineFailure)]
    [InlineData(400, CloudNote.EngineFailure)]
    [InlineData(413, CloudNote.EngineFailure)]
    [Trait("Spec", "V-T112")]
    public async Task V_T112_a_status_maps_to_its_cloud_note_and_is_asked_only_once(int status, CloudNote note)
    {
        var (error, handler) = await Fail(status);
        Assert.Equal(note, error.Note);
        Assert.Equal(status, error.StatusCode);
        Assert.False(error.TimedOut);
        Assert.Single(handler.Received);                                  // no retry, no second engine
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Spec", "V-T112")]
    public async Task V_T112_what_openai_wrote_back_and_the_key_are_not_in_the_exception()
    {
        var (error, _) = await Fail(401);
        var text = error.ToString();
        Assert.DoesNotContain("sk-test", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Incorrect API key", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Spec", "V-T112")]
    public async Task V_T112_a_server_that_does_not_answer_in_time_is_a_network_failure_that_timed_out()
    {
        await using var server = new LoopbackHttpServer();
        var (keys, _) = CloudFixtures.KeyStore();
        using var engine = new OpenAiTranscriber(keys, new HttpClientHandler(), timeout: TimeSpan.FromMilliseconds(300), endpoint: server.Url("/v1/audio/transcriptions"));
        var call = engine.TranscribeAsync(CloudFixtures.Audio(1), CloudFixtures.Request(), CancellationToken.None);
        var exchange = await server.NextExchange();                       // it arrived; the server says nothing
        var error = await Assert.ThrowsAsync<OpenAiTranscriptionException>(() => call.WaitAsync(LoopbackHttpServer.Guard));
        Assert.Equal(CloudNote.NetworkFailure, error.Note);
        Assert.True(error.TimedOut);
        Assert.Null(error.StatusCode);
        exchange.Abort();
    }

    // Windows review: HttpClient.Timeout stops at the reply's headers (the reply is read as it streams), so a server that sent its headers and a first event and then went quiet
    // held the call, and the orb in Processing, for ever. A quiet stream of the same 60 s ends as the same timed-out network failure.
    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Spec", "V-T112")]
    public async Task V_T112_a_stream_that_goes_quiet_after_its_first_event_is_a_network_failure_that_timed_out()
    {
        await using var server = new LoopbackHttpServer();
        var (keys, _) = CloudFixtures.KeyStore();
        using var engine = new OpenAiTranscriber(keys, new HttpClientHandler(), timeout: TimeSpan.FromMilliseconds(500), endpoint: server.Url("/v1/audio/transcriptions"));
        var call = engine.TranscribeAsync(CloudFixtures.Audio(1), CloudFixtures.Request(), CancellationToken.None);
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        await exchange.WriteChunk(System.Text.Encoding.UTF8.GetBytes(CloudFixtures.Sse(CloudFixtures.Delta("Hi "))));
        var error = await Assert.ThrowsAsync<OpenAiTranscriptionException>(() => call.WaitAsync(LoopbackHttpServer.Guard));
        Assert.Equal(CloudNote.NetworkFailure, error.Note);
        Assert.True(error.TimedOut);
        exchange.Abort();
    }

    // The time is for silence, not for the whole reply: a stream that keeps sending events lasts as long as it likes.
    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Spec", "V-T112")]
    public async Task V_T112_a_stream_that_keeps_sending_events_is_not_cut_off_by_the_time_of_its_silence()
    {
        await using var server = new LoopbackHttpServer();
        var (keys, _) = CloudFixtures.KeyStore();
        using var engine = new OpenAiTranscriber(keys, new HttpClientHandler(), timeout: TimeSpan.FromMilliseconds(700), endpoint: server.Url("/v1/audio/transcriptions"));
        var call = engine.TranscribeAsync(CloudFixtures.Audio(1), CloudFixtures.Request(), CancellationToken.None);
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        foreach (var word in new[] { "one ", "two ", "three ", "four ", "five " })
        {
            await exchange.WriteChunk(System.Text.Encoding.UTF8.GetBytes(CloudFixtures.Sse(CloudFixtures.Delta(word))));
            await Task.Delay(300);                                        // five events over 1.5 s, every gap under the 700 ms
        }
        await exchange.WriteChunk(System.Text.Encoding.UTF8.GetBytes(CloudFixtures.Sse(CloudFixtures.Done("one two three four five"))));
        await exchange.EndChunks();
        var result = await call.WaitAsync(LoopbackHttpServer.Guard);
        Assert.Equal(["one", "two", "three", "four", "five"], result.Words.Select(w => w.Text));
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Spec", "V-T112")]
    public async Task V_T112_a_refused_connection_is_a_network_failure()
    {
        var (keys, _) = CloudFixtures.KeyStore();
        using var engine = new OpenAiTranscriber(keys, new HttpClientHandler(), endpoint: LoopbackHttpServer.RefusedUrl("/v1/audio/transcriptions"));
        var error = await Assert.ThrowsAsync<OpenAiTranscriptionException>(() => engine.TranscribeAsync(CloudFixtures.Audio(1), CloudFixtures.Request(), CancellationToken.None));
        Assert.Equal(CloudNote.NetworkFailure, error.Note);
        Assert.False(error.TimedOut);
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Spec", "V-T112")]
    public async Task V_T112_a_reply_cut_off_in_the_middle_is_a_network_failure()
    {
        await using var server = new LoopbackHttpServer();
        var (keys, _) = CloudFixtures.KeyStore();
        using var engine = new OpenAiTranscriber(keys, new HttpClientHandler(), endpoint: server.Url("/v1/audio/transcriptions"));
        var call = engine.TranscribeAsync(CloudFixtures.Audio(1), CloudFixtures.Request(), CancellationToken.None);
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        await exchange.WriteChunk(System.Text.Encoding.UTF8.GetBytes(CloudFixtures.Sse(CloudFixtures.Delta("Hi "))));
        exchange.Abort();
        var error = await Assert.ThrowsAsync<OpenAiTranscriptionException>(() => call.WaitAsync(LoopbackHttpServer.Guard));
        Assert.Equal(CloudNote.NetworkFailure, error.Note);
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Spec", "V-T112")]
    public async Task V_T112_the_users_own_cancel_is_a_cancel_and_not_a_cloud_note()
    {
        await using var server = new LoopbackHttpServer();
        var (keys, _) = CloudFixtures.KeyStore();
        using var engine = new OpenAiTranscriber(keys, new HttpClientHandler(), endpoint: server.Url("/v1/audio/transcriptions"));
        using var cts = new CancellationTokenSource();
        var call = engine.TranscribeAsync(CloudFixtures.Audio(1), CloudFixtures.Request(), cts.Token);
        var exchange = await server.NextExchange();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(LoopbackHttpServer.Guard));
        exchange.Abort();
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Spec", "V-T113")]
    public async Task V_T113_without_a_key_loading_and_transcribing_give_the_cloud_note_and_send_nothing()
    {
        var (keys, _) = CloudFixtures.KeyStore(key: null);
        var handler = CloudFixtures.Answering(_ => InProcessHttpHandler.Reply(200, "{\"text\":\"x\"}"));
        using var engine = new OpenAiTranscriber(keys, handler);
        var load = await Assert.ThrowsAsync<OpenAiTranscriptionException>(() => engine.LoadAsync(new SpeechModel("gpt-transcribe", null), new EngineLoadOptions(), CancellationToken.None));
        Assert.Equal(CloudNote.KeyMissing, load.Note);
        var call = await Assert.ThrowsAsync<OpenAiTranscriptionException>(() => engine.TranscribeAsync(CloudFixtures.Audio(1), CloudFixtures.Request(), CancellationToken.None));
        Assert.Equal(CloudNote.KeyMissing, call.Note);
        Assert.Null(call.StatusCode);
        Assert.Empty(handler.Received);
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Spec", "V-T113")]
    public async Task V_T113_the_key_is_read_from_the_secret_store_each_call_and_from_nowhere_else()
    {
        var (keys, _) = CloudFixtures.KeyStore("sk-first");
        var handler = CloudFixtures.Answering(_ => InProcessHttpHandler.Reply(200, "{\"text\":\"x\"}"));
        using var engine = new OpenAiTranscriber(keys, handler);
        var variable = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-from-the-environment");
        try
        {
            await engine.TranscribeAsync(CloudFixtures.Audio(1), CloudFixtures.Request(), CancellationToken.None);
            keys.Save("sk-second");
            await engine.TranscribeAsync(CloudFixtures.Audio(1), CloudFixtures.Request(), CancellationToken.None);
            keys.Remove();
            var error = await Assert.ThrowsAsync<OpenAiTranscriptionException>(() => engine.TranscribeAsync(CloudFixtures.Audio(1), CloudFixtures.Request(), CancellationToken.None));
            Assert.Equal(CloudNote.KeyMissing, error.Note);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", variable);
        }
        Assert.Equal(["Bearer sk-first", "Bearer sk-second"], handler.Received.Select(r => r.Header("Authorization")));
    }
}
