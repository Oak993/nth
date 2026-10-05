// PORT-ONLY, owner-ruled addition (OD-0106 CUDA A, OD-0099 Q-V-9 C, OD-0104; first-release voice typing, VT-17): the sizes of NVIDIA's two archives.
namespace Marrow.App.Port.Voice.Cuda;

/// <summary>voice-pins.json "cuda.nvidiaCompanions": libcublas 12.4.2.65 (391,101,865 bytes) and cuda_cudart 12.4.99 (2,474,121 bytes).</summary>
public static class CudaCompanionPins
{
    public const long Bytes = 391_101_865L + 2_474_121L;
}
