// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-3): live recognition (voice-SPEC V-7.3, V-7.4, V-7.6, V-7.8).
using System.Runtime.InteropServices;
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Core.Port.Voice.Transcript;

/// <summary>Why a live pass runs.</summary>
public enum TranscriberPass { Live, LongPause }

/// <summary>What a pass changed: the operations, the words now held (committed first), how many are committed, the language detected.</summary>
public sealed record TranscriptDelta(IReadOnlyList<WordOp> Ops, IReadOnlyList<TranscriptWord> Words, int Committed, string? Language);

/// <summary>
/// LocalAgreement-2 over a sliding window of the session audio (V-7.3). The audio is 16 kHz mono; every position is a sample count from the
/// session start. The uncommitted buffer runs from 0.1 s before the end of the last committed word to the newest sample; while it holds no
/// speech it trails the newest sample (0.1 s behind it), so silence is never kept (V-7.4, ED-292 (6)). Main-actor only.
/// </summary>
public sealed class StreamingTranscriber
{
    public const int SampleRate = 16000;
    public const double MinNewAudioSeconds = 0.5;
    public const double LongPauseSeconds = 1.5;
    public const double BufferCapSeconds = 25;
    public const double CapAgeSeconds = 10;
    public const double WindowSeconds = 30;
    public const double OverlapSeconds = 0.1;
    public const double FinalSilenceSeconds = 0.3;
    public const int PromptCharacters = 200;

    /// <summary>
    /// ED-NEW-voicefix-3, ED-NEW-voicefix-4: with Auto-detect, a live or long-pause pass settles the session's language only on this much decoded speech (VAD speech in
    /// the decoded window, whatever the pass), and only in a language <see cref="AutoLanguagePolicy"/> trusts; until then nothing is committed. Whisper told "en" for
    /// Hindi or Urdu speech writes an English translation, and a short window is too little to tell the language from; a committed word never changes, so one early wrong
    /// guess used to stay in the text and steer every later pass through the prompt. The final pass settles on the language it decoded in.
    /// </summary>
    public const double SettleSeconds = 3.0;

    /// <summary>ED-NEW-voicefix-4: on this much decoded speech the language is settled on the policy's decision even when it is not trusted (waiting longer gains little).</summary>
    public const double ForceSettleSeconds = 10.0;

    /// <summary>ED-NEW-voicefix-4: an unsettled buffer this long (speech and pauses) is committed without settling, so the 30 s window never drops held words.</summary>
    public const double HeldBufferSeconds = 20.0;

    /// <summary>ED-NEW-voicefix-4: a held long pause whose silence lasts this long is committed without settling (the speech before it is not heard again).</summary>
    public const double HeldPauseSeconds = 10.0;

    private const double SameWordTolerance = 1e-3;

    private readonly ISpeechEngine engine;
    private readonly string language;
    private readonly bool processingOnly;
    private readonly bool liveWords;
    private readonly List<float> audio = [];
    private readonly List<(long Start, long End)> speechSpans = [];
    private readonly List<(string Text, double Start, double End)> committed = [];
    private List<(string Text, double Start, double End)> tail = [];
    private List<TranscriptWord> words = [];
    private long audioBase;
    private long total;
    private long bufferStart;
    private long silenceRun;
    private long lastPassStartTotal;
    private bool sawSpeech;
    private bool running;
    private bool cleared;
    private string? detectedLanguage;
    private string? settledLanguage;
    private readonly string? hindustaniBefore;
    private string? sessionHindustani;
    private long heldPauseSpeechEnd = -1;

    /// <param name="hindustaniBefore">ED-NEW-voicefix-4: the Hindustani code ("hi" or "ur") the user has dictated in before, or null; Auto-detect weighs it.</param>
    public StreamingTranscriber(ISpeechEngine engine, string language, bool liveWords = true, bool processingOnly = false, string? hindustaniBefore = null)
    {
        this.engine = engine;
        this.language = language;
        this.liveWords = liveWords;
        this.processingOnly = processingOnly;
        this.hindustaniBefore = AutoLanguagePolicy.IsHindustani(hindustaniBefore) ? hindustaniBefore : null;
    }

    /// <summary>All the audio appended so far.</summary>
    public TimeSpan AudioLength => ToTime(total);

