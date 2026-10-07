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

    public StreamingTranscriber(ISpeechEngine engine, string language, bool liveWords = true, bool processingOnly = false)
    {
        this.engine = engine;
        this.language = language;
        this.liveWords = liveWords;
        this.processingOnly = processingOnly;
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
            if (silenceRun >= (long)Math.Round(LongPauseSeconds * SampleRate)) return TranscriberPass.LongPause;
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
            var hypothesis = await engine.TranscribeAsync(samples, Request(start, DecodeMode.Live), cancellationToken);
            if (cleared) return Unchanged();
            return ApplyLive(hypothesis, start, pass, end);
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
            var hypothesis = await engine.TranscribeAsync(samples, Request(start, DecodeMode.Final), cancellationToken);
            if (cleared) return Unchanged();
            if (processingOnly)
            {
                detectedLanguage = hypothesis.Language;
                var next = hypothesis.Words.Select(word => new TranscriptWord(word.Text, true)).ToList();
                var ops = TranscriptOps.Diff(words, next);
                words = next;
                return new TranscriptDelta(ops, words, 0, detectedLanguage);
            }
            return ApplyFinal(hypothesis, start);
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

    private DecodeRequest Request(long start, DecodeMode mode) => new(language, Prompt(), mode, ToTime(start));

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
        if (hypothesis.Language is { } detected) detectedLanguage = detected;
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

    private TranscriptDelta ApplyLive(Hypothesis hypothesis, long windowStart, TranscriberPass pass, long decodedEnd)
    {
        var fresh = NewWords(hypothesis, windowStart);
        if (pass == TranscriberPass.LongPause)
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
        return Publish(hypothesis.Language);
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

    private TranscriptDelta ApplyFinal(Hypothesis hypothesis, long windowStart)
    {
        var fresh = NewWords(hypothesis, windowStart);
        // V-7.8: a word that lies wholly in VAD-silent spans (the padding included) is not a word.
        tail = fresh.Where(w => speechSpans.Any(span => w.Start < (double)span.End / SampleRate && w.End > (double)span.Start / SampleRate)).ToList();
        return Publish(hypothesis.Language, finalTail: true);
    }

    private TranscriptDelta Publish(string? language, bool finalTail = false)
    {
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
