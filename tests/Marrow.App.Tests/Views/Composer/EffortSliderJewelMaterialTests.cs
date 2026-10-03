using System.Text.Json;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// DEVIATION OD-0090 (locked 2026-10-03): the knob is the Jewel orb in its maker's colours. Every expected value here was
// computed by the locked page's own code, run with node by tests/fixtures/jewel-knob/expected.mjs:
// knob-variants.js liquidOf(palettes, maker, 'dark') -> material('jewel', liquid, 48) / strikeTint(liquid) / mixMaterial /
// mixTint (E:/Droppy/work/design/jewel-knob-locked/, sha256 in expected.json).
public sealed class EffortSliderJewelMaterialTests
{
    [Theory]
    [Trait("Decision", "ED-252")] // OD-0090: knob-variants.js jewel() at the app's 48 px knob ('m'), each maker's high liquid
    [InlineData("Claude")]
    [InlineData("OpenAI")]
    [InlineData("Gemini")]
    [InlineData("DeepSeek")]
    [InlineData("Grok")]
    [InlineData("Zai")]
    [InlineData("Pi")]
    [InlineData("Cursor")]
    public void The_jewel_material_of_each_maker_is_the_pages(string maker)
    {
        var expected = JewelFixtures.Expected.GetProperty("materials").GetProperty(maker).GetProperty("material");
        var material = JewelMaterial.For(JewelFixtures.BrandOf(maker));
        AssertMaterial(expected, material);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: spot values, material('jewel', liquidOf(P, 'Claude', 'dark'), 48) in node
    public void Claudes_jewel_is_a_dark_terracotta_core_with_a_bright_inner_fire()
    {
        var m = JewelMaterial.For(EffortBrand.Claude);
        Assert.Equal("#310A00", JewelColor.Hex(m.Think.ColorA)); // the core
        Assert.Equal("#C34500", JewelColor.Hex(m.Think.ColorC)); // the body
        Assert.Equal("#F59F79", JewelColor.Hex(m.Think.ColorD)); // the fire
        Assert.Equal("#E3926E", JewelColor.Hex(m.Idle.ColorD));
        Assert.Equal("#EAAE98", JewelColor.Hex(m.Surge));
        Assert.Equal((0.58, 0.24), (m.FxCharge, m.FxFlare));
        Assert.Equal((2.6, 4.2), (m.Idle.Speed, m.Think.Speed)); // the gem rests at 2.6 and the charge carries it to 4.2
    }

    [Theory]
    [Trait("Decision", "ED-252")] // OD-0090: jewel() by size: 's' up to 24 px, 'm' up to 64 px, 'l' above (the preset's own folds)
    [InlineData("materials140", 140)]
    [InlineData("materials24", 24)]
    public void The_recipe_calms_the_folds_by_the_knobs_size(string key, double px)
    {
        AssertMaterial(JewelFixtures.Expected.GetProperty(key), JewelMaterial.For(JewelLiquid.Of(EffortBrand.Claude), px));
    }

    [Theory]
    [Trait("Decision", "ED-252")] // OD-0090: strikeTint(liquid): the locked board's three kept, every other maker's derived
    [InlineData("Claude")]
    [InlineData("OpenAI")]
    [InlineData("Gemini")]
    [InlineData("DeepSeek")]
    [InlineData("Grok")]
    [InlineData("Zai")]
    [InlineData("Pi")]
    [InlineData("Cursor")]
    public void The_lightning_wears_its_makers_tint(string maker)
    {
        var expected = JewelFixtures.Expected.GetProperty("materials").GetProperty(maker).GetProperty("tint");
        var tint = StrikeTint.For(JewelFixtures.BrandOf(maker));
        Assert.Equal(JewelFixtures.Rgb(expected.GetProperty("outer").GetString()!), tint.Outer);
        Assert.Equal(JewelFixtures.Rgb(expected.GetProperty("inner").GetString()!), tint.Inner);
        Assert.Equal(JewelFixtures.Rgb(expected.GetProperty("wash").GetString()!), tint.Wash);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: mixMaterial / mixTint (Claude to OpenAI) at a quarter, half and three quarters
    public void A_model_change_mixes_the_materials_in_oklab_and_the_tints_channel_by_channel()
    {
        var claude = JewelMaterial.For(EffortBrand.Claude);
        var openAI = JewelMaterial.For(EffortBrand.Silver);
        foreach (var step in JewelFixtures.Expected.GetProperty("mix").EnumerateArray())
        {
            var u = step.GetProperty("u").GetDouble();
            AssertMaterial(step.GetProperty("material"), JewelMaterial.Mix(claude, openAI, u));
            var tint = step.GetProperty("tint");
            var mixed = StrikeTint.Mix(StrikeTint.For(EffortBrand.Claude), StrikeTint.For(EffortBrand.Silver), u);
            Assert.Equal(JewelFixtures.Rgb(tint.GetProperty("outer").GetString()!), mixed.Outer);
            Assert.Equal(JewelFixtures.Rgb(tint.GetProperty("inner").GetString()!), mixed.Inner);
            Assert.Equal(JewelFixtures.Rgb(tint.GetProperty("wash").GetString()!), mixed.Wash);
        }
        Assert.Same(openAI, JewelMaterial.Mix(claude, openAI, 1));
        Assert.Same(claude, JewelMaterial.Mix(claude, openAI, 0));
    }

    [Fact]
    [Trait("Decision", "ED-252")] // knob-variants.js:302 mixMaterial: fx too (mixObj), here to a blue liquid whose material takes a smaller share of the lightning (DeepSeek: 0.36 / 0.15)
    public void A_model_change_to_a_blue_liquid_mixes_the_lightnings_share_too()
    {
        var claude = JewelMaterial.For(EffortBrand.Claude);
        var deepseek = JewelMaterial.For(EffortBrand.Deepseek);
        Assert.NotEqual(claude.FxCharge, deepseek.FxCharge);
        foreach (var step in JewelFixtures.Expected.GetProperty("mixBlue").EnumerateArray())
            AssertMaterial(step.GetProperty("material"), JewelMaterial.Mix(claude, deepseek, step.GetProperty("u").GetDouble()));
    }

    private static void AssertMaterial(JsonElement expected, JewelMaterial material)
    {
        Assert.Equal(expected.GetProperty("liquid").GetString(), JewelColor.Hex(material.Liquid));
        Assert.Equal(expected.GetProperty("surge").GetString(), JewelColor.Hex(material.Surge));
        Assert.Equal(expected.GetProperty("fx").GetProperty("charge").GetDouble(), material.FxCharge, 12);
        Assert.Equal(expected.GetProperty("fx").GetProperty("flare").GetDouble(), material.FxFlare, 12);
        foreach (var (name, state) in new[] { ("think", material.Think), ("idle", material.Idle) })
        {
            var e = expected.GetProperty(name);
            foreach (var (key, value) in JewelFixtures.Colours(state))
                Assert.True(e.GetProperty(key).GetString() == JewelColor.Hex(value), $"{name}.{key}: page {e.GetProperty(key).GetString()}, port {JewelColor.Hex(value)}");
            foreach (var (key, value) in JewelFixtures.Numbers(state))
            {
                if (e.TryGetProperty(key, out var number)) Assert.True(value is { } v && Math.Abs(v - number.GetDouble()) < 1e-12, $"{name}.{key}: page {number}, port {value}");
                else Assert.Null(value);
            }
        }
    }
}
