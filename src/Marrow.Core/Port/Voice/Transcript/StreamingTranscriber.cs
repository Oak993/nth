// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-3): live recognition (voice-SPEC V-7.3, V-7.4, V-7.6, V-7.8).
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Core.Port.Voice.Transcript;

/// <summary>Why a live pass runs.</summary>
public enum TranscriberPass { Live, LongPause }

/// <summary>What a pass changed: the operations, the words now held (committed first), how many are committed, the language detected.</summary>
public sealed record TranscriptDelta(IReadOnlyList<WordOp> Ops, IReadOnlyList<TranscriptWord> Words, int Committed, string? Language);

/// <summary>LocalAgreement-2 over a sliding window of the session audio.</summary>
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

    public StreamingTranscriber(ISpeechEngine engine, string language) => throw new NotImplementedException();

    public TimeSpan AudioLength => throw new NotImplementedException();

    public TimeSpan UncommittedLength => throw new NotImplementedException();

    public bool SawSpeech => throw new NotImplementedException();

    public bool IsPassRunning => throw new NotImplementedException();

    public IReadOnlyList<TranscriptWord> Words => throw new NotImplementedException();

    public int CommittedCount => throw new NotImplementedException();

    public string? DetectedLanguage => throw new NotImplementedException();

    /// <summary>Adds audio at 16 kHz; <paramref name="speech"/> is the VAD's speech state (V-6.2) for all of it.</summary>
    public void Append(ReadOnlySpan<float> samples, bool speech) => throw new NotImplementedException();

    /// <summary>The pass that may start now, if any (V-7.3 gating, V-7.4 long pause).</summary>
    public TranscriberPass? DuePass => throw new NotImplementedException();

    public Task<TranscriptDelta> RunPass(TranscriberPass pass, CancellationToken cancellationToken) => throw new NotImplementedException();

    /// <summary>Zeroes and drops the session's audio and words (V-4.7).</summary>
    public void Clear() => throw new NotImplementedException();

    /// <summary>The final pass (V-7.6): the uncommitted buffer plus 0.3 s of silence, decoded with the final parameters.</summary>
    public Task<TranscriptDelta> RunFinal(CancellationToken cancellationToken) => throw new NotImplementedException();
}
