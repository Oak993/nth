using System.Reflection;
using System.Text.Json;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-4 (voice-SPEC V-11.4; V-T27): OrbMotion. Targets per state are the V-11.4 table (hand values below); the activation ramps are worked by
// hand from the clause (ease-out cubic 0.22 s up, smoothstep 0.65 s down, each from the value shown); the springs, the follower and a scripted
// run of 650 frames equal the design's code, executed from voice-composer.js in node (tests\fixtures\voice\orb-motion.json).
public sealed class OrbMotionTests
{
    private static readonly OrbBands Silent = default;

    private static OrbMotionInput Input(OrbMotionMode mode, bool hovered = false, bool pressed = false, double all = 0, double slot = 26, bool reduce = false) =>
        new(mode, hovered, pressed, new OrbBands(Low: 0, Mid: 0, High: 0, All: all), slot, reduce);

    private static void AssertTargets(OrbMotionTargets t, double speed, double act, double scale, double glow, double lift, double desat, double resp, double damp)
    {
        Assert.Equal(speed, t.Speed, 1e-12);
        Assert.Equal(act, t.Act, 1e-12);
        Assert.Equal(scale, t.Scale, 1e-12);
        Assert.Equal(glow, t.Glow, 1e-12);
        Assert.Equal(lift, t.Lift, 1e-12);
        Assert.Equal(desat, t.Desat, 1e-12);
        Assert.Equal(resp, t.ScaleResponse, 1e-12);
        Assert.Equal(damp, t.ScaleDamping, 1e-12);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4: the constants K, all 29, equal the page's table
    public void The_K_table_equals_the_pages()
    {
        using var doc = VoiceFixtureFile.Json("orb-motion.json");
        var k = doc.RootElement.GetProperty("K");
        var fields = typeof(OrbMotionK).GetFields(BindingFlags.Public | BindingFlags.Static).ToDictionary(f => f.Name);
        var count = 0;
        foreach (var entry in k.EnumerateObject())
        {
            var pascal = char.ToUpperInvariant(entry.Name[0]) + entry.Name[1..];
            Assert.True(fields.TryGetValue(pascal, out var field), "OrbMotionK has no " + pascal);
            Assert.Equal(entry.Value.GetDouble(), (double)field!.GetValue(null)!);
            count++;
        }

        Assert.Equal(29, count);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4 table: Rest and Landing without hover
    public void Rest_and_Landing_target_the_resting_look()
    {
        foreach (var mode in new[] { OrbMotionMode.Rest, OrbMotionMode.Landing })
            AssertTargets(OrbMotion.TargetsFor(mode, hovered: false, pressed: false, voice: 0.7), 1, 0, 1, 0, 0, 0, 0.32, 0.78);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4 table: Hover counts only at Rest or Landing: speed 2.0, act 0.3, scale 1.04 (0.22, 0.8), glow 0.55, lift 0.15
    public void Hover_counts_at_Rest_and_Landing()
    {
        foreach (var mode in new[] { OrbMotionMode.Rest, OrbMotionMode.Landing })
            AssertTargets(OrbMotion.TargetsFor(mode, hovered: true, pressed: false, voice: 0), 2.0, 0.3, 1.04, 0.55, 0.15, 0, 0.22, 0.8);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4 table: Listening: speed 1 + 2.2 v, act 1, scale 1.16 + 0.04 v (0.3, 0.62), glow 0.45 + 0.4 v, lift 1; hover does not count
    public void Listening_follows_the_voice()
    {
        AssertTargets(OrbMotion.TargetsFor(OrbMotionMode.Listening, false, false, 0), 1, 1, 1.16, 0.45, 1, 0, 0.3, 0.62);
        AssertTargets(OrbMotion.TargetsFor(OrbMotionMode.Listening, false, false, 0.5), 1 + 2.2 * 0.5, 1, 1.16 + 0.04 * 0.5, 0.45 + 0.4 * 0.5, 1, 0, 0.3, 0.62);
        AssertTargets(OrbMotion.TargetsFor(OrbMotionMode.Listening, true, false, 1), 3.2, 1, 1.2, 0.85, 1, 0, 0.3, 0.62);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4 table: Processing: speed 4.6, act 1, scale 1.1 (0.3, 0.75), glow 0.75, lift 0.8; voice and hover do not count
    public void Processing_is_fixed()
    {
        AssertTargets(OrbMotion.TargetsFor(OrbMotionMode.Processing, false, false, 0), 4.6, 1, 1.1, 0.75, 0.8, 0, 0.3, 0.75);
        AssertTargets(OrbMotion.TargetsFor(OrbMotionMode.Processing, true, false, 1), 4.6, 1, 1.1, 0.75, 0.8, 0, 0.3, 0.75);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4 table: Blocked: speed 0.6, act 0, scale 1 (0.32, 0.78), glow 0.15 if hovered else 0, lift 0, desat 1
    public void Blocked_is_calm_and_desaturated()
    {
        AssertTargets(OrbMotion.TargetsFor(OrbMotionMode.Blocked, false, false, 0.9), 0.6, 0, 1, 0, 0, 1, 0.32, 0.78);
        AssertTargets(OrbMotion.TargetsFor(OrbMotionMode.Blocked, true, false, 0.9), 0.6, 0, 1, 0.15, 0, 1, 0.32, 0.78);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4 table: Pressed (any but Processing): scale 0.94 x (1.16 if Listening else 1), spring (0.18, 0.8); the other targets stay the state's
    public void Pressed_squeezes_every_state_but_Processing()
    {
        AssertTargets(OrbMotion.TargetsFor(OrbMotionMode.Rest, false, true, 0), 1, 0, 0.94, 0, 0, 0, 0.18, 0.8);
        AssertTargets(OrbMotion.TargetsFor(OrbMotionMode.Rest, true, true, 0), 2.0, 0.3, 0.94, 0.55, 0.15, 0, 0.18, 0.8);
        AssertTargets(OrbMotion.TargetsFor(OrbMotionMode.Listening, false, true, 0.5), 2.1, 1, 0.94 * 1.16, 0.65, 1, 0, 0.18, 0.8);
        AssertTargets(OrbMotion.TargetsFor(OrbMotionMode.Blocked, false, true, 0), 0.6, 0, 0.94, 0, 0, 1, 0.18, 0.8);
        AssertTargets(OrbMotion.TargetsFor(OrbMotionMode.Landing, false, true, 0), 1, 0, 0.94, 0, 0, 0, 0.18, 0.8);
        AssertTargets(OrbMotion.TargetsFor(OrbMotionMode.Processing, false, true, 0), 4.6, 1, 1.1, 0.75, 0.8, 0, 0.3, 0.75);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // a new orb is at rest: act 0 (the page's demo starts at 0.15, a demo choice), speed 1, scale 1, no glow, the slot at its setting
    public void A_new_orb_is_at_rest()
    {
        var motion = new OrbMotion(26);
        Assert.Equal(0, motion.Act);
        Assert.Equal(1, motion.Speed);
        Assert.Equal(1, motion.Scale);
        Assert.Equal(0, motion.ScaleVelocity);
        Assert.Equal(0, motion.Glow);
        Assert.Equal(0, motion.Lift);
        Assert.Equal(0, motion.Desat);
        Assert.Equal(0, motion.Voice);
        Assert.Equal(26, motion.Diameter);
        Assert.Equal(0, motion.DiameterVelocity);
        motion.Step(1.0, 1.0 / 60, Input(OrbMotionMode.Rest));
        Assert.Equal(0, motion.Act);
        Assert.Equal(1, motion.Scale);
        Assert.Equal(26, motion.Diameter);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4: act rises with ease-out cubic over 0.22 s, from the value shown: hover 0 -> 0.3
    public void Act_rises_with_an_ease_out_cubic_over_0_22_seconds()
    {
        var motion = new OrbMotion(26);
        motion.Step(1.0, 1.0 / 60, Input(OrbMotionMode.Rest));
        motion.Step(2.0, 1.0 / 60, Input(OrbMotionMode.Rest, hovered: true)); // the ramp starts at 2.0 s
        Assert.Equal(0, motion.Act, 1e-12);
        motion.Step(2.055, 1.0 / 60, Input(OrbMotionMode.Rest, hovered: true)); // x = 0.25: 1 - 0.75^3 = 0.578125
        Assert.Equal(0.3 * 0.578125, motion.Act, 1e-12);
        motion.Step(2.11, 1.0 / 60, Input(OrbMotionMode.Rest, hovered: true)); // x = 0.5: 1 - 0.5^3 = 0.875
        Assert.Equal(0.3 * 0.875, motion.Act, 1e-12);
        motion.Step(2.22, 1.0 / 60, Input(OrbMotionMode.Rest, hovered: true));
        Assert.Equal(0.3, motion.Act, 1e-12);
        motion.Step(5.0, 1.0 / 60, Input(OrbMotionMode.Rest, hovered: true));
        Assert.Equal(0.3, motion.Act, 1e-12);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4: act falls with smoothstep over 0.65 s, from the value shown
    public void Act_falls_with_a_smoothstep_over_0_65_seconds()
    {
        var motion = new OrbMotion(26);
        motion.Step(1.0, 1.0 / 60, Input(OrbMotionMode.Rest, hovered: true));
        motion.Step(2.0, 1.0 / 60, Input(OrbMotionMode.Rest, hovered: true));
        Assert.Equal(0.3, motion.Act, 1e-12);
        motion.Step(3.0, 1.0 / 60, Input(OrbMotionMode.Rest)); // the fall starts at 3.0 s from 0.3
        Assert.Equal(0.3, motion.Act, 1e-12);
        motion.Step(3.1625, 1.0 / 60, Input(OrbMotionMode.Rest)); // x = 0.25: smoothstep 0.15625
        Assert.Equal(0.3 * (1 - 0.15625), motion.Act, 1e-12);
        motion.Step(3.325, 1.0 / 60, Input(OrbMotionMode.Rest)); // x = 0.5: 0.5
        Assert.Equal(0.15, motion.Act, 1e-12);
        motion.Step(3.65, 1.0 / 60, Input(OrbMotionMode.Rest));
        Assert.Equal(0, motion.Act, 1e-12);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4: "each from the value shown when the target changed": a retarget mid-ramp starts from the value on screen
    public void A_retarget_in_mid_ramp_starts_from_the_value_shown()
    {
        var motion = new OrbMotion(26);
        motion.Step(1.0, 1.0 / 60, Input(OrbMotionMode.Rest));
        motion.Step(2.0, 1.0 / 60, Input(OrbMotionMode.Rest, hovered: true));
        motion.Step(2.11, 1.0 / 60, Input(OrbMotionMode.Rest, hovered: true));
        Assert.Equal(0.2625, motion.Act, 1e-12);
        motion.Step(2.11, 1.0 / 60, Input(OrbMotionMode.Listening, hovered: true)); // toward 1: from 0.2625
        Assert.Equal(0.2625, motion.Act, 1e-12);
        motion.Step(2.22, 1.0 / 60, Input(OrbMotionMode.Listening, hovered: true)); // x = 0.5 -> 0.875 of the way
        Assert.Equal(0.2625 + (1 - 0.2625) * 0.875, motion.Act, 1e-12);
        // and down again from the middle: toward 0 from the value shown, smoothstep over 0.65 s
        var shown = motion.Act;
        motion.Step(2.5, 1.0 / 60, Input(OrbMotionMode.Rest));
        Assert.Equal(shown, motion.Act, 1e-12);
        motion.Step(2.825, 1.0 / 60, Input(OrbMotionMode.Rest));
        Assert.Equal(shown * 0.5, motion.Act, 1e-12);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4: speed follows with tau 0.05 up and down while Listening, else 0.2 up, 0.5 down; glow 0.12 / 0.25; lift 0.12 / 0.3; desat 0.117 / 0.2
    public void The_followers_use_the_published_time_constants()
    {
        double Follow(double cur, double target, double dt, double tau) => cur + (target - cur) * (1 - Math.Exp(-dt / tau));

        // Rest -> hover: speed 1 -> 2.0 with tau 0.2 up, glow 0 -> 0.55 with 0.12, lift 0 -> 0.15 with 0.12
        var motion = new OrbMotion(26);
        motion.Step(1.0, 0.1, Input(OrbMotionMode.Rest, hovered: true));
        Assert.Equal(Follow(1, 2.0, 0.1, 0.2), motion.Speed, 1e-12);
        Assert.Equal(Follow(0, 0.55, 0.1, 0.12), motion.Glow, 1e-12);
        Assert.Equal(Follow(0, 0.15, 0.1, 0.12), motion.Lift, 1e-12);

        // hover -> rest: speed 0.5 down, glow 0.25 down, lift 0.3 down
        var speed = motion.Speed;
        var glow = motion.Glow;
        var lift = motion.Lift;
        motion.Step(1.1, 0.04, Input(OrbMotionMode.Rest));
        Assert.Equal(Follow(speed, 1, 0.04, 0.5), motion.Speed, 1e-12);
        Assert.Equal(Follow(glow, 0, 0.04, 0.25), motion.Glow, 1e-12);
        Assert.Equal(Follow(lift, 0, 0.04, 0.3), motion.Lift, 1e-12);

        // listening: speed tau 0.05 both ways (the voice follower first: attack 0.12)
        var listening = new OrbMotion(26);
        listening.Step(1.0, 0.02, Input(OrbMotionMode.Listening, all: 1));
        var v = Follow(0, 1, 0.02, 0.12);
        Assert.Equal(v, listening.Voice, 1e-12);
        Assert.Equal(Follow(1, 1 + 2.2 * v, 0.02, 0.05), listening.Speed, 1e-12);
        var v0 = listening.Voice;
        var s0 = listening.Speed;
        listening.Step(1.02, 0.02, Input(OrbMotionMode.Listening, all: 0)); // voice release 0.45; speed falling uses 0.05 while Listening
        var v1 = Follow(v0, 0, 0.02, 0.45);
        Assert.Equal(v1, listening.Voice, 1e-12);
        Assert.Equal(Follow(s0, 1 + 2.2 * v1, 0.02, 0.05), listening.Speed, 1e-12);

        // blocked: desat 0 -> 1 with 0.117 (0.35 / 3) up; back with 0.2 (0.6 / 3) down
        var blocked = new OrbMotion(26);
        blocked.Step(1.0, 0.05, Input(OrbMotionMode.Blocked));
        Assert.Equal(Follow(0, 1, 0.05, 0.35 / 3), blocked.Desat, 1e-12);
        var d = blocked.Desat;
        blocked.Step(1.05, 0.05, Input(OrbMotionMode.Rest));
        Assert.Equal(Follow(d, 0, 0.05, 0.6 / 3), blocked.Desat, 1e-12);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4: spring integration equals the design's `spring` (fixture): six springs over 160 steps of mixed dt
    public void The_spring_equals_the_designs_spring()
    {
        using var doc = VoiceFixtureFile.Json("orb-motion.json");
        var springs = doc.RootElement.GetProperty("springs").EnumerateArray().ToList();
        Assert.Equal(6, springs.Count);
        foreach (var s in springs)
        {
            var dts = VoiceFixtureFile.Doubles(s.GetProperty("dts"));
            var target = s.GetProperty("target").GetDouble();
            var resp = s.GetProperty("resp").GetDouble();
            var zeta = s.GetProperty("zeta").GetDouble();
            double p = s.GetProperty("p0").GetDouble(), v = s.GetProperty("v0").GetDouble();
            var samples = s.GetProperty("samples").EnumerateArray().ToList();
            var next = 0;
            for (var i = 0; i < 160; i++)
            {
                OrbEasing.Spring(ref p, ref v, target, dts[i % dts.Length], resp, zeta);
                if (next < samples.Count && samples[next].GetProperty("i").GetInt32() == i)
                {
                    VoiceFixtureFile.AssertClose(samples[next].GetProperty("p").GetDouble(), p, $"spring ({resp}, {zeta}) step {i} position");
                    VoiceFixtureFile.AssertClose(samples[next].GetProperty("v").GetDouble(), v, $"spring ({resp}, {zeta}) step {i} velocity");
                    next++;
                }
            }

            Assert.Equal(samples.Count, next);
        }
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // voice-composer.js spring(): it snaps to the target inside 1e-4 position and 1e-3 velocity
    public void A_spring_snaps_to_its_target_when_it_has_settled()
    {
        double p = 1.00005, v = 0.0005;
        OrbEasing.Spring(ref p, ref v, 1, 0.004, 0.3, 0.62);
        Assert.Equal(1, p);
        Assert.Equal(0, v);
        double q = 1.5, w = 0;
        OrbEasing.Spring(ref q, ref w, 1, 0.004, 0.3, 0.62);
        Assert.NotEqual(1, q);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // voice-composer.js follow(), smooth(), easeOutCubic(): equal to the design's
    public void Follow_smooth_and_ease_out_cubic_equal_the_designs()
    {
        using var doc = VoiceFixtureFile.Json("orb-motion.json");
        foreach (var f in doc.RootElement.GetProperty("follow").EnumerateArray())
            VoiceFixtureFile.AssertClose(
                f.GetProperty("value").GetDouble(),
                OrbEasing.Follow(f.GetProperty("cur").GetDouble(), f.GetProperty("target").GetDouble(), f.GetProperty("dt").GetDouble(), f.GetProperty("up").GetDouble(), f.GetProperty("down").GetDouble()),
                "follow");
        Assert.Equal(0, OrbEasing.Smooth(-1));
        Assert.Equal(1, OrbEasing.Smooth(2));
        Assert.Equal(0.5, OrbEasing.Smooth(0.5));
        Assert.Equal(0, OrbEasing.EaseOutCubic(0));
        Assert.Equal(1, OrbEasing.EaseOutCubic(1.5));
        Assert.Equal(0.875, OrbEasing.EaseOutCubic(0.5));
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4: the whole integrator set against the design's code over a scripted run of 594 frames (rest, hover, press, listening with a voice, processing, landing, size change, blocked)
    public void A_scripted_run_equals_the_designs_integrators_frame_for_frame()
    {
        using var doc = VoiceFixtureFile.Json("orb-motion.json");
        var steps = doc.RootElement.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(594, steps.Count);
        var motion = new OrbMotion(26);
        var index = 0;
        foreach (var s in steps)
        {
            var mode = s.GetProperty("mode").GetString() switch
            {
                "rest" => OrbMotionMode.Rest,
                "listening" => OrbMotionMode.Listening,
                "processing" => OrbMotionMode.Processing,
                "error" => OrbMotionMode.Blocked,
                "landing" => OrbMotionMode.Landing,
                var other => throw new InvalidOperationException(other),
            };
            motion.Step(
                s.GetProperty("now").GetDouble(),
                s.GetProperty("dt").GetDouble(),
                Input(mode, s.GetProperty("hover").GetBoolean(), s.GetProperty("pressed").GetBoolean(), s.GetProperty("all").GetDouble(), s.GetProperty("size").GetDouble()));
            var at = $"step {index} ({mode}, now {s.GetProperty("now").GetDouble():R})";
            VoiceFixtureFile.AssertClose(s.GetProperty("act").GetDouble(), motion.Act, at + " act");
            VoiceFixtureFile.AssertClose(s.GetProperty("v").GetDouble(), motion.Voice, at + " voice");
            VoiceFixtureFile.AssertClose(s.GetProperty("speed").GetDouble(), motion.Speed, at + " speed");
            VoiceFixtureFile.AssertClose(s.GetProperty("scale").GetDouble(), motion.Scale, at + " scale");
            VoiceFixtureFile.AssertClose(s.GetProperty("scaleV").GetDouble(), motion.ScaleVelocity, at + " scale velocity");
            VoiceFixtureFile.AssertClose(s.GetProperty("glow").GetDouble(), motion.Glow, at + " glow");
            VoiceFixtureFile.AssertClose(s.GetProperty("lift").GetDouble(), motion.Lift, at + " lift");
            VoiceFixtureFile.AssertClose(s.GetProperty("desat").GetDouble(), motion.Desat, at + " desat");
            VoiceFixtureFile.AssertClose(s.GetProperty("d").GetDouble(), motion.Diameter, at + " diameter");
            VoiceFixtureFile.AssertClose(s.GetProperty("dV").GetDouble(), motion.DiameterVelocity, at + " diameter velocity");
            index++;
        }
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4: the slot size D springs to the setting with response 0.34 and damping 0.86
    public void The_slot_springs_to_the_setting_with_0_34_and_0_86()
    {
        var motion = new OrbMotion(26);
        var p = 26.0;
        var v = 0.0;
        var now = 1.0;
        for (var i = 0; i < 120; i++)
        {
            now += 1.0 / 60;
            OrbEasing.Spring(ref p, ref v, 32, 1.0 / 60, 0.34, 0.86);
            motion.Step(now, 1.0 / 60, Input(OrbMotionMode.Rest, slot: 32));
            VoiceFixtureFile.AssertClose(p, motion.Diameter, "diameter at frame " + i);
        }

        Assert.Equal(32, motion.Diameter);
        Assert.Equal(0, motion.DiameterVelocity);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // a frame longer than 0.05 s counts as 0.05 s (the page clamps its frame delta)
    public void A_long_frame_counts_as_fifty_milliseconds()
    {
        var a = new OrbMotion(26);
        var b = new OrbMotion(26);
        a.Step(1.0, 1.0, Input(OrbMotionMode.Listening, all: 0.8));
        b.Step(1.0, 0.05, Input(OrbMotionMode.Listening, all: 0.8));
        Assert.Equal(b.Voice, a.Voice);
        Assert.Equal(b.Speed, a.Speed);
        Assert.Equal(b.Scale, a.Scale);
        Assert.Equal(b.Glow, a.Glow);
        var c = new OrbMotion(26);
        c.Step(1.0, -3.0, Input(OrbMotionMode.Listening, all: 0.8));
        Assert.Equal(1, c.Speed); // a negative delta counts 0
        Assert.Equal(0, c.Voice);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4: reduce motion lands every integrator at once on its target (and the slot on its setting)
    public void Reduce_motion_lands_every_integrator_at_once()
    {
        var motion = new OrbMotion(26);
        motion.Step(1.0, 1.0 / 60, Input(OrbMotionMode.Rest, hovered: true, slot: 32, reduce: true));
        Assert.Equal(0.3, motion.Act);
        Assert.Equal(2.0, motion.Speed);
        Assert.Equal(1.04, motion.Scale);
        Assert.Equal(0, motion.ScaleVelocity);
        Assert.Equal(0.55, motion.Glow);
        Assert.Equal(0.15, motion.Lift);
        Assert.Equal(0, motion.Desat);
        Assert.Equal(32, motion.Diameter);
        Assert.Equal(0, motion.DiameterVelocity);

        // listening with a voice: the voice follower lands too, so the targets that depend on it are the settled ones
        motion.Step(1.02, 1.0 / 60, Input(OrbMotionMode.Listening, all: 0.5, slot: 32, reduce: true));
        Assert.Equal(1, motion.Act);
        Assert.Equal(0.5, motion.Voice);
        Assert.Equal(1 + 2.2 * 0.5, motion.Speed, 1e-12);
        Assert.Equal(1.16 + 0.04 * 0.5, motion.Scale, 1e-12);
        Assert.Equal(0.45 + 0.4 * 0.5, motion.Glow, 1e-12);
        Assert.Equal(1, motion.Lift);

        // blocked
        motion.Step(1.04, 1.0 / 60, Input(OrbMotionMode.Blocked, slot: 32, reduce: true));
        Assert.Equal(0, motion.Act);
        Assert.Equal(0.6, motion.Speed);
        Assert.Equal(1, motion.Desat);
        Assert.Equal(1, motion.Scale);

        // motion on again: it moves from where it landed
        motion.Step(1.06, 1.0 / 60, Input(OrbMotionMode.Rest, slot: 32));
        Assert.True(motion.Desat < 1 && motion.Desat > 0.8);
        Assert.True(motion.Speed > 0.6 && motion.Speed < 0.7);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // V-11.4: "While Listening the export's applyAudioUniforms takes the smoothed bands; otherwise none"; speed = shown speed x K.restBase
    public void The_frame_state_hands_the_orb_its_integrators_and_the_bands_only_while_Listening()
    {
        var motion = new OrbMotion(26);
        var bands = new OrbBands(Low: 0.2, Mid: 0.4, High: 0.1, All: 0.5);
        for (var i = 0; i < 30; i++)
            motion.Step(1.0 + i / 60.0, 1.0 / 60, new OrbMotionInput(OrbMotionMode.Listening, false, false, bands, 26));
        var listening = motion.FrameState(OrbMotionMode.Listening, bands, reduceMotion: false);
        Assert.Equal(motion.Act, listening.Act);
        Assert.Equal(motion.Speed * OrbMotionK.RestBase, listening.Speed);
        Assert.Equal(motion.Scale, listening.Scale);
        Assert.Equal(motion.Glow, listening.Glow);
        Assert.Equal(motion.Lift, listening.Lift);
        Assert.Equal(motion.Desat, listening.Desat);
        Assert.Equal(bands, listening.Bands);
        Assert.False(listening.ReduceMotion);
        foreach (var mode in new[] { OrbMotionMode.Rest, OrbMotionMode.Processing, OrbMotionMode.Blocked, OrbMotionMode.Landing })
            Assert.Null(motion.FrameState(mode, bands, reduceMotion: true).Bands);
        Assert.True(motion.FrameState(OrbMotionMode.Rest, bands, reduceMotion: true).ReduceMotion);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T27")]
    [Trait("Decision", "ED-267")] // the orb runtime and the motion together: a hovered Siri orb after half a second draws a faster, larger, glowing orb than a resting one
    public void The_motion_drives_the_runtime_end_to_end()
    {
        var rest = new VoiceOrbRuntime(VoiceOrbKind.Siri, new VoiceOrbRuntimeOptions(SizePx: 72, BallPx: 32));
        var hover = new VoiceOrbRuntime(VoiceOrbKind.Siri, new VoiceOrbRuntimeOptions(SizePx: 72, BallPx: 32));
        var restMotion = new OrbMotion(32);
        var hoverMotion = new OrbMotion(32);
        for (var i = 0; i <= 60; i++)
        {
            var now = 1.0 + i / 60.0;
            restMotion.Step(now, 1.0 / 60, Input(OrbMotionMode.Rest, slot: 32));
            hoverMotion.Step(now, 1.0 / 60, Input(OrbMotionMode.Rest, hovered: true, slot: 32));
            rest.Frame(now, restMotion.FrameState(OrbMotionMode.Rest, Silent, false));
            hover.Frame(now, hoverMotion.FrameState(OrbMotionMode.Rest, Silent, false));
        }

        Assert.True(hover.Values[4] > rest.Values[4]); // scale 1.04
        Assert.True(hover.Values[17] > rest.Values[17]); // glow
        Assert.True(hover.Phase > rest.Phase); // faster
        Assert.Equal(0.3, hoverMotion.Act, 1e-12);
    }
}
