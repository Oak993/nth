using Marrow.Testing;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (adversarial, unit jewel-knob, scoped review round 9 on cbf4b9c0e): each test states what the unit's tests must do
// and failed on the head reviewed; each passes from the round 9 answer, its body unchanged, kept under a plain name (the
// test it names is now EffortSliderJewelWiringTests.Under_reduce_motion_turning_fast_mode_off_drops_the_signal_at_once_and_the_knob_stays_as_lit_as_the_bolt_still_fading).
public sealed class EffortSliderJewelReviewR9Tests
{
    [Fact]
    [Trait("Rule", "R-TEST-1")] // a test's name says only what it proves
    public void A_test_that_pins_the_knob_as_lit_as_the_fading_bolt_names_the_reduce_motion_it_holds_under()
    {
        // ED-252 round 8, finding (5): turning fast mode off drops the knob's signal at once. The test that pins it,
        // EffortSliderJewelWiringTests.Turning_fast_mode_off_drops_the_signal_at_once_and_the_knob_stays_as_lit_as_the_bolt_still_fading,
        // builds its rig with reduceMotion: true (the bolt read at the strike's still moment, so "as lit as the bolt" is its
        // opacity times charge 1 and the still flare), but its name and its Decision line state the knob stays as lit as
        // the fading bolt without that condition. Without reduce motion it does not: the knob does not read a strike inside
        // the 0.3 s fade (pinned by A_strike_inside_the_fade_out_of_fast_mode_does_not_charge_or_flare_the_knob, as ED-252
        // decides), so the bolt flares on screen while the knob's flare only falls. Measured on cbf4b9c0e (a scratch probe,
        // the same rig without reduce motion, fast mode turned off 0.1 s before a strike, the bolt's reading taken through
        // KnobSignal at its own clock and opacity): frame 6 of the fade, the bolt's flare 0.3811 at opacity 0.381, the knob's
        // 0.3183. The name must carry the condition it is proven under (ED-252 itself says "pinned under reduce motion").
        var named = Traceability.TestMethods(typeof(EffortSliderJewelReviewR9Tests).Assembly)
            .Where(m => m.DeclaringType == typeof(EffortSliderJewelWiringTests) && m.Name.Contains("as_lit_as_the_bolt", StringComparison.Ordinal))
            .ToList();
        var unqualified = named.Where(m => !m.Name.StartsWith("Under_reduce_motion", StringComparison.Ordinal)).Select(m => m.Name).ToList();
        Assert.True(unqualified.Count == 0, "claims the knob stays as lit as the fading bolt in general, proven under reduce motion only: " + string.Join(", ", unqualified));
    }
}
