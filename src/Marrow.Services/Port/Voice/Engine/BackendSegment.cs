// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): a recognised segment (V-7.2).
namespace Marrow.Services.Port.Voice.Engine;

/// <summary>A segment: its tokens and the language the model reported for it (null when it reports none).</summary>
public sealed record BackendSegment(IReadOnlyList<BackendToken> Tokens, string? Language);
