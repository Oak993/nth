// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): the settings of one pass (V-7.1).
namespace Marrow.Services.Port.Voice.Engine;

/// <summary>
/// One pass: the language code (null = detect), the initial prompt, the sampling strategy with its width, the threads, the temperature (0, with the library's default
/// fallback left alone) and token timestamps (on, V-7.2).
/// </summary>
public sealed record BackendDecode(string? Language, string Prompt, BackendSampling Sampling, int BestOf, int BeamSize, int Threads, float Temperature, bool TokenTimestamps);
