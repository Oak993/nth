// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): a Whisper.net model (V-7.1, V-7.2).
using Marrow.Core.Port.Voice.Seams;
using Whisper.net;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary>An <see cref="IWhisperModel"/> over a <c>WhisperFactory</c>. Each pass builds its own processor with the pass's settings and disposes it.</summary>
public sealed class WhisperNetModel(WhisperFactory factory, RuntimeKind runtime) : IWhisperModel
{
    public RuntimeKind Runtime { get; } = runtime;

    public async Task<IReadOnlyList<BackendSegment>> DecodeAsync(ReadOnlyMemory<float> audio, BackendDecode decode, CancellationToken cancellationToken)
    {
        var builder = factory.CreateBuilder().WithThreads(decode.Threads).WithTokenTimestamps().WithTemperature(decode.Temperature);
        builder = decode.Language is null ? builder.WithLanguageDetection() : builder.WithLanguage(decode.Language);
        if (decode.Prompt.Length > 0) builder = builder.WithPrompt(decode.Prompt);
        builder = decode.Sampling == BackendSampling.Greedy
            ? builder.WithGreedySamplingStrategy(g => g.WithBestOf(decode.BestOf))
            : builder.WithBeamSearchSamplingStrategy(b => b.WithBeamSize(decode.BeamSize));
        await using var processor = builder.Build();
        var segments = new List<BackendSegment>();
        await foreach (var segment in processor.ProcessAsync(audio, cancellationToken))
            // ED-NEW-voicefix-2: a character split across tokens comes out of each token as U+FFFD; the segment's text is decoded whole and mends them
            segments.Add(new BackendSegment(WhisperTokenText.Repair([.. segment.Tokens.Select(ToToken)], segment.Text), segment.Language));
        return segments;
    }

    // ED-NEW-voicefix-4: Whisper.net 1.9.1's WhisperProcessor.DetectLanguageWithProbability runs whisper_lang_auto_detect_with_state on the audio's first 30 s and returns the
    // top language (or, with candidates, the most likely of them) and its probability; null and 0 when whisper.cpp fails. One call for the top, a second for the runners-up
    // only when the top is not one of them. The native calls are synchronous, so they run on the thread pool; the token is checked before each.
    public Task<LanguageOdds?> DetectLanguageAsync(ReadOnlyMemory<float> audio, IReadOnlyList<string> runnersUp, int threads, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var processor = factory.CreateBuilder().WithThreads(threads).Build();
            var (top, probability) = processor.DetectLanguageWithProbability(audio.Span, ReadOnlySpan<string>.Empty);
            if (string.IsNullOrEmpty(top)) return null;
            if (runnersUp.Count == 0) return new LanguageOdds(top, probability);
            if (runnersUp.Contains(top, StringComparer.Ordinal)) return new LanguageOdds(top, probability, top, probability);
            cancellationToken.ThrowIfCancellationRequested();
            var (runnerUp, runnerUpProbability) = processor.DetectLanguageWithProbability(audio.Span, [.. runnersUp]);
            // with no candidate it knows, Whisper.net answers the top again: that is no runner-up
            return runnerUp is not null && runnersUp.Contains(runnerUp, StringComparer.Ordinal)
                ? new LanguageOdds(top, probability, runnerUp, runnerUpProbability)
                : new LanguageOdds(top, probability);
        }, cancellationToken);

    public void Dispose() => factory.Dispose();

    // WhisperToken.Start and End are whisper.cpp's t0 and t1: 10 ms units (confirmed on the Windows run: 22 words between 0.23 s and 10.01 s for a 10.02 s clip). A special token's text is "[_BEG_]", "[_TT_n]" or "<|...|>".
    public static BackendToken ToToken(WhisperToken token)
    {
        var text = token.Text ?? "";
        return new BackendToken(text, token.Start * 0.01, token.End * 0.01,
            (text.StartsWith("[_", StringComparison.Ordinal) && text.EndsWith(']')) || text.StartsWith("<|", StringComparison.Ordinal));
    }
}
