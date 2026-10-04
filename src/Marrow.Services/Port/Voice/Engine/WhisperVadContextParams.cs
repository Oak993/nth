// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): whisper.cpp's VAD context parameters (V-6.1).
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary>
/// <c>struct whisper_vad_context_params { int n_threads; bool use_gpu; int gpu_device; }</c> of whisper.h, passed by value to
/// <c>whisper_vad_init_from_file_with_params</c>. UNVERIFIED layout until the voice-evidence tool has run on Windows: <c>whisper_vad_default_context_params</c> is not among the
/// five exports VT-0 recorded, so the struct is filled here.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct WhisperVadContextParams
{
    public int Threads;
    public byte UseGpu;
    public int GpuDevice;
}
