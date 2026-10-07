using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeInkVectorTests
{
    private static MethodInfo Method(string name)
    {
        var type = typeof(Strike).Assembly.GetType("Marrow.App.Views.Composer.StrikeInk"); Assert.NotNull(type);
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static); Assert.NotNull(method); return method;
    }
    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 1e-7);
    private static SKColor Color(JsonElement e)
    {
        var values = e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.GetByte()).ToArray() : e.GetString()!.Replace("rgb(", "", StringComparison.Ordinal).Replace(")", "", StringComparison.Ordinal).Split(',').Select(x => byte.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        return new(values[0], values[1], values[2]);
    }
    private static void Colors(JsonElement expected, dynamic actual)
    {
        Assert.Equal(Color(expected.GetProperty("outer")), (SKColor)actual.Tint.Outer); Assert.Equal(Color(expected.GetProperty("inner")), (SKColor)actual.Tint.Inner);
        Assert.Equal(Color(expected.GetProperty("wash")), (SKColor)actual.Tint.Wash); Assert.Equal(Color(expected.GetProperty("deep")), (SKColor)actual.Deep);
    }
    private static void Palette(JsonElement expected, dynamic actual)
    {
        var keys = new[] { "core", "midA", "midB", "haloA", "haloB" };
        for (var i = 0; i < keys.Length; i++) Assert.Equal(Color(expected.GetProperty(keys[i])), (SKColor)actual.At(i));
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void The_thirty_maker_palette_fixture_keeps_the_locked_source_bytes()
    {
        var path = Path.Combine(StrikeVectors.Folder, "liquid-palettes.json"); Assert.True(File.Exists(path));
        Assert.Equal("A600BAEFBCF22ACA861392456FF4D9C05088C37627FE30E95CD5142D5C441F15", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void All_thirty_makers_in_both_appearances_match_contrast_ink_rim_palette_tint_and_half_width()
    {
        var of = Method("Of"); using var doc = StrikeVectors.Read("ink.json");
        foreach (var row in doc.RootElement.GetProperty("makers").EnumerateArray())
        {
            var liquid = StrikeOracleLiquid.From(row.GetProperty("maker").GetString()!, row.GetProperty("theme").GetString()!);
            Assert.Equal(row.GetProperty("deep").GetString(), JewelColor.Hex(liquid.Deep)); Assert.Equal(row.GetProperty("light").GetString(), JewelColor.Hex(liquid.Light)); Assert.Equal(row.GetProperty("achro").GetBoolean(), liquid.Achro);
            dynamic actual = of.Invoke(null, [liquid])!; Near(row.GetProperty("contrast").GetDouble(), (double)actual.Contrast); Near(row.GetProperty("ink").GetDouble(), (double)actual.Amount); Near(row.GetProperty("hw").GetDouble(), (double)actual.HalfWidth);
            Assert.Equal(Color(row.GetProperty("inkc")), (SKColor)actual.InkColor); Palette(row.GetProperty("inkpal"), actual.Palette); Colors(row.GetProperty("tint"), actual);
        }
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void The_twenty_two_step_gamut_search_and_all_five_drifting_stops_match_exact_color_bytes()
    {
        var okc = Method("Okc"); var of = Method("Of"); using var doc = StrikeVectors.Read("ink.json");
        foreach (var row in doc.RootElement.GetProperty("okc").EnumerateArray()) Assert.Equal(Color(row.GetProperty("rgb")), (SKColor)okc.Invoke(null, [row.GetProperty("L").GetDouble(), row.GetProperty("C").GetDouble(), row.GetProperty("H").GetDouble()])!);
        foreach (var row in doc.RootElement.GetProperty("inkGrad").EnumerateArray())
        {
            dynamic actual = of.Invoke(null, [StrikeOracleLiquid.From(row.GetProperty("maker").GetString()!)])!;
            foreach (var halo in new[] { true, false })
            {
                var gradient = row.GetProperty(halo ? "halo" : "mid"); Assert.Equal(new[] { 0, 0, 298, 0 }, gradient.GetProperty("args").EnumerateArray().Select(x => x.GetInt32()));
                for (var i = 0; i < 5; i++) { Assert.Equal(i / 4d, gradient.GetProperty("stops")[i][0].GetDouble()); Assert.Equal(Color(gradient.GetProperty("stops")[i][1]), (SKColor)actual.Gradient(halo, row.GetProperty("t").GetDouble(), row.GetProperty("ph").GetDouble(), i)); }
            }
        }
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void All_mix_tint_channels_rims_and_palettes_match_the_locked_crossfade()
    {
        var of = Method("Of"); var mix = Method("Mix"); using var doc = StrikeVectors.Read("ink.json");
        foreach (var row in doc.RootElement.GetProperty("mixTint2").EnumerateArray())
        {
            var a = of.Invoke(null, [StrikeOracleLiquid.From(row.GetProperty("from").GetString()!)])!; var b = of.Invoke(null, [StrikeOracleLiquid.From(row.GetProperty("to").GetString()!)])!;
            dynamic actual = mix.Invoke(null, [a, b, row.GetProperty("u").GetDouble()])!; var expected = row.GetProperty("tint"); Colors(expected, actual); Near(expected.GetProperty("ink").GetDouble(), (double)actual.Amount);
            Assert.Equal(Color(expected.GetProperty("inkc")), (SKColor)actual.InkColor); Palette(expected.GetProperty("inkpal"), actual.Palette);
        }
    }
}