    /// <summary>From the start of the uncommitted buffer to the newest sample.</summary>
    public TimeSpan UncommittedLength => ToTime(total - bufferStart);

    /// <summary>The audio still held in memory (V-4.7): zero after <see cref="Clear"/>.</summary>
    public TimeSpan HeldAudio => ToTime(audio.Count);

    /// <summary>True once the VAD has reported speech in this session (V-6.4).</summary>
    public bool SawSpeech => sawSpeech;

    public bool IsPassRunning => running;

    /// <summary>The words as the composer holds them: committed (solid) then the tentative tail.</summary>
    public IReadOnlyList<TranscriptWord> Words => words;

    public int CommittedCount => committed.Count;

    public string? DetectedLanguage => detectedLanguage;

    /// <summary>With Auto-detect, the language the session settled on (<see cref="SettleSeconds"/>); every later pass is decoded in it. Null before, and for a forced language.</summary>
    public string? SettledLanguage => settledLanguage;

    /// <summary>The language this session's text is written in: the forced language, or with Auto-detect the settled one (null until it settles).</summary>
    public string? SessionLanguage => AutoDetect ? settledLanguage : language;

    /// <summary>
    /// ED-NEW-voicefix-4: the Hindustani code ("hi" or "ur") this session wrote in: the forced or settled language when it is one, or with Auto-detect not yet settled the
    /// Hindi or Urdu the policy trusted; null otherwise.
    /// </summary>
    public string? HindustaniWritten => AutoLanguagePolicy.IsHindustani(SessionLanguage) ? SessionLanguage : AutoDetect && settledLanguage is null ? sessionHindustani : null;

    private bool AutoDetect => string.Equals(language, VoiceLanguage.AutoCode, StringComparison.Ordinal);

    private bool SpeechInBuffer => speechSpans.Count > 0 && speechSpans[^1].End > bufferStart;

    /// <summary>Adds audio at 16 kHz; <paramref name="speech"/> is the VAD's speech state (V-6.2) for all of it.</summary>
    public void Append(ReadOnlySpan<float> samples, bool speech)
    {
        if (cleared || samples.Length == 0) return;
        var from = total;
        if (!processingOnly && !SpeechInBuffer)
        {
            // No speech in the buffer: it trails the newest audio by the overlap, so silence is dropped (V-7.4) and speech starts 0.1 s in.
            var keep = (long)Math.Round(OverlapSeconds * SampleRate);
            bufferStart = Math.Max(bufferStart, (speech ? from : from + samples.Length) - keep);
        }
        foreach (var sample in samples) audio.Add(sample);
        total += samples.Length;
        if (speech)
        {
            sawSpeech = true;
            silenceRun = 0;
            if (speechSpans.Count > 0 && speechSpans[^1].End == from) speechSpans[^1] = (speechSpans[^1].Start, total);
            else speechSpans.Add((from, total));
        }
        else silenceRun += samples.Length;
        if (!processingOnly) TrimFront();
    }

    /// <summary>The pass that may start now, if any (V-7.3 gating, V-7.4 long pause).</summary>
    public TranscriberPass? DuePass
    {
        get
        {
            if (processingOnly || cleared || running || !SpeechInBuffer) return null;
            if (silenceRun >= Samples(LongPauseSeconds))
            {
                // ED-NEW-voicefix-4: a pause whose pass held its words is not decoded again while the same silence goes on, until it has lasted HeldPauseSeconds
                return speechSpans[^1].End > heldPauseSpeechEnd || silenceRun >= Samples(HeldPauseSeconds) ? TranscriberPass.LongPause : null;
            }
            if (total - lastPassStartTotal >= (long)Math.Round(MinNewAudioSeconds * SampleRate)) return TranscriberPass.Live;
            return null;
        }
    }

