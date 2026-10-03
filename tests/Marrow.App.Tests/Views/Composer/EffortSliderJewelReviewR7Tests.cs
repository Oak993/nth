using Marrow.App.Views.Composer;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (adversarial, unit jewel-knob, review round 7 on a45e0fd9b): each test states what the unit's tests must do and
// fails on the head reviewed.
public sealed class EffortSliderJewelReviewR7Tests
{
    [Fact]
    [Trait("Rule", "R-TEST-1")] // a test that carries a Req trait proves every clause of that record
    public void REVIEW_no_test_credits_a_record_whose_42_point_height_OD_0090_made_48()
    {
        // OD-0090 made EffortSlider.ThumbSize 48 (ED-252: EffortSlider.swift:713 thumbSize 42, now 48; the control's height is
        // the knob's). Two of the source's uses of that constant are records of their own, each stating 42 points:
        // :620-624 the single-level note, .frame(height: EffortSlider.thumbSize): PR-COMPOSER-117 "occupying exactly 42 points
        // of height", UI-COMPOSER-036 "Occupies exactly 42 points of height in place of the slider"; and :820
        // .frame(height: Self.thumbSize): PR-COMPOSER-126 "The whole control reserves 42 points of height". Round 6 took
        // UI-COMPOSER-035 off EffortSliderSweepTests.Drag_clamps_and_snaps_to_each_stop_and_preserves_track_and_knob_sizes
        // because its 42 point geometry is superseded and the test asserts 48 px; the unit's own commit efb32a9df changed
        // EffortSliderSweepTests.Empty_levels_show_standard_and_the_single_level_note to assert the note 48 tall ("OD-0090: the
        // thumb size the note takes is the 48 px knob") and left it crediting PR-COMPOSER-117 and UI-COMPOSER-036, and
        // EffortSliderSweepTests.Accessibility_adjustments_clamp_at_the_extremes still credits PR-COMPOSER-126 (neither its
        // 42 point height, nor its pair label, nor its "Standard" value is asserted). No test can prove those clauses until
        // the lead amends the records for OD-0090 at merge; ED-252's deviation list does not name :620-624 either.
        Assert.Equal(48, EffortSlider.ThumbSize); // the premise: the note and the control take the 48 px knob's height
        string[] superseded = ["PR-COMPOSER-117", "UI-COMPOSER-036", "PR-COMPOSER-126"];
        var credited = Traceability.TestMethods(typeof(EffortSliderJewelReviewR7Tests).Assembly)
            .SelectMany(m => Traceability.Traits(m).Where(t => t.Name == "Req" && superseded.Contains(t.Value))
                .Select(t => $"{m.DeclaringType!.Name}.{m.Name} ({t.Value})"))
            .ToList();
        Assert.True(credited.Count == 0, "credited to a record whose 42 point height OD-0090 made 48: " + string.Join(", ", credited));
    }
}
