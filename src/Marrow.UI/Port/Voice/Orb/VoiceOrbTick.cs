// PORT-ONLY, T33: one immutable sample from the injected application frame source.
using Marrow.Core.Port.Voice.Orb;
namespace Marrow.UI.Port.Voice.Orb;

internal sealed record VoiceOrbTick(OrbSceneInput Input, double At, double Dpr);
