using System.Security.Cryptography;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-4 (voice-SPEC V-11.1, V-11.2; V-T28): the two presets' stateSeeds are the owner's exports, float for float. The expected floats are read
// from tests\fixtures\voice\seeds.json, a byte copy of E:\Droppy\work\voice\reference\orb-exports\seeds.json (make-vt4-fixtures.js copies it
// and, on every run, proves the design's voice-orbs.js SEEDS literal equals it).
public sealed class OrbSeedsTests
{
    [Theory]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.1: stateSeeds byte-identical to seeds.json
    [InlineData(VoiceOrbKind.Siri, "siri-wave")]
    [InlineData(VoiceOrbKind.Membrane, "voice-membrane")]
    public void Each_seed_equals_seeds_json_float_for_float(VoiceOrbKind kind, string preset)
    {
        using var doc = VoiceFixtureFile.Json("seeds.json");
        var seeds = doc.RootElement.GetProperty(preset).GetProperty("stateSeeds");
        foreach (var (state, actual) in new[] { ("idle", OrbSeeds.Idle(kind).ToArray()), ("thinking", OrbSeeds.Thinking(kind).ToArray()) })
        {
            var expected = VoiceFixtureFile.Floats(seeds.GetProperty(state));
            Assert.Equal(136, expected.Length);
            Assert.Equal(OrbSeeds.Length, actual.Length);
            for (var i = 0; i < expected.Length; i++)
                Assert.True(BitConverter.SingleToInt32Bits(expected[i]) == BitConverter.SingleToInt32Bits(actual[i]), $"{preset} {state}[{i}]: expected {expected[i]:R}, got {actual[i]:R}");
        }
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.1: the literals in the port were generated from this very seeds.json
    public void The_port_literals_were_generated_from_the_seeds_json_beside_the_fixtures()
    {
        var sha = Convert.ToHexString(SHA256.HashData(VoiceFixtureFile.Bytes("seeds.json"))).ToLowerInvariant();
        Assert.Equal(OrbSeeds.SourceSha256, sha);
        using var runtime = VoiceFixtureFile.Json("orb-runtime.json");
        Assert.Equal(sha, runtime.RootElement.GetProperty("seedsSha256").GetString());
    }

    [Theory]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.1: Siri Wave = style 9, Voice Membrane = style 19; the page backgrounds of the exports
    [InlineData(VoiceOrbKind.Siri, 9, "#030409")]
    [InlineData(VoiceOrbKind.Membrane, 19, "#020105")]
    public void Style_numbers_and_backgrounds_are_the_exports(VoiceOrbKind kind, int style, string background)
    {
        Assert.Equal(style, OrbSeeds.Style(kind));
        Assert.Equal(style, (int)Math.Round(OrbSeeds.Idle(kind)[15]));
        Assert.Equal(style, (int)Math.Round(OrbSeeds.Thinking(kind)[15]));
        Assert.Equal(background, OrbSeeds.Background(kind));
    }

    [Theory]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T28")]
    [Trait("Decision", "ED-267")] // V-11.2: rest speed = the preset's Idle speed: Siri Wave 0.246, Voice Membrane 0.266
    [InlineData(VoiceOrbKind.Siri, 0.246f)]
    [InlineData(VoiceOrbKind.Membrane, 0.266f)]
    public void The_rest_speed_is_the_Idle_seed_speed(VoiceOrbKind kind, float speed) => Assert.Equal(speed, OrbSeeds.IdleSpeed(kind));
}
