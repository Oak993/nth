using Marrow.Testing;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (adversarial, unit jewel-knob, review round 5 on 822263064): each test states what the unit's tests must do and
// failed on the head reviewed; it passes from 76dbfda5c (round 5 answered), kept under a plain name.
public sealed class EffortSliderJewelReviewR5Tests
{
    [Fact]
    [Trait("Rule", "R-TEST-1")] // a test that verifies a requirement carries that requirement's Req trait, not another's
    public void No_jewel_knob_test_credits_the_track_and_knob_requirement_with_focus_or_the_chosen_stop_alone()
    {
        // PR-COMPOSER-123 is the track and the knob as drawn (EffortSlider.swift:712-713, 741-775, 781-796: the 34 point
        // capsule, stop centres a knob radius in, the fill to the knob's far edge, the passed and unpassed stop colours, the
        // knob and its 1.06 scale while dragged). Round 4 took PR-COMPOSER-116 off the knob tests that do not test it; two
        // knob tests the unit added still carry Req PR-COMPOSER-123 and assert none of its clauses:
        // A_pointer_press_and_drag_show_no_focus_ring asserts focus only (cited :826-831, the accessibility element of
        // PR-COMPOSER-126, and ED-252's no focus ring), and The_effort_changes_where_the_knob_passes_the_midpoint_between_two_stops
        // asserts the chosen index only (cited :808-813, 833-836, nearestStop of PR-COMPOSER-125, moved to the knob's
        // midpoints by OD-0090). req_verify credits PR-COMPOSER-123 with two tests that cannot fail on it.
        string[] outside = ["A_pointer_press_and_drag_show_no_focus_ring", "The_effort_changes_where_the_knob_passes_the_midpoint_between_two_stops"];
        var tests = Traceability.TestMethods(typeof(EffortSliderJewelReviewR5Tests).Assembly)
            .Where(m => m.DeclaringType == typeof(EffortSliderJewelKnobTests) && outside.Contains(m.Name)).ToList();
        Assert.True(tests.Count == outside.Length, "precondition: the two knob tests are in EffortSliderJewelKnobTests under these names");
        var credited = tests.Where(m => Traceability.Traits(m).Contains(("Req", "PR-COMPOSER-123")))
            .Select(m => m.DeclaringType!.Name + "." + m.Name).ToList();
        Assert.True(credited.Count == 0, "credited to PR-COMPOSER-123 (the track and the knob as drawn) without testing it: " + string.Join(", ", credited));
    }
}
