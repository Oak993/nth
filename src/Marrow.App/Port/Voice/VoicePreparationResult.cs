// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-15, voice-SPEC V-10.3, V-10.7): the result of the install and speed test.
using Marrow.Core.Port.Voice;

namespace Marrow.App.Port.Voice;

/// <summary>
/// <see cref="Record"/> is what was stored (null unless Prepared). <see cref="AskSlowQuestion"/> is true when the processor was too slow for live words and the question of
/// V-7.7 (Q-V-1 C) is still to be asked (Welcome's slow states, WV-1). <see cref="Error"/> says why a run failed.
/// </summary>
public sealed record VoicePreparationResult(VoicePreparationStatus Status, VoiceHardwareRecord? Record, bool AskSlowQuestion, string? Error);
