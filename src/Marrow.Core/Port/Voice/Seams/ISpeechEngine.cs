// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-3): the recognition seam (voice-SPEC V-2.4, V-7.1, V-18.1).
namespace Marrow.Core.Port.Voice.Seams;

/// <summary>
/// The model to load: a catalogue id and, for a local engine, the model store's verified path (V-7.1). The cloud engine has no file
/// (<see cref="Path"/> null).
/// </summary>
public sealed record SpeechModel(string Id, string? Path);

/// <summary>Load options. A null member leaves the choice to the engine (GPU when a GPU runtime loaded; threads per V-2.2).</summary>
public sealed record EngineLoadOptions(bool? UseGpu = null, int? Threads = null);

/// <summary>How a pass decodes (V-7.1): live passes are greedy; the final pass is a beam search.</summary>
public enum DecodeMode { Live, Final }

/// <summary>
/// One decode. <see cref="Language"/> is a Whisper code or "auto" (V-7.13); <see cref="Prompt"/> is the last 200 characters of
/// committed text (V-7.3). <see cref="AudioStart"/> is the session time of the first sample passed: a real engine ignores it, a
/// scripted engine uses it to answer by session time while the window slides.
/// </summary>
public sealed record DecodeRequest(string Language, string Prompt, DecodeMode Mode, TimeSpan AudioStart);

/// <summary>A recognised word. <see cref="Start"/> and <see cref="End"/> are seconds from the first sample of the decoded audio (V-7.2).</summary>
public readonly record struct HypothesisWord(string Text, double Start, double End);

/// <summary>An engine's answer: words in order, and the language the engine detected when it reports one (V-12.10).</summary>
public sealed record Hypothesis(IReadOnlyList<HypothesisWord> Words, string? Language = null)
{
    public static Hypothesis Empty { get; } = new([]);
}

/// <summary>
/// What an engine's language detection heard (ED-NEW-voicefix-4): the most likely of all its languages with its probability, and the most likely of the runners-up it
/// was asked about with that one's probability (the top itself when the top is one of them; null when none of them could be measured).
/// </summary>
public sealed record LanguageOdds(string Top, float TopProbability, string? RunnerUp = null, float RunnerUpProbability = 0f);

/// <summary>
/// The recogniser (V-2.4). Production: <c>WhisperNetEngine</c> (VT-6) and <c>OpenAiTranscriber</c> (VT-16). Tests: <see cref="ScriptedEngine"/>.
/// Both calls take a token that aborts the work in flight (V-7.10).
/// </summary>
public interface ISpeechEngine
{
    Task LoadAsync(SpeechModel model, EngineLoadOptions options, CancellationToken cancellationToken);

    Task<Hypothesis> TranscribeAsync(ReadOnlyMemory<float> audio16k, DecodeRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Detects the language of <paramref name="audio16k"/> without decoding it (ED-NEW-voicefix-4): the top language and the most likely of <paramref name="runnersUp"/>,
    /// with their probabilities. Null when the engine has no detection (the cloud engine) or the detection failed; the caller then decodes with "auto".
    /// </summary>
    Task<LanguageOdds?> DetectLanguageAsync(ReadOnlyMemory<float> audio16k, IReadOnlyList<string> runnersUp, CancellationToken cancellationToken);
}
