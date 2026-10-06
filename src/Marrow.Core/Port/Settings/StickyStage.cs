// PORT-ONLY CZ-6 (customization-SPEC CZ-3.5; OWNER OD-0089 amendment, OD-0076; DESIGN cz.js:250-268 stageTick, gen.py:468-476): the arithmetic of the Customization page's sticky stage, with no
// control in it. The page's scroll offset y gives the pinned block's place (pin and release), the applied compaction `col` (how far the stage has folded, 0 to 92) and the hairline;
// `col` gives the stage's height, the conversation text's opacity and lift. Everything is a pure function of its arguments so the controller (App) and the tests share one set of numbers.
namespace Marrow.Core.Port.Settings;

public static class StickyStage
{
    /// <summary>CZ-3.5.2: the most the stage folds: 212 down to 120.</summary>
    public const double MaxCompaction = 92;

    /// <summary>CZ-3.5.2: a step of at most this far (a wheel, touchpad or drag step) is followed exactly; further is a jump and is eased.</summary>
    public const double ExactRange = 72;

    /// <summary>CZ-3.5.2: the time constant of an eased jump, in seconds.</summary>
    public const double EaseSeconds = 0.06;

    /// <summary>An eased compaction within this of its target is taken to it (a quarter of a device pixel at 100 %; the ease is never cut short by more).</summary>
    public const double SettleDistance = 0.05;

    /// <summary>CZ-3.5.2: the conversation text fades with `1 - 1.8 k` and lifts `14 k` points, `k = col / 92`.</summary>
    public const double ChatFadeRate = 1.8, ChatLift = 14;

    /// <summary>CZ-3.5.5: the hairline shows while the offset is past this, and fades over <see cref="HairlineSeconds"/>.</summary>
    public const double HairlineOffset = 4, HairlineSeconds = 0.25;

    /// <summary>CZ-3.5.6: a control that takes keyboard focus is kept this far below the block's bottom; the scroll that does it takes <see cref="FocusSeconds"/>.</summary>
    public const double FocusGap = 8, FocusSeconds = 0.3;

    /// <summary>Layout rounds positions to device pixels, so "already there" means within this.</summary>
    public const double FocusTolerance = 0.5;

    /// <summary>The compaction the offset asks for: `min(92, y)`, never below 0.</summary>
    public static double Target(double offset) => Math.Clamp(offset, 0, MaxCompaction);

    /// <summary>CZ-3.5.1: the pinned block's visual top inside the sticky container: `clamp(y, 0, H_c - h_pin)`; <paramref name="offset"/> is the scroll offset measured from the container's top.</summary>
    public static double PinTop(double offset, double containerHeight, double blockHeight) => Math.Clamp(offset, 0, Math.Max(0, containerHeight - blockHeight));

    /// <summary>CZ-3.5.2: the block's visual height: its rest height less the compaction.</summary>
    public static double BlockHeight(double restHeight, double compaction) => restHeight - compaction;

    /// <summary>CZ-3.5.2: the stage's height, 212 at rest and 120 fully compacted.</summary>
    public static double StageHeight(double restStageHeight, double compaction) => restStageHeight - compaction;

    /// <summary>CZ-3.5.2: the conversation text's opacity, `clamp(1 - 1.8 k, 0, 1)`.</summary>
    public static double ChatOpacity(double compaction) => Math.Clamp(1 - ChatFadeRate * (compaction / MaxCompaction), 0, 1);

    /// <summary>CZ-3.5.2: the conversation text's vertical offset, `-14 k`.</summary>
    public static double ChatOffset(double compaction) => -ChatLift * (compaction / MaxCompaction);

    /// <summary>CZ-3.5.5: whether the hairline shows at this offset.</summary>
    public static bool HairlineShown(double offset) => offset > HairlineOffset;

    /// <summary>
    /// CZ-3.5.2 on a scroll: a step the compaction can follow exactly is taken at once; a jump (further than <see cref="ExactRange"/> from where the compaction is, or any step while a jump is still being eased)
    /// starts, or goes on, easing. Under reduce motion every target is taken at once.
    /// ENGINEERING DECISION: once a jump is easing it goes on to its end. The design (cz.js:259-262) tests `|target - col| <= 72` again every frame, so it eases for one frame and then snaps the last 72 points;
    /// the owner's rule (OD-0076) is that nothing cuts, and CZ-T61 asks that a 300 point jump ease with tau 0.06 s, which only the whole curve is.
    /// </summary>
    public static Compaction Scrolled(Compaction state, double offset, bool still)
    {
        var target = Target(offset);
        if (still || (!state.Easing && Math.Abs(target - state.Value) <= ExactRange)) return new Compaction(target, false);
        return new Compaction(state.Value, state.Value != target);
    }

    /// <summary>CZ-3.5.2 on a frame, `dt` seconds after the last: an easing compaction moves `(target - col) * (1 - exp(-dt / 0.06))` towards the offset's target and ends when it is there.</summary>
    public static Compaction Frame(Compaction state, double offset, double seconds, bool still)
    {
        var target = Target(offset);
        if (still) return new Compaction(target, false);
        if (!state.Easing) return state;
        var next = state.Value + (target - state.Value) * (1 - Math.Exp(-Math.Max(0, seconds) / EaseSeconds));
        return Math.Abs(target - next) < SettleDistance ? new Compaction(target, false) : new Compaction(next, true);
    }

    /// <summary>
    /// CZ-3.5.6: whether a control that has taken keyboard focus needs the page to scroll. Its top is above the block's bottom plus <see cref="FocusGap"/>, or the control is not wholly above the
    /// viewport's bottom (the spec says "below the viewport"; ENGINEERING DECISION: a control cut by the bottom edge counts, as a browser's focus scroll would). All in viewport terms.
    /// </summary>
    public static bool FocusNeedsScroll(double controlTop, double controlBottom, double blockBottom, double viewportHeight) =>
        controlTop < blockBottom + FocusGap - FocusTolerance || controlBottom > viewportHeight + FocusTolerance;

    /// <summary>
    /// CZ-3.5.6: the offset that puts a control whose top is <paramref name="controlTop"/> (in the content's coordinates, from the sticky container's top) <see cref="FocusGap"/> below the block's bottom,
    /// computed with the compaction that offset will produce: the block is `rest - min(92, y')` high while pinned, so `top - y' = rest - 92 + 8` once `y' >= 92`. Never below 0 and never past <paramref name="maxOffset"/>.
    /// A control whose top is within the block's own rest height cannot be cleared by any offset (it is under the block, pinned or not); the least offset that fully compacts the stage is used.
    /// </summary>
    public static double FocusOffset(double controlTop, double restHeight, double maxOffset)
    {
        var wanted = controlTop - (restHeight - MaxCompaction) - FocusGap;
        return Math.Clamp(Math.Max(wanted, MaxCompaction), 0, Math.Max(0, maxOffset));
    }
}
