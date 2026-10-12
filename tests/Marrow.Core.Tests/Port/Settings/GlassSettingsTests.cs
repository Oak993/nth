using System.Text;
using Marrow.Core.Port.Settings;

namespace Marrow.Core.Tests.Port.Settings;

// ED-NEW-glasslook-2 (OD-0107; owner 2026-10-11: glass settings in Customization) and OD-0147 (the owner approved the design canvas and asked for
// every one of its Glass settings, shipped at its defaults): the glass setting, the canvas's Look stops and Transparency slider, its eight
// fine-tuning values with their ranges, steps and units, its behaviour switches, and the JSON. Every expected number below is the canvas's own
// (E:/Droppy/work/design/glass-ui/index.html: LOOKS, FINE, TINTS, lookAt, lookName, stopName, isCustom, Reset).
public sealed class GlassSettingsTests
{
    private static GlassSettings Read(string json) => GlassSettings.FromJson(Encoding.UTF8.GetBytes(json));

    private static string Json(GlassSettings settings) => Encoding.UTF8.GetString(settings.ToJson());

    private static double[] Values(GlassSettings s) => [s.Refraction, s.Bezel, s.Frost, s.Fringe, s.Rim, s.Tint, s.Saturation, s.Shadow];

    // the canvas's LOOKS
    private static readonly double[] Clear = [1.40, 12, 24, 0.5, 0.55, 0.04, 1.25, 1];
    private static readonly double[] Regular = [1.30, 12, 32, 0.4, 0.50, 0.12, 1.22, 1];
    private static readonly double[] Tinted = [1.10, 11, 44, 0.3, 0.50, 0.24, 1.15, 1];

    [Fact]
    [Trait("Decision", "OD-0147")] // the shipped defaults: Regular at 50, every value the canvas's Regular, no tint colour, the switches following Windows, the sidebar translucent; written as {}
    public void The_default_is_the_canvas_regular_and_writes_nothing_but_braces()
    {
        var d = GlassSettings.Default;
        Assert.Equal("glass", GlassSettings.StoreKey);
        Assert.Equal(50, d.Transparency);
        Assert.Equal(GlassVariant.Regular, d.Look);
        Assert.Equal(Regular, Values(d));
        Assert.Equal(GlassTintColour.None, d.TintColour);
        Assert.Equal(((bool?)null, (bool?)null, (bool?)null, true), (d.ReducedMotion, d.IncreaseContrast, d.ReduceTransparency, d.TranslucentSidebar));
        Assert.False(d.IsCustom);
        Assert.Equal("{}", Json(d));
        Assert.Equal(d, Read("{}"));
        Assert.Equal(["refraction", "bezel", "frost", "fringe", "rim", "tint", "saturation", "shadow"], GlassSettings.Knobs.Select(k => k.Id));
    }

    [Fact]
    [Trait("Decision", "OD-0147")] // the canvas's FINE table: every name, description, range, step and unit
    public void The_fine_tuning_values_are_the_canvas_names_ranges_steps_and_units()
    {
        Assert.Equal(
        [
            ("Refraction", 0.4, 2.4, 0.05, "1.30\u00d7"), ("Bezel width", 6d, 24d, 0.5, "12.0 px"), ("Frost", 20d, 64d, 1d, "32 px"),
            ("Colour fringe", 0d, 1d, 0.05, "0.40 px"), ("Rim light", 0.1, 1d, 0.01, "0.50"), ("Tint", 0d, 0.5, 0.01, "0.12"),
            ("Saturation", 0.8, 1.8, 0.01, "1.22\u00d7"), ("Shadow depth", 0d, 2d, 0.05, "1.00\u00d7"),
        ], GlassSettings.Knobs.Select(k => (k.Name, k.Min, k.Max, k.Step, k.Format(GlassSettings.Default.Get(k.Id)))));
        Assert.Equal(
        [
            "Bend at the edge, times the bezel; also sets the bezel magnification.", "How far in from the edge the lens reaches.",
            "Blur radius of the body; never below 20, the text scrim grows as it drops.", "Red-to-blue split in the sharp rim, at most 1 px.",
            "The one-pixel line on the lit side.", "Takes colour out (not light) and calms bright content.", "Colour of what shows through.",
            "Soft shadow and contact shadow.",
        ], GlassSettings.Knobs.Select(k => k.Description));
        var t = GlassSettings.TransparencyKnob;
        Assert.Equal(("Transparency", "From more clear to more tinted.", 0d, 100d, 1d, "50"), (t.Name, t.Description, t.Min, t.Max, t.Step, t.Format(50)));
    }

