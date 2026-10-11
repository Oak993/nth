using System.Text;
using Marrow.Core.Port.Settings;

namespace Marrow.Core.Tests.Port.Settings;

// ED-NEW-glasslook-2 (OD-0107; owner 2026-10-11: glass settings in Customization): the glassLook setting, its Look, knobs, ranges and JSON.
public sealed class GlassSettingsTests
{
    private static GlassSettings Read(string json) => GlassSettings.FromJson(Encoding.UTF8.GetBytes(json));

    private static string Json(GlassSettings settings) => Encoding.UTF8.GetString(settings.ToJson());

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // the defaults: every knob at its surface's own look (1, clarity the surface's own), written as {}
    public void The_default_is_every_surfaces_own_look_and_writes_nothing_but_braces()
    {
        var d = GlassSettings.Default;
        Assert.Equal("glassLook", GlassSettings.StoreKey);
        Assert.Equal(GlassVariant.Regular, d.Look);
        Assert.Equal((1d, 1d, (double?)null, 1d, 1d, 1d, 1d, 1d, 1d), (d.Refraction, d.Bezel, d.Clarity, d.Frost, d.Fringe, d.Rim, d.Tint, d.Saturation, d.Shadow));
        Assert.Equal("{}", Json(d));
        Assert.Equal(d, Read("{}"));
        Assert.Equal(["refraction", "bezel", "clarity", "frost", "fringe", "rim", "tint", "saturation", "shadow"], GlassSettings.Knobs.Select(k => k.Id));
    }

