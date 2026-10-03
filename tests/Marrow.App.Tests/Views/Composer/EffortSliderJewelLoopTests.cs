using System.Text.Json;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Composer;

// OD-0090's locked page (E:/Droppy/work/design/jewel-knob-locked/jewel-knob.html) runs the knob in one loop: each display
// refresh it asks the locked board's frameGate (strike-engine.js:56-66) whether to draw (:867-871), and a drawn frame steps
// the springs by the time since the last drawn frame, at most 50 ms (tick(), :808-814), then puts the bolt's exit at
// tl.exAt = Math.round((x - 2) * 4) / 4 for the knob's centre x (stepSlider, :593). The expected values are the page's own
// functions run by node (tests/fixtures/jewel-knob/expected.mjs: gate, slider.longFrame / longRelease, exit).
[Collection("Headless")]
public sealed class EffortSliderJewelLoopTests(HeadlessSession headless)
{
    private static JsonElement Slider => JewelFixtures.Expected.GetProperty("slider");

    public static TheoryData<string> GateRuns => new(JewelFixtures.Expected.GetProperty("gate").EnumerateArray().Select(r => r.GetProperty("name").GetString()!));

    // The page's frames, each after the last by its own length (t, s; a 1/60 s frame as the rig's own), compared with the
    // port's knob after each: its centre, its scale, the lift and the chosen stop.
    private static void AssertRun(JewelRig rig, IEnumerable<JsonElement> expected, double previous)
    {
        foreach (var e in expected)
        {
            var t = e.GetProperty("t").GetDouble();
            var frame = t - previous;
            previous = t;
            if (Math.Abs(frame - 1.0 / 60) < 1e-5) rig.Frame(); else rig.Frame(frame);
            var x = e.GetProperty("x").GetDouble();
            Assert.True(Math.Abs(rig.Slider.KnobX - x) < 1e-3, $"x at {t}: page {x}, port {rig.Slider.KnobX}");
            Assert.True(Math.Abs(rig.Slider.KnobScale - e.GetProperty("scale").GetDouble()) < 1e-5, $"scale at {t}: page {e.GetProperty("scale")}, port {rig.Slider.KnobScale}");
            Assert.True(Math.Abs(rig.Slider.LiftAmount - e.GetProperty("lift").GetDouble()) < 1e-4, $"lift at {t}");
            Assert.Equal(e.GetProperty("index").GetInt32(), rig.Index.Value);
        }
    }

    [Theory]
    [Trait("Decision", "ED-252")] // strike-engine.js:56-66 frameGate (r: the smallest recent gap, 2 < d < 50 ms, min(d, 0.9 r + 0.1 d); every = max(1, floor(1000 / 60 / r + 0.05)); drawn once every r - r / 2 has passed); jewel-knob.html:867-871
    [MemberData(nameof(GateRuns))]
    public void The_knob_steps_on_the_frames_the_pages_frame_gate_draws(string name) => headless.OnUiThread(() =>
    {
        var run = JewelFixtures.Expected.GetProperty("gate").EnumerateArray().Single(r => r.GetProperty("name").GetString() == name);
        var draws = run.GetProperty("draws").EnumerateArray().Select(d => d.GetInt32()).ToList();
        Assert.Equal(0, draws[0]); // the page draws its first frame, as the port does
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        var steps = new List<int>();
        var last = rig.Slider.Orb.Phase;
        var frame = 0;
        foreach (var gap in run.GetProperty("gaps").EnumerateArray())
        {
            frame++;
            rig.Frame(gap.GetInt64() / 1e7); // whole ticks, as recorded
            if (rig.Slider.Orb.Phase != last) { steps.Add(frame); last = rig.Slider.Orb.Phase; }
        }
        var expected = draws.Skip(1).ToList();
        var first = Enumerable.Range(0, Math.Max(expected.Count, steps.Count)).FirstOrDefault(i => i >= expected.Count || i >= steps.Count || expected[i] != steps[i], -1);
        Assert.True(first < 0, $"{name}: the page draws {expected.Count} of {frame} frames, the port stepped on {steps.Count}; first difference at draw {first}: " +
            $"page {(first >= 0 && first < expected.Count ? expected[first] : -1)}, port {(first >= 0 && first < steps.Count ? steps[first] : -1)}");
    });

    [Fact]
    [Trait("Decision", "ED-252")] // jewel-knob.html:813 tick(): dt = min(0.05, max(0, t - sliderT)); :448-452 spring(), :582-586 stepSlider()
    public void A_long_frame_moves_the_springs_by_fifty_milliseconds_and_they_then_settle_on_the_stop() => headless.OnUiThread(() =>
    {
        Assert.Equal(0.05, Slider.GetProperty("dtCap").GetDouble()); // read from the page's tick()
        var run = Slider.GetProperty("longRelease").EnumerateArray().ToArray();
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        rig.Down(200);
        AssertRun(rig, run.Take(60), 0);
        rig.Up(200);
        AssertRun(rig, run.Skip(60), run[59].GetProperty("t").GetDouble()); // a 1 s frame, then 1/60 s frames
        Assert.Equal(24 + 500.0 / 3, rig.Slider.KnobX, 9);
        Assert.Equal(1, rig.Slider.KnobScale);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // jewel-knob.html:813 (the cap) on a press: one 0.2 s frame moves the knob as one 0.05 s frame does
    public void After_a_stall_the_knob_goes_on_from_where_it_was() => headless.OnUiThread(() =>
    {
        var page = Slider.GetProperty("longFrame")[0];
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        rig.Down(200);
        rig.Frame(0.2);
        Assert.True(Math.Abs(rig.Slider.KnobX - page.GetProperty("x").GetDouble()) < 1e-6, $"page x {page.GetProperty("x")}, port {rig.Slider.KnobX}");
        Assert.True(Math.Abs(rig.Slider.KnobScale - page.GetProperty("scale").GetDouble()) < 1e-9, $"page scale {page.GetProperty("scale")}, port {rig.Slider.KnobScale}");
    });

    [Theory]
    [Trait("Decision", "ED-252")] // jewel-knob.html:593 stepSlider: tl.exAt = Math.round((x - 2) * 4) / 4; :335 drawTile: g.ex = tl.exAt
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void At_each_stop_the_bolt_leaves_where_the_pages_does(int index) => headless.OnUiThread(() =>
    {
        var page = JewelFixtures.Expected.GetProperty("exit")[index];
        using var rig = JewelFixtures.Rig(index: index, fast: true);
        for (var i = 0; i < 6; i++) rig.Frame();
        Assert.Equal(page.GetProperty("x").GetDouble(), rig.Slider.KnobX, 9);
        var effect = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single(e => e.Opacity >= 1);
        var exit = effect.StrikeGeometryFor(effect.Bounds.Size).Knob;
        Assert.True(Math.Abs(exit - page.GetProperty("ex").GetDouble()) < 1e-9, $"the knob at {rig.Slider.KnobX}: the page's bolt leaves at {page.GetProperty("ex")}, the port's at {exit}");
    });
}
