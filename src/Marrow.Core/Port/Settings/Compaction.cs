// PORT-ONLY CZ-6 (customization-SPEC CZ-3.5.2): the applied compaction of the sticky stage (`col`, 0 to 92) and whether it is easing towards a far target (see StickyStage.Scrolled).
namespace Marrow.Core.Port.Settings;

/// <param name="Value">How far the stage has folded now, in points.</param>
/// <param name="Easing">Whether a jump is being eased: the value then follows the target on frames instead of exactly.</param>
public readonly record struct Compaction(double Value, bool Easing);