    [Theory]
    [Trait("Decision", "ED-NEW-glasslook-2")] // persistence: each knob writes its id and its value and reads back the same
    [InlineData("refraction", 1.35)]
    [InlineData("bezel", 0.8)]
    [InlineData("clarity", 0)]
    [InlineData("clarity", 0.65)]
    [InlineData("frost", 1.35)]
    [InlineData("fringe", 0.45)]
    [InlineData("rim", 0.4)]
    [InlineData("tint", 0.3)]
    [InlineData("saturation", 1.25)]
    [InlineData("shadow", 0)]
    public void Each_knob_writes_its_id_and_reads_back(string id, double value)
    {
        var settings = GlassSettings.Default.With(id, value);
        Assert.Equal((double?)value, settings.Get(id));
        var json = Json(settings);
        Assert.Equal("{\"" + id + "\":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", json);
        Assert.Equal(settings, Read(json));
        foreach (var other in GlassSettings.Knobs.Where(k => k.Id != id)) Assert.Equal(GlassSettings.Default.Get(other.Id), Read(json).Get(other.Id));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // persistence: every knob at once and every Look read back exactly; the Look is written first, Regular not at all
    public void Every_look_and_a_mixed_setting_read_back()
    {
        var mixed = new GlassSettings { Look = GlassVariant.Tinted, Refraction = 1.25, Bezel = 1.1, Clarity = 0.4, Frost = 0.85, Fringe = 0.2, Rim = 1.15, Tint = 0.8, Saturation = 0.6, Shadow = 1.4 };
        Assert.Equal(mixed, Read(Json(mixed)));
        Assert.StartsWith("{\"look\":\"tinted\",\"refraction\":1.25,", Json(mixed), StringComparison.Ordinal);
        foreach (var (id, _, look) in GlassSettings.Looks)
        {
            var only = GlassSettings.Default with { Look = look };
            Assert.Equal(look == GlassVariant.Regular ? "{}" : "{\"look\":\"" + id + "\"}", Json(only));
            Assert.Equal(only, Read(Json(only)));
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // the Look, as Apple's Liquid Glass setting (iOS 27): Clear, Regular, Tinted in that order; Regular the default
    public void The_looks_are_clear_regular_and_tinted()
    {
        Assert.Equal(["clear", "regular", "tinted"], GlassSettings.Looks.Select(l => l.Id));
        Assert.Equal(["Clear", "Regular", "Tinted"], GlassSettings.Looks.Select(l => l.Name));
        Assert.Equal([GlassVariant.Clear, GlassVariant.Regular, GlassVariant.Tinted], GlassSettings.Looks.Select(l => l.Value));
        Assert.Equal("clear", GlassSettings.IdOf(GlassVariant.Clear));
        Assert.Equal("tinted", GlassSettings.IdOf(GlassVariant.Tinted));
        Assert.Equal(GlassVariant.Clear, Read("{\"look\":\"clear\",\"tint\":0.5}").Look);
        Assert.Equal(0.5, Read("{\"look\":\"clear\",\"tint\":0.5}").Tint);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // the lenient read of the Look: an unknown or mistyped Look is Regular and the knobs still read; an undefined Look is held to Regular
    public void An_unknown_look_is_regular()
    {
        Assert.Equal(GlassSettings.Default with { Tint = 0.5 }, Read("{\"look\":\"frosted\",\"tint\":0.5}"));
        Assert.Equal(GlassSettings.Default, Read("{\"look\":2}"));
        Assert.Equal(GlassSettings.Default, Read("{\"look\":\"Clear\"}"));
        Assert.Equal(GlassSettings.Default, Read("{\"look\":null}"));
        var undefined = GlassSettings.Default with { Look = (GlassVariant)7 };
        Assert.Equal(GlassVariant.Regular, undefined.Held().Look);
        Assert.Equal("{}", Json(undefined));
    }

    [Theory]
    [Trait("Decision", "ED-NEW-glasslook-3")] // clamping: a value past a knob's range is held to it, by With and by the read; no knob reaches a plastic look (the owner's principles) and text on glass stays at 4.5:1
    [InlineData("refraction", 9, 2)]
    [InlineData("refraction", -1, 0)]
    [InlineData("bezel", 0.1, 0.75)]
    [InlineData("bezel", 4, 1.25)]
    [InlineData("clarity", 3, 1)]
    [InlineData("clarity", -2, 0)]
    [InlineData("frost", 0, 0.5)]
    [InlineData("frost", 7, 1.5)]
    [InlineData("fringe", 10, 1)]
    [InlineData("rim", 5, 1.25)]
    [InlineData("tint", 4, 1)]
    [InlineData("tint", -1, 0)]
    [InlineData("saturation", 3, 1.5)]
    [InlineData("saturation", 0, 0.5)]
    [InlineData("shadow", -0.5, 0)]
    [InlineData("shadow", 4, 1.5)]
    public void A_value_past_its_range_is_held_to_it(string id, double value, double held)
    {
        Assert.Equal((double?)held, GlassSettings.Default.With(id, value).Get(id));
        Assert.Equal((double?)held, Read("{\"" + id + "\":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}").Get(id));
        Assert.Equal((double?)held, (GlassSettings.Default with { Refraction = 1 }).With(id, value).Held().Get(id));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // clamping: a record made past a range is held when it is written; a non-finite value is the default's
    public void A_record_made_past_its_ranges_is_held_when_written_and_non_finite_is_the_default()
    {
        var wild = new GlassSettings { Refraction = 40, Bezel = 0, Clarity = 7, Frost = 0.01, Fringe = -3, Rim = 9, Tint = 99, Saturation = -1, Shadow = 12 };
        Assert.Equal("{\"refraction\":2,\"bezel\":0.75,\"clarity\":1,\"frost\":0.5,\"fringe\":0,\"rim\":1.25,\"saturation\":0.5,\"shadow\":1.5}", Json(wild));
        Assert.Equal(1.0, GlassSettings.Default.With("rim", double.NaN).Rim);
        Assert.Equal(1.0, GlassSettings.Default.With("tint", double.PositiveInfinity).Tint);
        Assert.Equal(0.5, GlassSettings.Knobs.Single(k => k.Id == "frost").Min);
        // the owner's principles (ED-NEW-glasslook-3): never more fill than the surface's own, a fringe never wider, a rim at most a quarter brighter
        Assert.Equal(1.0, GlassSettings.Knobs.Single(k => k.Id == "tint").Max);
        Assert.Equal(1.0, GlassSettings.Knobs.Single(k => k.Id == "fringe").Max);
        Assert.Equal(1.25, GlassSettings.Knobs.Single(k => k.Id == "rim").Max);
        Assert.Equal(2.0, GlassSettings.Knobs.Single(k => k.Id == "refraction").Max);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // the lenient read: unreadable data is the default; a mistyped or unknown key keeps its default
    public void Unreadable_or_mistyped_data_keeps_the_default()
    {
        foreach (var bad in new[] { "{oops", "[]", "3", "null", "\"tint\"", "" }) Assert.Equal(GlassSettings.Default, Read(bad));
        var read = Read("{\"tint\":\"lots\",\"rim\":true,\"frost\":null,\"wobble\":3,\"fringe\":0.5}");
        Assert.Equal(GlassSettings.Default with { Fringe = 0.5 }, read);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // clarity alone may be the surface's own (null); every other knob takes a number
    public void Only_clarity_takes_the_surfaces_own()
    {
        var clear = GlassSettings.Default.With("clarity", 0.8);
        Assert.Null(clear.With("clarity", null).Clarity);
        Assert.Throws<ArgumentNullException>(() => GlassSettings.Default.With("tint", null));
        Assert.Throws<ArgumentOutOfRangeException>(() => GlassSettings.Default.With("wobble", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GlassSettings.Default.Get("wobble"));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // Reset glides: from where the knobs are to the defaults, every knob between the two ends, at the target's Look, the end exact
    public void A_glide_passes_between_the_ends_and_lands_on_the_target_exactly()
    {
        var start = new GlassSettings { Look = GlassVariant.Clear, Refraction = 2, Clarity = 1, Frost = 0.6, Tint = 0.2, Rim = 1.2 };
        var to = GlassSettings.Default;
        Assert.Equal(start with { Look = GlassVariant.Regular }, start.Toward(to, 0, 0.3));
        Assert.Equal(1.0, start.Toward(to, 0, 0.3).Clarity);
        Assert.Same(to, start.Toward(to, 1, 0.3));
        var half = start.Toward(to, 0.5, 0.3);
        Assert.Equal(GlassVariant.Regular, half.Look); // the Look itself crossfades in the material (GlassLooks.Blend)
        Assert.Equal((start.Refraction + to.Refraction) / 2, half.Refraction, 9);
        Assert.Equal((1.0 + 0.3) / 2, half.Clarity!.Value, 9);
        Assert.Equal((start.Tint + to.Tint) / 2, half.Tint, 9);
        Assert.Equal((start.Frost + to.Frost) / 2, half.Frost, 9);
        var previous = start.Refraction;
        for (var step = 1; step < 10; step++)
        {
            var now = start.Toward(to, step / 10.0, 0.3).Refraction;
            Assert.True(now < previous, "the refraction falls steadily at step " + step.ToString(System.Globalization.CultureInfo.InvariantCulture));
            previous = now;
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // a new Look glides the Look alone: the knobs stay, and Auto clarity at both ends stays Auto (each surface its own) all the way
    public void A_look_change_keeps_the_knobs_and_auto_clarity()
    {
        var start = GlassSettings.Default.With("tint", 0.6);
        var to = start with { Look = GlassVariant.Tinted };
        for (var step = 1; step < 10; step++)
        {
            var now = start.Toward(to, step / 10.0, 0.42);
            Assert.Equal(to, now);
            Assert.Null(now.Clarity);
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // the page shows each knob as a percentage of the surface's own
    public void A_knob_shows_as_a_percentage()
    {
        var knob = GlassSettings.Knobs[0];
        Assert.Equal("100%", knob.Format(1));
        Assert.Equal("235%", knob.Format(2.35));
        Assert.Equal("0%", knob.Format(0));
    }
}
