// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-8, voice-SPEC V-11): what the render thread needs for one orb, as one immutable value (V-11.3).
using Marrow.Core.Port.Voice.Orb;

namespace Marrow.UI.Port.Voice.Orb;

/// <summary>The scene's input, plus the clock the view's frames are timed with.</summary>
public sealed record VoiceOrbSnapshot(OrbSceneInput Input, TimeProvider Clock, double? ManualOrigin = null);
