using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port.Voice;

// VT-3 (voice-SPEC V-7.3, V-7.4, V-7.6, V-7.8, V-7.10). Expected values are worked out from the clauses on a session timeline in seconds: the
// uncommitted buffer runs from the end of the last committed word minus 0.1 s to now; a pass needs 0.5 s of new audio since the last pass began,
// the previous pass done and speech in the buffer; the longest common prefix of two hypotheses is committed; 1.5 s out of speech after speech
// commits everything and drops the silence; over 25 s uncommitted, tentative words ending more than 10 s back are committed; a decode never
// exceeds 30 s. Hypothesis words are seconds from the first sample handed to the engine (V-7.2). ED-NEW-f-vt-3 (6) lists the choices the
// clauses leave open: exact text comparison, words ending at or before the last committed word are dropped, and the 30 s window.
public sealed class StreamingTranscriberTests
{
    private static HypothesisWord W(string text, double start, double end) => new(text, start, end);

    private static Hypothesis H(params HypothesisWord[] words) => new(words);

    private static (StreamingTranscriber Transcriber, ScriptedEngine Engine) Make(string language, params Hypothesis[] answers)
    {
        var queue = new Queue<Hypothesis>(answers);
        var engine = new ScriptedEngine(_ => queue.Count > 0 ? queue.Dequeue() : Hypothesis.Empty);
        return (new StreamingTranscriber(engine, language), engine);
    }

    private static void Feed(StreamingTranscriber transcriber, double seconds, bool speech) =>
        transcriber.Append(new float[(int)Math.Round(seconds * 16000)], speech);

