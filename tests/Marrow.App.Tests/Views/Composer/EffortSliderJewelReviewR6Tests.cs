using Marrow.App.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (adversarial, unit jewel-knob, review round 6 on ebb2bfc58): each test states what the unit's code must do and
// fails on the head reviewed. ED-252 says the knob's frame gate is the locked page's frameGate, "every refresh at 60 to 75
// Hz, every 4th at 240 Hz" (strike-engine.js:56-66, OD-0090's locked page). The page's gate measures the refresh r (the
// smallest recent frame gap), draws every Nth refresh with N = max(1, floor(1000 / 60 / r + 0.05)), and so paces its
// frames evenly on the display's refresh: at 75 Hz every refresh (75 a second), at 144 Hz every 2nd (72 a second), at
// 239 Hz every 4th. The port's gate is a fixed 1/60 s due time with 1 ms slack (EffortSlider.cs OnFrame), which caps the
// knob at 60 a second and paces it unevenly whenever the refresh is not a divisor of 60 Hz.
[Collection("Headless")]
public sealed class EffortSliderJewelReviewR6Tests(HeadlessSession headless)
{
    // the frames (counted from the first after the start) at which the knob steps
    private static List<int> StepFrames(JewelRig rig, double hertz, int frames)
    {
        rig.Frame();
        var steps = new List<int>();
        var last = rig.Slider.Orb.Phase;
        for (var i = 1; i <= frames; i++)
        {
            rig.Frame(1.0 / hertz);
            if (rig.Slider.Orb.Phase != last) { steps.Add(i); last = rig.Slider.Orb.Phase; }
        }
        return steps;
    }

    [Fact]
    [Trait("Decision", "ED-252")] // strike-engine.js:56 "60 to 75 Hz: every refresh", :63-64 (r = 13.33 ms: N = 1, draw when 6.67 ms have passed)
    public void REVIEW_on_a_seventy_five_hertz_display_the_knob_steps_at_every_frame() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        var steps = StepFrames(rig, 75, 75);
        Assert.True(steps.Count == 75, $"one second of 75 Hz frames: the page draws at all 75, the port stepped at {steps.Count}");
    });

    [Fact]
    [Trait("Decision", "ED-252")] // strike-engine.js:56 "120 to 165 Hz: every 2nd", :63-64 (r = 6.94 ms: N = 2, draw when 10.4 ms have passed)
    public void REVIEW_on_a_144_hertz_display_the_knob_steps_on_every_second_frame() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        var steps = StepFrames(rig, 144, 144);
        var gaps = steps.Zip(steps.Skip(1), (a, b) => b - a).Distinct().OrderBy(g => g).ToList();
        Assert.True(steps.Count == 72 && gaps.SequenceEqual([2]),
            $"one second of 144 Hz frames: the page draws every 2nd (72, evenly); the port stepped {steps.Count} times, {string.Join(" or ", gaps)} frames apart");
    });

    [Fact]
    [Trait("Decision", "ED-252")] // strike-engine.js:63-64 (r = 4.184 ms: N = 4, draw when 14.6 ms have passed); the owner's display runs at 239 Hz (ED-252's native note)
    public void REVIEW_on_a_239_hertz_display_the_knob_steps_on_every_fourth_frame_and_never_sooner() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        var steps = StepFrames(rig, 239, 239 * 4);
        var gaps = steps.Zip(steps.Skip(1), (a, b) => b - a).Distinct().OrderBy(g => g).ToList();
        Assert.True(gaps.SequenceEqual([4]), $"four seconds of 239 Hz frames: the page draws every 4th frame; the port's steps came {string.Join(" or ", gaps)} frames apart");
    });
}
