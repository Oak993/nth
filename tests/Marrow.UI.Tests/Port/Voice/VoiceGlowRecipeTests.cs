using System.Text.Json;
using Marrow.UI.Port.Voice.Glow;

namespace Marrow.UI.Tests.Port.Voice;

// VT-9, V-T60 (voice-SPEC V-13.1, V-13.2): the stylesheet recipe `Ya` of voice-glow.js as numbers. tests/fixtures/voice/make-vt9-fixtures.js calls the design's Ya() for the 13 prop
// sets of VT-4's glow-config.json and reads the numbers back out of the stylesheet it returns (every lobe's width, height, offset, colour, alpha and fade, the masks, the wash,
// the inner light's edge depth and shadow, the bloom's blur, the stroke ring, the epicentre, the three opacities and the filter functions); VoiceGlowRecipe must carry the same.
public sealed class VoiceGlowRecipeTests
{
    private const int Digits = 9;

    private static List<(string Name, VoiceGlowRecipe Recipe, JsonElement Facts)> Sets()
    {
        using var doc = VoiceGlowRecipeFixture.Load();
        var list = new List<(string, VoiceGlowRecipe, JsonElement)>();
        foreach (var set in doc.RootElement.GetProperty("sets").EnumerateArray())
        {
            var style = VoiceGlowRecipeFixture.Style(set.GetProperty("style"));
            list.Add((set.GetProperty("name").GetString()!, VoiceGlowRecipe.From(style), set.GetProperty("facts").Clone()));
        }
        return list;
    }

    private static void AssertLobes(string what, GlowLobeSet set, JsonElement lobes)
    {
        var list = lobes.EnumerateArray().ToList();
        Assert.Equal(7, list.Count);
        for (var i = 0; i < 7; i++)
        {
            var l = list[i];
            Assert.Equal(i, l.GetProperty("k").GetInt32());
            Assert.Equal(l.GetProperty("w").GetDouble(), set.Width(i), Digits);
            Assert.Equal(l.GetProperty("h").GetDouble(), set.Height(i), Digits);
            Assert.Equal(l.GetProperty("y").GetDouble(), set.Y, Digits);
            Assert.Equal(l.GetProperty("fade").GetDouble(), set.FadePercent, Digits);
            Assert.Equal(l.GetProperty("alpha").GetDouble(), set.Alpha, Digits);
            var rgb = l.GetProperty("rgb").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var color = set.Colors[i % set.Colors.Count];
            Assert.True(rgb.SequenceEqual([color.R, color.G, color.B]), $"{what} lobe {i}: colour {color} vs {string.Join(", ", rgb)}");
        }
    }

    private static void AssertMask(GlowEllipseMask mask, JsonElement facts)
    {
        Assert.Equal(facts.GetProperty("w").GetDouble(), mask.Width, Digits);
        Assert.Equal(facts.GetProperty("h").GetDouble(), mask.Height, Digits);
        Assert.Equal(facts.GetProperty("half").GetDouble(), mask.HalfPercent, Digits);
        Assert.Equal(facts.GetProperty("tail").GetDouble(), mask.Tail, Digits);
    }

