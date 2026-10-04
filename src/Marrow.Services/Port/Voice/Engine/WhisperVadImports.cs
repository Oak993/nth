// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): the five whisper.cpp VAD exports (V-6.1; voice-pins.json whisperNet.nativeVadExports).
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary>P/Invoke declarations of the five exports. The library name "whisper" is resolved by <see cref="WhisperVadNative"/> to the whisper.dll it was asked to use.</summary>
public static class WhisperVadImports
{
    /// <summary>The library name the imports use; resolved by <see cref="WhisperVadNative"/>.</summary>
    public const string Library = "whisper";

    /// <summary>The five export names (V-6.1).</summary>
    public static IReadOnlyList<string> Exports { get; } =
    [
        "whisper_vad_init_from_file_with_params", "whisper_vad_detect_speech", "whisper_vad_n_probs", "whisper_vad_probs", "whisper_vad_free",
    ];

    [DllImport(Library, EntryPoint = "whisper_vad_init_from_file_with_params", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint InitFromFileWithParams(nint pathUtf8, WhisperVadContextParams parameters);

    [DllImport(Library, EntryPoint = "whisper_vad_detect_speech", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte DetectSpeech(nint context, float[] samples, int sampleCount);

    [DllImport(Library, EntryPoint = "whisper_vad_n_probs", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ProbabilityCount(nint context);

    [DllImport(Library, EntryPoint = "whisper_vad_probs", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint Probabilities(nint context);

    [DllImport(Library, EntryPoint = "whisper_vad_free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Free(nint context);
}
