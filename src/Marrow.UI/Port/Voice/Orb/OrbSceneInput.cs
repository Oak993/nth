// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-8, voice-SPEC V-11): what the host tells the orb scene each frame (V-11.4, V-11.5).
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;

namespace Marrow.UI.Port.Voice.Orb;

/// <summary>The chosen orb, the motion state (<see cref="OrbMotionInput"/>'s members) and the theme.</summary>
public readonly record struct OrbSceneInput(
    VoiceOrbKind Kind,
    OrbMotionMode Mode,
    bool Hovered,
    bool Pressed,
    OrbBands Bands,
    double SlotDiameter,
    bool Dark,
    bool ReduceMotion,
    double ViewSize = OrbScene.ViewPx,
    double? TileGlow = null);
