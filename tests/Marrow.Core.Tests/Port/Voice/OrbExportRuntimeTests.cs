using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-4 (voice-SPEC V-11.1; V-T28): the export runtime equals the design's. Expected values come from orb-runtime.json, recorded by running
// voice-orbs.js in node (values arrays compared float for float), and from hand-worked cases of the export's rules.
public sealed class OrbExportRuntimeTests
{
    private static VoiceOrbKind Kind(string preset) => preset == "siri-wave" ? VoiceOrbKind.Siri : VoiceOrbKind.Membrane;

    private static void AssertSameFloats(float[] expected, float[] actual, string what)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.True(BitConverter.SingleToInt32Bits(expected[i]) == BitConverter.SingleToInt32Bits(actual[i]) || (expected[i] == 0 && actual[i] == 0),
                $"{what}[{i}]: expected {expected[i]:R}, got {actual[i]:R}");
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.1: srgbToLinear and linearToSrgb equal the export's
    public void The_sRGB_curves_equal_the_exports()
    {
        using var doc = VoiceFixtureFile.Json("orb-runtime.json");
        foreach (var row in doc.RootElement.GetProperty("srgb").EnumerateArray())
        {
            var x = VoiceFixtureFile.Number(row.GetProperty("x"));
            VoiceFixtureFile.AssertClose(VoiceFixtureFile.Number(row.GetProperty("toLinear")), OrbExportRuntime.SrgbToLinear(x), "srgbToLinear(" + x + ")");
            VoiceFixtureFile.AssertClose(VoiceFixtureFile.Number(row.GetProperty("toSrgb")), OrbExportRuntime.LinearToSrgb(x), "linearToSrgb(" + x + ")");
        }
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.1: mixSrgb mixes in linear light
    public void MixSrgb_equals_the_export_and_mixes_in_linear_light()
    {
        using var doc = VoiceFixtureFile.Json("orb-runtime.json");
        foreach (var row in doc.RootElement.GetProperty("mix").EnumerateArray())
        {
            var from = VoiceFixtureFile.Number(row.GetProperty("from"));
            var to = VoiceFixtureFile.Number(row.GetProperty("to"));
            var p = VoiceFixtureFile.Number(row.GetProperty("p"));
            VoiceFixtureFile.AssertClose(VoiceFixtureFile.Number(row.GetProperty("value")), OrbExportRuntime.MixSrgb(from, to, p), $"mixSrgb({from}, {to}, {p})");
        }

        // linear-light mixing of 0.1 and 0.9 lands well above the arithmetic mean 0.5
        var mid = OrbExportRuntime.MixSrgb(0.1, 0.9, 0.5);
        Assert.InRange(mid, 0.6, 0.7);
        VoiceFixtureFile.AssertClose(0.1, OrbExportRuntime.MixSrgb(0.1, 0.9, 0), "p = 0");
        VoiceFixtureFile.AssertClose(0.9, OrbExportRuntime.MixSrgb(0.1, 0.9, 1), "p = 1");
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.1: sampleTransition for both presets at several progress values, float for float
    public void SampleTransition_equals_the_export_float_for_float()
    {
        using var doc = VoiceFixtureFile.Json("orb-runtime.json");
        var rows = doc.RootElement.GetProperty("transitions").EnumerateArray().ToList();
        Assert.Equal(12, rows.Count);
        foreach (var row in rows)
        {
            var kind = Kind(row.GetProperty("orb").GetString()!);
            var progress = VoiceFixtureFile.Number(row.GetProperty("progress"));
            var displayed = new float[OrbSeeds.Length];
            OrbExportRuntime.SampleTransition(OrbSeeds.Idle(kind), OrbSeeds.Thinking(kind), progress, displayed);
            var expected = VoiceFixtureFile.Floats(row.GetProperty("values"));
            // indexes 0 to 2 are the caller's: the export leaves them as they were (zero in a fresh array)
            for (var i = 0; i < 3; i++) expected[i] = 0;
            AssertSameFloats(expected, displayed, $"{kind} sampleTransition({progress})");
        }
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.1: index >= 40 and (index - 40) % 4 < 3 mixed in linear light, the rest linearly; indexes 0..2 untouched
    public void SampleTransition_mixes_colours_in_linear_light_and_everything_else_linearly()
    {
        var from = new float[OrbSeeds.Length];
        var target = new float[OrbSeeds.Length];
        var displayed = new float[OrbSeeds.Length];
        Array.Fill(displayed, -7f);
        from[39] = 2f; target[39] = 4f;     // a number below 40: linear
        from[40] = 0.1f; target[40] = 0.9f; // a colour component: linear light
        from[43] = 0f; target[43] = 1f;     // alpha of the first colour ((43 - 40) % 4 == 3): linear
        from[44] = 0.1f; target[44] = 0.9f; // the next colour's red: linear light
        from[135] = 10f; target[135] = 20f; // (135 - 40) % 4 == 3: linear
        OrbExportRuntime.SampleTransition(from, target, 0.5, displayed);

        Assert.Equal(-7f, displayed[0]);
        Assert.Equal(-7f, displayed[1]);
        Assert.Equal(-7f, displayed[2]);
        Assert.Equal(3f, displayed[39]);
        Assert.Equal(0.5f, displayed[43]);
        Assert.Equal(15f, displayed[135]);
        Assert.InRange(displayed[40], 0.6f, 0.7f);
        Assert.Equal(displayed[40], displayed[44]);
        Assert.Equal((float)OrbExportRuntime.MixSrgb(0.1f, 0.9f, 0.5), displayed[40]);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.1: applyAudioUniforms for both presets and a range of band inputs, float for float
    public void ApplyAudioUniforms_equals_the_export_float_for_float()
    {
        using var doc = VoiceFixtureFile.Json("orb-runtime.json");
        var rows = doc.RootElement.GetProperty("audio").EnumerateArray().ToList();
        Assert.Equal(14, rows.Count);
        foreach (var row in rows)
        {
            var kind = Kind(row.GetProperty("orb").GetString()!);
            var values = new float[OrbSeeds.Length];
            OrbExportRuntime.SampleTransition(OrbSeeds.Idle(kind), OrbSeeds.Thinking(kind), 0.4, values);
            if (row.TryGetProperty("style", out var style)) values[15] = (float)style.GetInt32();
            var b = row.GetProperty("bands");
            var bands = new OrbBands(
                Low: VoiceFixtureFile.Number(b.GetProperty("low")), Mid: VoiceFixtureFile.Number(b.GetProperty("mid")),
                High: VoiceFixtureFile.Number(b.GetProperty("high")), All: VoiceFixtureFile.Number(b.GetProperty("all")));
            OrbExportRuntime.ApplyAudioUniforms(values, bands);
            var expected = VoiceFixtureFile.Floats(row.GetProperty("values"));
            for (var i = 0; i < 3; i++) expected[i] = 0;
            AssertSameFloats(expected, values, $"{kind} applyAudioUniforms({bands})");
        }
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.1: the five audio rules and the six flow strengths, as the export lists them
    public void The_rule_table_and_flow_strengths_are_the_exports()
    {
        using var doc = VoiceFixtureFile.Json("orb-runtime.json");
        var rules = doc.RootElement.GetProperty("audioRules").EnumerateArray().ToList();
        Assert.Equal(rules.Count, OrbExportRuntime.AudioRules.Count);
        for (var i = 0; i < rules.Count; i++)
        {
            var r = OrbExportRuntime.AudioRules[i];
            Assert.Equal(rules[i][0].GetInt32(), r.Index);
            Assert.Equal(rules[i][1].GetString(), r.Band.ToString().ToLowerInvariant());
            Assert.Equal(rules[i][2].GetDouble(), r.Additive);
            Assert.Equal(rules[i][3].GetDouble(), r.Proportional);
            Assert.Equal(rules[i][4].GetDouble(), r.Ceiling);
        }

        var strengths = doc.RootElement.GetProperty("audioFlowStrengths");
        foreach (var style in new[] { 9, 10, 11, 14, 19, 21 })
            Assert.Equal(strengths.GetProperty(style.ToString(System.Globalization.CultureInfo.InvariantCulture)).GetDouble(), OrbExportRuntime.AudioFlowStrength(style));
        Assert.Equal(0, OrbExportRuntime.AudioFlowStrength(5));
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.1: a worked case of the rules on a zeroed uniform block of Voice Membrane (strength 1), every band at 1
    public void Every_band_at_one_moves_the_five_uniforms_by_their_rules()
    {
        var values = new float[OrbSeeds.Length];
        values[15] = 19;
        values[3] = 1;
        values[14] = 1;
        OrbExportRuntime.ApplyAudioUniforms(values, new OrbBands(Low: 1, Mid: 1, High: 1, All: 1));
        Assert.Equal(1.7f, values[3]);    // min(max(5, 1), 1 * (1 + 0.7) + 0)
        Assert.Equal(0.85f, values[6]);   // min(max(7, 0), 0 * 1 + 0.85)
        Assert.Equal(0.075f, values[21]); // min(max(1, 0), 0 + 0.075)
        Assert.Equal(0.16f, values[10]);  // min(max(2, 0), 0 + 0.16)
        Assert.Equal(1.12f, values[14]);  // min(max(4, 1), 1 * (1 + 0.12) + 0)
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.1: Siri Wave's strength is 0.8: all at 1 on a speed of 0.246 gives 0.246 * (1 + 0.7 * 0.8)
    public void Siri_wave_takes_the_audio_at_strength_zero_point_eight()
    {
        var values = new float[OrbSeeds.Length];
        values[15] = 9;
        values[3] = 0.246f;
        OrbExportRuntime.ApplyAudioUniforms(values, new OrbBands(Low: 0, Mid: 0, High: 0, All: 1));
        Assert.Equal((float)(0.246f * (1 + 0.7 * 0.8)), values[3]);
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.1: a band that is not finite counts as 0, one above 1 as 1, one below 0 as 0
    public void Bands_outside_zero_to_one_or_not_finite_are_clamped_or_ignored()
    {
        var quiet = new float[OrbSeeds.Length];
        quiet[15] = 19;
        quiet[3] = 1;
        var loud = (float[])quiet.Clone();
        OrbExportRuntime.ApplyAudioUniforms(quiet, new OrbBands(Low: -3, Mid: double.NaN, High: double.NegativeInfinity, All: double.PositiveInfinity));
        OrbExportRuntime.ApplyAudioUniforms(loud, new OrbBands(Low: 0, Mid: 0, High: 0, All: 1));
        Assert.Equal(1f, quiet[3]);   // All = +Infinity is not finite: ignored
        Assert.Equal(0f, quiet[6]);
        Assert.Equal(0f, quiet[21]);
        Assert.Equal(0f, quiet[10]);
        Assert.Equal(1.7f, loud[3]);

        var over = new float[OrbSeeds.Length];
        over[15] = 19;
        over[3] = 1;
        OrbExportRuntime.ApplyAudioUniforms(over, new OrbBands(Low: 0, Mid: 0, High: 0, All: 9));
        Assert.Equal(1.7f, over[3]);
    }
}