    [Fact]
    [Trait("Decision", "OD-0147")] // the Look stops: Clear at 0, Regular at 50, Tinted at 100, each the canvas's values; between them every value in between (lookAt) and no Look
    public void The_looks_are_the_canvas_stops_and_the_slider_blends_between_them()
    {
        Assert.Equal(new[] { ("clear", "Clear", GlassVariant.Clear, 0d), ("regular", "Regular", GlassVariant.Regular, 50d), ("tinted", "Tinted", GlassVariant.Tinted, 100d) }, GlassSettings.Looks);
        Assert.Equal(Clear, Values(GlassSettings.Default.At(GlassVariant.Clear)));
        Assert.Equal(Tinted, Values(GlassSettings.Default.At(GlassVariant.Tinted)));
        Assert.Equal(Regular, Values(GlassSettings.Default.At(0d).At(GlassVariant.Regular)));
        var quarter = GlassSettings.Default.At(25);
        Assert.Null(quarter.Look);
        Assert.Equal(Clear.Zip(Regular, (a, b) => Math.Round(a + (b - a) * 0.5, 4)), Values(quarter));
        var late = GlassSettings.Default.At(90);
        Assert.Equal(Regular.Zip(Tinted, (a, b) => Math.Round(a + (b - a) * 0.8, 4)), Values(late));
        Assert.False(quarter.IsCustom);
        Assert.Equal((GlassVariant?)GlassVariant.Clear, GlassSettings.Default.At(0d).Look);
        Assert.Equal((GlassVariant?)GlassVariant.Tinted, GlassSettings.Default.At(100).Look);
        // the slider's words: More clear under 20, Default to 80, More tinted past it
        Assert.Equal(["More clear", "Default", "Default", "More tinted"], new[] { 19d, 20, 79, 80 }.Select(GlassSettings.LookName));
    }

    [Fact]
    [Trait("Decision", "OD-0147")] // moving the slider sets every fine-tuning value to the look there (the canvas's setG) and keeps the switches; whole steps, held to 0 to 100
    public void The_slider_sets_every_value_and_keeps_the_switches()
    {
        var tuned = GlassSettings.Default.With("rim", 0.9) with { TintColour = GlassTintColour.Blue, IncreaseContrast = true, TranslucentSidebar = false };
        Assert.True(tuned.IsCustom);
        var moved = tuned.With("transparency", 74.6);
        Assert.Equal(75, moved.Transparency);
        Assert.False(moved.IsCustom);
        Assert.Equal(Values(GlassSettings.Default.At(75)), Values(moved));
        Assert.Equal((GlassTintColour.Blue, (bool?)true, false), (moved.TintColour, moved.IncreaseContrast, moved.TranslucentSidebar));
        Assert.Equal(0, GlassSettings.Default.With("transparency", -40).Transparency);
        Assert.Equal(100, GlassSettings.Default.With("transparency", 140).Transparency);
    }

    [Theory]
    [Trait("Decision", "OD-0147")] // persistence: each fine-tuning value writes its id and its value and reads back the same, at any slider position
    [InlineData("refraction", 2.05, 50)]
    [InlineData("bezel", 18.5, 50)]
    [InlineData("frost", 58, 50)]
    [InlineData("fringe", 0.85, 0)]
    [InlineData("rim", 0.95, 100)]
    [InlineData("tint", 0.45, 50)]
    [InlineData("saturation", 1.65, 30)]
    [InlineData("shadow", 0, 50)]
    public void Each_value_writes_its_id_and_reads_back(string id, double value, double at)
    {
        var settings = GlassSettings.Default.At(at).With(id, value);
        Assert.True(settings.IsCustom);
        var json = Json(settings);
        Assert.Contains("\"" + id + "\":", json, StringComparison.Ordinal);
        var back = Read(json);
        Assert.Equal(settings, back);
        Assert.Equal(value, back.Get(id));
        Assert.Equal(at, back.Transparency);
    }

    [Fact]
    [Trait("Decision", "OD-0147")] // persistence of the slider, the tint colour and every switch, alone and together, with the user's choice of a switch (true or false) kept apart from Windows' (null)
    public void The_slider_the_tint_colour_and_every_switch_read_back()
    {
        foreach (var settings in new[]
        {
            GlassSettings.Default.At(0d), GlassSettings.Default.At(100), GlassSettings.Default.At(63),
            GlassSettings.Default with { TintColour = GlassTintColour.Green }, GlassSettings.Default with { ReducedMotion = true }, GlassSettings.Default with { ReducedMotion = false },
            GlassSettings.Default with { IncreaseContrast = true }, GlassSettings.Default with { IncreaseContrast = false },
            GlassSettings.Default with { ReduceTransparency = true }, GlassSettings.Default with { ReduceTransparency = false },
            GlassSettings.Default with { TranslucentSidebar = false },
            GlassSettings.Default.At(12).With("bezel", 20) with { TintColour = GlassTintColour.Pink, ReducedMotion = true, IncreaseContrast = true, ReduceTransparency = false, TranslucentSidebar = false },
        })
            Assert.Equal(settings, Read(Json(settings)));
        Assert.Equal("{\"transparency\":0}", Json(GlassSettings.Default.At(0d)));
        Assert.Equal("{\"tintColour\":\"silver\",\"motion\":\"full\",\"increaseContrast\":false,\"reduceTransparency\":true,\"translucentSidebar\":false}",
            Json(GlassSettings.Default with { TintColour = GlassTintColour.Silver, ReducedMotion = false, IncreaseContrast = false, ReduceTransparency = true, TranslucentSidebar = false }));
        Assert.Equal(["none", "silver", "blue", "purple", "pink", "orange", "green"], GlassSettings.TintColours.Select(c => c.Id));
        var blue = GlassSettings.TintColours.Single(c => c.Value == GlassTintColour.Blue);
        Assert.Equal((0d, 0.48, 1d), (blue.R, blue.G, blue.B));
    }

