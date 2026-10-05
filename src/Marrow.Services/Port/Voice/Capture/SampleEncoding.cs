// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): the sample encodings a capture endpoint's mix format can have (voice-SPEC V-4.2).
namespace Marrow.Services.Port.Voice.Capture;

/// <summary>How one sample sits in a device buffer: float32 or little-endian signed PCM of 16, 24 or 32 bits (V-4.2).</summary>
public enum SampleEncoding { Float32, Pcm16, Pcm24, Pcm32 }
