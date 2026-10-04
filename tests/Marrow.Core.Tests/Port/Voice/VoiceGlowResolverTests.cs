using System.Globalization;
using Marrow.Core.Port.Voice.Glow;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-4 (voice-SPEC V-13.1, V-13.3; V-T29): the prop resolution of mountVoiceGlow (theme and type presets, clamps, colours) equals the design's,
// for 13 prop sets run through voice-glow.js in node (tests\fixtures\voice\glow-config.json), and Marrow's own configuration is the V-13.3 one.
public sealed class VoiceGlowResolverTests
{
    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T29")]
    [Trait("Decision", "ED-267")] // V-13.1: the 7 lobes (x 0, +-36, +-72, +-108; w 74/54/48/42; h 46/40/32/26)
    public void The_seven_lobes_are_the_designs()
    {
        using var doc = VoiceFixtureFile.Json("glow-config.json");
        var lobes = doc.RootElement.GetProperty("lobes").EnumerateArray().ToList();
        Assert.Equal(7, lobes.Count);
        Assert.Equal(7, VoiceGlowResolver.Lobes.Count);
        for (var i = 0; i < 7; i++)
        {
            Assert.Equal(lobes[i].GetProperty("x").GetDouble(), VoiceGlowResolver.Lobes[i].X);
            Assert.Equal(lobes[i].GetProperty("w").GetDouble(), VoiceGlowResolver.Lobes[i].W);
            Assert.Equal(lobes[i].GetProperty("h").GetDouble(), VoiceGlowResolver.Lobes[i].H);
            Assert.Equal(lobes[i].GetProperty("band").GetInt32(), VoiceGlowResolver.Lobes[i].Band);
        }

        Assert.Equal(new double[] { 0, -36, 36, -72, 72, -108, 108 }, VoiceGlowResolver.Lobes.Select(l => l.X).ToArray());
        Assert.Equal(new double[] { 74, 54, 54, 48, 48, 42, 42 }, VoiceGlowResolver.Lobes.Select(l => l.W).ToArray());
        Assert.Equal(new double[] { 46, 40, 40, 32, 32, 26, 26 }, VoiceGlowResolver.Lobes.Select(l => l.H).ToArray());
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T29")]
    [Trait("Decision", "ED-267")] // V-13.1: the driver's config (cfg) for every prop set equals the design's, key for key
    public void The_driver_config_equals_the_designs_for_every_prop_set()
    {
        using var doc = VoiceFixtureFile.Json("glow-config.json");
        var sets = doc.RootElement.GetProperty("resolved").EnumerateArray().ToList();
        Assert.Equal(13, sets.Count);
        foreach (var set in sets)
        {
            var name = set.GetProperty("name").GetString()!;
            var resolved = VoiceGlowResolver.Resolve(VoiceGlowFixtureReader.Props(set.GetProperty("props")));
            VoiceGlowFixtureReader.AssertRecord(resolved.Config, set.GetProperty("cfg"), name + " cfg", "id", "paused");
        }
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T29")]
    [Trait("Decision", "ED-267")] // V-13.1: the stylesheet recipe's inputs (Ya's arguments) for every prop set equal the design's
    public void The_stylesheet_inputs_equal_the_designs_for_every_prop_set()
    {
        using var doc = VoiceFixtureFile.Json("glow-config.json");
        foreach (var set in doc.RootElement.GetProperty("resolved").EnumerateArray())
        {
            var name = set.GetProperty("name").GetString()!;
            var resolved = VoiceGlowResolver.Resolve(VoiceGlowFixtureReader.Props(set.GetProperty("props")));
            VoiceGlowFixtureReader.AssertRecord(resolved.Style, set.GetProperty("style"), name + " style", "id");
        }
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T29")]
    [Trait("Decision", "ED-267")] // V-13.1: the wrapper's own values: --voice-strength, the distortion filter's frequency (0.012 x detail, 0.05 x detail), the core and distortion flags
    public void The_wrapper_values_equal_the_designs_for_every_prop_set()
    {
        using var doc = VoiceFixtureFile.Json("glow-config.json");
        foreach (var set in doc.RootElement.GetProperty("resolved").EnumerateArray())
        {
            var name = set.GetProperty("name").GetString()!;
            var resolved = VoiceGlowResolver.Resolve(VoiceGlowFixtureReader.Props(set.GetProperty("props")));
            VoiceFixtureFile.AssertClose(set.GetProperty("strength").GetDouble(), resolved.Strength, name + " strength");
            Assert.Equal(set.GetProperty("cfg").GetProperty("distortion").GetDouble() > 0, resolved.Distort);
            Assert.Equal(set.GetProperty("cfg").GetProperty("coreLight").GetDouble() > 0, resolved.Core);
            Assert.Equal(set.GetProperty("props").TryGetProperty("type", out var t) ? t.GetString() : "default", resolved.Type.ToString().ToLowerInvariant());
            var frequency = set.GetProperty("turbulenceFrequency");
            if (resolved.Distort)
            {
                var parts = frequency.GetString()!.Split(' ');
                Assert.Equal(double.Parse(parts[0], CultureInfo.InvariantCulture), 0.012 * resolved.Detail, 5.1e-5);
                Assert.Equal(double.Parse(parts[1], CultureInfo.InvariantCulture), 0.05 * resolved.Detail, 5.1e-5);
            }
            else
            {
                Assert.Equal(System.Text.Json.JsonValueKind.Null, frequency.ValueKind);
            }
        }
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T29")]
    [Trait("Decision", "ED-267")] // V-13.3: Marrow's configuration: type default, scale 0.7, glowWidth 1.6, lobeSpacing 2.1, rangeWidth 1.9, processingTravel 2.05, borderRadius 22; the rest the package defaults
    public void Marrows_configuration_resolves_to_the_published_numbers()
    {
        var props = new VoiceGlowProps
        {
            Type = VoiceGlowType.Default, Theme = VoiceGlowTheme.Dark, Scale = 0.7, GlowWidth = 1.6, LobeSpacing = 2.1, RangeWidth = 1.9,
            ProcessingTravel = 2.05, BorderRadius = 22,
        };
        var c = VoiceGlowResolver.Resolve(props).Config;
        Assert.Equal(0.7, c.Scale);
        Assert.Equal(22, c.Radius);
        Assert.Equal(2.05, c.ProcessingTravel);
        Assert.Equal(2.1 * 0.7, c.LobeSpacing, 1e-12); // lobeSpacing x scale
        Assert.Equal(1.9 * 0.7, c.RangeWidth, 1e-12);  // rangeWidth x scale
        Assert.Equal(0.7, c.RangeHeight, 1e-12);       // the package's rangeHeight 1 x scale
        Assert.Equal(48 * 0.7, c.Flow, 1e-12);         // the package's flow 48 x scale
        Assert.Equal(0.18, c.Idle);                    // package defaults
        Assert.Equal(1.2, c.Reach);
        Assert.Equal(1.05, c.Spread);
        Assert.Equal(0.325, c.Attack);
        Assert.Equal(0.86, c.Release);
        Assert.Equal(0.015, c.Threshold);
        Assert.Equal(3.1, c.Sensitivity);
        Assert.Equal(VoiceGlowTheme.Dark, c.Theme);
        Assert.Equal(new VoiceGlowBandColors("255, 255, 255", "255, 70, 80", "90, 255, 150", "80, 140, 255"), c.BandColors);
        Assert.False(c.StaticColors);
        var light = VoiceGlowResolver.Resolve(props with { Theme = VoiceGlowTheme.Light }).Config;
        Assert.Equal(new VoiceGlowBandColors("197, 139, 255", "255, 122, 182", "126, 196, 255", "45, 255, 171"), light.BandColors);
        Assert.Equal(40, light.HueRange);
        Assert.Equal(8.5, light.HueDuration);
        Assert.Equal(1.8, light.Reach); // light raises reach and spread unless the type fixes them
    }

    [Fact]
    [Trait("Feature", "VT-4")]
    [Trait("Spec", "V-T29")]
    [Trait("Decision", "ED-267")] // two equal prop sets give equal configs, so the driver can tell a real change (V-13.1 re-registration)
    public void Equal_props_resolve_to_equal_configs()
    {
        var props = new VoiceGlowProps { Scale = 0.7, BorderRadius = 22 };
        Assert.Equal(VoiceGlowResolver.Resolve(props).Config, VoiceGlowResolver.Resolve(props with { }).Config);
        Assert.NotEqual(VoiceGlowResolver.Resolve(props).Config, VoiceGlowResolver.Resolve(props with { Processing = true }).Config);
        Assert.NotEqual(VoiceGlowResolver.Resolve(props).Config, VoiceGlowResolver.Resolve(props with { ReducedMotion = true }).Config);
    }
}
