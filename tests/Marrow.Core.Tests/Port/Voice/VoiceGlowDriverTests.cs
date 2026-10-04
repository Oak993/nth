using System.Text.Json;
using Marrow.Core.Port.Voice.Glow;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-4 (voice-SPEC V-13.1, V-13.2; V-T29): VoiceGlowDriver equals voice-glow.js's driver (ka) for scripted level sequences: lobe positions and
// levels, scan, hue, warp, the corner follow and the band polyline. Four runs of 120 to 250 frames, recorded by running voice-glow.js in node
// under a manual requestAnimationFrame (tests\fixtures\voice\glow-driver.json), plus hand-worked first-frame cases.
public sealed class VoiceGlowDriverTests
{
    private const double Frame60 = 1.0 / 60;

    private static VoiceGlowProps MarrowProps(bool reduced = false, bool light = false) => new()
    {
        Type = VoiceGlowType.Default, Theme = light ? VoiceGlowTheme.Light : VoiceGlowTheme.Dark, Scale = 0.7, GlowWidth = 1.6, LobeSpacing = 2.1,
        RangeWidth = 1.9, ProcessingTravel = 2.05, BorderRadius = 22, ReducedMotion = reduced,
    };

    private static VoiceGlowConfig ConfigOf(VoiceGlowProps props) => VoiceGlowResolver.Resolve(props).Config;

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T29")]
    [Trait("Decision", "ED-267")] // V-13.1, V-13.2: four scripted runs (speech, a loud burst, processing on and off; dark, light with reduced motion, a narrow composer, the pill type) equal the design's driver
    public void Scripted_runs_equal_the_designs_driver_frame_for_frame()
    {
        using var doc = VoiceFixtureFile.Json("glow-driver.json");
        var runs = doc.RootElement.GetProperty("runs").EnumerateArray().ToList();
        Assert.Equal(4, runs.Count);
        foreach (var run in runs)
        {
            var name = run.GetProperty("name").GetString()!;
            var width = run.GetProperty("widthPx").GetDouble();
            var height = run.GetProperty("heightPx").GetDouble();
            var props = VoiceGlowFixtureReader.Props(run.GetProperty("props"));
            var changes = run.GetProperty("cfgChanges").EnumerateArray().ToList();
            VoiceGlowFixtureReader.AssertRecord(ConfigOf(props), changes[0].GetProperty("cfg"), name + " initial cfg", "id", "paused");
            var driver = new VoiceGlowDriver(ConfigOf(props));
            var nextChange = 1;
            var previousMs = 0.0;
            var bandsChecked = 0;
            foreach (var row in run.GetProperty("rows").EnumerateArray())
            {
                var i = row.GetProperty("i").GetInt32();
                if (nextChange < changes.Count && changes[nextChange].GetProperty("at").GetInt32() == i)
                {
                    props = props with { Processing = !props.Processing };
                    VoiceGlowFixtureReader.AssertRecord(ConfigOf(props), changes[nextChange].GetProperty("cfg"), $"{name} cfg at frame {i}", "id", "paused");
                    driver.Configure(ConfigOf(props));
                    nextChange++;
                }

                var ms = row.GetProperty("ms").GetDouble();
                var dt = i == 0 ? Frame60 : (ms - previousMs) / 1000;
                previousMs = ms;
                var frame = driver.Step(dt, row.GetProperty("level").GetDouble(), width, height);
                var at = $"{name}, frame {i}";

                var state = row.GetProperty("state");
                VoiceFixtureFile.AssertClose(state.GetProperty("level").GetDouble(), frame.Level, at + " level");
                var bands = state.GetProperty("bands").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                for (var b = 0; b < 3; b++) VoiceFixtureFile.AssertClose(bands[b], frame.Bands[b], $"{at} band {b}");
                VoiceFixtureFile.AssertClose(state.GetProperty("phase").GetDouble(), frame.Phase, at + " phase");
                VoiceFixtureFile.AssertClose(state.GetProperty("scanA").GetDouble(), frame.ScanA, at + " scanA");
                VoiceFixtureFile.AssertClose(state.GetProperty("scanT").GetDouble(), frame.ScanT, at + " scanT");
                VoiceFixtureFile.AssertClose(state.GetProperty("t").GetDouble(), frame.Time, at + " time");
                VoiceFixtureFile.AssertClose(state.GetProperty("warp").GetDouble(), frame.Warp, at + " warp");

                var vars = row.GetProperty("vars");
                VoiceFixtureFile.AssertClose(vars.GetProperty("level").GetDouble(), frame.Level, at + " --vb-level");
                VoiceFixtureFile.AssertClose(vars.GetProperty("cx").GetDouble(), frame.Cx, at + " --vb-cx");
                VoiceFixtureFile.AssertClose(vars.GetProperty("mw").GetDouble(), frame.Mw, at + " --vb-mw");
                VoiceFixtureFile.AssertClose(vars.GetProperty("bh").GetDouble(), frame.Bh, at + " --vb-bh");
                VoiceFixtureFile.AssertClose(vars.GetProperty("bendA").GetDouble(), frame.BendA, at + " --vb-bendA");
                VoiceFixtureFile.AssertClose(vars.GetProperty("cy").GetDouble(), frame.Cy, at + " --vb-cy");
                VoiceFixtureFile.AssertClose(vars.GetProperty("glow").GetDouble(), frame.Glow, at + " --vb-glow");
                VoiceFixtureFile.AssertClose(vars.GetProperty("h").GetDouble(), frame.H, at + " --vb-h");
                VoiceFixtureFile.AssertClose(vars.GetProperty("w").GetDouble(), frame.W, at + " --vb-w");
                VoiceFixtureFile.AssertClose(vars.GetProperty("hue").GetDouble(), frame.Hue, at + " --vb-hue");

                var lobes = row.GetProperty("lobes").EnumerateArray().ToList();
                Assert.Equal(VoiceGlowFrame.LobeCount, lobes.Count);
                for (var k = 0; k < lobes.Count; k++)
                {
                    VoiceFixtureFile.AssertClose(lobes[k].GetProperty("x").GetDouble(), frame.LobeX[k], $"{at} lobe {k} x");
                    VoiceFixtureFile.AssertClose(lobes[k].GetProperty("l").GetDouble(), frame.LobeL[k], $"{at} lobe {k} level");
                    VoiceFixtureFile.AssertClose(lobes[k].GetProperty("y").GetDouble(), frame.LobeY[k], $"{at} lobe {k} y");
                }

                var displace = row.GetProperty("displace");
                if (displace.ValueKind == JsonValueKind.Null)
                {
                    Assert.False(frame.DisplaceActive, at + " displacement should be off");
                }
                else
                {
                    Assert.True(frame.DisplaceActive, at + " displacement should be on");
                    VoiceFixtureFile.AssertClose(displace.GetDouble(), frame.DisplaceScale, at + " displacement scale");
                    VoiceFixtureFile.AssertClose(row.GetProperty("dx").GetDouble(), frame.NoiseDx, at + " noise dx");
                    VoiceFixtureFile.AssertClose(row.GetProperty("dy").GetDouble(), frame.NoiseDy, at + " noise dy");
                }

                Assert.Equal(row.GetProperty("warpOff").GetBoolean(), frame.WarpOff);

                if (row.TryGetProperty("band", out var band))
                {
                    var points = band.EnumerateArray().ToList();
                    Assert.Equal(VoiceGlowFrame.BandPointCount, points.Count);
                    Assert.True(frame.HasBand);
                    for (var p = 0; p < points.Count; p++)
                    {
                        VoiceFixtureFile.AssertClose(points[p][0].GetDouble(), frame.BandX[p], $"{at} band point {p} x");
                        VoiceFixtureFile.AssertClose(points[p][1].GetDouble(), frame.BandY[p], $"{at} band point {p} y");
                    }

                    bandsChecked++;
                }
            }

            Assert.Equal(changes.Count, nextChange);
            Assert.True(bandsChecked >= 10, name + ": band polylines checked: " + bandsChecked);
        }
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T29")]
    [Trait("Decision", "ED-267")] // V-13.1 ka(): reduced motion, silence, first frame: idle 0.18 x 0.5 = 0.09 -> glow 0.15 + 0.85 x 0.09, h 0.5 + 1.2 x 0.09, w 0.85 + 1.05 x 0.09; lobe 0 at 0, lobe 1 at -36 x 1.47 with level 0.6 x cn
    public void A_silent_reduced_motion_frame_has_the_idle_envelope_and_the_lobes_at_rest()
    {
        var driver = new VoiceGlowDriver(ConfigOf(MarrowProps(reduced: true)));
        var f = driver.Step(0.5, 0, 720, 96);
        Assert.Equal(0, f.Level);
        Assert.Equal(0.15 + 0.85 * 0.09, f.Glow, 1e-12);
        Assert.Equal(0.5 + 1.2 * 0.09, f.H, 1e-12);
        Assert.Equal(0.85 + 1.05 * 0.09, f.W, 1e-12);
        Assert.Equal(0, f.Phase); // reduced motion: the flow phase does not move
        Assert.Equal(0, f.Hue);   // and the hue does not drift
        Assert.Equal(0, f.Cx);
        Assert.Equal(1, f.Mw);
        Assert.Equal(1, f.Warp);
        Assert.Equal(0, f.LobeX[0]);
        Assert.Equal(0.6, f.LobeL[0], 1e-12);
        Assert.Equal(-36 * 1.47, f.LobeX[1], 1e-9);
        Assert.Equal(36 * 1.47, f.LobeX[2], 1e-9);
        Assert.Equal(0.6 * (1 - Math.Pow(36 * 1.47 / (252 * 1.47 / 2 + 4), 2)), f.LobeL[1], 1e-12);
        Assert.Equal(f.LobeL[1], f.LobeL[2], 1e-12);
        Assert.All(f.LobeY, y => Assert.Equal(0, y)); // no processing: the corner follow is 0
        Assert.True(f.HasBand);
        Assert.Equal(VoiceGlowFrame.BandPointCount, f.BandX.Length);
        Assert.True(f.DisplaceActive);
        Assert.Equal(0, f.DisplaceScale); // reduced motion: no displacement
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T29")]
    [Trait("Decision", "ED-267")] // V-13.1 ka(): the first frame after a registration is a 1/60 s one whatever the caller measured
    public void The_first_step_is_one_sixtieth_of_a_second()
    {
        var a = new VoiceGlowDriver(ConfigOf(MarrowProps()));
        var b = new VoiceGlowDriver(ConfigOf(MarrowProps()));
        var fa = a.Step(0.016, 0.8, 720, 96);
        var fb = b.Step(0.5, 0.8, 720, 96);
        Assert.Equal(Frame60, fa.Time, 1e-15);
        Assert.Equal(fa.Time, fb.Time);
        Assert.Equal(fa.Level, fb.Level);
        Assert.Equal(fa.Phase, fb.Phase);
        // after that the delta counts, capped at 0.05 s
        a.Step(0.03, 0.8, 720, 96);
        Assert.Equal(Frame60 + 0.03, a.Frame.Time, 1e-12);
        b.Step(2.0, 0.8, 720, 96);
        Assert.Equal(Frame60 + 0.05, b.Frame.Time, 1e-12);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T29")]
    [Trait("Decision", "ED-267")] // V-13.1: the page re-registers the driver when its config changes (the first frame after is 1/60 s); an equal config does not
    public void A_changed_config_or_a_restart_makes_the_next_step_a_sixtieth()
    {
        var props = MarrowProps();
        var driver = new VoiceGlowDriver(ConfigOf(props));
        driver.Step(0.02, 0, 720, 96);
        driver.Configure(ConfigOf(props with { }));
        driver.Step(0.03, 0, 720, 96);
        Assert.Equal(Frame60 + 0.03, driver.Frame.Time, 1e-12);
        driver.Configure(ConfigOf(props with { Processing = true }));
        driver.Step(0.04, 0, 720, 96);
        Assert.Equal(Frame60 + 0.03 + Frame60, driver.Frame.Time, 1e-12);
        driver.Restart();
        driver.Step(0.04, 0, 720, 96);
        Assert.Equal(Frame60 + 0.03 + Frame60 + Frame60, driver.Frame.Time, 1e-12);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T29")]
    [Trait("Decision", "ED-267")] // V-13.1 ka(): the envelope Ge(level, pa(raw), u, attack 0.325, release 0.86); band 0 follows the raw level
    public void The_level_follows_the_raw_level_with_the_published_attack()
    {
        var driver = new VoiceGlowDriver(ConfigOf(MarrowProps()));
        var f = driver.Step(Frame60, 1, 720, 96);
        var expected = 1 - Math.Exp(-Frame60 / 0.325);
        Assert.Equal(expected, f.Level, 1e-12);
        Assert.Equal(expected, f.Bands[0], 1e-12);
        // a raw level under the threshold (0.015) is silence
        var quiet = new VoiceGlowDriver(ConfigOf(MarrowProps()));
        Assert.Equal(0, quiet.Step(Frame60, 0.015, 720, 96).Level);
        // a level above 1 counts as 1; one below 0 as 0
        var loud = new VoiceGlowDriver(ConfigOf(MarrowProps()));
        Assert.Equal(expected, loud.Step(Frame60, 7, 720, 96).Level, 1e-12);
        var negative = new VoiceGlowDriver(ConfigOf(MarrowProps()));
        Assert.Equal(0, negative.Step(Frame60, -3, 720, 96).Level);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T29")]
    [Trait("Decision", "ED-267")] // V-13.1 ka(): processing starts the scan at half its duration (0.55 s), scanA rises with tau 0.54, the warp falls with tau 0.06
    public void Processing_starts_the_scan_and_collapses_the_warp()
    {
        var props = MarrowProps();
        var driver = new VoiceGlowDriver(ConfigOf(props));
        driver.Step(Frame60, 0, 720, 96);
        driver.Configure(ConfigOf(props with { Processing = true }));
        var f = driver.Step(0.04, 0, 720, 96); // a re-registration: 1/60 s
        Assert.Equal(0.55 + Frame60, f.ScanT, 1e-12);
        Assert.Equal(1 - Math.Exp(-Frame60 / 0.54), f.ScanA, 1e-12);
        Assert.Equal(Math.Exp(-Frame60 / 0.06), f.Warp, 1e-12);
        Assert.Equal(2 * Frame60, f.Time, 1e-12);
        // and back: the scan fades with tau 0.48, then its clock resets under 1e-3
        driver.Configure(ConfigOf(props));
        for (var i = 0; i < 400; i++) driver.Step(Frame60, 0, 720, 96);
        Assert.Equal(0, driver.Frame.ScanT);
        Assert.True(driver.Frame.ScanA < 1e-3);
        Assert.True(driver.Frame.Warp > 0.999);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T29")]
    [Trait("Decision", "ED-267")] // V-13.1 ka(): hue = -hueRange + 2 hueRange Tn(t / hueDuration), Tn(x) = (1 - cos 2 pi x) / 2; static colours stop it
    public void The_hue_drifts_with_the_cosine_of_the_clock()
    {
        var driver = new VoiceGlowDriver(ConfigOf(MarrowProps()));
        var f = driver.Step(Frame60, 0, 720, 96);
        Assert.Equal(-24 + 48 * (1 - Math.Cos(2 * Math.PI * (Frame60 / 12))) / 2, f.Hue, 1e-12);
        for (var i = 0; i < 360; i++) driver.Step(Frame60, 0, 720, 96); // 6 s = half a hue period: the far end of the drift
        Assert.Equal(24, driver.Frame.Hue, 1e-3);
        var still = new VoiceGlowDriver(ConfigOf(MarrowProps() with { StaticColors = true }));
        for (var i = 0; i < 30; i++) still.Step(Frame60, 0, 720, 96);
        Assert.Equal(0, still.Frame.Hue);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T29")]
    [Trait("Decision", "ED-267")] // V-13.2: no per-frame allocation (one frame object), and a shape with no size computes no band
    public void The_driver_reuses_its_frame_and_a_sizeless_shape_has_no_band()
    {
        var driver = new VoiceGlowDriver(ConfigOf(MarrowProps()));
        var first = driver.Step(Frame60, 0.3, 720, 96);
        Assert.Same(first, driver.Frame);
        Assert.True(first.HasBand);
        var second = driver.Step(Frame60, 0.3, 0, 0);
        Assert.Same(first, second);
        Assert.False(second.HasBand);
        Assert.True(double.IsFinite(second.Cy));
        Assert.True(double.IsFinite(second.Glow));
    }
}
