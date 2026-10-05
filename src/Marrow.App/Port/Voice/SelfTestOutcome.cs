// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-1 C; first-release voice typing, VT-14): what a first-run style self-test decided (voice-SPEC V-10.3).
namespace Marrow.App.Port.Voice;

/// <summary>The model to keep after the self-test (it may be a rung below the candidate), the real-time factor measured for it, and whether live words are on (V-7.7).</summary>
internal sealed record SelfTestOutcome(string ModelId, double Rtf, bool LiveWords);