    public async Task<TranscriptDelta> RunPass(TranscriberPass pass, CancellationToken cancellationToken)
    {
        if (running) throw new InvalidOperationException("A pass is already running.");
        if (cleared || !SpeechInBuffer) return Unchanged(); // V-7.8: no pass on VAD-silent audio
        running = true;
        var previousPassStart = lastPassStartTotal;
        lastPassStartTotal = total;
        try
        {
            var end = pass == TranscriberPass.LongPause ? speechSpans[^1].End : total; // V-7.4: silence never reaches Whisper
            var (start, samples) = Window(end, padSilence: false);
            var speech = SpeechSeconds(start, end);
            // ED-NEW-voicefix-4: held words wait for more audio only so long; then they are committed (and the language settled only on enough speech)
            var overdue = speech >= ForceSettleSeconds || ToTime(end - start).TotalSeconds >= HeldBufferSeconds
                || (pass == TranscriberPass.LongPause && silenceRun >= Samples(HeldPauseSeconds));
            var choice = await Choose(samples, cancellationToken);
            if (cleared) return Unchanged();
            if (choice.Odds && !choice.Trusted && !overdue)
            {
                // ED-NEW-voicefix-4: not a language the policy trusts: nothing is decoded or shown; the next pass hears this audio again with more after it
                Hold(pass);
                return Unchanged();
            }
            var hypothesis = await engine.TranscribeAsync(samples, Request(choice.Language, start, DecodeMode.Live), cancellationToken);
            if (cleared) return Unchanged();
            var settles = Settles(choice, hypothesis, speech);
            var hold = choice.Pending && !settles && !overdue && (choice.Odds || hypothesis.Language is not null);
            if (hold) Hold(pass);
            return ApplyLive(hypothesis, start, pass, end, hold, Reported(choice, hypothesis));
        }
        catch
        {
            lastPassStartTotal = previousPassStart; // a pass that did not finish does not use up the 0.5 s
            throw;
        }
        finally
        {
            running = false;
        }
    }

    /// <summary>The final pass (V-7.6): the uncommitted buffer plus 0.3 s of silence, decoded with the final parameters.</summary>
    public async Task<TranscriptDelta> RunFinal(CancellationToken cancellationToken)
    {
        if (running) throw new InvalidOperationException("A pass is already running.");
        if (cleared || !SpeechInBuffer) return Unchanged(); // V-6.4, V-7.8: nothing with speech left to decode, so the engine is not called
        running = true;
        try
        {
            // V-18.1/V-18.2: the cloud gets the complete recording once, after stop, including pauses. Local window/padding and VAD timestamp filtering do not apply to
            // cloud words (OpenAI returns no word times). The cap limits this in-memory recording to five minutes.
            var (start, samples) = processingOnly ? (0L, audio.ToArray()) : Window(total, padSilence: true);
            if (processingOnly)
            {
                var heard = await engine.TranscribeAsync(samples, Request(language, start, DecodeMode.Final), cancellationToken);
                if (cleared) return Unchanged();
                detectedLanguage = heard.Language;
                var next = heard.Words.Select(word => new TranscriptWord(word.Text, true)).ToList();
                var ops = TranscriptOps.Diff(words, next);
                words = next;
                return new TranscriptDelta(ops, words, 0, detectedLanguage);
            }
            var choice = await Choose(samples, cancellationToken);
            if (cleared) return Unchanged();
            // the final pass decodes in the policy's language, trusted or not: its words are the session's answer, and it settles on that language
            var hypothesis = await engine.TranscribeAsync(samples, Request(choice.Language, start, DecodeMode.Final), cancellationToken);
            if (cleared) return Unchanged();
            if (choice.Pending) settledLanguage = choice.Odds ? choice.Language : hypothesis.Language;
            return ApplyFinal(hypothesis, start, Reported(choice, hypothesis));
        }
        finally
        {
            running = false;
        }
    }

    /// <summary>Zeroes and drops the session's audio and words (V-4.7).</summary>
    public void Clear()
    {
        cleared = true;
        CollectionsMarshal.AsSpan(audio).Clear();
        audio.Clear();
        speechSpans.Clear();
        committed.Clear();
        tail = [];
        words = [];
        audioBase = total;
        bufferStart = total;
    }

    private static TimeSpan ToTime(long samples) => TimeSpan.FromTicks(samples * TimeSpan.TicksPerSecond / SampleRate);

    private TranscriptDelta Unchanged() => new([], words, committed.Count, null);

    private static long Samples(double seconds) => (long)Math.Round(seconds * SampleRate);

    private DecodeRequest Request(string requestLanguage, long start, DecodeMode mode) => new(requestLanguage, Prompt(), mode, ToTime(start));

