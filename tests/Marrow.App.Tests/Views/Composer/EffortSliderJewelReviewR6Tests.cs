using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (adversarial, unit jewel-knob, review round 6 on ebb2bfc58): each test states what the unit's code must do and
// failed on the head reviewed; they pass from 111a8a675 (round 6 answered), kept under plain names. The frame gate: ED-252 says the knob's frame gate is the locked page's frameGate, "every refresh at 60 to 75
// Hz, every 4th at 240 Hz" (strike-engine.js:56-66, OD-0090's locked page). The page's gate measures the refresh r (the
// smallest recent frame gap), draws every nth refresh with N = max(1, floor(1000 / 60 / r + 0.05)), and so paces its
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
    public void On_a_seventy_five_hertz_display_the_knob_steps_at_every_frame() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        var steps = StepFrames(rig, 75, 75);
        Assert.True(steps.Count == 75, $"one second of 75 Hz frames: the page draws at all 75, the port stepped at {steps.Count}");
    });

    [Fact]
    [Trait("Decision", "ED-252")] // strike-engine.js:56 "120 to 165 Hz: every 2nd", :63-64 (r = 6.94 ms: N = 2, draw when 10.4 ms have passed)
    public void On_a_144_hertz_display_the_knob_steps_on_every_second_frame() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        var steps = StepFrames(rig, 144, 144);
        var gaps = steps.Zip(steps.Skip(1), (a, b) => b - a).Distinct().OrderBy(g => g).ToList();
        Assert.True(steps.Count == 72 && gaps.SequenceEqual([2]),
            $"one second of 144 Hz frames: the page draws every 2nd (72, evenly); the port stepped {steps.Count} times, {string.Join(" or ", gaps)} frames apart");
    });

    [Fact]
    [Trait("Decision", "ED-252")] // strike-engine.js:63-64 (r = 4.184 ms: N = 4, draw when 14.6 ms have passed); the owner's display runs at 239 Hz (ED-252's native note)
    public void On_a_239_hertz_display_the_knob_steps_on_every_fourth_frame_and_never_sooner() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        var steps = StepFrames(rig, 239, 239 * 4);
        var gaps = steps.Zip(steps.Skip(1), (a, b) => b - a).Distinct().OrderBy(g => g).ToList();
        Assert.True(gaps.SequenceEqual([4]), $"four seconds of 239 Hz frames: the page draws every 4th frame; the port's steps came {string.Join(" or ", gaps)} frames apart");
    });

    // The page's tick() moves the springs by at most 50 ms a frame (jewel-knob.html:813, dt = min(0.05, max(0, t - sliderT))):
    // after a long frame (a stall, a compile) the knob goes on from where it was. The port passes the whole gap to the springs
    // (EffortSlider.cs OnFrame: dt = now - lastFrame, uncapped), so after a 0.2 s frame the knob has jumped most of the way.
    // Expected: the page's own spring() and stepSlider() run by node on this press (scratchpad dtcap-r6.mjs): a 298 px track,
    // the knob at stop 0 (x 24), pressed on the track at 200, then one frame 0.2 s long: x 113.244335139, scale 1.03042420516
    // (the same as one 0.05 s frame).
    [Fact]
    [Trait("Decision", "ED-252")] // jewel-knob.html:813 (the 0.05 s cap), :448-452 spring(), :582-586 stepSlider()
    [Trait("Req", "PR-COMPOSER-125")] // EffortSlider.swift:801-818,833-836 as amended by OD-0090 (grip, springs, release on a lost capture)
    public void A_long_frame_moves_the_knob_by_no_more_than_fifty_milliseconds_of_its_springs() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        rig.Down(200);
        rig.Frame(0.2);
        Assert.True(Math.Abs(rig.Slider.KnobX - 113.244335139) < 1e-6 && Math.Abs(rig.Slider.KnobScale - 1.03042420516) < 1e-9,
            $"after one 0.2 s frame the page's knob is at x 113.244335139, scale 1.03042420516; the port's at x {rig.Slider.KnobX}, scale {rig.Slider.KnobScale}");
    });

    // The page's bolt leaves from tl.exAt = Math.round((x - 2) * 4) / 4, x the knob's centre (jewel-knob.html:593 and :818,
    // drawTile :317-321 and :335 g.ex = tl.exAt: two pixels before the centre, on a quarter pixel). The port's strike
    // geometry puts it at the knob's centre exactly (TrackEffect.StrikeGeometryFor: Knob = the fill's width - 24), and the
    // fixture expected.mjs records the bursts and the exit angles at ex = 274 for a knob at 274, where the page uses 272.
    [Fact]
    [Trait("Decision", "ED-252")] // jewel-knob.html:593 tl.exAt = Math.round((x - 2) * 4) / 4; :335 ex: tl.exAt
    public void The_bolt_leaves_two_pixels_before_the_knobs_centre_on_a_quarter_pixel() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: 1, fast: true);
        for (var i = 0; i < 6; i++) rig.Frame();
        var effect = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single(e => e.Opacity >= 1);
        var g = effect.StrikeGeometryFor(effect.Bounds.Size);
        var page = Math.Round((rig.Slider.KnobX - 2) * 4) / 4; // the knob at stop 1: 24 + 250 / 3 = 107.33, the page's exit 105.25
        Assert.True(Math.Abs(g.Knob - page) < 1e-9, $"the knob's centre is {rig.Slider.KnobX}: the page's bolt leaves at {page}, the port's at {g.Knob}");
    });
}
