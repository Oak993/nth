using System.Text.Json;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-4 (voice-SPEC V-11.1, V-11.2, V-11.3; V-T28): the orb runtime's frame (state sample, audio rules, integrated motion phase, framing, glow)
// equals voice-orbs.js's makeOrb().frame, run in node with a recording WebGL2 stub (tests\fixtures\voice\orb-runtime.json): the 136-float
// uniform block float for float, the mrw uniform and the phase.
public sealed class VoiceOrbRuntimeTests
{
    private static OrbFrameState StateFrom(JsonElement s)
    {
        OrbBands? bands = null;
        if (s.TryGetProperty("bands", out var b))
            bands = new OrbBands(
                Low: VoiceFixtureFile.Number(b.GetProperty("low")), Mid: VoiceFixtureFile.Number(b.GetProperty("mid")),
                High: VoiceFixtureFile.Number(b.GetProperty("high")), All: VoiceFixtureFile.Number(b.GetProperty("all")));
        double? Opt(string name) => s.TryGetProperty(name, out var v) ? VoiceFixtureFile.Number(v) : null;
        return new OrbFrameState(
            Act: Opt("act") ?? 0, Bands: bands, Speed: Opt("speed"), Scale: Opt("scale"), Glow: Opt("glow"),
            Lift: Opt("lift") ?? 0, Desat: Opt("desat") ?? 0, ReduceMotion: s.TryGetProperty("reduceMotion", out var r) && r.GetBoolean());
    }

