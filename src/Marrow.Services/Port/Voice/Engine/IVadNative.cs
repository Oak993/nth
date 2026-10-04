// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): the whisper.cpp VAD under SileroVad (V-6.1).
namespace Marrow.Services.Port.Voice.Engine;

/// <summary>One whisper.cpp VAD context: the speech probability of every 512-sample frame of the samples given. Production: <see cref="WhisperVadNative"/>.</summary>
public interface IVadNative : IDisposable
{
    /// <summary>One probability per 512-sample frame of <paramref name="samples"/>, in order. The native call starts from a fresh state each time.</summary>
    float[] Detect(ReadOnlySpan<float> samples);
}
