using System.Text.Json;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Settings;

namespace Marrow.Core.Tests.Port.Settings;

// customization-SPEC CZ-6, CZ-7 (the rows' details, value formats and dimmed rules): the expected text is emitted by node from the locked cz.js's own beamRows(), auraRows() and resetRow()
// (tests/fixtures/customization/page.generate.cjs), not by this port.
public sealed class CzRowTextTests
{
    private static JsonDocument Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir!.FullName, "tests/fixtures/customization/page.json")));
    }

    private static ComposerLightSettings Settings(string style, JsonElement? beam, JsonElement? aura)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("style", style);
            if (beam is { } b) { w.WritePropertyName("beam"); b.WriteTo(w); }
            if (aura is { } a) { w.WritePropertyName("aura"); a.WriteTo(w); }
            w.WriteEndObject();
        }
        return ComposerLightSettings.FromJson(stream.ToArray());
    }

    private static string D(JsonElement row) => row.GetProperty("d").GetString()!;
    private static string Vt(JsonElement row) => row.GetProperty("vt").GetString()!;
    private static double V(JsonElement row) => double.Parse(row.GetProperty("v").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    private static bool IsAuto(JsonElement row) => row.GetProperty("auOn").GetString() == "true";

    [Fact]
    [Trait("Feature", "CZ-4")]
    [Trait("Spec", "CZ-T14")]
    [Trait("Decision", "ED-251")]
    public void Every_border_beam_row_says_what_the_design_says_for_every_branch()
    {
        using var f = Fixture();
        var cases = 0;
        foreach (var c in f.RootElement.GetProperty("beams").EnumerateArray())
        {
            var name = c.GetProperty("name").GetString();
            var b = Settings("beam", c.GetProperty("beam"), null).Beam;
            var rows = c.GetProperty("rows");
            JsonElement R(string key) => rows.GetProperty(key);
            var mono = b.Palette == BeamPalette.Mono;
            var line = b.Type == BeamType.Line;
            Assert.True(CzRowText.MotionDetail(b.Family) == D(R("motion")), name);
            Assert.True(CzText.ShapeDetails[ComposerLightSettings.TypeName(b.Type)] == D(R("shape")), name);
            Assert.True(CzText.PaletteNames[ComposerLightSettings.PaletteName(b.Palette)] == D(R("colours")), name);
            Assert.True(CzRowText.SpeedDetail(b.Duration is null, b.Type) == D(R("speed")), name);
            Assert.True(CzRowText.Seconds(V(R("speed"))) == Vt(R("speed")), name);
            Assert.True(CzRowText.BrightnessDetail(b.Brightness is null) == D(R("bright")), name);
            Assert.True(CzRowText.X2(V(R("bright"))) == Vt(R("bright")), name);
            Assert.True(CzRowText.Percent01(V(R("strength"))) == Vt(R("strength")), name);
            Assert.True(CzRowText.Deg(V(R("hue"))) == Vt(R("hue")), name);
            Assert.True(CzRowText.SaturationDetail(b.Saturation is null) == D(R("sat")), name);
            Assert.True(CzRowText.X2(V(R("sat"))) == Vt(R("sat")), name);
            Assert.True(CzRowText.DriftDetail(b) == D(R("drift")), name);
            Assert.True(CzRowText.Drift(b.HueRange, line) == Vt(R("drift")), name);
            Assert.True(CzRowText.DriftDimmed(b) == R("drift").GetProperty("dim").GetBoolean(), name);
            Assert.True(CzRowText.HoldDetail(mono) == D(R("still")), name);
            Assert.True(mono == R("still").GetProperty("dim").GetBoolean(), name);
            Assert.True((mono || b.StaticColors) == (R("still").GetProperty("on").GetString() == "true"), name);
            foreach (var key in new[] { "glow", "edge", "inner", "bloom" }) Assert.True(CzRowText.X2(V(R(key))) == Vt(R(key)), name + key);
            Assert.True(CzRowText.RadiusDetail(b.Radius is null) == D(R("radius")), name);
            Assert.True(CzRowText.Corner(V(R("radius"))) == Vt(R("radius")), name);
            Assert.True(CzRowText.TunedDetail(b.Theme) == D(R("theme")), name);
            Assert.True(CzRowText.Px(V(R("core"))) == Vt(R("core")), name);
            Assert.True(CzRowText.Px(V(R("bblur"))) == Vt(R("bblur")), name);
            Assert.True(CzRowText.X2(V(R("gx"))) == Vt(R("gx")), name);
            Assert.True(CzRowText.X2(V(R("gy"))) == Vt(R("gy")), name);
            Assert.True(CzRowText.X2(V(R("boost"))) == Vt(R("boost")), name);
            Assert.True(CzRowText.X2(V(R("spike"))) == Vt(R("spike")), name);
            var po = b.Type == BeamType.PulseOutside;
            foreach (var key in new[] { "capPo", "core", "bblur", "gx", "gy", "boost" }) Assert.True(!po == R(key).GetProperty("fold").GetBoolean(), name + key);
            foreach (var key in new[] { "capLine", "spike" }) Assert.True(!line == R(key).GetProperty("fold").GetBoolean(), name + key);
            Assert.Equal(IsAuto(R("speed")), b.Duration is null);
            cases++;
        }
        Assert.True(cases >= 25, cases + " cases");
    }

    [Fact]
    [Trait("Feature", "CZ-4")]
    [Trait("Spec", "CZ-T17")]
    [Trait("Decision", "ED-251")]
    public void The_reset_rows_and_the_default_test_follow_the_design()
    {
        using var f = Fixture();
        var first = f.RootElement.GetProperty("beams")[0].GetProperty("reset");
        Assert.Equal(D(first.GetProperty("default")), CzRowText.BeamResetDetail(true));
        Assert.Equal(D(first.GetProperty("changed")), CzRowText.BeamResetDetail(false));
        var aura = f.RootElement.GetProperty("auras")[0].GetProperty("reset");
        Assert.Equal(D(aura.GetProperty("default")), CzRowText.AuraResetDetail(true));
        Assert.Equal(D(aura.GetProperty("changed")), CzRowText.AuraResetDetail(false));
        // the keys the design leaves out of the test (fit, css, faithful, active) do not make the beam "changed"; every other key does
        Assert.True(CzRowText.BeamIsDefault(BorderBeamOptions.Default));
        Assert.True(CzRowText.BeamIsDefault(BorderBeamOptions.Default with { Fitted = false, Faithful = true, Active = false }));
        Assert.False(CzRowText.BeamIsDefault(BorderBeamOptions.Default with { Duration = 4 }));
        Assert.False(CzRowText.BeamIsDefault(BorderBeamOptions.Default.WithType(BeamType.Line)));
        Assert.False(CzRowText.BeamIsDefault(BorderBeamOptions.Default with { Palette = BeamPalette.Gold }));
        Assert.False(CzRowText.BeamIsDefault(BorderBeamOptions.Default with { Respond = true }));
        Assert.True(CzRowText.AuraIsDefault(UiverseAuraOptions.Default));
        Assert.False(CzRowText.AuraIsDefault(UiverseAuraOptions.Default with { Speed = 12.5 }));
        Assert.False(CzRowText.AuraIsDefault(UiverseAuraOptions.Default with { Scheme = AuraScheme.Accent }));
    }

    [Fact]
    [Trait("Feature", "CZ-4")]
    [Trait("Spec", "CZ-T18")]
    [Trait("Decision", "ED-251")]
    public void Every_uiverse_row_says_what_the_design_says_for_every_branch()
    {
        using var f = Fixture();
        var maker = f.RootElement.GetProperty("makerName").GetString()!;
        var accent = f.RootElement.GetProperty("accentName").GetString()!;
        var cases = 0;
        foreach (var c in f.RootElement.GetProperty("auras").EnumerateArray())
        {
            var name = c.GetProperty("name").GetString();
            var a = Settings("aura", null, c.GetProperty("aura")).Aura;
            var rows = c.GetProperty("rows");
            JsonElement R(string key) => rows.GetProperty(key);
            Assert.True(CzText.AuraColours(a.Scheme.ToString().ToLowerInvariant(), maker, accent) == D(R("colours")), name);
            Assert.True((a.Scheme != AuraScheme.Custom) == R("custom").GetProperty("fold").GetBoolean(), name);
            Assert.True(CzRowText.Seconds1(V(R("speed"))) == Vt(R("speed")), name);
            foreach (var key in new[] { "rest", "typing", "running", "ds" }) Assert.True(CzRowText.Percent(V(R(key))) == Vt(R(key)), name + key);
            foreach (var key in new[] { "hs", "hb", "rw", "dc" }) Assert.True(CzRowText.Px(V(R(key))) == Vt(R(key)), name + key);
            Assert.True(CzRowText.RimWidthDetail(a.Rim) == D(R("rw")), name);
            Assert.True(!a.Rim == R("rw").GetProperty("dim").GetBoolean(), name);
            Assert.True(CzRowText.DotCellDetail(a.Dots) == D(R("dc")), name);
            Assert.True(CzRowText.DotStrengthDetail(a.Dots) == D(R("ds")), name);
            Assert.True(!a.Dots == R("dc").GetProperty("dim").GetBoolean(), name);
            Assert.True(!a.Dots == R("ds").GetProperty("dim").GetBoolean(), name);
            cases++;
        }
        Assert.Equal(17, cases);
    }

    [Fact]
    [Trait("Feature", "CZ-4")]
    [Trait("Spec", "CZ-T19")]
    [Trait("Decision", "ED-251")]
    public void The_value_formats_follow_javascript_rounding()
    {
        Assert.Equal("1.25×", CzRowText.X2(1.25));
        Assert.Equal("3.69 s", CzRowText.Seconds(3.69));
        Assert.Equal("14 s", CzRowText.Seconds(14));
        Assert.Equal("2.5 px", CzRowText.Px(2.5));
        Assert.Equal("0%", CzRowText.Percent01(0.004));
        Assert.Equal("37%", CzRowText.Percent01(0.37));
        Assert.Equal("1%", CzRowText.Percent01(0.005));
        Assert.Equal("0°", CzRowText.Deg(-0.4)); // JavaScript prints Math.round(-0.4) + '°' as 0°
        Assert.Equal("-45°", CzRowText.Deg(-45));
        Assert.Equal("-1°", CzRowText.Deg(-1.4));
        Assert.Equal("0°", CzRowText.Deg(-0.5)); // Math.round(-0.5) is -0
        Assert.Equal("±13°", CzRowText.Drift(100, true));
        Assert.Equal("±100°", CzRowText.Drift(100, false));
        Assert.Equal("±8°", CzRowText.Drift(8, true));
        Assert.Equal("12.5 s", CzRowText.Seconds1(12.5));
        Assert.Equal("40 s", CzRowText.Seconds1(40));
        Assert.Equal("22 px", CzRowText.Corner(22));
    }
}