    // The language a pass decodes in. Forced or settled: that one. Auto-detect before it settles: AutoLanguagePolicy's decision on the engine's detection (ED-NEW-voicefix-4),
    // or "auto" when the engine has no detection (then the language the decode reports is the guess).
    private readonly record struct LanguageChoice(string Language, bool Pending, bool Odds, bool Trusted);

    private async Task<LanguageChoice> Choose(float[] samples, CancellationToken cancellationToken)
    {
        if (!AutoDetect) return new(language, false, false, true);
        if (settledLanguage is { } settled) return new(settled, false, false, true);
        if (await engine.DetectLanguageAsync(samples, AutoLanguagePolicy.Hindustani, cancellationToken) is not { } odds) return new(language, true, false, true);
        var decision = AutoLanguagePolicy.Decide(odds, sessionHindustani ?? hindustaniBefore);
        // the session's own Hindustani (and its script) counts from its first trusted decision for Hindi or Urdu
        if (decision.Trusted && AutoLanguagePolicy.IsHindustani(decision.Language)) sessionHindustani ??= decision.Language;
        return new(decision.Language, true, true, decision.Trusted);
    }

    // Auto-detect: a live or long-pause pass settles the session's language on SettleSeconds of decoded speech in a language the policy trusts, or on ForceSettleSeconds in
    // the one it decided (ED-NEW-voicefix-4); without detection, on the language the decode reported (a decode that reports none is not held, as before).
    private bool Settles(LanguageChoice choice, Hypothesis hypothesis, double speech)
    {
        if (!choice.Pending || speech < SettleSeconds) return false;
        if (choice.Odds)
        {
            if (!choice.Trusted && speech < ForceSettleSeconds) return false;
            settledLanguage = choice.Language;
            return true;
        }
        if (hypothesis.Language is not { } heard) return false;
        settledLanguage = heard;
        return true;
    }

    // The language the pill names for a pass (V-12.10). With detection it is the policy's: a pass in a language the policy does not trust is never decoded before it
    // is overdue, so no untrusted guess is named while the words are held (ED-NEW-voicefix-4). Without detection, the one the engine reported.
    private string? Reported(LanguageChoice choice, Hypothesis hypothesis) => choice.Odds ? choice.Language : hypothesis.Language ?? settledLanguage;

    // A held long-pause pass: the same silence does not start another pass until it has lasted HeldPauseSeconds.
    private void Hold(TranscriberPass pass)
    {
        if (pass == TranscriberPass.LongPause && speechSpans.Count > 0) heldPauseSpeechEnd = speechSpans[^1].End;
    }

    // The VAD speech in [start, end), in seconds.
    private double SpeechSeconds(long start, long end)
    {
        long sum = 0;
        foreach (var (spanStart, spanEnd) in speechSpans)
        {
            var from = Math.Max(start, spanStart);
            var to = Math.Min(end, spanEnd);
            if (to > from) sum += to - from;
        }
        return (double)sum / SampleRate;
    }

    // V-7.3 step 2: the prompt is the last 200 characters of committed text.
    private string Prompt()
    {
        var text = string.Join(' ', committed.Select(c => c.Text));
        return text.Length <= PromptCharacters ? text : text[^PromptCharacters..];
    }

    // The audio to decode: from the buffer start to <end>, never more than the newest 30 s (V-7.4); the final pass adds 0.3 s of silence (V-7.6).
    private (long Start, float[] Samples) Window(long end, bool padSilence)
    {
        var start = bufferStart;
        var maximum = (long)Math.Round(WindowSeconds * SampleRate);
        // V-7.7: the after-stop path has no earlier committed passes. Its final decode must retain the entire recording.
        if (liveWords && end - start > maximum) start = end - maximum;
        var pad = padSilence ? (int)Math.Round(FinalSilenceSeconds * SampleRate) : 0;
        var samples = new float[(int)(end - start) + pad];
        audio.CopyTo((int)(start - audioBase), samples, 0, (int)(end - start));
        return (start, samples);
    }

    private List<(string Text, double Start, double End)> NewWords(Hypothesis hypothesis, long windowStart)
    {
        var offset = (double)windowStart / SampleRate;
        var committedEnd = committed.Count == 0 ? double.NegativeInfinity : committed[^1].End;
        var result = new List<(string Text, double Start, double End)>();
        foreach (var word in hypothesis.Words)
        {
            var end = offset + word.End;
            if (end <= committedEnd + SameWordTolerance) continue; // already committed: the 0.1 s overlap heard again (ED-292 (6))
            result.Add((word.Text, offset + word.Start, end));
        }
        return result;
    }

