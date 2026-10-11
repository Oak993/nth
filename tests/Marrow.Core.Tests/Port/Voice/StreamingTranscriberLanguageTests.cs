using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.Core.Tests.Port.Voice;

// ED-NEW-voicefix-3 (release 2026-10-11: "Haan bhai, kya chal raha hai aajkal" was written "Abi, what's going on?"): voice typing never translates. With Auto-detect the
// session's language is settled on enough audio (3 s, or a long pause's or the final pass's whole buffer) and every later pass is decoded in it; a guess from a short
// window commits nothing, so an English rendering of Hindi speech from a 0.5 s window can never stay in the text or reach the prompt.
public sealed class StreamingTranscriberLanguageTests
{
    private static HypothesisWord W(string text, double start, double end) => new(text, start, end);

    private static void Feed(StreamingTranscriber transcriber, double seconds) => transcriber.Append(new float[(int)Math.Round(seconds * 16000)], true);

    private static readonly string[] Hindi = ["हाँ", "भाई,", "क्या", "चल", "रहा", "है", "आजकल?"];
    private static readonly string[] English = ["Abi,", "what's", "going", "on?"];

    // Whisper as it behaves: on a short window Auto-detect guesses "en" and writes English; on 3 s or more it hears Hindi; told a language it writes in that language
    private static Hypothesis Whisper(ScriptedDecode decode)
    {
        var seconds = decode.AudioLength.TotalSeconds;
        var auto = decode.Request.Language == "auto";
        var hindi = !auto ? decode.Request.Language == "hi" : seconds >= 3.0;
        var words = hindi ? Hindi : English;
        var count = Math.Clamp((int)(seconds / 0.4), 1, words.Length);
        var list = Enumerable.Range(0, count).Select(i => W(words[i], i * 0.4, i * 0.4 + 0.35)).ToArray();
        return new Hypothesis(list, auto ? (hindi ? "hi" : "en") : null);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-3")]
    public async Task A_short_window_guess_commits_nothing_and_the_settled_language_decodes_every_later_pass()
    {
        var engine = new ScriptedEngine(Whisper);
        var transcriber = new StreamingTranscriber(engine, "auto");
        for (var i = 0; i < 4; i++)
        {
            Feed(transcriber, 0.6);
            await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
            // the English guesses agree with each other, but none is committed and none becomes the prompt
            Assert.Equal(0, transcriber.CommittedCount);
            Assert.Null(transcriber.SettledLanguage);
        }
        Assert.All(engine.Decodes, d => Assert.Equal("", d.Request.Prompt));
        Feed(transcriber, 0.6); // 3.0 s: enough to settle on
        await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        Assert.Equal("hi", transcriber.SettledLanguage);
        Feed(transcriber, 0.6);
        var delta = await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        Assert.Equal("hi", engine.Decodes[^1].Request.Language);
        Assert.True(delta.Committed > 0);
        Assert.Equal("hi", delta.Language);
        var final = await transcriber.RunFinal(CancellationToken.None);
        Assert.Equal("hi", engine.Decodes[^1].Request.Language);
        Assert.DoesNotContain(final.Words, w => English.Contains(w.Text));
        Assert.Equal(Hindi, final.Words.Take(Hindi.Length).Select(w => w.Text).ToArray());
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-3")]
    public async Task A_short_utterance_is_written_in_the_language_the_final_pass_hears_on_all_of_it()
    {
        // Whisper hears Hindi on the whole 2.6 s utterance (with its 0.3 s of padding) and guesses English on every live window before it
        var engine = new ScriptedEngine(d => d.Request.Mode == DecodeMode.Final
            ? new Hypothesis(Hindi.Take(4).Select((t, i) => W(t, i * 0.5, i * 0.5 + 0.4)).ToArray(), "hi")
            : Whisper(d));
        var transcriber = new StreamingTranscriber(engine, "auto");
        for (var i = 0; i < 4; i++)
        {
            Feed(transcriber, 0.65);
            await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        }
        Assert.Equal(0, transcriber.CommittedCount);
        var final = await transcriber.RunFinal(CancellationToken.None);
        Assert.Equal(TimeSpan.Zero, engine.Decodes[^1].Request.AudioStart); // the final pass hears the whole utterance
        Assert.Equal(Hindi.Take(4), final.Words.Select(w => w.Text));
        Assert.Equal("hi", transcriber.SettledLanguage);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-3")]
    public async Task A_long_pause_settles_the_language_of_everything_said_before_it()
    {
        var engine = new ScriptedEngine(d => new Hypothesis([W("नमस्ते", 0.0, 0.5)], d.Request.Language == "auto" ? "hi" : null));
        var transcriber = new StreamingTranscriber(engine, "auto");
        Feed(transcriber, 0.8);
        transcriber.Append(new float[(int)(1.6 * 16000)], false);
        Assert.Equal(TranscriberPass.LongPause, transcriber.DuePass);
        var delta = await transcriber.RunPass(TranscriberPass.LongPause, CancellationToken.None);
        Assert.Equal(1, delta.Committed);
        Assert.Equal("hi", transcriber.SettledLanguage);
        Feed(transcriber, 0.6);
        await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        Assert.Equal("hi", engine.Decodes[^1].Request.Language);
    }

    [Theory]
    [InlineData("hi")]
    [InlineData("ur")]
    [InlineData("en")]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-3")]
    public async Task A_forced_language_is_asked_for_on_every_pass_and_never_replaced(string code)
    {
        var engine = new ScriptedEngine(_ => new Hypothesis([W("a", 0.0, 0.2)], "xx"));
        var transcriber = new StreamingTranscriber(engine, code);
        Feed(transcriber, 0.6);
        await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        Feed(transcriber, 0.6);
        await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        await transcriber.RunFinal(CancellationToken.None);
        Assert.All(engine.Decodes, d => Assert.Equal(code, d.Request.Language));
        Assert.Null(transcriber.SettledLanguage);
    }
}
