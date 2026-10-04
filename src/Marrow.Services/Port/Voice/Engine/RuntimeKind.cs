// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): the Whisper.net native runtimes the engine chooses between (voice-SPEC V-3.2, V-7.1; voice-pins.json runtimeLibraryNames).
namespace Marrow.Services.Port.Voice.Engine;

/// <summary>A whisper.cpp native runtime: the downloaded CUDA 12 folder, the shipped Vulkan runtime, the shipped CPU runtime.</summary>
public enum RuntimeKind { Cuda12, Vulkan, Cpu }