    private TranscriptDelta ApplyLive(Hypothesis hypothesis, long windowStart, TranscriberPass pass, long decodedEnd, bool hold, string? reported)
    {
        var fresh = NewWords(hypothesis, windowStart);
        if (hold)
        {
            // the language is not settled: every word stays tentative, and the next pass hears this audio again
            tail = fresh;
        }
        else if (pass == TranscriberPass.LongPause)
        {
            // V-7.4: a pause of 1.5 s commits every word and drops the silent audio.
            committed.AddRange(fresh);
            tail = [];
            // Only the silence goes: speech that arrived while this pass decoded (a span ending after the decoded end) stays in the buffer,
            // from its 0.1 s lead-in, for the next pass (review, unit/integ-vt3).
            var resumed = speechSpans.FindIndex(span => span.End > decodedEnd);
            bufferStart = resumed < 0 ? total : Math.Max(decodedEnd, speechSpans[resumed].Start - (long)Math.Round(OverlapSeconds * SampleRate));
            TrimFront();
        }
        else
        {
            // V-7.3 step 3: the longest common prefix of the new and the previous hypothesis is committed; the rest is the tentative tail.
            var agreed = 0;
            while (agreed < fresh.Count && agreed < tail.Count && string.Equals(fresh[agreed].Text, tail[agreed].Text, StringComparison.Ordinal)) agreed++;
            committed.AddRange(fresh.Take(agreed));
            tail = fresh.Skip(agreed).ToList();
            MoveBufferStart();
            CapUncommitted();
        }
        return Publish(reported);
    }

    // V-7.4: over 25 s uncommitted, tentative words ending more than 10 s before the newest audio are committed and the buffer is cut after the last.
    private void CapUncommitted()
    {
        if (total - bufferStart <= (long)Math.Round(BufferCapSeconds * SampleRate)) return;
        var cutoff = (double)total / SampleRate - CapAgeSeconds;
        var take = 0;
        while (take < tail.Count && tail[take].End < cutoff) take++;
        if (take == 0) return;
        committed.AddRange(tail.Take(take));
        tail = tail.Skip(take).ToList();
        MoveBufferStart();
    }

    // V-7.3 step 1: the uncommitted buffer starts 0.1 s before the end of the last committed word.
    private void MoveBufferStart()
    {
        if (committed.Count == 0) return;
        var wanted = (long)Math.Round(Math.Max(0, committed[^1].End - OverlapSeconds) * SampleRate);
        if (wanted > bufferStart) bufferStart = Math.Min(wanted, total);
        TrimFront();
    }

    private TranscriptDelta ApplyFinal(Hypothesis hypothesis, long windowStart, string? reported)
    {
        var fresh = NewWords(hypothesis, windowStart);
        // V-7.8: a word that lies wholly in VAD-silent spans (the padding included) is not a word.
        tail = fresh.Where(w => speechSpans.Any(span => w.Start < (double)span.End / SampleRate && w.End > (double)span.Start / SampleRate)).ToList();
        return Publish(reported, finalTail: true);
    }

    private TranscriptDelta Publish(string? language, bool finalTail = false)
    {
        if (language is not null) detectedLanguage = language;
        _ = finalTail;
        var wanted = committed.Select(c => new TranscriptWord(c.Text, false)).Concat(tail.Select(t => new TranscriptWord(t.Text, true))).ToList();
        var ops = TranscriptOps.Diff(words, wanted);
        words = wanted;
        return new TranscriptDelta(ops, wanted, committed.Count, language);
    }

    // Keeps memory bounded: audio and speech spans before the buffer start are not needed again.
    private void TrimFront()
    {
        var drop = bufferStart - audioBase;
        if (drop >= SampleRate * 10L || (drop > 0 && audio.Count == 0))
        {
            var count = (int)Math.Min(drop, audio.Count);
            audio.RemoveRange(0, count);
            audioBase += count;
        }
        speechSpans.RemoveAll(span => span.End <= bufferStart && span.End <= audioBase);
    }
}
