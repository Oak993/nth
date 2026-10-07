using System.Text.Json;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Composer;

// OD-0090's locked page (E:/Droppy/work/design/jewel-knob-locked/jewel-knob.html) runs the knob in one loop: each display
// refresh it asks the locked board's frameGate (strike-engine.js:56-66) whether to draw (:867-871), and a drawn frame steps
// the springs by the time since the last drawn frame, at most 50 ms (tick(), :808-814), then puts the bolt's exit at
// tl.exAt = Math.round((x - 2) * 4) / 4 for the knob's centre x (stepSlider, :593), which the bolt drawn and the knob's
// signal then read on that same frame (:815-822). The expected values are the page's own functions run by node
// (tests/fixtures/jewel-knob/expected.mjs: gate, slider.longFrame / longRelease / hz240, exit).
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
    [Trait("Req", "PR-COMPOSER-125")] // EffortSlider.swift:801-818,833-836 as amended by OD-0090 (grip, springs, release on a lost capture)
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
    [Trait("Decision", "ED-252")] // jewel-knob.html:813 tick(): dt = min(0.05, max(0, t - sliderT)), sliderT the last DRAWN frame's; :867-871 drawn on the refreshes strike-engine.js:56-66 frameGate passes
    [Trait("Req", "PR-COMPOSER-125")] // EffortSlider.swift:801-818,833-836 as amended by OD-0090 (grip, springs, release on a lost capture)
    public void On_a_240_hertz_display_each_drawn_frame_moves_the_springs_by_the_time_since_the_last_drawn_frame() => headless.OnUiThread(() =>
    {
        // expected.mjs slider.hz240: the page's gate and tick() over the rig's frames (one 1/60 s frame, 240 refreshes of
        // 41667 ticks, a press on the track at 200, 40 refreshes, the release, 360 refreshes), each refresh after the press
        // as shown: drawn on every 4th, each drawn one moving the springs by 4 refreshes (16.7 ms), not by the one refresh
        // since the last frame the gate held back (a knob springing at a quarter of its speed)
        var run = Slider.GetProperty("hz240").EnumerateArray().ToArray();
        Assert.Equal(400, run.Length);
        Assert.Equal(100, run.Count(e => e.GetProperty("drawn").GetBoolean()));
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        for (var i = 0; i < 240; i++) rig.Frame(41667 / 1e7);
        rig.Down(200);
        for (var i = 0; i < run.Length; i++)
        {
            if (i == 40) rig.Up(200);
            var e = run[i];
            rig.Frame(e.GetProperty("ticks").GetInt64() / 1e7);
            var x = e.GetProperty("x").GetDouble();
            Assert.True(Math.Abs(rig.Slider.KnobX - x) < 1e-3, $"refresh {i + 1} after the press ({(e.GetProperty("drawn").GetBoolean() ? "drawn" : "held back")}): page x {x}, port {rig.Slider.KnobX}");
            Assert.True(Math.Abs(rig.Slider.KnobScale - e.GetProperty("scale").GetDouble()) < 1e-5, $"refresh {i + 1}: page scale {e.GetProperty("scale")}, port {rig.Slider.KnobScale}");
            Assert.Equal(e.GetProperty("index").GetInt32(), rig.Index.Value);
        }
        Assert.Equal(24 + 500.0 / 3, rig.Slider.KnobX, 9); // settled on the stop the press chose
    });

    [Fact]
    [Trait("Decision", "ED-252")] // jewel-knob.html:808-822 tick(): stepSlider sets tl.exAt for the knob's new x (:593), then drawAll draws the bolt from it (:335 g.ex) and signal() reads the same exit (orbAim)
    public void While_the_knob_moves_it_reads_the_exit_of_the_bolt_the_tube_draws_on_the_same_frame() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(fast: true);
        rig.Frame();
        var effect = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single(e => e.Opacity >= 1);
        var seed = effect.StrikeSeed;
        // into strike 0's reading (from its charge, 0.14 + its charge s before it strikes, to 1.9 s after): every frame below
        // reads it, so the knob's exit angle is the strike's, from where its bolt leaves
        var first=(int)Math.Floor(effect.StrikeFrameTime/2.4)-1;
        var plan=Enumerable.Range(first,12).Select(k=>StrikeMix.Timing(k,seed)).First(p=>p.At-p.Lead-p.Charge>effect.StrikeFrameTime&&p.Charge>0);
        var reading = plan.At-plan.Lead-plan.Charge-effect.StrikeOffset;
        rig.Frame(Math.Max(1.0 / 60, reading + 0.02 - effect.TimeAt(SwiftDate.Now)));
        // on the track, at stop 2 (below the top: the look stays fast, the same effect): the knob springs from its stop to
        // the pointer
        rig.Down(180);
        Assert.Equal(2, rig.Index.Value);
        var last = rig.Slider.KnobX;
        var moved = 0;
        for (var i = 1; i <= 40; i++)
        {
            rig.Frame();
            Assert.Same(effect, Assert.Single(rig.Slider.GetVisualDescendants().OfType<TrackEffect>()));
            var x = rig.Slider.KnobX;
            var g = effect.StrikeGeometryFor(effect.Bounds.Size); // the bolt the tube draws this frame, laid out for the knob at x
            Assert.Equal(TrackEffect.ExitAt(x), g.Knob, 9);
            var t = effect.StrikeFrameTime;
            var page = new KnobSignal();
            page.ReadMix(g,new StrikePlans(seed),t,0); // SPEC A-21/B-10: the production Mix signal and shared strike time.
            Assert.NotNull(page.MixK);
            Assert.True(Math.Abs(page.ExitAngle - rig.Slider.Signal.ExitAngle) < 1e-12,
                $"frame {i}, the knob at {x} (from {last}): the bolt drawn leaves at {g.Knob} for an exit angle {page.ExitAngle}; the knob read {rig.Slider.Signal.ExitAngle}");
            if (TrackEffect.ExitAt(x) != TrackEffect.ExitAt(last)) moved++;
            last = x;
        }
        Assert.True(moved >= 10, $"the exit moved on {moved} frames");
        rig.Up(180);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // jewel-knob.html:813 (the cap) on a press: one 0.2 s frame moves the knob as one 0.05 s frame does
    [Trait("Req", "PR-COMPOSER-125")] // EffortSlider.swift:801-818,833-836 as amended by OD-0090 (grip, springs, release on a lost capture)
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
