// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13): what the render thread needs for one glow, as one immutable value.
namespace Marrow.UI.Port.Voice.Glow;

/// <summary>The scene's input, plus the clock the view's frames are timed with.</summary>
public sealed record VoiceGlowSnapshot(VoiceGlowInput Input, TimeProvider Clock, double? ManualOrigin = null);