    private static Task<TranscriptDelta> Live(StreamingTranscriber transcriber) => transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);

    private static string Texts(IEnumerable<TranscriptWord> words) => string.Join(" ", words.Select(w => w.Text));

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T22")]
    [Trait("Decision", "ED-267")] // V-7.3: the first pass has nothing to agree with, so everything is tentative; the engine gets the forced language
    public async Task The_first_pass_shows_every_word_tentative_and_commits_nothing()
    {
        var (transcriber, engine) = Make("en", H(W("hello", 0.0, 0.4), W("wor", 0.5, 0.6)));
        Feed(transcriber, 0.6, true);
        Assert.Equal(TranscriberPass.Live, transcriber.DuePass);
        var delta = await Live(transcriber);
        Assert.Equal([WordOp.Add(0, "hello", true), WordOp.Add(1, "wor", true)], delta.Ops);
        Assert.Equal(0, delta.Committed);
        Assert.All(delta.Words, w => Assert.True(w.Tentative));
        var decode = Assert.Single(engine.Decodes);
        Assert.Equal(new DecodeRequest("en", "", DecodeMode.Live, TimeSpan.Zero), decode.Request);
        Assert.Equal(TimeSpan.FromSeconds(0.6), decode.AudioLength);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T22")]
    [Trait("Decision", "ED-267")] // V-7.3 step 3: the longest common prefix of the new and the previous hypothesis is committed; the rest is the tentative tail
    public async Task Only_the_common_prefix_is_committed_and_a_changed_tail_word_is_revised()
    {
        var (transcriber, _) = Make("en",
            H(W("hello", 0.0, 0.4), W("wor", 0.5, 0.6)),
            H(W("hello", 0.0, 0.4), W("world", 0.5, 0.9), W("how", 1.0, 1.2)));
        Feed(transcriber, 0.6, true);
        await Live(transcriber);
        Feed(transcriber, 0.6, true);
        var delta = await Live(transcriber);
        Assert.Equal([WordOp.Confirm(0), WordOp.Revise(1, "world"), WordOp.Add(2, "how", true)], delta.Ops);
        Assert.Equal(1, delta.Committed);
        Assert.Equal([new TranscriptWord("hello", false), new TranscriptWord("world", true), new TranscriptWord("how", true)], delta.Words);
        Assert.Equal(1, transcriber.CommittedCount);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T22")]
    [Trait("Decision", "ED-267")] // V-7.3 step 3: agreement stops at the first difference, even if later words match again
    public async Task Agreement_stops_at_the_first_difference()
    {
        var (transcriber, _) = Make("en",
            H(W("a", 0.0, 0.1), W("b", 0.2, 0.3), W("c", 0.4, 0.5), W("d", 0.6, 0.7)),
            H(W("a", 0.0, 0.1), W("b", 0.2, 0.3), W("X", 0.4, 0.5), W("d", 0.6, 0.7)));
        Feed(transcriber, 0.8, true);
        await Live(transcriber);
        Feed(transcriber, 0.6, true);
        var delta = await Live(transcriber);
        Assert.Equal(2, delta.Committed);
        Assert.Equal("a b X d", Texts(delta.Words));
        Assert.Equal([false, false, true, true], delta.Words.Select(w => w.Tentative));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T22")]
    [Trait("Decision", "ED-267")] // V-7.3 steps 1 and 4: the next pass starts 0.1 s before the last committed word's end; committed words never change
    public async Task Committed_words_never_change_and_the_window_slides_to_the_last_committed_word()
    {
        var (transcriber, engine) = Make("en",
            H(W("hello", 0.0, 0.4), W("wor", 0.5, 0.6)),
            H(W("hello", 0.0, 0.4), W("world", 0.5, 0.9), W("how", 1.0, 1.2)),
            // The third pass starts at 0.4 - 0.1 = 0.3 s: words are seconds from there. "hallo" is the engine hearing the committed word's
            // last 0.09 s differently (absolute 0.3 to 0.39); it ends before the committed word's end, so it is not a new word.
            H(W("hallo", 0.0, 0.09), W("world", 0.2, 0.6), W("how", 0.7, 0.9), W("are", 1.0, 1.2)));
        Feed(transcriber, 0.6, true);
        await Live(transcriber);
        Feed(transcriber, 0.6, true);
        await Live(transcriber);
        Feed(transcriber, 0.6, true);
        var delta = await Live(transcriber);
        var third = engine.Decodes[2];
        Assert.Equal(TimeSpan.FromSeconds(0.3), third.Request.AudioStart);
        Assert.Equal(TimeSpan.FromSeconds(1.5), third.AudioLength);
        Assert.Equal("hello", third.Request.Prompt);
        Assert.Equal("hello world how are", Texts(delta.Words));
        Assert.Equal(3, delta.Committed);
        Assert.Equal([WordOp.Confirm(1), WordOp.Confirm(2), WordOp.Add(3, "are", true)], delta.Ops);
        // "how" ends at 0.3 + 0.9 = 1.2 s; the buffer now starts 0.1 s before that, 1.1 s, and audio runs to 1.8 s.
        Assert.Equal(TimeSpan.FromSeconds(0.7), transcriber.UncommittedLength);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T22")]
    [Trait("Decision", "ED-267")] // V-7.3 step 2: the prompt is the last 200 characters of committed text
    public async Task The_prompt_is_the_last_200_characters_of_the_committed_text()
    {
        var words = Enumerable.Range(0, 6).Select(i => new string((char)('a' + i), 40)).ToArray();
        var hypothesis = H(words.Select((w, i) => W(w, i * 0.1, i * 0.1 + 0.08)).ToArray());
        var (transcriber, engine) = Make("auto", hypothesis, hypothesis, H());
        Feed(transcriber, 1.0, true);
        await Live(transcriber);
        Feed(transcriber, 0.5, true);
        await Live(transcriber);
        Assert.Equal(6, transcriber.CommittedCount);
        Feed(transcriber, 0.5, true);
        await Live(transcriber);
        var committed = string.Join(" ", words);
        Assert.Equal(245, committed.Length);
        Assert.Equal(committed[^200..], engine.Decodes[2].Request.Prompt);
        Assert.Equal(200, engine.Decodes[2].Request.Prompt.Length);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T23")]
    [Trait("Decision", "ED-267")] // V-7.3 step 2: no pass without speech in the buffer, however much audio there is
    public void No_pass_is_due_on_audio_the_VAD_never_saw_speech_in()
    {
        var (transcriber, engine) = Make("auto");
        Feed(transcriber, 5.0, false);
        Assert.Null(transcriber.DuePass);
        Assert.False(transcriber.SawSpeech);
        Assert.Empty(engine.Decodes);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T23")]
    [Trait("Decision", "ED-267")] // V-7.3 step 2: a pass starts at 0.5 s of new audio, not before
    public void The_first_pass_is_due_at_0_5_s_of_audio_with_speech()
    {
        var (transcriber, _) = Make("auto");
        Feed(transcriber, 0.49, true);
        Assert.Null(transcriber.DuePass);
        Feed(transcriber, 0.01, true);
        Assert.Equal(TranscriberPass.Live, transcriber.DuePass);
        Assert.True(transcriber.SawSpeech);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T23")]
    [Trait("Decision", "ED-267")] // V-7.3 step 2: 0.5 s of new audio since the last pass began
    public async Task The_next_pass_is_due_0_5_s_after_the_last_one_began()
    {
        var (transcriber, _) = Make("auto", H(W("a", 0.0, 0.2)));
        Feed(transcriber, 0.6, true);
        await Live(transcriber);
        Feed(transcriber, 0.49, true);
        Assert.Null(transcriber.DuePass);
        Feed(transcriber, 0.01, true);
        Assert.Equal(TranscriberPass.Live, transcriber.DuePass);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T23")]
    [Trait("Decision", "ED-267")] // V-7.3 step 2: the previous pass must be done; audio that arrived while it ran counts as new audio since it began
    public async Task No_pass_is_due_while_one_runs_and_audio_that_arrived_meanwhile_counts()
    {
        using var runtime = new TestRuntime();
        var (transcriber, engine) = Make("auto", H(W("a", 0.0, 0.2)));
        engine.Latency = TimeSpan.FromSeconds(0.4);
        Feed(transcriber, 0.6, true);
        var running = Live(transcriber);
        Assert.True(transcriber.IsPassRunning);
        Feed(transcriber, 2.0, true);
        Assert.Null(transcriber.DuePass);
        runtime.Advance(TimeSpan.FromSeconds(0.4));
        await running;
        Assert.False(transcriber.IsPassRunning);
        Assert.Equal(TranscriberPass.Live, transcriber.DuePass);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T23")]
    [Trait("Decision", "ED-267")] // V-7.3 step 2: only 0.4 s arrived while the pass ran: not due until 0.1 s more
    public async Task Less_than_0_5_s_of_audio_during_a_pass_is_not_enough()
    {
        using var runtime = new TestRuntime();
        var (transcriber, engine) = Make("auto", H(W("a", 0.0, 0.2)));
        engine.Latency = TimeSpan.FromSeconds(0.4);
        Feed(transcriber, 0.6, true);
        var running = Live(transcriber);
        Feed(transcriber, 0.4, true);
        runtime.Advance(TimeSpan.FromSeconds(0.4));
        await running;
        Assert.Null(transcriber.DuePass);
        Feed(transcriber, 0.1, true);
        Assert.Equal(TranscriberPass.Live, transcriber.DuePass);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T24")]
    [Trait("Decision", "ED-267")] // V-7.4: out of speech for 1.5 s after speech, a pass on the uncommitted buffer commits all its words
    public async Task A_pause_of_1_5_s_commits_everything_and_decodes_only_up_to_the_end_of_speech()
    {
        var (transcriber, engine) = Make("en",
            H(W("hello", 0.0, 0.5), W("there", 1.0, 1.5)),
            H(W("hello", 0.0, 0.5), W("there", 1.0, 1.5), W("friend", 1.6, 2.0)));
        Feed(transcriber, 2.0, true);
        await Live(transcriber);
        Feed(transcriber, 1.49, false);
        Assert.NotEqual(TranscriberPass.LongPause, transcriber.DuePass);
        Feed(transcriber, 0.01, false);
        Assert.Equal(TranscriberPass.LongPause, transcriber.DuePass);
        var delta = await transcriber.RunPass(TranscriberPass.LongPause, CancellationToken.None);
        Assert.Equal(3, delta.Committed);
        Assert.All(delta.Words, w => Assert.False(w.Tentative));
        Assert.Equal("hello there friend", Texts(delta.Words));
        Assert.Equal([WordOp.Confirm(0), WordOp.Confirm(1), WordOp.Add(2, "friend", false)], delta.Ops);
        Assert.Equal(TimeSpan.FromSeconds(2.0), engine.Decodes[1].AudioLength);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T24")]
    [Trait("Decision", "ED-267")] // V-7.4: the silent audio is dropped from the buffer, and silence alone never makes another pass due
    public async Task After_a_long_pause_the_silence_is_dropped_and_the_next_pass_starts_at_the_new_speech()
    {
        var (transcriber, engine) = Make("en", H(W("hello", 0.0, 0.5)), H(W("again", 0.0, 0.4)));
        Feed(transcriber, 1.0, true);
        Feed(transcriber, 1.5, false);
        await transcriber.RunPass(TranscriberPass.LongPause, CancellationToken.None);
        Assert.Equal(TimeSpan.Zero, transcriber.UncommittedLength);
        Feed(transcriber, 10.0, false);
        Assert.Null(transcriber.DuePass);
        Feed(transcriber, 0.6, true);
        Assert.Equal(TranscriberPass.Live, transcriber.DuePass);
        await Live(transcriber);
        // The silent audio is not kept (bar a 0.1 s lead-in, ED-NEW-f-vt-3 (6)): the 10 s of silence ends at 12.5 s, the new speech runs 0.6 s.
        Assert.Equal(TimeSpan.FromSeconds(12.4), engine.Decodes[1].Request.AudioStart);
        Assert.Equal(TimeSpan.FromSeconds(0.7), engine.Decodes[1].AudioLength);
        Assert.Equal("hello", engine.Decodes[1].Request.Prompt);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T24")]
    [Trait("Decision", "ED-267")] // V-7.4: over 25 s uncommitted without a commit, tentative words ending more than 10 s back are committed
    public async Task Over_25_s_without_a_commit_the_words_ending_more_than_10_s_back_are_committed()
    {
        // 13 words, word k from 2k to 2k + 1.5. At 26 s, "more than 10 s back" is an end before 16 s: ends 1.5, 3.5, ... 15.5 (k = 0 to 7).
        var words = Enumerable.Range(0, 13).Select(k => W("w" + k, 2.0 * k, 2.0 * k + 1.5)).ToArray();
        var (transcriber, engine) = Make("en", H(words), H());
        Feed(transcriber, 26.0, true);
        var delta = await Live(transcriber);
        Assert.Equal(8, delta.Committed);
        Assert.Equal(13, delta.Words.Count);
        Assert.All(delta.Words.Take(8), w => Assert.False(w.Tentative));
        Assert.All(delta.Words.Skip(8), w => Assert.True(w.Tentative));
        // The buffer is cut at the end of the last committed word (15.5 s) less the 0.1 s overlap.
        Assert.Equal(TimeSpan.FromSeconds(26.0 - 15.4), transcriber.UncommittedLength);
        Feed(transcriber, 0.6, true);
        await Live(transcriber);
        Assert.Equal(TimeSpan.FromSeconds(15.4), engine.Decodes[1].Request.AudioStart);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T24")]
    [Trait("Decision", "ED-267")] // V-7.4: at 24 s (under the cap) nothing is forced
    public async Task Under_25_s_nothing_is_committed_without_agreement()
    {
        var words = Enumerable.Range(0, 12).Select(k => W("w" + k, 2.0 * k, 2.0 * k + 1.5)).ToArray();
        var (transcriber, _) = Make("en", H(words));
        Feed(transcriber, 24.0, true);
        var delta = await Live(transcriber);
        Assert.Equal(0, delta.Committed);
        Assert.Equal(TimeSpan.FromSeconds(24.0), transcriber.UncommittedLength);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T24")]
    [Trait("Decision", "ED-267")] // V-7.4: a decode never exceeds 30 s (whisper.cpp's window): the newest 30 s of a longer buffer
    public async Task A_decode_never_gets_more_than_the_newest_30_s()
    {
        var (transcriber, engine) = Make("en", H());
        Feed(transcriber, 40.0, true);
        await Live(transcriber);
        var decode = Assert.Single(engine.Decodes);
        Assert.Equal(TimeSpan.FromSeconds(30), decode.AudioLength);
        Assert.Equal(TimeSpan.FromSeconds(10), decode.Request.AudioStart);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T26")]
    [Trait("Decision", "ED-267")] // V-7.8: no pass on VAD-silent audio, even when asked for one
    public async Task A_pass_asked_for_on_silent_audio_never_reaches_the_engine()
    {
        var (transcriber, engine) = Make("auto", H(W("ghost", 0.0, 0.5)));
        Feed(transcriber, 3.0, false);
        var delta = await Live(transcriber);
        Assert.Empty(delta.Ops);
        Assert.Empty(delta.Words);
        Assert.Empty(engine.Decodes);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T26")]
    [Trait("Decision", "ED-267")] // V-6.4, V-7.8: a session in which the VAD never saw speech never calls the engine, not even for the final pass
    public async Task The_final_pass_of_a_silent_session_never_reaches_the_engine()
    {
        var (transcriber, engine) = Make("auto", H(W("ghost", 0.0, 0.5)));
        Feed(transcriber, 4.0, false);
        var delta = await transcriber.RunFinal(CancellationToken.None);
        Assert.Empty(delta.Words);
        Assert.Empty(engine.Decodes);
        Assert.False(transcriber.SawSpeech);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T26")]
    [Trait("Decision", "ED-267")] // V-7.6: the final pass decodes the uncommitted buffer plus 0.3 s of silence with the final parameters
    public async Task The_final_pass_adds_0_3_s_of_silence_and_decodes_with_the_final_parameters()
    {
        var (transcriber, engine) = Make("hi", H(W("hello", 0.0, 0.4)), H(W("hello", 0.0, 0.4), W("world.", 0.5, 0.9)));
        Feed(transcriber, 0.6, true);
        await Live(transcriber);
        Feed(transcriber, 0.4, true);
        var delta = await transcriber.RunFinal(CancellationToken.None);
        var decode = engine.Decodes[1];
        Assert.Equal(DecodeMode.Final, decode.Request.Mode);
        Assert.Equal("hi", decode.Request.Language);
        Assert.Equal(TimeSpan.FromSeconds(1.0 + 0.3), decode.AudioLength);
        Assert.Equal("hello world.", Texts(delta.Words));
        Assert.Equal([WordOp.Add(1, "world.", true)], delta.Ops);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T26")]
    [Trait("Decision", "ED-267")] // V-7.8: the final pass drops words that lie wholly in VAD-silent spans, and keeps one that touches speech
    public async Task The_final_pass_drops_words_that_lie_only_in_silence()
    {
        var hypothesis = H(W("good", 0.2, 0.6), W("edge", 0.9, 1.2), W("ghost", 2.0, 2.4), W("padding", 3.0, 3.3));
        var (transcriber, _) = Make("en", hypothesis);
        Feed(transcriber, 1.0, true);
        Feed(transcriber, 2.0, false);
        var delta = await transcriber.RunFinal(CancellationToken.None);
        Assert.Equal("good edge", Texts(delta.Words));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T26")]
    [Trait("Decision", "ED-267")] // V-7.6: the final words replace the tentative tail; committed words stay as they are
    public async Task The_final_pass_replaces_the_tentative_tail_and_keeps_the_committed_words()
    {
        var (transcriber, _) = Make("en",
            H(W("a", 0.0, 0.1), W("b", 0.2, 0.3), W("tool", 0.4, 0.5)),
            H(W("a", 0.0, 0.1), W("b", 0.2, 0.3), W("team", 0.4, 0.5)),
            // The final pass starts at 0.3 - 0.1 = 0.2 s: "b" (absolute 0.2 to 0.3) ends at the committed word's end and is not new.
            H(W("b", 0.0, 0.1), W("tooltip", 0.2, 0.5), W("works.", 0.6, 0.8)));
        Feed(transcriber, 0.6, true);
        await Live(transcriber);
        Feed(transcriber, 0.6, true);
        await Live(transcriber);
        Assert.Equal(2, transcriber.CommittedCount);
        var delta = await transcriber.RunFinal(CancellationToken.None);
        Assert.Equal("a b tooltip works.", Texts(delta.Words));
        Assert.Equal(2, delta.Committed);
        Assert.Equal([false, false, true, true], delta.Words.Select(w => w.Tentative));
        Assert.Equal([WordOp.Revise(2, "tooltip"), WordOp.Add(3, "works.", true)], delta.Ops);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T22")]
    [Trait("Decision", "ED-267")] // V-7.12 of the language: the engine's detected language is kept for the pill (V-12.10)
    public async Task The_language_the_engine_detects_is_kept()
    {
        var (transcriber, _) = Make("auto", new Hypothesis([W("namaste", 0.0, 0.5)], "hi"));
        Assert.Null(transcriber.DetectedLanguage);
        Feed(transcriber, 0.6, true);
        var delta = await Live(transcriber);
        Assert.Equal("hi", delta.Language);
        Assert.Equal("hi", transcriber.DetectedLanguage);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T22")]
    [Trait("Decision", "ED-267")] // V-7.10: a cancelled pass aborts in the engine and changes nothing
    public async Task A_cancelled_pass_aborts_in_the_engine_and_leaves_the_words_alone()
    {
        using var runtime = new TestRuntime();
        var (transcriber, engine) = Make("auto", H(W("a", 0.0, 0.2)));
        engine.Latency = TimeSpan.FromSeconds(1);
        Feed(transcriber, 0.6, true);
        using var cancellation = new CancellationTokenSource();
        var running = transcriber.RunPass(TranscriberPass.Live, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(1, engine.Aborted);
        Assert.False(transcriber.IsPassRunning);
        Assert.Empty(transcriber.Words);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T22")]
    [Trait("Decision", "ED-267")] // V-7.1: an engine failure reaches the caller and frees the transcriber
    public async Task An_engine_failure_propagates_and_frees_the_transcriber()
    {
        var (transcriber, engine) = Make("auto", H(W("a", 0.0, 0.2)));
        engine.FailNextDecode(new InvalidOperationException("boom"));
        Feed(transcriber, 0.6, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Live(transcriber));
        Assert.False(transcriber.IsPassRunning);
        Assert.Equal(TranscriberPass.Live, transcriber.DuePass);
    }
}
