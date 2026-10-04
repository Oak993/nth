// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): the data a scripted speaker replays (voice-SPEC V-14.8).
// UNRESOLVED (ED-NEW-f-vt-3 (7)): the design's own SCRIPT, SYN (rng(91)) and STREAM (rng(7)) tables of voice-composer.js:42-83 and :221-240 are
// not in the repository (only E:\Droppy\work\design\...). This file is the mechanism that replays any such table; the design's rows go in as
// ScriptedWord values when the fixture extracted from voice-composer.js is committed.
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Core.Port.Voice.Session;

/// <summary>
/// One spoken word: when it is said (seconds of session audio), the text a good recogniser ends with, the text a first guess shows
/// (null: none), how long after the word ended the first guess appears (the design's 0.3 to 0.7 s) and how long after that it is revised
/// (the design's 0.35 to 0.6 s).
/// </summary>
public sealed record ScriptedWord(string Text, double Start, double End, string? FirstGuess = null, double ArriveAfter = 0.4, double ReviseAfter = 0.45);

/// <summary>
/// A scripted speaker: the capture signal, the VAD's speech spans and the engine's hypotheses that replay <see cref="Words"/> on the
/// session clock. It touches no device, model or network.
/// </summary>
public sealed class VoiceScript(IReadOnlyList<ScriptedWord> words, string? language = null)
{
    /// <summary>Words closer than this are one stretch of speech to the VAD.</summary>
    public const double MergeGap = 0.3;

    public IReadOnlyList<ScriptedWord> Words { get; } = words;

    /// <summary>The language the scripted engine reports once it has recognised a word.</summary>
    public string? Language { get; } = language;

    /// <summary>When the last word ends.</summary>
    public double SpeechEnd => Words.Count == 0 ? 0 : Words.Max(w => w.End);

    /// <summary>The speech stretches in seconds: the words with gaps under <see cref="MergeGap"/> joined.</summary>
    public IReadOnlyList<(double Start, double End)> SpeechSpans
    {
        get
        {
            var spans = new List<(double Start, double End)>();
            foreach (var word in Words.OrderBy(w => w.Start))
            {
                if (spans.Count > 0 && word.Start - spans[^1].End < MergeGap) spans[^1] = (spans[^1].Start, Math.Max(spans[^1].End, word.End));
                else spans.Add((word.Start, word.End));
            }
            return spans;
        }
    }

    /// <summary>A VAD that answers speech inside <see cref="SpeechSpans"/>.</summary>
    public ScriptedVad CreateVad() => ScriptedVad.Speech(SpeechSpans.ToArray());

    /// <summary>The capture signal: a tone inside the speech spans and a room's noise elsewhere (never exactly zero, V-4.5).</summary>
    public Func<long, float> Signal(int sampleRate) =>
        ScriptedSignals.SpeechSpans(SpeechSpans, ScriptedSignals.Tone(220, 0.3, sampleRate), sampleRate);

    /// <summary>An engine that answers <see cref="Answer"/>, taking <paramref name="latency"/> on the clock per decode.</summary>
    public ScriptedEngine CreateEngine(TimeSpan latency = default) => new(Answer, latency);

    /// <summary>
    /// What a recogniser that heard <paramref name="decode"/> says: the words that end inside the decoded audio, each shown once its
    /// arrival delay has passed (as its first guess until its revision delay has passed too), all of them in their final form for the
    /// final pass. Word times are seconds from the first decoded sample.
    /// </summary>
    public Hypothesis Answer(ScriptedDecode decode)
    {
        var start = decode.Request.AudioStart.TotalSeconds;
        var end = decode.AudioEnd.TotalSeconds;
        var final = decode.Request.Mode == DecodeMode.Final;
        var shown = new List<HypothesisWord>();
        foreach (var word in Words.OrderBy(w => w.Start))
        {
            if (word.End <= start || word.Start >= end) continue;
            string text;
            if (final) text = word.Text;
            else
            {
                if (end < word.End + word.ArriveAfter) continue;
                text = word.FirstGuess is not null && end < word.End + word.ArriveAfter + word.ReviseAfter ? word.FirstGuess : word.Text;
            }
            shown.Add(new HypothesisWord(text, Math.Max(0, word.Start - start), Math.Max(0, word.End - start)));
        }
        return new Hypothesis(shown, shown.Count > 0 ? Language : null);
    }
}
