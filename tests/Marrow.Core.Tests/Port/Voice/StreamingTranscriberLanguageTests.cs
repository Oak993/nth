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

    // ED-NEW-voicefix-5 (replaces A_long_pause_settles_the_language_of_everything_said_before_it, which settled on 0.8 s): a long pause after a short stretch of speech
    // settles nothing and commits nothing, the same silence does not start another pass, and the next pass still asks Whisper to detect.
    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-5")]
    public async Task A_long_pause_after_short_speech_settles_nothing_and_is_not_decoded_again()
    {
        var engine = new ScriptedEngine(d => new Hypothesis([W("नमस्ते", 0.0, 0.5)], d.Request.Language == "auto" ? "hi" : null));
        var transcriber = new StreamingTranscriber(engine, "auto");
        Feed(transcriber, 0.8);
        transcriber.Append(new float[(int)(1.6 * 16000)], false);
        Assert.Equal(TranscriberPass.LongPause, transcriber.DuePass);
        var delta = await transcriber.RunPass(TranscriberPass.LongPause, CancellationToken.None);
        Assert.Equal(0, delta.Committed);
        Assert.Null(transcriber.SettledLanguage);
        Assert.Equal([new TranscriptWord("नमस्ते", true)], delta.Words);
        Assert.Null(transcriber.DuePass); // the same silence goes on: nothing new to hear
        transcriber.Append(new float[3 * 16000], false);
        Assert.Null(transcriber.DuePass);
        Feed(transcriber, 0.6);
        Assert.Equal(TranscriberPass.Live, transcriber.DuePass);
        await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        Assert.Equal(2, engine.Decodes.Count);
        Assert.Equal("auto", engine.Decodes[^1].Request.Language);
        Assert.Equal(TimeSpan.Zero, engine.Decodes[^1].Request.AudioStart); // the held speech is heard again
        Assert.Null(transcriber.SettledLanguage);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-5")]
    public async Task A_long_pause_after_three_seconds_of_speech_settles_the_language_of_everything_said_before_it()
    {
        var engine = new ScriptedEngine(d => new Hypothesis([W("नमस्ते", 0.0, 0.5)], d.Request.Language == "auto" ? "hi" : null));
        var transcriber = new StreamingTranscriber(engine, "auto");
        Feed(transcriber, 3.2);
        transcriber.Append(new float[(int)(1.6 * 16000)], false);
        Assert.Equal(TranscriberPass.LongPause, transcriber.DuePass);
        var delta = await transcriber.RunPass(TranscriberPass.LongPause, CancellationToken.None);
        Assert.Equal(1, delta.Committed);
        Assert.Equal("hi", transcriber.SettledLanguage);
        Assert.Null(transcriber.DuePass); // committed: the silence is dropped
        Feed(transcriber, 0.6);
        await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        Assert.Equal("hi", engine.Decodes[^1].Request.Language);
    }

    // The speech in the decoded window, not the window, is what settles: 1 s of speech and 2.5 s of pauses in a 3.5 s window settle nothing.
    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-5")]
    public async Task A_window_of_three_seconds_that_is_mostly_pauses_does_not_settle()
    {
        var engine = new ScriptedEngine(d => new Hypothesis([W("Abi,", 0.0, 0.4)], d.Request.Language == "auto" ? "en" : null));
        var transcriber = new StreamingTranscriber(engine, "auto");
        Feed(transcriber, 0.5);
        transcriber.Append(new float[(int)(1.2 * 16000)], false);
        Feed(transcriber, 0.5);
        transcriber.Append(new float[(int)(1.3 * 16000)], false);
        await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        Assert.True(engine.Decodes[^1].AudioLength.TotalSeconds >= 3.0);
        Assert.Null(transcriber.SettledLanguage);
        Assert.Equal(0, transcriber.CommittedCount);
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

    // ---- ED-NEW-voicefix-4: Auto-detect with Whisper's language probabilities ----------------------------------------------------------------------------------

    private static readonly string[] Urdu = ["ہاں", "بھائی،", "کیا", "چل", "رہا", "ہے", "آج کل؟"];

    // Whisper told a language writes in it: told "en" for this Hindustani speech, the English translation; asked to auto-detect, as Whisper() above
    private static Hypothesis Told(ScriptedDecode decode)
    {
        var words = decode.Request.Language switch { "hi" => Hindi, "ur" => Urdu, "auto" => null, _ => English };
        if (words is null) return Whisper(decode);
        var count = Math.Clamp((int)(decode.AudioLength.TotalSeconds / 0.4), 1, words.Length);
        return new Hypothesis(Enumerable.Range(0, count).Select(i => W(words[i], i * 0.4, i * 0.4 + 0.35)).ToArray());
    }

    private static ScriptedEngine Detecting(Func<double, LanguageOdds?> odds) => new(Told) { Detect = (audio, _) => odds(audio.AudioLength.TotalSeconds) };

    private static void Silence(StreamingTranscriber transcriber, double seconds) => transcriber.Append(new float[(int)Math.Round(seconds * 16000)], false);

    private static void NoEnglish(IEnumerable<TranscriptDelta> deltas) =>
        Assert.All(deltas, d => Assert.DoesNotContain(d.Words, w => English.Contains(w.Text)));

    // The owner's sentence is about 2.5 s: no live window reaches 3 s, and the final pass decodes it whole. Whisper leaning English on it decides nothing.
    [Theory]
    [InlineData(null, 0.60f, 0.30f)] // no Hindustani before: an unsure English with Hindi a strong runner-up
    [InlineData("hi", 0.85f, 0.03f)] // Hindi before: a fairly sure English is still not decisive
    [InlineData("ur", 0.85f, 0.03f)] // Urdu before: the same, in the user's own script
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public async Task A_short_utterance_is_never_decoded_or_shown_in_English_by_the_live_or_the_final_pass(string? before, float english, float hindi)
    {
        var engine = Detecting(_ => new LanguageOdds("en", english, "hi", hindi));
        var transcriber = new StreamingTranscriber(engine, "auto", hindustaniBefore: before);
        var deltas = new List<TranscriptDelta>();
        for (var i = 0; i < 5; i++)
        {
            Feed(transcriber, 0.5);
            deltas.Add(await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None));
        }
        Assert.Equal(0, transcriber.CommittedCount);
        Assert.Null(transcriber.SettledLanguage);
        deltas.Add(await transcriber.RunFinal(CancellationToken.None));
        var expected = before ?? "hi";
        Assert.Equal(6, engine.Decodes.Count);
        Assert.All(engine.Decodes, d => Assert.Equal(expected, d.Request.Language));
        NoEnglish(deltas);
        Assert.All(deltas, d => Assert.Equal(expected, d.Language));
        // the final pass decodes the 2.5 s with its 0.3 s of padding: the scripted Whisper writes one word per 0.4 s of it, six words
        Assert.Equal((expected == "ur" ? Urdu : Hindi).Take(6), deltas[^1].Words.Select(w => w.Text));
        Assert.Equal(expected, transcriber.SettledLanguage);
        Assert.Equal(expected, transcriber.HindustaniWritten);
        Assert.Equal(6, engine.Detections.Count);
        Assert.All(engine.Detections, d => Assert.Equal(["hi", "ur"], d.RunnersUp));
    }

    // "Haan bhai," then a pause to think: the pause's pass decodes 0.8 s, which settles nothing, is not decoded again while the silence lasts, and the language is
    // settled only once 3 s of speech have been heard in a trusted language.
    [Theory]
    [InlineData(null, 0.60f, 0.30f)]
    [InlineData("hi", 0.88f, 0.04f)]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public async Task A_pause_after_Haan_bhai_settles_nothing_and_English_is_never_decoded(string? before, float english, float hindi)
    {
        // on a window with under 3 s of speech Whisper leans English; on more it hears Hindi
        var speech = 0.0;
        var engine = Detecting(_ => speech < 3.0 ? new LanguageOdds("en", english, "hi", hindi) : new LanguageOdds("hi", 0.8f, "hi", 0.8f));
        var transcriber = new StreamingTranscriber(engine, "auto", hindustaniBefore: before);
        var deltas = new List<TranscriptDelta>();
        Feed(transcriber, 0.8);
        speech = 0.8;
        Silence(transcriber, 1.6);
        Assert.Equal(TranscriberPass.LongPause, transcriber.DuePass);
        deltas.Add(await transcriber.RunPass(TranscriberPass.LongPause, CancellationToken.None));
        Assert.Equal(0, transcriber.CommittedCount);
        Assert.Null(transcriber.SettledLanguage);
        Assert.Equal(["हाँ", "भाई,"], deltas[^1].Words.Select(w => w.Text));
        Assert.All(deltas[^1].Words, w => Assert.True(w.Tentative));
        Silence(transcriber, 1.0);
        Assert.Null(transcriber.DuePass);
        var detectionsAtPause = engine.Detections.Count;
        for (var i = 0; i < 6; i++)
        {
            Feed(transcriber, 0.5);
            speech += 0.5;
            Assert.Equal(TranscriberPass.Live, transcriber.DuePass);
            deltas.Add(await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None));
            Assert.Equal(speech >= 3.0 ? "hi" : null, transcriber.SettledLanguage);
        }
        // detected again on every pass until the language settled (3.3 s of speech, the fifth pass), never after
        Assert.Equal(detectionsAtPause + 5, engine.Detections.Count);
        deltas.Add(await transcriber.RunFinal(CancellationToken.None));
        Assert.Equal(before ?? "hi", deltas[1].Language);
        Assert.All(engine.Decodes, d => Assert.Equal(before ?? "hi", d.Request.Language));
        NoEnglish(deltas);
        Assert.True(transcriber.CommittedCount > 0);
    }

    [Theory]
    [InlineData(0.50f, 0.35f, "hi")] // a low-confidence English with Hindi close behind is Hindi
    [InlineData(0.95f, 0.02f, "en")] // a sure English, and no Hindustani before: an English speaker is written in English
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public async Task A_three_second_window_settles_on_the_policys_language_and_detection_stops(float english, float hindi, string expected)
    {
        var engine = Detecting(_ => new LanguageOdds("en", english, "hi", hindi));
        var transcriber = new StreamingTranscriber(engine, "auto");
        Feed(transcriber, 3.2);
        var delta = await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        Assert.Equal(expected, transcriber.SettledLanguage);
        Assert.Equal(expected, delta.Language);
        Feed(transcriber, 0.6);
        delta = await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        Assert.True(delta.Committed > 0);
        await transcriber.RunFinal(CancellationToken.None);
        Assert.Single(engine.Detections);
        Assert.All(engine.Decodes, d => Assert.Equal(expected, d.Request.Language));
    }

    // A language the policy does not trust is never decoded while the words could still wait: nothing is shown and the pill names nothing; the final pass decodes in it.
    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public async Task An_untrusted_language_is_neither_decoded_nor_shown_live()
    {
        var engine = Detecting(_ => new LanguageOdds("en", 0.60f, "hi", 0.02f));
        var transcriber = new StreamingTranscriber(engine, "auto");
        for (var i = 0; i < 6; i++)
        {
            Feed(transcriber, 0.5);
            var delta = await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
            Assert.Empty(delta.Ops);
            Assert.Empty(delta.Words);
            Assert.Null(delta.Language);
        }
        Assert.Empty(engine.Decodes);
        Assert.Equal(6, engine.Detections.Count);
        Assert.Null(transcriber.SettledLanguage);
        var final = await transcriber.RunFinal(CancellationToken.None);
        Assert.Equal("en", Assert.Single(engine.Decodes).Request.Language);
        Assert.Equal("en", final.Language);
        Assert.Equal("en", transcriber.SettledLanguage);
        Assert.Null(transcriber.HindustaniWritten);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public async Task An_untrusted_language_is_settled_on_ten_seconds_of_speech()
    {
        var engine = Detecting(_ => new LanguageOdds("fr", 0.55f, "hi", 0.01f));
        var transcriber = new StreamingTranscriber(engine, "auto");
        for (var i = 0; i < 19; i++)
        {
            Feed(transcriber, 0.5);
            await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        }
        Assert.Empty(engine.Decodes);
        Assert.Null(transcriber.SettledLanguage);
        Feed(transcriber, 0.5); // 10 s of speech
        await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        Assert.Equal("fr", Assert.Single(engine.Decodes).Request.Language);
        Assert.Equal("fr", transcriber.SettledLanguage);
    }

    // A pause whose words were held is committed (not settled) once its silence has lasted 10 s: the speech before it will not be heard again.
    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public async Task A_held_pause_is_committed_without_settling_after_ten_seconds_of_silence()
    {
        var engine = Detecting(_ => new LanguageOdds("hi", 0.9f, "hi", 0.9f));
        var transcriber = new StreamingTranscriber(engine, "auto");
        Feed(transcriber, 0.8);
        Silence(transcriber, 1.6);
        await transcriber.RunPass(TranscriberPass.LongPause, CancellationToken.None);
        Assert.Equal(0, transcriber.CommittedCount);
        Silence(transcriber, 8.3);
        Assert.Null(transcriber.DuePass);
        Silence(transcriber, 0.1);
        Assert.Equal(TranscriberPass.LongPause, transcriber.DuePass);
        var delta = await transcriber.RunPass(TranscriberPass.LongPause, CancellationToken.None);
        Assert.Equal(["हाँ", "भाई,"], delta.Words.Select(w => w.Text));
        Assert.Equal(2, delta.Committed);
        Assert.Null(transcriber.SettledLanguage);
        Assert.Equal("hi", transcriber.HindustaniWritten);
        Assert.Null(transcriber.DuePass);
        Assert.Equal(TimeSpan.Zero, transcriber.UncommittedLength); // the held speech and its silence are dropped
        Feed(transcriber, 0.6);
        await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        Assert.Equal(3, engine.Detections.Count); // still detected: nothing settled
    }

    // Short fragments with pauses between them are held, but the held buffer never grows past HeldBufferSeconds: no held word ever falls out of the 30 s window.
    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public async Task Held_fragments_are_committed_before_the_buffer_outgrows_the_window_and_no_word_is_lost()
    {
        const double Fragment = 0.4, Gap = 3.0, Step = Fragment + Gap;
        const int Fragments = 10;
        var engine = new ScriptedEngine(decode =>
        {
            var start = decode.Request.AudioStart.TotalSeconds;
            var end = decode.AudioEnd.TotalSeconds;
            var heard = new List<HypothesisWord>();
            for (var k = 0; k < Fragments; k++)
            {
                var (from, to) = (k * Step, k * Step + Fragment);
                if (to <= start || from >= end) continue;
                heard.Add(W("w" + k, Math.Max(0, from - start), Math.Min(end, to) - start));
            }
            return new Hypothesis(heard);
        }) { Detect = (_, _) => new LanguageOdds("hi", 0.9f, "hi", 0.9f) };
        var transcriber = new StreamingTranscriber(engine, "auto");
        async Task Pump()
        {
            while (transcriber.DuePass is { } pass) await transcriber.RunPass(pass, CancellationToken.None);
        }
        for (var k = 0; k < Fragments; k++)
        {
            Feed(transcriber, Fragment);
            await Pump();
            Silence(transcriber, 1.6);
            await Pump();
            Silence(transcriber, Gap - 1.6);
            await Pump();
            Assert.True(transcriber.UncommittedLength.TotalSeconds <= StreamingTranscriber.HeldBufferSeconds + Step);
            Assert.Null(transcriber.SettledLanguage); // never 3 s of speech in one window
        }
        Assert.True(transcriber.CommittedCount > 0);
        Assert.All(engine.Decodes, d => Assert.True(d.AudioLength.TotalSeconds <= StreamingTranscriber.HeldBufferSeconds + 1));
        var final = await transcriber.RunFinal(CancellationToken.None);
        Assert.Equal(Enumerable.Range(0, Fragments).Select(k => "w" + k), final.Words.Select(w => w.Text));
    }

    [Theory]
    [InlineData("hi")]
    [InlineData("ur")]
    [InlineData("en")]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public async Task A_forced_language_is_never_replaced_by_detection_whatever_it_says(string code)
    {
        var engine = Detecting(_ => code == "en" ? new LanguageOdds("hi", 0.99f, "hi", 0.99f) : new LanguageOdds("en", 0.99f, "hi", 0.001f));
        var transcriber = new StreamingTranscriber(engine, code, hindustaniBefore: code == "en" ? "hi" : null);
        Feed(transcriber, 0.6);
        Silence(transcriber, 1.6);
        await transcriber.RunPass(TranscriberPass.LongPause, CancellationToken.None);
        Feed(transcriber, 3.6);
        await transcriber.RunPass(TranscriberPass.Live, CancellationToken.None);
        await transcriber.RunFinal(CancellationToken.None);
        Assert.Empty(engine.Detections);
        Assert.Equal(3, engine.Decodes.Count);
        Assert.All(engine.Decodes, d => Assert.Equal(code, d.Request.Language));
        Assert.Null(transcriber.SettledLanguage);
        Assert.Equal(code, transcriber.SessionLanguage);
        Assert.Equal(code == "en" ? null : code, transcriber.HindustaniWritten);
    }
}
