// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): the Whisper.net backend (V-7.1).
using Whisper.net;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary>
/// <see cref="IWhisperBackend"/> over Whisper.net 1.9.1: sets the runtime options, then <c>WhisperFactory.FromPath</c> with the GPU on when asked. The native load runs on the
/// thread pool (the inference thread of V-2.2 is the engine's one-at-a-time lock). Its tests are Windows-only.
/// </summary>
public sealed class WhisperNetBackend : IWhisperBackend
{
    /// <summary>The Whisper.net members this backend and the VAD load test use, in the names of voice-pins.json whisperNet.apiMembers (V-T2).</summary>
    public static IReadOnlyList<string> UsedMembers { get; } =
    [
        "WhisperFactory.FromPath(String,WhisperFactoryOptions)", "WhisperFactoryOptions.UseGpu", "WhisperFactory.CreateBuilder()", "WhisperFactory.Dispose()",
        "WhisperProcessorBuilder.Build()", "WhisperProcessorBuilder.WithLanguage(String)", "WhisperProcessorBuilder.WithLanguageDetection()", "WhisperProcessorBuilder.WithThreads(Int32)",
        "WhisperProcessorBuilder.WithGreedySamplingStrategy(Action`1)", "WhisperProcessorBuilder.WithBeamSearchSamplingStrategy(Action`1)", "WhisperProcessorBuilder.WithPrompt(String)",
        "WhisperProcessorBuilder.WithTokenTimestamps()", "WhisperProcessorBuilder.WithTemperature(Single)", "GreedySamplingStrategyBuilder.WithBestOf(Int32)",
        "BeamSearchSamplingStrategyBuilder.WithBeamSize(Int32)", "WhisperProcessor.ProcessAsync(ReadOnlyMemory`1,CancellationToken)", "WhisperProcessor.Dispose()",
        "WhisperProcessor.DetectLanguageWithProbability(ReadOnlySpan`1,ReadOnlySpan`1)",
        "SegmentData.Text", "SegmentData.Language", "SegmentData.Tokens", "WhisperToken.Text", "WhisperToken.Start", "WhisperToken.End",
        "WhisperVadFactory.FromPath(String,WhisperFactoryOptions)", "WhisperVadFactory.CreateBuilder()", "WhisperVadFactory.Dispose()", "WhisperVadProcessorBuilder.Build()",
        "WhisperVadProcessorBuilder.WithThreads(Int32)", "WhisperVadProcessor.DetectSpeech(ReadOnlySpan`1)", "WhisperVadProcessor.Dispose()",
        "LibraryLoader.RuntimeOptions.LibraryPath", "LibraryLoader.RuntimeOptions.LoadedLibrary", "LibraryLoader.RuntimeOptions.RuntimeLibraryOrder",
    ];

    public Task<IWhisperModel> LoadAsync(BackendLoad load, CancellationToken cancellationToken) =>
        Task.Run<IWhisperModel>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Whisper.net 1.9.1 makes a factory over a missing file without a word (the failure comes at the first pass), so the file is checked here (VT-6 Windows review)
            if (!File.Exists(load.ModelPath)) throw new FileNotFoundException("The model file is not there.", load.ModelPath);
            WhisperNativeRuntimes.Configure(load.RuntimeOrder, load.LibraryRoot);
            var factory = WhisperFactory.FromPath(load.ModelPath, new WhisperFactoryOptions { UseGpu = load.UseGpu });
            try
            {
                return new WhisperNetModel(factory, WhisperNativeRuntimes.Loaded());
            }
            catch
            {
                factory.Dispose();
                throw;
            }
        }, cancellationToken);
}