    private static VoiceOrbRuntime RuntimeFrom(JsonElement config)
    {
        var kind = config.GetProperty("orb").GetString() == "siri-wave" ? VoiceOrbKind.Siri : VoiceOrbKind.Membrane;
        return new VoiceOrbRuntime(kind, new VoiceOrbRuntimeOptions(
            SizePx: config.GetProperty("sizePx").GetDouble(),
            BallPx: config.GetProperty("ballPx").GetDouble(),
            DevicePixelRatio: config.GetProperty("dpr").GetDouble(),
            Dark: config.GetProperty("dark").GetBoolean(),
            EdgeSource: config.GetProperty("edgeSource").GetBoolean(),
            Supersample: config.GetProperty("supersample").GetBoolean()));
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.1, V-11.2: every recorded frame of five layouts and both presets, float for float
    public void Frames_equal_the_designs_uniform_blocks_float_for_float()
    {
        using var doc = VoiceFixtureFile.Json("orb-runtime.json");
        var configs = doc.RootElement.GetProperty("frames").EnumerateArray().ToList();
        Assert.Equal(5, configs.Count);
        foreach (var config in configs)
        {
            var name = config.GetProperty("name").GetString();
            var runtime = RuntimeFrom(config);
            foreach (var frame in config.GetProperty("frames").EnumerateArray())
            {
                var t = frame.GetProperty("t").GetDouble();
                runtime.Frame(t, StateFrom(frame.GetProperty("state")));
                var expected = VoiceFixtureFile.Floats(frame.GetProperty("values"));
                var actual = runtime.Values.ToArray();
                Assert.Equal(OrbSeeds.Length, actual.Length);
                for (var i = 0; i < expected.Length; i++)
                    Assert.True(BitConverter.SingleToInt32Bits(expected[i]) == BitConverter.SingleToInt32Bits(actual[i]) || (expected[i] == 0 && actual[i] == 0),
                        $"{name}, t={t}: values[{i}] expected {expected[i]:R}, got {actual[i]:R}");
                var mrw = frame.GetProperty("mrw").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                Assert.Equal(mrw[0], runtime.Mrw.Lift);
                Assert.Equal(mrw[1], runtime.Mrw.Desat);
                Assert.Equal(mrw[2], runtime.Mrw.Dark);
                Assert.Equal(0, mrw[3]);
                VoiceFixtureFile.AssertClose(frame.GetProperty("phase").GetDouble(), runtime.Phase, $"{name}, t={t}: phase");
                Assert.Equal(frame.GetProperty("canvasWidth").GetInt32(), runtime.RenderPx);
            }
        }
    }

    [Theory]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.3: below 128 device px the shader runs into a 2x surface; dev = max(2, round(px * dpr)), dpr capped at 3, round half up
    [InlineData(32, 1, true, 32, 2, 64)]
    [InlineData(72, 2, true, 144, 1, 144)]
    [InlineData(72, 1, true, 72, 2, 144)]
    [InlineData(72, 1, false, 72, 1, 72)]
    [InlineData(40, 3.7, true, 120, 2, 240)]
    [InlineData(10.5, 1, true, 11, 2, 22)]
    [InlineData(0.4, 1, true, 2, 2, 4)]
    [InlineData(128, 1, true, 128, 1, 128)]
    [InlineData(127.4, 1, true, 127, 2, 254)]
    public void The_surface_is_laid_out_as_the_designs_resize(double size, double dpr, bool supersample, int dev, int scale, int rdev)
    {
        var runtime = new VoiceOrbRuntime(VoiceOrbKind.Siri, new VoiceOrbRuntimeOptions(SizePx: size, BallPx: size * 0.8, DevicePixelRatio: dpr, Supersample: supersample));
        Assert.Equal(dev, runtime.DevicePx);
        Assert.Equal(scale, runtime.RenderScale);
        Assert.Equal(rdev, runtime.RenderPx);
        runtime.Frame(0, new OrbFrameState());
        Assert.Equal((float)rdev, runtime.Values[0]);
        Assert.Equal((float)rdev, runtime.Values[1]);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.2: framing values[4] = (ball / size) * scale; values[16] = clamp((1.5 / max(radPx, 1) - 0.01) / 2, 0.005, 0.15), radPx = values[4] * rdev / 2
    public void The_ball_is_framed_and_its_edge_softened_by_the_published_formulas()
    {
        // 32 px slot, 26 px ball, dpr 1: rdev 64; scale 1.16 -> values[4] = 26/32 * 1.16 = 0.9425; radPx = 0.9425 * 64 / 2 = 30.16
        var runtime = new VoiceOrbRuntime(VoiceOrbKind.Siri, new VoiceOrbRuntimeOptions(SizePx: 32, BallPx: 26, DevicePixelRatio: 1));
        runtime.Frame(0, new OrbFrameState(Scale: 1.16));
        Assert.Equal(0.9425f, runtime.Values[4], 1e-6f);
        Assert.Equal((1.5 / 30.16 - 0.01) / 2, runtime.Values[16], 1e-6);
        // a big ball on a big slot lowers the softness to its floor, a tiny one raises it to its ceiling
        var big = new VoiceOrbRuntime(VoiceOrbKind.Siri, new VoiceOrbRuntimeOptions(SizePx: 400, BallPx: 400, DevicePixelRatio: 3));
        big.Frame(0, new OrbFrameState());
        Assert.Equal(0.005f, big.Values[16], 1e-7f);
        var tiny = new VoiceOrbRuntime(VoiceOrbKind.Siri, new VoiceOrbRuntimeOptions(SizePx: 4, BallPx: 1, DevicePixelRatio: 1));
        tiny.Frame(0, new OrbFrameState());
        Assert.Equal(0.15f, tiny.Values[16], 1e-7f);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.2: speed = speed * idleSeed[3]; glow -> values[17] floored at 0; mrw = (lift, desat, dark ? 1 : 0, 0) clamped to 0..1
    public void Speed_glow_and_the_mrw_uniform_come_from_the_state_layer()
    {
        var runtime = new VoiceOrbRuntime(VoiceOrbKind.Membrane, new VoiceOrbRuntimeOptions(SizePx: 72, BallPx: 32, Dark: false));
        runtime.Frame(1, new OrbFrameState(Speed: 2, Glow: 0.55, Lift: 0.4, Desat: 0.25));
        Assert.Equal((float)(2 * 0.266f), runtime.Values[3]);
        Assert.Equal(0.55f, runtime.Values[17]);
        Assert.Equal(new OrbMrw(0.4, 0.25, 0), runtime.Mrw);
        runtime.SetDark(true);
        runtime.Frame(2, new OrbFrameState(Speed: -4, Glow: -1, Lift: 9, Desat: -9));
        Assert.Equal(0f, runtime.Values[3]);
        Assert.Equal(0f, runtime.Values[17]);
        Assert.Equal(new OrbMrw(1, 0, 1), runtime.Mrw);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.1: phase += dt * max(values[3], 0) with dt capped at 0.1 and never negative; values[2] = phase / max(values[3], 0.001); reduce motion stops the phase
    public void The_motion_phase_integrates_the_speed_and_time_is_phase_over_speed()
    {
        var runtime = new VoiceOrbRuntime(VoiceOrbKind.Siri, new VoiceOrbRuntimeOptions(SizePx: 72, BallPx: 32));
        var speed = 0.246f; // Siri Wave's Idle speed at a speed multiple of 1
        runtime.Frame(5.0, new OrbFrameState());
        Assert.Equal(0, runtime.Phase); // the first frame has no delta
        runtime.Frame(5.05, new OrbFrameState());
        Assert.Equal(0.05 * speed, runtime.Phase, 1e-12);
        runtime.Frame(8.0, new OrbFrameState()); // a gap of 2.95 s counts 0.1 s
        Assert.Equal((0.05 + 0.1) * speed, runtime.Phase, 1e-12);
        runtime.Frame(7.0, new OrbFrameState()); // time going backwards counts 0
        Assert.Equal((0.05 + 0.1) * speed, runtime.Phase, 1e-12);
        Assert.Equal((float)(runtime.Phase / speed), runtime.Values[2], 1e-5f);
        var before = runtime.Phase;
        runtime.Frame(7.04, new OrbFrameState(ReduceMotion: true));
        Assert.Equal(before, runtime.Phase);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.5: the new orb takes the old one's phase; the option starts a runtime there
    public void A_runtime_can_start_from_another_orbs_phase()
    {
        var runtime = new VoiceOrbRuntime(VoiceOrbKind.Membrane, new VoiceOrbRuntimeOptions(SizePx: 72, BallPx: 32, Phase: 12.5));
        Assert.Equal(12.5, runtime.Phase);
        runtime.Frame(0, new OrbFrameState());
        Assert.Equal((float)(12.5 / 0.266f), runtime.Values[2], 1e-3f);
        runtime.Phase = 3;
        Assert.Equal(3, runtime.Phase);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // voice-orbs.js frame(): `+s.act || 0` and the 0..1 clamp: a NaN act is 0, one above 1 is 1
    public void Act_is_clamped_and_a_NaN_act_is_zero()
    {
        float[] Sample(double act)
        {
            var r = new VoiceOrbRuntime(VoiceOrbKind.Siri, new VoiceOrbRuntimeOptions(SizePx: 72, BallPx: 32));
            r.Frame(0, new OrbFrameState(Act: act));
            return r.Values.ToArray();
        }

        Assert.Equal(Sample(0), Sample(double.NaN));
        Assert.Equal(Sample(1), Sample(7));
        Assert.Equal(Sample(0), Sample(-2));
        Assert.NotEqual(Sample(0), Sample(1));
    }
}