    [Theory]
    [Trait("Decision", "OD-0147")] // range clamps: a value past its range is held to it, set or read (the anti-plastic clamps: frost never under 20, a fringe of 1 px at most, a rim line of 1 at most, a tint of 0.5 at most)
    [InlineData("refraction", 9, 2.4)]
    [InlineData("refraction", 0, 0.4)]
    [InlineData("bezel", 2, 6)]
    [InlineData("bezel", 80, 24)]
    [InlineData("frost", 4, 20)]
    [InlineData("frost", 200, 64)]
    [InlineData("fringe", 5, 1)]
    [InlineData("fringe", -1, 0)]
    [InlineData("rim", 3, 1)]
    [InlineData("rim", 0, 0.1)]
    [InlineData("tint", 0.9, 0.5)]
    [InlineData("tint", -0.2, 0)]
    [InlineData("saturation", 4, 1.8)]
    [InlineData("saturation", 0.2, 0.8)]
    [InlineData("shadow", 5, 2)]
    [InlineData("shadow", -1, 0)]
    public void A_value_past_its_range_is_held_to_it(string id, double value, double held)
    {
        Assert.Equal(held, GlassSettings.Default.With(id, value).Get(id));
        Assert.Equal(held, Read("{\"" + id + "\":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}").Get(id));
    }

    [Fact]
    [Trait("Decision", "OD-0147")] // settings made past their ranges are held when written; a value that is not a number is the low end, an unknown tint colour none
    public void A_record_made_past_its_ranges_is_held_when_written()
    {
        var wild = new GlassSettings { Transparency = 250, Refraction = 9, Bezel = -4, Frost = 1, Fringe = 3, Rim = 7, Tint = 2, Saturation = 9, Shadow = double.NaN, TintColour = (GlassTintColour)42 };
        var held = wild.Held();
        Assert.Equal((100d, 2.4, 6d, 20d, 1d, 1d, 0.5, 1.8, 0d, GlassTintColour.None), (held.Transparency, held.Refraction, held.Bezel, held.Frost, held.Fringe, held.Rim, held.Tint, held.Saturation, held.Shadow, held.TintColour));
        Assert.Equal(held, Read(Json(wild)));
    }

    [Fact]
    [Trait("Decision", "OD-0147")] // the lenient read: unreadable, mistyped or unknown data keeps each default; the earlier glassLook schema is a different key
    public void Unreadable_or_mistyped_data_keeps_the_default()
    {
        foreach (var json in new[] { "", "not json", "[]", "7", "{\"refraction\":\"big\"}", "{\"transparency\":true}", "{\"tintColour\":\"plaid\"}", "{\"motion\":\"wild\"}", "{\"increaseContrast\":1}", "{\"translucentSidebar\":\"no\"}" })
            Assert.Equal(GlassSettings.Default, Read(json));
        Assert.NotEqual("glassLook", GlassSettings.StoreKey);
    }

    [Fact]
    [Trait("Decision", "OD-0147")] // the canvas's Reset: the fine-tuning back to the look at the slider; the slider and the switches stay
    public void Reset_fine_tuning_returns_to_the_look_at_the_slider_and_keeps_the_rest()
    {
        var tuned = GlassSettings.Default.At(80).With("frost", 60).With("tint", 0.4) with { TintColour = GlassTintColour.Orange, ReduceTransparency = true };
        Assert.True(tuned.IsCustom);
        var reset = tuned.ResetFineTuning();
        Assert.False(reset.IsCustom);
        Assert.Equal(Values(GlassSettings.Default.At(80)), Values(reset));
        Assert.Equal((80d, GlassTintColour.Orange, (bool?)true), (reset.Transparency, reset.TintColour, reset.ReduceTransparency));
        // a difference under the canvas's 0.001 is not custom
        Assert.False(GlassSettings.Default.With("rim", 0.5005).IsCustom);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // a glide on the page passes between the ends and lands on the target exactly; the switches are the target's throughout
    public void A_glide_passes_between_the_ends_and_lands_on_the_target_exactly()
    {
        var from = GlassSettings.Default.At(0d);
        var to = GlassSettings.Default.At(100).With("bezel", 20) with { IncreaseContrast = true, TintColour = GlassTintColour.Blue };
        Assert.Equal(from, from.Toward(to, 0));
        Assert.Equal(to, from.Toward(to, 1));
        var half = from.Toward(to, 0.5);
        Assert.Equal(50, half.Transparency);
        Assert.Equal((12 + 20) / 2.0, half.Bezel);
        Assert.Equal((24 + 44) / 2.0, half.Frost);
        Assert.Equal(((bool?)true, GlassTintColour.Blue), (half.IncreaseContrast, half.TintColour));
    }
}
