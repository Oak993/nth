using System.Text.Json;
using Marrow.UI.Port.Settings.ColorPicker;

namespace Marrow.UI.Tests.Port.Settings;

// Unit CZ-2 (OD-0099 Look Picks, Q-CZ-3 B). SOURCE FACT: the picker's maths is lp-pickers.js's. tests/fixtures/customization/picker/
// gen-picker-fixture.mjs evaluates the design's own colour functions and recent-list expression under node (and guards the strip and
// square formulas against the design's text) and writes picker.json; these tests hold the C# maths to that output.
public sealed class PickerMathTests
{
    private const double Eps = 1e-9;

    private static JsonElement Fixture { get; } = Load();

    private static JsonElement Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "tests", "fixtures", "customization", "picker", "picker.json")));
        return json.RootElement.Clone();
    }

    private static double[] Numbers(JsonElement e) => e.EnumerateArray().Select(x => x.GetDouble()).ToArray();

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Hex_to_rgb_to_hsv_and_back_match_the_design()
    {
        foreach (var c in Fixture.GetProperty("colours").EnumerateArray())
        {
            var hex = c.GetProperty("hex").GetString()!;
            Assert.Equal(Numbers(c.GetProperty("rgb")), PickerMath.HexRgb(hex));
            var want = Numbers(c.GetProperty("hsv"));
            var color = PickerMath.FromHex(hex);
            Assert.Equal(want[0], color.H, Eps); Assert.Equal(want[1], color.S, Eps); Assert.Equal(want[2], color.V, Eps);
            Assert.Equal(1, color.A);
            Assert.Equal(c.GetProperty("back").GetString(), PickerMath.HsvHex(color));
            Assert.Equal(hex, PickerMath.HsvHex(color));
        }
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Hsv_to_rgb_and_the_rounding_of_a_hex_match_the_design()
    {
        foreach (var c in Fixture.GetProperty("hsv").EnumerateArray())
        {
            var got = PickerMath.HsvRgb(c.GetProperty("h").GetDouble(), c.GetProperty("s").GetDouble(), c.GetProperty("v").GetDouble());
            var want = Numbers(c.GetProperty("rgb"));
            for (var i = 0; i < 3; i++) Assert.Equal(want[i], got[i], Eps);
            Assert.Equal(c.GetProperty("hex").GetString(), PickerMath.RgbHex(got));
        }
        foreach (var c in Fixture.GetProperty("rgbHex").EnumerateArray())   // halves go up, out-of-range channels clamp
            Assert.Equal(c.GetProperty("hex").GetString(), PickerMath.RgbHex(Numbers(c.GetProperty("rgb"))));
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_squares_pointer_and_thumb_positions_match_the_design()
    {
        var sq = Fixture.GetProperty("square");
        double w = sq.GetProperty("width").GetDouble(), h = sq.GetProperty("height").GetDouble();
        foreach (var p in sq.GetProperty("points").EnumerateArray())
        {
            var (s, v) = PickerMath.SquareAt(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble(), w, h);
            Assert.Equal(p.GetProperty("s").GetDouble(), s, Eps);
            Assert.Equal(p.GetProperty("v").GetDouble(), v, Eps);
        }
        foreach (var t in sq.GetProperty("thumbs").EnumerateArray())
        {
            var (x, y) = PickerMath.SquareThumbAt(t.GetProperty("s").GetDouble(), t.GetProperty("v").GetDouble(), w, h);
            var want = Numbers(t.GetProperty("thumb"));
            Assert.Equal(want[0], x, Eps); Assert.Equal(want[1], y, Eps);
        }
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_strips_pointer_and_thumb_positions_match_the_design()
    {
        var strips = Fixture.GetProperty("strips");
        var w = strips.GetProperty("width").GetDouble();
        foreach (var p in strips.GetProperty("xs").EnumerateArray())
        {
            var x = p.GetProperty("x").GetDouble();
            Assert.Equal(p.GetProperty("u").GetDouble(), PickerMath.StripShare(x, w), Eps);
            Assert.Equal(p.GetProperty("hue").GetDouble(), PickerMath.HueAt(x, w), Eps);
            Assert.Equal(p.GetProperty("alpha").GetDouble(), PickerMath.AlphaAt(x, w), Eps);
        }
        foreach (var t in strips.GetProperty("thumbs").EnumerateArray())
            Assert.Equal(t.GetProperty("left").GetDouble(), PickerMath.StripThumbAt(t.GetProperty("u").GetDouble(), w), Eps);
        foreach (var t in strips.GetProperty("hueThumb").EnumerateArray())
            Assert.Equal(t.GetProperty("left").GetDouble(), PickerMath.StripThumbAt(t.GetProperty("h").GetDouble() / PickerMath.HueMax, w), Eps);
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_hex_field_takes_what_the_design_takes_and_an_eighth_pair_as_the_alpha_the_designs_other_field_reads()
    {
        foreach (var c in Fixture.GetProperty("hex").EnumerateArray())
        {
            var text = c.GetProperty("text").GetString();
            var got = PickerMath.ParseHexEntry(text);
            var b = c.GetProperty("b");
            var a = c.GetProperty("a");
            if (a.ValueKind == JsonValueKind.Null) { Assert.Null(got); continue; }
            Assert.NotNull(got);
            Assert.Equal(a.GetProperty("rgb").GetString(), got!.Value.Rgb);
            var alpha = a.GetProperty("alpha");
            if (alpha.ValueKind == JsonValueKind.Null) Assert.Null(got.Value.Alpha); else Assert.Equal(alpha.GetDouble(), got.Value.Alpha!.Value, Eps);
            if (b.ValueKind != JsonValueKind.Null) Assert.Equal(b.GetProperty("rgb").GetString(), got.Value.Rgb);   // everything the design's own picker takes is taken, the same
            else Assert.NotNull(got.Value.Alpha);                                                                      // what it does not is only the eight-digit form
        }
        Assert.Null(PickerMath.ParseHexEntry(null));
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_colour_is_stored_as_eight_digits_and_a_six_digit_one_reads_opaque()
    {
        Assert.Equal("#8E7CFF80", PickerMath.Stored(PickerMath.FromHex("#8E7CFF", 128 / 255.0)));
        Assert.Equal("#8E7CFFFF", PickerMath.Stored(PickerMath.FromHex("#8E7CFF")));
        Assert.Equal("#8E7CFF00", PickerMath.Stored(PickerMath.FromHex("#8E7CFF", 0)));
        var six = PickerMath.FromStored("#38c9b4");
        Assert.Equal(1, six!.Value.A);
        Assert.Equal("#38C9B4", PickerMath.HsvHex(six.Value));
        var eight = PickerMath.FromStored("#38C9B440");
        Assert.Equal(0x40 / 255.0, eight!.Value.A, Eps);
        Assert.Null(PickerMath.FromStored("#38C9B"));
        Assert.Null(PickerMath.FromStored(null));
        for (var hundredth = 0; hundredth <= 100; hundredth++)   // every opacity the strip can make survives storing
            Assert.Equal(hundredth, PickerMath.JsRound(PickerMath.FromStored(PickerMath.Stored(PickerMath.FromHex("#112233", hundredth / 100.0)))!.Value.A * 100));
    }
}
