// PORT-ONLY, T33: one immutable sample from the injected application frame source.
namespace Marrow.UI.Port.Voice.Glow;

internal sealed record VoiceGlowTick(VoiceGlowInput Input, double At);