    private static void AssertFilter(VoiceGlowRecipe recipe, JsonElement filter)
    {
        Assert.Equal(filter.GetProperty("hueBase").GetDouble(), recipe.HueBase, Digits);
        Assert.Equal(filter.GetProperty("brightness").GetDouble(), recipe.Brightness, Digits);
        Assert.Equal(filter.GetProperty("saturate").GetDouble(), recipe.Saturation, Digits);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_recipe_has_the_stylesheets_numbers_for_the_thirteen_prop_sets()
    {
        var count = 0;
        foreach (var (name, recipe, facts) in Sets())
        {
            count++;
            var stroke = facts.GetProperty("stroke");
            AssertLobes(name + " stroke", recipe.Stroke, stroke.GetProperty("lobes"));
            AssertMask(recipe.StrokeMask, stroke.GetProperty("mask"));
            var wash = stroke.GetProperty("wash");
            Assert.Equal(wash.GetProperty("w").GetDouble(), recipe.Wash.Width, Digits);
            Assert.Equal(wash.GetProperty("h").GetDouble(), recipe.Wash.Height, Digits);
            Assert.Equal(wash.GetProperty("rgb")[0].GetInt32(), recipe.Wash.Grey);
            Assert.Equal(wash.GetProperty("first").GetDouble(), recipe.Wash.First, Digits);
            Assert.Equal(wash.GetProperty("second").GetDouble(), recipe.Wash.Second, Digits);
            Assert.Equal(wash.GetProperty("secondAt").GetDouble(), recipe.Wash.SecondPercent, Digits);
            Assert.Equal(wash.GetProperty("endAt").GetDouble(), recipe.Wash.EndPercent, Digits);
            Assert.Equal(stroke.GetProperty("opacity").GetDouble(), recipe.StrokeOpacity, Digits);
            Assert.Equal(stroke.GetProperty("ringRadius").GetDouble(), recipe.RingRadius, Digits);
            Assert.Equal(stroke.GetProperty("padding").GetDouble(), recipe.BorderWidth, Digits);
            AssertFilter(recipe, stroke.GetProperty("filter"));

            var inner = facts.GetProperty("inner");
            AssertLobes(name + " inner", recipe.Inner, inner.GetProperty("lobes"));
            AssertMask(recipe.InnerMask, inner.GetProperty("mask"));
            Assert.Equal(inner.GetProperty("edge").GetDouble(), recipe.EdgeMargin, Digits);
            var shadow = inner.GetProperty("shadow");
            Assert.Equal(shadow.GetProperty("blur").GetDouble(), recipe.ShadowBlur, Digits);
            var rgba = shadow.GetProperty("rgba").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            Assert.Equal(rgba[0], recipe.InnerShadow.Red);
            Assert.Equal(rgba[1], recipe.InnerShadow.Green);
            Assert.Equal(rgba[2], recipe.InnerShadow.Blue);
            Assert.Equal(Math.Round(rgba[3] * 255, MidpointRounding.AwayFromZero), recipe.InnerShadow.Alpha);
            Assert.Equal(inner.GetProperty("radius").GetDouble(), recipe.Radius, Digits);
            Assert.Equal(inner.GetProperty("opacity").GetDouble(), recipe.InnerOpacity, Digits);
            AssertFilter(recipe, inner.GetProperty("filter"));

            var bloom = facts.GetProperty("bloom");
            AssertLobes(name + " bloom", recipe.Bloom, bloom.GetProperty("lobes"));
            AssertMask(recipe.BloomMask, bloom.GetProperty("mask"));
            Assert.Equal(bloom.GetProperty("opacity").GetDouble(), recipe.BloomOpacity, Digits);
            Assert.Equal(bloom.GetProperty("filter").GetProperty("blur").GetDouble(), recipe.BloomBlur, Digits);
            Assert.Equal(bloom.GetProperty("radius").GetDouble(), recipe.RingRadius, Digits);
            AssertFilter(recipe, bloom.GetProperty("filter"));

            var core = facts.GetProperty("core");
            if (core.ValueKind == JsonValueKind.Null)
            {
                Assert.Null(recipe.Core);
                continue;
            }
            Assert.NotNull(recipe.Core);
            Assert.Equal(core.GetProperty("w").GetDouble(), recipe.Core.Width, Digits);
            Assert.Equal(core.GetProperty("h").GetDouble(), recipe.Core.Height, Digits);
            Assert.Equal(core.GetProperty("solidAt").GetDouble(), recipe.Core.SolidPercent, Digits);
            Assert.Equal(core.GetProperty("midAlpha").GetDouble(), recipe.Core.MidAlpha, Digits);
            Assert.Equal(core.GetProperty("midAt").GetDouble(), recipe.Core.MidPercent, Digits);
            Assert.Equal(core.GetProperty("endAt").GetDouble(), recipe.Core.EndPercent, Digits);
            Assert.Equal(core.GetProperty("blur").GetDouble(), recipe.Core.Blur, Digits);
            Assert.Equal(core.GetProperty("opacityA").GetDouble() * core.GetProperty("opacityB").GetDouble(), recipe.Core.OpacityScale, Digits);
        }
        Assert.Equal(13, count);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void Marrows_two_themes_have_the_darks_pastels_and_the_lights_deep_colours_with_an_epicentre_only_on_light()
    {
        var sets = Sets().ToDictionary(s => s.Name);
        var dark = sets["Marrow dark"].Recipe;
        var light = sets["Marrow light"].Recipe;
        Assert.True(dark.Dark);
        Assert.False(light.Dark);
        Assert.Null(dark.Core);
        Assert.NotNull(light.Core);
        Assert.Equal((1.16, 0.47, 0.89, 1.15, 1.2), (dark.StrokeOpacity, dark.InnerOpacity, dark.BloomOpacity, dark.Brightness, dark.Saturation));
        Assert.Equal((1.2, 0.85, 0.5, 0.95, 1.6), (light.StrokeOpacity, light.InnerOpacity, light.BloomOpacity, light.Brightness, light.Saturation));
        Assert.Equal(5, light.HueBase);
        Assert.Equal(0.9, dark.Bloom.Alpha);
        Assert.Equal(0.7, light.Bloom.Alpha);
        Assert.Equal(22, dark.Radius);
        Assert.Equal(21, dark.RingRadius);
        Assert.True(dark.Distortion && light.Distortion);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_mono_variant_runs_at_six_tenths_of_every_opacity()
    {
        var mono = Sets().Single(s => s.Name == "mono variant").Recipe;
        Assert.Equal(VoiceGlowCss.Fixed(1.16 * 0.6, 2), mono.StrokeOpacity);
        Assert.Equal(VoiceGlowCss.Fixed(0.47 * 0.6, 2), mono.InnerOpacity);
        Assert.Equal(VoiceGlowCss.Fixed(0.89 * 0.6, 2), mono.BloomOpacity);
    }
}
