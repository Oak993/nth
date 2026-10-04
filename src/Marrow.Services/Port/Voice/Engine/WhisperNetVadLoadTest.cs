// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): the real VAD load test (V-10.1: "confirmed by a tiny load test of the VAD model on it").
using Whisper.net;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary>
/// Opens the bundled Silero VAD model through Whisper.net with the GPU asked for, runs it over one second of silence, and reports the runtime that loaded. A failure to load
/// throws. Windows-only in practice; its test is Windows-only.
/// </summary>
public sealed class WhisperNetVadLoadTest(string vadModelPath) : IVadLoadTest
{
    public RuntimeKind? Run(IReadOnlyList<RuntimeKind> order, string? libraryRoot)
    {
        WhisperNativeRuntimes.Configure(order, libraryRoot);
        using var factory = WhisperVadFactory.FromPath(vadModelPath, new WhisperFactoryOptions { UseGpu = true });
        using var processor = factory.CreateBuilder().WithUseGpu(true).WithThreads(1).Build();
        _ = processor.DetectSpeech(new float[16000]);
        return WhisperNativeRuntimes.Loaded();
    }
}
