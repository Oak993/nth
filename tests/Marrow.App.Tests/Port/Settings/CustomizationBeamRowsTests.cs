using Avalonia.Automation;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-6 (the Border beam's rows), CZ-12.2 (sliders, Auto, dimmed rows), CZ-13.2 (the Corner's range). What each row says is the locked cz.js's own beamRows()
// (tests/fixtures/customization/page.json, made by page.generate.cjs): every fixture case is put into the app's settings and the page's rows are read.
[Collection("Headless")]
public sealed class CustomizationBeamRowsTests(HeadlessSession headless)
{
    private static (CzRow Row, CzSliderRow? Slider, CzToggleRow? Toggle) RowOf(BeamSections b, string key) => key switch
    {
        "motion" => (b.Motion.Row, null, null),
        "shape" => (b.Shape, null, null),
        "colours" => (b.Colours, null, null),
        "speed" => (b.Speed.Row, b.Speed, null),
        "bright" => (b.Brightness.Row, b.Brightness, null),
        "strength" => (b.Intensity.Row, b.Intensity, null),
        "respond" => (b.Follow.Row, null, b.Follow),
        "hue" => (b.ColourShift.Row, b.ColourShift, null),
        "sat" => (b.Saturation.Row, b.Saturation, null),
        "drift" => (b.ColourDrift.Row, b.ColourDrift, null),
        "still" => (b.HoldStill.Row, null, b.HoldStill),
        "glow" => (b.GlowSize.Row, b.GlowSize, null),
        "edge" => (b.EdgeLine.Row, b.EdgeLine, null),
        "inner" => (b.InnerGlow.Row, b.InnerGlow, null),
        "bloom" => (b.OuterGlow.Row, b.OuterGlow, null),
        "radius" => (b.Corner.Row, b.Corner, null),
        "theme" => (b.TunedFor.Row, null, null),
        "core" => (b.CoreSoftness.Row, b.CoreSoftness, null),
        "bblur" => (b.BloomSoftness.Row, b.BloomSoftness, null),
        "gx" => (b.GlowWidth.Row, b.GlowWidth, null),
        "gy" => (b.GlowHeight.Row, b.GlowHeight, null),
        "boost" => (b.GlowBoost.Row, b.GlowBoost, null),
        "spike" => (b.Flare.Row, b.Flare, null),
        _ => throw new ArgumentException(key),
    };

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T14, CZ-T15, CZ-T19, CZ-6.1 to CZ-6.4: in every fixture case each row's detail, shown value and dimmed state are the design's
    [Trait("Spec", "CZ-T14")]
    [Trait("Spec", "CZ-T15")]
    [Trait("Spec", "CZ-T19")]
    public void Every_beam_row_says_what_the_design_says_in_every_fixture_case() => headless.OnUiThread(() =>
    {
        using var fixture = PageFixture.Open();
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var cases = 0;
        foreach (var c in fixture.RootElement.GetProperty("beams").EnumerateArray())
        {
            var name = c.GetProperty("name").GetString()!;
            rig.SetLight(PageFixture.Settings("beam", c.GetProperty("beam"), null));
            var beam = rig.Page.Beam!;
            foreach (var pair in c.GetProperty("rows").EnumerateObject())
            {
                if (pair.Value.GetProperty("kind").GetString() == "Cap") continue;
                var (row, slider, toggle) = RowOf(beam, pair.Name);
                var expected = pair.Value;
                Assert.True(expected.GetProperty("d").GetString() == row.Detail.Text, $"{name} / {pair.Name}: detail '{row.Detail.Text}'");
                Assert.True(expected.GetProperty("dim").GetBoolean() == row.Dimmed, $"{name} / {pair.Name}: dimmed");
                if (slider is null) continue;
                var auto = expected.GetProperty("auOn").GetString() == "true";
                Assert.True(auto == slider.Slider.IsAuto, $"{name} / {pair.Name}: auto");
                if (!auto) Assert.True(expected.GetProperty("vt").GetString() == slider.Slider.ValueText.Text, $"{name} / {pair.Name}: value '{slider.Slider.ValueText.Text}'");
            }
            cases++;
        }
        Assert.True(cases >= 25, cases + " cases");
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T10, CZ-6, CZ-12.2.6: Auto shows BorderBeamResolved.AutoValues at the preview composer's size; moving the slider stores a value; the capsule toggles between null and the shown value
    [Trait("Spec", "CZ-T10")]
    public void Auto_shows_the_resolved_value_moving_stores_and_the_capsule_toggles() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var beam = rig.Page.Beam!;
        var pill = rig.Page.Stage!.Preview.Pill.Bounds;
        Assert.Equal((472.0, 46.0), (pill.Width, pill.Height)); // the preview pill is the real composer's 46 high; the Auto values are asked for at h = 46 (SPEC CZ-6)
        var auto = BorderBeamResolved.AutoValues(rig.Light.Beam, true, 472, 46, 22);
        Assert.NotEqual(BorderBeamResolved.AutoValues(rig.Light.Beam, true, 472, 52, 22).Duration, auto.Duration); // not the playground's mock 52
        Assert.True(beam.Speed.Slider.IsAuto);
        Assert.Equal(auto.Duration, beam.Speed.Slider.ShownValue);
        Assert.Equal(auto.Brightness, beam.Brightness.Slider.ShownValue);
        Assert.Equal(22, beam.Corner.Slider.ShownValue);
        Assert.True(beam.Speed.Slider.AutoButton.Checked);
        Assert.Equal("Automatic Speed", AutomationProperties.GetName(beam.Speed.Slider.AutoButton));
        // moving stores a value (Auto goes off) ...
        rig.Do(() => beam.Speed.Slider.SetValue(5.55));
        Assert.Equal(5.55, rig.Light.Beam.Duration);
        Assert.False(beam.Speed.Slider.IsAuto);
        Assert.Equal("5.55 s", beam.Speed.Slider.ValueText.Text);
        Assert.Equal("Seconds for one lap", beam.Speed.Row.Detail.Text);
        // ... and the capsule goes back to Auto, and out of Auto at the value it shows
        rig.Do(beam.Speed.Slider.ToggleAuto);
        Assert.Null(rig.Light.Beam.Duration);
        Assert.Equal("Auto: the designed pace", beam.Speed.Row.Detail.Text);
        rig.Do(beam.Speed.Slider.ToggleAuto);
        Assert.Equal(BorderBeamOptions.DurationSlider.Snap(auto.Duration), rig.Light.Beam.Duration); // the store steps 0.01 (CZ-13.2)
        Assert.False(beam.Speed.Slider.IsAuto);
        // Auto follows the settings: a Line beam sweeps at its own pace
        rig.SetLight(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default.WithType(BeamType.Line) });
        var line = BorderBeamResolved.AutoValues(rig.Light.Beam, true, 472, 46, 22);
        Assert.Equal(line.Duration, beam.Speed.Slider.ShownValue);
        Assert.NotEqual(auto.Duration, line.Duration);
        // Pulse outside: the glow sliders' Auto values come from the same resolver
        rig.SetLight(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default.WithType(BeamType.PulseOutside) });
        rig.Do(() => rig.Page.Advanced.Open = true);
        var outside = BorderBeamResolved.AutoValues(rig.Light.Beam, true, 472, 46, 22);
        Assert.Equal((outside.CoreBlur, outside.BloomBlur, outside.GlowX, outside.GlowY),
            (beam.CoreSoftness.Slider.ShownValue, beam.BloomSoftness.Slider.ShownValue, beam.GlowWidth.Slider.ShownValue, beam.GlowHeight.Slider.ShownValue));
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T10, CZ-6: the Auto values are the window's theme's (the light recipe differs from the dark one)
    [Trait("Spec", "CZ-T10")]
    public void Auto_follows_the_windows_theme() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(light: true, reduceMotion: true);
        var pill = rig.Page.Stage!.Preview.Pill.Bounds;
        var auto = BorderBeamResolved.AutoValues(rig.Light.Beam, false, 472, 46, 22);
        Assert.Equal(auto.Brightness, rig.Page.Beam!.Brightness.Slider.ShownValue);
        Assert.Equal(auto.Saturation, rig.Page.Beam.Saturation.Slider.ShownValue);
        var dark = BorderBeamResolved.AutoValues(rig.Light.Beam, true, 472, 46, 22);
        Assert.NotEqual((dark.Brightness, dark.Saturation), (auto.Brightness, auto.Saturation));
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T12, CZ-6.1: a Motion pick sets the family's first shape; the Shape sets swap with the family and each writes the type
    [Trait("Spec", "CZ-T12")]
    public void Motion_sets_the_families_first_shape_and_the_shape_sets_swap() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var beam = rig.Page.Beam!;
        Assert.Equal(["Rotate", "Pulse"], beam.Motion.Control.Buttons.Select(b => AutomationProperties.GetName(b)));
        Assert.Equal(["Large", "Line", "Small"], beam.RotateShapes.Buttons.Select(b => AutomationProperties.GetName(b)));
        Assert.Equal(["Inside", "Outside"], beam.PulseShapes.Buttons.Select(b => AutomationProperties.GetName(b)));
        Assert.True(beam.ShapeSets.FirstShown);
        rig.Do(() => beam.RotateShapes.Select("line"));
        Assert.Equal((BeamFamily.Rotate, BeamType.Line), (rig.Light.Beam.Family, rig.Light.Beam.Type));
        Assert.Equal("A short streak glides along the edge", beam.Shape.Detail.Text);
        rig.Do(() => beam.Motion.Control.Select("pulse"));
        Assert.Equal((BeamFamily.Pulse, BeamType.PulseInner), (rig.Light.Beam.Family, rig.Light.Beam.Type));
        Assert.False(beam.ShapeSets.FirstShown);
        Assert.Equal("pulse-inner", beam.PulseShapes.SelectedId);
        Assert.Equal("Breathes in place", beam.Motion.Row.Detail.Text);
        Assert.Equal("Colour pools just inside the edge", beam.Shape.Detail.Text);
        rig.Do(() => beam.PulseShapes.Select("pulse-outside"));
        Assert.Equal(BeamType.PulseOutside, rig.Light.Beam.Type);
        rig.Do(() => beam.Motion.Control.Select("rotate"));
        Assert.Equal((BeamFamily.Rotate, BeamType.Large), (rig.Light.Beam.Family, rig.Light.Beam.Type));
        Assert.True(beam.ShapeSets.FirstShown);
        Assert.Equal("md", beam.RotateShapes.SelectedId);
        // a change from outside (the settings) shows in the controls
        rig.SetLight(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default.WithType(BeamType.Small) });
        Assert.Equal("sm", beam.RotateShapes.SelectedId);
        Assert.Equal("rotate", beam.Motion.Control.SelectedId);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T13, CZ-6.2: eight palettes in order, named; a pick writes the palette and the row's detail names it
    [Trait("Spec", "CZ-T13")]
    public void The_colours_row_has_the_eight_palettes_and_a_pick_writes_one() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var swatches = rig.Page.Beam!.Swatches;
        Assert.Equal(["Colourful", "Mono", "Ocean", "Sunset", "Forest", "Candy", "Ice", "Gold"], swatches.Buttons.Select(b => AutomationProperties.GetName(b)));
        Assert.Equal("Colours", AutomationProperties.GetName(swatches));
        Assert.Equal("Colourful", rig.Page.Beam.Colours.Detail.Text);
        foreach (var (id, palette, name) in new[] { ("gold", BeamPalette.Gold, "Gold"), ("mono", BeamPalette.Mono, "Mono"), ("candy", BeamPalette.Candy, "Candy") })
        {
            rig.Do(() => swatches.Select(id));
            Assert.Equal(palette, rig.Light.Beam.Palette);
            Assert.Equal(name, rig.Page.Beam.Colours.Detail.Text);
        }
        rig.SetLight(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default with { Palette = BeamPalette.Ice } });
        Assert.Equal("ice", swatches.SelectedId);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T14, CZ-T42, CZ-12.2.7: Colour drift is dimmed and inert (not focusable) for Pulse, Mono and Hold colours still; Line caps it at 13
    [Trait("Spec", "CZ-T14")]
    [Trait("Spec", "CZ-T42")]
    public void Colour_drift_is_dimmed_and_inert_where_it_does_nothing() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        rig.Do(() => rig.Page.Advanced.Open = true);
        var drift = rig.Page.Beam!.ColourDrift;
        Assert.False(drift.Row.Dimmed);
        Assert.True(drift.Slider.IsEnabled);
        Assert.True(drift.Slider.Rail.Focusable);
        foreach (var beam in new[]
        {
            BorderBeamOptions.Default.WithType(BeamType.PulseInner),
            BorderBeamOptions.Default with { Palette = BeamPalette.Mono },
            BorderBeamOptions.Default with { StaticColors = true },
        })
        {
            rig.SetLight(ComposerLightSettings.Default with { Beam = beam });
            Assert.True(drift.Row.Dimmed);
            Assert.False(drift.Slider.IsEnabled);
            Assert.False(drift.Slider.Rail.Focusable);
            var before = rig.Light.Beam.HueRange;
            rig.Do(() => drift.Slider.SetValue(100)); // inert
            Assert.Equal(before, rig.Light.Beam.HueRange);
        }
        Assert.Equal("Off while the colours hold still", drift.Row.Detail.Text);
        rig.SetLight(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default.WithType(BeamType.PulseInner) });
        Assert.Equal("Pulse always runs through every hue", drift.Row.Detail.Text);
        rig.SetLight(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default.WithType(BeamType.Line) with { HueRange = 100 } });
        Assert.False(drift.Row.Dimmed);
        Assert.Equal("±13°", drift.Slider.ValueText.Text);
        Assert.Equal("How far the colours wander (Line keeps it under 13°)", drift.Row.Detail.Text);
        rig.SetLight(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default with { HueRange = 100 } });
        Assert.Equal("±100°", drift.Slider.ValueText.Text);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T15, CZ-6.4, CZ-12.3: Hold colours still is shown on, dimmed and inert under Mono; elsewhere it is the setting
    [Trait("Spec", "CZ-T15")]
    public void Hold_colours_still_is_forced_on_and_inert_under_mono() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        rig.Do(() => rig.Page.Advanced.Open = true);
        var hold = rig.Page.Beam!.HoldStill;
        Assert.False(hold.Switch.Checked);
        rig.Do(hold.Switch.Toggle);
        Assert.True(rig.Light.Beam.StaticColors);
        Assert.True(hold.Switch.Checked);
        rig.Do(hold.Switch.Toggle);
        Assert.False(rig.Light.Beam.StaticColors);
        rig.SetLight(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default with { Palette = BeamPalette.Mono } });
        Assert.True(hold.Switch.Checked);
        Assert.True(hold.Row.Dimmed);
        Assert.True(hold.Switch.Dimmed);
        Assert.Equal("Mono always holds still", hold.Row.Detail.Text);
        rig.Do(hold.Switch.Toggle);
        Assert.False(rig.Light.Beam.StaticColors);
        Assert.True(hold.Switch.Checked);
        rig.SetLight(ComposerLightSettings.Default);
        Assert.False(hold.Switch.Checked);
        Assert.False(hold.Row.Dimmed);
        Assert.Equal("The palette stops drifting", hold.Row.Detail.Text);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T11, CZ-13.2: the Corner runs 0..26; a stored 30 shows at the right end and is not rewritten until the user moves it
    [Trait("Spec", "CZ-T11")]
    public void A_stored_corner_above_the_sliders_end_shows_at_the_end_and_is_not_rewritten() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        rig.Do(() => rig.Page.Advanced.Open = true);
        var corner = rig.Page.Beam!.Corner;
        Assert.Equal((0.0, 26.0, 1.0), (corner.Slider.Minimum, corner.Slider.Maximum, corner.Slider.Step));
        rig.SetLight(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default with { Radius = 30 } });
        Assert.Equal(1, corner.Slider.Fraction);
        Assert.Equal("30 px", corner.Slider.ValueText.Text);
        Assert.Equal(30, rig.Light.Beam.Radius);
        Assert.Equal("Rounder or squarer than the composer", corner.Row.Detail.Text);
        rig.Do(() => corner.Slider.SetValue(26));
        Assert.Equal(26, rig.Light.Beam.Radius);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T8, CZ-6.3, CZ-6.4: every Border beam slider has the design's range, step, direction and Auto (the table of CZ-6), and the glow height's Auto is 0.35 at the preview's 46 high
    public void Every_border_beam_slider_has_the_designs_range_step_direction_and_auto() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        rig.Do(() => rig.Page.Advanced.Open = true);
        var b = rig.Page.Beam!;
        (CzSliderRow Row, double Min, double Max, double Step, bool Inverted, bool Auto)[] table =
        [
            (b.Speed, 0.5, 14, 0.05, true, true), (b.Brightness, 0.2, 3, 0.05, false, true), (b.Intensity, 0, 1, 0.01, false, false),
            (b.ColourShift, -180, 180, 1, false, false), (b.Saturation, 0, 2.5, 0.05, false, true), (b.ColourDrift, 0, 180, 1, false, false),
            (b.GlowSize, 0.25, 3, 0.05, false, false), (b.EdgeLine, 0, 2, 0.05, false, false), (b.InnerGlow, 0, 2, 0.05, false, false), (b.OuterGlow, 0, 2, 0.05, false, false),
            (b.Corner, 0, 26, 1, false, true), (b.CoreSoftness, 0, 20, 0.5, false, true), (b.BloomSoftness, 0, 60, 0.5, false, true),
            (b.GlowWidth, 0.35, 4, 0.01, false, true), (b.GlowHeight, 0.35, 4, 0.01, false, true), (b.GlowBoost, 0.5, 2, 0.01, false, false), (b.Flare, 0, 3, 0.05, false, false),
        ];
        foreach (var (row, min, max, step, inverted, auto) in table)
        {
            var name = row.Row.Title.Text;
            Assert.True((min, max, step, inverted) == (row.Slider.Minimum, row.Slider.Maximum, row.Slider.Step, row.Slider.Inverted), $"{name}: range {row.Slider.Minimum}..{row.Slider.Maximum} step {row.Slider.Step}");
            Assert.True(auto == row.Slider.AutoValue.HasValue, $"{name}: Auto");
        }
        Assert.Equal(17, table.Length);
        Assert.Equal(0.35, b.GlowHeight.Slider.AutoValue); // clamp(46 / 140, 0.35, 4): the design's 52 gave 0.371
        Assert.Equal(1.349, b.GlowWidth.Slider.AutoValue); // clamp(472 / 350, 0.35, 4)
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T8, CZ-12.2.4, CZ-13.2: the page's Speed steps 0.05 and the values on that step (0.55, 3.7, 13.95) are stored as they are
    public void The_speed_slider_stores_the_values_it_shows() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var speed = rig.Page.Beam!.Speed.Slider;
        Assert.Equal((0.5, 14.0, 0.05, true), (speed.Minimum, speed.Maximum, speed.Step, speed.Inverted));
        foreach (var value in new[] { 0.55, 3.7, 13.95 })
        {
            rig.Do(() => speed.SetValue(value));
            Assert.Equal(value, rig.Light.Beam.Duration);
            Assert.Equal(value, speed.Value);
        }
        rig.Do(() => speed.SetValue(0.525)); // a half rounds up
        Assert.Equal(0.55, rig.Light.Beam.Duration);
        rig.Do(() => speed.SetValue(99));
        Assert.Equal(14, rig.Light.Beam.Duration);
    });
}
