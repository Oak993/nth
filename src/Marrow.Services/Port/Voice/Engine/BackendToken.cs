// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): a recognised token (V-7.2).
namespace Marrow.Services.Port.Voice.Engine;

/// <summary>A token with its text (a leading space starts a word), its start and end in seconds from the first decoded sample, and whether it is a special token (not text).</summary>
public readonly record struct BackendToken(string Text, double Start, double End, bool IsSpecial);
