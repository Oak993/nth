using System.Text;
using Marrow.Core.Models.ComposerLights;

namespace Marrow.Core.Tests.Models.ComposerLights;

// DEVIATION OD-0089 (owner-ruled addition; ED-251): the composer light's settings are the playground's options. Defaults are
// logic.js AURA_DEFAULTS and BEAM_DEFAULTS (OD-0089: Border beam by default with the playground's opening values); presets
// are BEAM_CALM, "Library defaults", AURA_ORIGINAL and "Calm defaults"; ranges and steps are beamRows()/auraRows()'s
// slider() rows and slider()'s set (Math.round(clamp(v, min, max) / step) * step).
public sealed class ComposerLightSettingsTests
{
    [Fact]
    [Trait("Decision", "ED-251")] // OD-0089 and logic.js BEAM_DEFAULTS
    public void Border_beam_is_the_default_with_the_playgrounds_opening_values()
    {
        var s = ComposerLightSettings.Default;
        Assert.Equal(ComposerLightStyle.Beam, s.Style);
        var b = s.Beam;
        Assert.Equal(BeamFamily.Rotate, b.Family);
        Assert.Equal(BeamType.Large, b.Type);
        Assert.Equal(BeamPalette.Colorful, b.Palette);
        Assert.Equal(BeamTheme.Auto, b.Theme);
        Assert.True(b.Active);
        Assert.Null(b.Duration); Assert.Null(b.Radius); Assert.Null(b.Brightness); Assert.Null(b.Saturation);
        Assert.Null(b.CoreBlur); Assert.Null(b.BloomBlur); Assert.Null(b.GlowX); Assert.Null(b.GlowY);
        Assert.Equal(0, b.HueShift); Assert.Equal(1, b.Strength); Assert.Equal(1, b.GlowSize); Assert.Equal(30, b.HueRange);
        Assert.False(b.StaticColors); Assert.Equal(1, b.StrokeX); Assert.Equal(1, b.InnerX); Assert.Equal(1, b.BloomX);
        Assert.Equal(1, b.Boost); Assert.Equal(1, b.Spike); Assert.False(b.Faithful); Assert.False(b.Respond); Assert.True(b.Fitted);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // logic.js AURA_DEFAULTS
    public void The_uiverse_light_defaults_are_the_playgrounds_calm_values()
    {
        var a = ComposerLightSettings.Default.Aura;
        Assert.Equal(AuraScheme.Original, a.Scheme);
        Assert.Equal("#8E7CFF", a.Color1); Assert.Equal("#38C9B4", a.Color2);
        Assert.Equal(12, a.Speed); Assert.True(a.Breathing);
        Assert.Equal((40, 55, 75), (a.Rest, a.Typing, a.Running));
        Assert.Equal(2, a.HaloSize); Assert.Equal(30, a.HaloBlur); Assert.True(a.Rim); Assert.Equal(1.5, a.RimWidth);
        Assert.True(a.Dots); Assert.Equal(3, a.DotCell); Assert.Equal(67, a.DotStrength);
        Assert.Equal(55, a.IntensityFor(ComposerLightPhase.Typing));
        Assert.Equal(75, a.IntensityFor(ComposerLightPhase.Running));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // logic.js BEAM_CALM, beamRows() "Library defaults", AURA_ORIGINAL, auraRows() "Calm defaults"
    public void The_presets_are_the_playgrounds_buttons()
    {
        var b = BorderBeamOptions.Default.WithType(BeamType.Line) with { Palette = BeamPalette.Ocean, Fitted = false, Strength = 0.3, HueShift = 40 };
        var calm = b.Calm();
        Assert.Equal((9.0, 0.55, 18.0, 1.3, true), (calm.Duration!.Value, calm.Strength, calm.HueRange, calm.GlowSize, calm.Respond));
        Assert.Equal(40, calm.HueShift);
        var lib = b.LibraryDefaults();
        Assert.Equal(BorderBeamOptions.Default with { Type = BeamType.Line, Palette = BeamPalette.Ocean, Fitted = false }, lib);
        var a = UiverseAuraOptions.Default with { Scheme = AuraScheme.Custom, Color1 = "#112233", Speed = 30, Rest = 5 };
        var orig = a.Original();
        Assert.Equal((AuraScheme.Original, 8.0, 50.0, 50.0, 100.0, 0.0, 32.0, 2.0), (orig.Scheme, orig.Speed, orig.Rest, orig.Typing, orig.Running, orig.HaloSize, orig.HaloBlur, orig.RimWidth));
        Assert.Equal("#112233", orig.Color1);
        Assert.Equal(UiverseAuraOptions.Default with { Scheme = AuraScheme.Custom, Color1 = "#112233" }, a.CalmDefaults());
    }

    [Fact]
    [Trait("Decision", "ED-251")] // logic.js beamRows() famSeg: a new family starts on its first type; the family follows the type
    public void Family_and_type_stay_consistent()
    {
        var p = BorderBeamOptions.Default.WithFamily(BeamFamily.Pulse);
        Assert.Equal((BeamFamily.Pulse, BeamType.PulseInner), (p.Family, p.Type));
        var r = p.WithFamily(BeamFamily.Rotate);
        Assert.Equal((BeamFamily.Rotate, BeamType.Large), (r.Family, r.Type));
        Assert.Equal(BeamFamily.Pulse, BorderBeamOptions.Default.WithType(BeamType.PulseOutside).Family);
        Assert.Equal(BeamFamily.Rotate, (BorderBeamOptions.Default with { Family = BeamFamily.Pulse }).Clamped().Family);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // logic.js slider(): set = Math.round(clamp(v, min, max) / step) * step (a half rounds up: 66.5 -> 67), the ranges of beamRows() and auraRows()
    public void Values_are_clamped_and_stepped_as_the_sliders_set_them()
    {
        var b = (BorderBeamOptions.Default with
        {
            Duration = 3.333, Radius = 40, Brightness = 0.1, HueShift = -2.5, Strength = 0.555, Saturation = 9, GlowSize = 0.1, HueRange = 200,
            StrokeX = 2.6, InnerX = 1.024, BloomX = -1, CoreBlur = 0.74, BloomBlur = 61, GlowX = 0.2, GlowY = 4.004, Boost = 0.1, Spike = 1.026,
        }).Clamped();
        Assert.Equal(3.34, b.Duration!.Value, 9); Assert.Equal(32, b.Radius); Assert.Equal(0.2, b.Brightness!.Value, 9); Assert.Equal(-2, b.HueShift);
        Assert.Equal(0.56, b.Strength, 9); Assert.Equal(2.5, b.Saturation!.Value, 9); Assert.Equal(0.25, b.GlowSize, 9); Assert.Equal(180, b.HueRange);
        Assert.Equal(2, b.StrokeX, 9); Assert.Equal(1, b.InnerX, 9); Assert.Equal(0, b.BloomX, 9); Assert.Equal(0.5, b.CoreBlur!.Value, 9);
        Assert.Equal(60, b.BloomBlur!.Value, 9); Assert.Equal(0.35, b.GlowX!.Value, 9); Assert.Equal(4, b.GlowY!.Value, 9); Assert.Equal(0.5, b.Boost, 9);
        Assert.Equal(1.05, b.Spike, 9);
        var a = (UiverseAuraOptions.Default with { Speed = 2, Rest = 100.6, Typing = -4, Running = 49.5, HaloSize = 41, HaloBlur = 2, RimWidth = 1.3, DotCell = 2.2, DotStrength = 66.5, Color1 = "red", Color2 = "#a1b2c3" }).Clamped();
        Assert.Equal((3.0, 100.0, 0.0, 50.0, 40.0, 4.0, 1.25, 2.0, 67.0), (a.Speed, a.Rest, a.Typing, a.Running, a.HaloSize, a.HaloBlur, a.RimWidth, a.DotCell, a.DotStrength));
        Assert.Equal("#8E7CFF", a.Color1);
        Assert.Equal("#A1B2C3", a.Color2);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // the stored form: one JSON object, the playground's own keys, Auto as null
    public void Settings_round_trip_through_json_with_the_playgrounds_keys()
    {
        var s = ComposerLightSettings.Default with
        {
            Style = ComposerLightStyle.Aura,
            Beam = BorderBeamOptions.Default.WithType(BeamType.PulseOutside) with { Palette = BeamPalette.Gold, Theme = BeamTheme.Light, Duration = 6.5, GlowX = 1.2, Respond = true, Fitted = false, Faithful = true },
            Aura = UiverseAuraOptions.Default with { Scheme = AuraScheme.Custom, Color1 = "#102030", Speed = 20.5, Dots = false },
        };
        var json = s.ToJson();
        var text = Encoding.UTF8.GetString(json);
        Assert.Contains("\"style\":\"aura\"", text, StringComparison.Ordinal);
        Assert.Contains("\"size\":\"pulse-outside\"", text, StringComparison.Ordinal);
        Assert.Contains("\"family\":\"pulse\"", text, StringComparison.Ordinal);
        Assert.Contains("\"color\":\"gold\"", text, StringComparison.Ordinal);
        Assert.Contains("\"radius\":null", text, StringComparison.Ordinal);
        Assert.Contains("\"fit\":false", text, StringComparison.Ordinal);
        Assert.Contains("\"rimW\":1.5", text, StringComparison.Ordinal);
        Assert.Contains("\"dotStr\":67", text, StringComparison.Ordinal);
        Assert.Equal(s, ComposerLightSettings.FromJson(json));
        Assert.Equal(ComposerLightSettings.Default, ComposerLightSettings.FromJson(ComposerLightSettings.Default.ToJson()));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // lenient reading: unreadable data is the default, an unknown name or a wrong type keeps that value's default, ranges clamp
    public void Stored_values_are_read_leniently()
    {
        Assert.Equal(ComposerLightSettings.Default, ComposerLightSettings.FromJson("not json"u8));
        Assert.Equal(ComposerLightSettings.Default, ComposerLightSettings.FromJson("[1,2]"u8));
        var s = ComposerLightSettings.FromJson("""{"style":"lava","beam":{"size":"huge","color":"ocean","strength":7,"duration":"x","hueShift":-999,"fit":0},"aura":{"speed":1,"c1":"#zzzzzz"}}"""u8);
        Assert.Equal(ComposerLightStyle.Beam, s.Style);
        Assert.Equal(BeamType.Large, s.Beam.Type);
        Assert.Equal(BeamPalette.Ocean, s.Beam.Palette);
        Assert.Equal(1, s.Beam.Strength);
        Assert.Null(s.Beam.Duration);
        Assert.Equal(-180, s.Beam.HueShift);
        Assert.True(s.Beam.Fitted);
        Assert.Equal(3, s.Aura.Speed);
        Assert.Equal("#8E7CFF", s.Aura.Color1);
        var off = ComposerLightSettings.FromJson("""{"style":"off"}"""u8);
        Assert.Equal(ComposerLightSettings.Default with { Style = ComposerLightStyle.Off }, off);
    }
}
