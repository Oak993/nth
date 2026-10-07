using Marrow.Core.Port.Voice.Transcript;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Voice.Cloud;

// VT-16, voice-SPEC V-18.1 and V-T111: the streamed text arrives as transcript.text.delta events that become word operations while the
// call runs (Processing); transcript.text.done ends it; a JSON reply ({"text": ...}) takes the one-shot path.
[Trait("Spec", "V-T111")]
public sealed class OpenAiTranscriberReplyTests
{
    private static async Task<(Core.Port.Voice.Seams.Hypothesis Result, List<IReadOnlyList<WordOp>> Batches)> Run(HttpResponseMessage reply, double seconds = 2)
    {
        var (keys, _) = CloudFixtures.KeyStore();
        using var engine = new OpenAiTranscriber(keys, CloudFixtures.Answering(_ => reply));
        var batches = new List<IReadOnlyList<WordOp>>();
        engine.OpsProduced += batches.Add;
        var result = await engine.TranscribeAsync(CloudFixtures.Audio(seconds), CloudFixtures.Request(), CancellationToken.None);
        return (result, batches);
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T111_deltas_become_add_revise_and_confirm_operations_in_order()
    {
        var body = CloudFixtures.Sse(CloudFixtures.Delta("Hel"), CloudFixtures.Delta("lo wor"), CloudFixtures.Delta("ld."), CloudFixtures.Done("Hello world."));
        var (result, batches) = await Run(InProcessHttpHandler.Reply(200, body, "text/event-stream"));
        Assert.Equal(
            [
                [WordOp.Add(0, "Hel", true)],                                   // the unfinished word is tentative
                [WordOp.Revise(0, "Hello"), WordOp.Confirm(0), WordOp.Add(1, "wor", true)],
                [WordOp.Revise(1, "world.")],
                [WordOp.Confirm(1)],                                            // the done event makes the last word solid
            ],
            batches.Select(b => b.ToArray()));
        Assert.Equal(["Hello", "world."], result.Words.Select(w => w.Text));
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T111_the_done_text_is_the_result_even_when_it_differs_from_the_deltas()
    {
        var body = CloudFixtures.Sse(CloudFixtures.Delta("Hello wurld"), CloudFixtures.Done("Hello world"));
        var (result, batches) = await Run(InProcessHttpHandler.Reply(200, body, "text/event-stream"));
        Assert.Equal(["Hello", "world"], result.Words.Select(w => w.Text));
        Assert.Equal([WordOp.Revise(1, "world"), WordOp.Confirm(1)], batches[^1].ToArray());
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T111_a_stream_that_ends_without_done_still_finishes_its_words()
    {
        var body = CloudFixtures.Sse(CloudFixtures.Delta("Good "), CloudFixtures.Delta("morn"));
        var (result, batches) = await Run(InProcessHttpHandler.Reply(200, body, "text/event-stream"));
        Assert.Equal(["Good", "morn"], result.Words.Select(w => w.Text));
        Assert.Equal([WordOp.Confirm(1)], batches[^1].ToArray());
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T111_the_json_reply_gives_the_text_as_one_batch_of_solid_words()
    {
        var (result, batches) = await Run(InProcessHttpHandler.Reply(200, "{\"text\":\"Hello brave new world\"}"));
        Assert.Equal(["Hello", "brave", "new", "world"], result.Words.Select(w => w.Text));
        var one = Assert.Single(batches);
        Assert.Equal(["Hello", "brave", "new", "world"], one.Select(o => o.Text));
        Assert.All(one, o => { Assert.Equal(WordOpKind.Add, o.Kind); Assert.False(o.Tentative); });
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T111_the_words_are_spread_over_the_audio_so_the_final_pass_keeps_them()
    {
        var (result, _) = await Run(InProcessHttpHandler.Reply(200, "{\"text\":\"one two three four\"}"), seconds: 2);
        Assert.Equal([0.0, 0.5, 1.0, 1.5], result.Words.Select(w => w.Start));
        Assert.Equal([0.5, 1.0, 1.5, 2.0], result.Words.Select(w => w.End));
        Assert.Null(result.Language);
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T111_an_empty_text_is_an_empty_hypothesis_and_no_operations()
    {
        var (result, batches) = await Run(InProcessHttpHandler.Reply(200, "{\"text\":\"\"}"));
        Assert.Empty(result.Words);
        Assert.Empty(batches);
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T111_a_reply_that_is_not_the_expected_json_is_an_engine_failure()
    {
        foreach (var body in new[] { "not json", "{\"nope\":1}", "[1]", "{\"text\":5}" })
        {
            var error = await Assert.ThrowsAsync<OpenAiTranscriptionException>(() => Run(InProcessHttpHandler.Reply(200, body)));
            Assert.Equal(CloudNote.EngineFailure, error.Note);
        }
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T111_operations_arrive_while_the_reply_is_still_coming()
    {
        // the server sends the first delta and holds the rest: the operation for it must be raised before the call returns
        await using var server = new LoopbackHttpServer();
        var (keys, _) = CloudFixtures.KeyStore();
        using var engine = new OpenAiTranscriber(keys, new HttpClientHandler(), endpoint: server.Url("/v1/audio/transcriptions"));
        var first = new TaskCompletionSource<IReadOnlyList<WordOp>>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.OpsProduced += batch => first.TrySetResult(batch);
        var call = engine.TranscribeAsync(CloudFixtures.Audio(1), CloudFixtures.Request(), CancellationToken.None);
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        await exchange.WriteChunk(System.Text.Encoding.UTF8.GetBytes(CloudFixtures.Sse(CloudFixtures.Delta("Hi "))));
        var batch = await first.Task.WaitAsync(LoopbackHttpServer.Guard);
        Assert.False(call.IsCompleted);                                   // the call is still waiting for the rest
        Assert.Equal([WordOp.Add(0, "Hi", false)], batch.ToArray());
        await exchange.WriteChunk(System.Text.Encoding.UTF8.GetBytes(CloudFixtures.Sse(CloudFixtures.Done("Hi there"))));
        await exchange.EndChunks();
        var result = await call.WaitAsync(LoopbackHttpServer.Guard);
        Assert.Equal(["Hi", "there"], result.Words.Select(w => w.Text));
        exchange.Finish();
    }
}
