using System.Globalization;
using Avalonia.Automation;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Settings;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Settings;

// OD-0147 (the owner approved the Glass UI canvas, 2026-10-11: "ship it with option for user to be able to set all those settings ... but ship with
// its current default values") with ED-NEW-glasslook-2: the Glass section of Settings > Customization is the canvas's Glass settings panel. The
// preview tile, the Look stops and the Transparency slider between them, the eight fine-tuning sliders with Reset, the tint colour, the motion, the
// three switches and Reset to defaults, each with the canvas's words, range, step, unit and default. Every control writes the app's glass setting,
// the material follows at once, a Look, a switch or a reset glides every surface, and the store is written when a drag or a glide ends.
[Collection("Headless")]
public sealed class CustomizationGlassTests(HeadlessSession headless)
{
    private static CustomizationPageRig Rig(bool reduceMotion = true, IGlassSystem? system = null)
    {
        GlassLooks.System = system ?? new FixedGlassSystem();
        return new CustomizationPageRig(reduceMotion: reduceMotion);
    }

    private static void Restore(CustomizationPageRig rig)
    {
        rig.Do(() => rig.App.Settings.Glass = GlassSettings.Default);
        GlassLooks.Blend(GlassSettings.Default, 1);
        GlassLooks.Tuning = GlassSettings.Default;
        GlassLooks.System = new WindowsGlassSystem();
    }

    private static string Stored(CustomizationPageRig rig) => System.Text.Encoding.UTF8.GetString((byte[])rig.Defaults.Object("glass")!);

    // The material the preview's composer pill is drawn with now.
    private static GlassCanvas PillGlass(GlassSections glass) => GlassLooks.Of(SwiftGlass.GetEffect(glass.Preview.Pill)!).Canvas!;

    [Fact]
    [Trait("Decision", "OD-0147")] // the section is the canvas's panel: its rows, words, ranges, steps, units and the shipped defaults
    public void The_glass_section_is_the_canvas_panel_at_its_defaults() => headless.OnUiThread(() =>
    {
        using var rig = Rig();
        try
        {
            var glass = rig.Page.Glass!;
            Assert.Same(glass.Section, rig.Page.Rest.Children[^1]);
            Assert.Equal("Glass", glass.Section.Title);
            Assert.Equal(["Clear", "Regular", "Tinted"], glass.Look.Control.Buttons.Select(b => AutomationProperties.GetName(b)));
            Assert.Equal("regular", glass.Look.Control.SelectedId);
            // the Transparency slider: 0 to 100 by 1, at 50 (Regular), its value shown as the canvas shows it
            Assert.Equal((0d, 100d, 1d, (double?)50), (glass.Transparency.Minimum, glass.Transparency.Maximum, glass.Transparency.Step, glass.Transparency.Value));
            Assert.Equal("Liquid Glass, more clear to more tinted", AutomationProperties.GetName(glass.Transparency));
            Assert.Equal("From more clear to more tinted.", AutomationProperties.GetHelpText(glass.Transparency));
            Assert.Equal(["Regular, default (50)", "Clear (0)", "Tinted (100)", "More clear (12)", "Default (61)", "More tinted (80)"],
                new double[] { 50, 0, 100, 12, 61, 80 }.Select(glass.Transparency.Format));
            // the eight fine-tuning sliders, in the canvas's order, with its names, descriptions, ranges, steps and formats
            Assert.Equal(["refraction", "bezel", "frost", "fringe", "rim", "tint", "saturation", "shadow"], glass.Sliders.Keys);
            Assert.Equal(["Refraction", "Bezel width", "Frost", "Colour fringe", "Rim light", "Tint", "Saturation", "Shadow depth"],
                glass.Sliders.Values.Select(s => AutomationProperties.GetName(s)));
            foreach (var knob in GlassSettings.Knobs)
            {
                var slider = glass.Sliders[knob.Id];
                Assert.Equal((knob.Min, knob.Max, knob.Step), (slider.Minimum, slider.Maximum, slider.Step));
                Assert.Equal(knob.Description, AutomationProperties.GetHelpText(slider));
                Assert.Equal(GlassSettings.Default.Get(knob.Id), slider.Value);
            }
            Assert.Equal(["1.30×", "12.0 px", "32 px", "0.40 px", "0.50", "0.12", "1.22×", "1.00×"],
                GlassSettings.Knobs.Select(k => glass.Sliders[k.Id].Format(GlassSettings.Default.Get(k.Id))));
            Assert.True(glass.ResetFineTuning.Dimmed, "the fine-tuning Reset has nothing to do at the look's own values");
            // the behaviour: no tint colour, full motion, the switches off (Windows' own), the sidebar translucent
            Assert.Equal(["No tint", "Silver", "Blue", "Purple", "Pink", "Orange", "Green"], glass.TintColour.Items.Select(i => i.Label.Text));
            Assert.Equal("none", glass.TintColour.SelectedId);
            Assert.Equal("full", glass.Motion.Control.SelectedId);
            Assert.False(glass.IncreaseContrast.Switch.Checked);
            Assert.False(glass.ReduceTransparency.Switch.Checked);
            Assert.True(glass.TranslucentSidebar.Switch.Checked);
            Assert.Equal(["Increase contrast", "Reduce transparency", "Translucent sidebar"],
                new[] { glass.IncreaseContrast, glass.ReduceTransparency, glass.TranslucentSidebar }.Select(t => AutomationProperties.GetName(t.Switch)));
            Assert.True(glass.Reset.Dimmed, "Reset to defaults has nothing to do at the defaults");
            // the preview tile: a composer pill and a glass button of the app's material, on their surfaces, at the shipped glass
            Assert.Equal(GlassSurface.Composer, GlassLooks.SurfaceOf(SwiftGlass.GetEffect(glass.Preview.Pill)!));
            Assert.Equal(GlassSurface.Button, GlassLooks.SurfaceOf(SwiftGlass.GetEffect(glass.Preview.Button)!));
            Assert.Equal(new GlassCanvas(GlassKind.Composer, GlassTuning.Of(GlassSettings.Default, new FixedGlassSystem())), PillGlass(glass));
        }
        finally { Restore(rig); }
    });

    [Theory]
    [Trait("Decision", "OD-0147")] // each fine-tuning slider writes the setting and the material follows live; the drag's end writes the store; the Look stays, the Custom mark shows
    [InlineData("refraction", 1.85)]
    [InlineData("bezel", 18.5)]
    [InlineData("frost", 44)]
    [InlineData("fringe", 0.75)]
    [InlineData("rim", 0.8)]
    [InlineData("tint", 0.3)]
    [InlineData("saturation", 1.5)]
    [InlineData("shadow", 1.6)]
    public void Each_slider_writes_the_setting_and_the_material_follows_live(string id, double value) => headless.OnUiThread(() =>
    {
        using var rig = Rig();
        try
        {
            var glass = rig.Page.Glass!;
            rig.Do(() => glass.Sliders[id].SetValue(value, dragging: true));
            var settings = rig.Get(() => rig.App.Settings.Glass);
            Assert.Equal(value, settings.Get(id), 9);
            Assert.Equal(GlassSettings.Default.With(id, value), settings);
            Assert.Equal(settings, GlassLooks.Tuning);
            Assert.Equal(new GlassCanvas(GlassKind.Composer, GlassTuning.Of(settings, new FixedGlassSystem())), PillGlass(glass));
            Assert.Equal("regular", glass.Look.Control.SelectedId);
            Assert.False(glass.ResetFineTuning.Dimmed);
            Assert.False(glass.Reset.Dimmed);
            rig.Do(() => rig.App.Settings.FlushPendingWrites());
            Assert.Equal("{\"" + id + "\":" + value.ToString(CultureInfo.InvariantCulture) + "}", Stored(rig));
        }
        finally { Restore(rig); }
    });

    [Theory]
    [Trait("Decision", "OD-0147")] // range clamps: a slider held past either end gives its end, and the setting and the material take that
    [InlineData("transparency")]
    [InlineData("refraction")]
    [InlineData("bezel")]
    [InlineData("frost")]
    [InlineData("fringe")]
    [InlineData("rim")]
    [InlineData("tint")]
    [InlineData("saturation")]
    [InlineData("shadow")]
    public void A_slider_past_its_ends_is_held_to_them(string id) => headless.OnUiThread(() =>
    {
        using var rig = Rig();
        try
        {
            var glass = rig.Page.Glass!;
            var knob = id == "transparency" ? GlassSettings.TransparencyKnob : GlassSettings.Knobs.Single(k => k.Id == id);
            var slider = id == "transparency" ? glass.Transparency : glass.Sliders[id];
            foreach (var (past, end) in new[] { (knob.Max + 50, knob.Max), (knob.Min - 50, knob.Min) })
            {
                rig.Do(() => slider.SetValue(past, dragging: true));
                Assert.Equal(end, slider.Value);
                var settings = rig.Get(() => rig.App.Settings.Glass);
                Assert.Equal(end, id == "transparency" ? settings.Transparency : settings.Get(id), 9);
                Assert.Equal(settings, GlassLooks.Tuning);
            }
        }
        finally { Restore(rig); }
    });

    [Fact]
    [Trait("Decision", "OD-0147")] // the Transparency slider is the canvas's continuous Look: every fine-tuning value follows it between the stops, live, the Look row naming the stop or none
    public void The_transparency_slider_moves_every_value_between_the_stops_live() => headless.OnUiThread(() =>
    {
        using var rig = Rig();
        try
        {
            var glass = rig.Page.Glass!;
            rig.Do(() => glass.Transparency.SetValue(75, dragging: true));
            var between = rig.Get(() => rig.App.Settings.Glass);
            Assert.Equal(GlassSettings.Default.At(75), between);
            Assert.Equal(1.20, between.Refraction, 9); // half way from Regular's 1.30 to Tinted's 1.10
            Assert.Equal(between, GlassLooks.Tuning);
            Assert.Equal("", glass.Look.Control.SelectedId);
            Assert.Equal(1.20, glass.Sliders["refraction"].Value!.Value, 9);
            Assert.True(glass.ResetFineTuning.Dimmed, "every value is the slider's own");
            rig.Do(() => glass.Transparency.SetValue(100, dragging: true));
            Assert.Equal(GlassSettings.Default.At(GlassVariant.Tinted), rig.Get(() => rig.App.Settings.Glass));
            Assert.Equal("tinted", glass.Look.Control.SelectedId);
            Assert.Equal(new GlassCanvas(GlassKind.Composer, GlassTuning.Of(GlassSettings.Default.At(GlassVariant.Tinted), new FixedGlassSystem())), PillGlass(glass));
            rig.Do(() => rig.App.Settings.FlushPendingWrites());
            Assert.Equal("{\"transparency\":100}", Stored(rig));
        }
        finally { Restore(rig); }
    });

    [Fact]
    [Trait("Decision", "OD-0147")] // a Look stop glides every surface there (no pop), its values the stop's own (the canvas's setG); Reset to defaults glides everything back to the shipped glass
    public void A_look_glides_and_reset_glides_back_to_the_shipped_glass() => headless.OnUiThread(() =>
    {
        using var rig = Rig(reduceMotion: false);
        try
        {
            rig.Advance(500);
            var glass = rig.Page.Glass!;
            rig.Do(() => glass.Sliders["refraction"].SetValue(1.85));
            var tinted = GlassSettings.Default.At(GlassVariant.Tinted);
            rig.Do(() => glass.Look.Control.Show("tinted"));
            var tints = new List<double>();
            for (var frame = 0; frame < 12; frame++)
            {
                rig.Advance(1000.0 / 60);
                Assert.Equal(tinted, rig.Get(() => rig.App.Settings.Glass)); // the setting is the stop at once
                Assert.Equal(tinted, GlassLooks.Tuning);
                tints.Add(PillGlass(glass).Tuning.Tint);
            }
            var shown = string.Join(", ", tints.Select(t => t.ToString("F3", CultureInfo.InvariantCulture)));
            Assert.True(tints[0] > 0.12 && tints[^1] < 0.24, "between the two Looks: " + shown);
            Assert.True(tints.Zip(tints.Skip(1)).All(p => p.Second >= p.First), "and rising steadily: " + shown);
            Assert.True(tints.Zip(tints.Skip(1)).Max(p => p.Second - p.First) <= 0.25 * (0.24 - 0.12), "with no jump between two frames: " + shown);
            rig.Advance(600);
            Assert.Equal(new GlassCanvas(GlassKind.Composer, GlassTuning.Of(tinted, new FixedGlassSystem())), PillGlass(glass));
            Assert.Equal(1.10, glass.Sliders["refraction"].Value!.Value, 9);
            Assert.Equal("tinted", glass.Look.Control.SelectedId);
            Assert.Equal("{\"transparency\":100}", Stored(rig));
            Assert.False(glass.Reset.Dimmed);

            rig.Do(glass.Reset.Activate);
            rig.Advance(100);
            var midway = PillGlass(glass).Tuning;
            Assert.True(midway.Refraction > 1.10 && midway.Refraction < 1.30, "Reset glides: " + midway.Refraction.ToString("F3", CultureInfo.InvariantCulture));
            var slid = glass.Sliders["refraction"].Value!.Value;
            Assert.True(slid > 1.10 && slid < 1.30, "and its sliders glide: " + slid.ToString("F3", CultureInfo.InvariantCulture));
            rig.Advance(600);
            Assert.Equal(GlassSettings.Default, rig.Get(() => rig.App.Settings.Glass));
            Assert.Equal(GlassSettings.Default, GlassLooks.Tuning);
            Assert.Equal(new GlassCanvas(GlassKind.Composer, GlassTuning.Of(GlassSettings.Default, new FixedGlassSystem())), PillGlass(glass));
            Assert.True(glass.Reset.Dimmed);
            Assert.Equal("regular", glass.Look.Control.SelectedId);
            Assert.Equal(1.30, glass.Sliders["refraction"].Value!.Value, 9);
            Assert.Equal("{}", Stored(rig));
        }
        finally { Restore(rig); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // under Reduce motion a Look is taken at once: the setting, the material and the store, with no frame advanced
    public void Under_reduce_motion_a_look_is_taken_at_once() => headless.OnUiThread(() =>
    {
        using var rig = Rig(reduceMotion: true);
        try
        {
            var glass = rig.Page.Glass!;
            rig.Do(() => glass.Look.Control.Show("clear"));
            var clear = GlassSettings.Default.At(GlassVariant.Clear);
            Assert.Equal(clear, rig.Get(() => rig.App.Settings.Glass));
            Assert.Equal(clear, GlassLooks.Tuning);
            Assert.Equal(new GlassCanvas(GlassKind.Composer, GlassTuning.Of(clear, new FixedGlassSystem())), PillGlass(glass));
            Assert.Equal("{\"transparency\":0}", Stored(rig));
        }
        finally { Restore(rig); }
    });

    [Fact]
    [Trait("Decision", "OD-0147")] // the fine-tuning Reset gives every value back to the Look's own and keeps the Look and the switches
    public void The_fine_tuning_reset_gives_back_the_looks_own_values() => headless.OnUiThread(() =>
    {
        using var rig = Rig();
        try
        {
            var glass = rig.Page.Glass!;
            rig.Do(() => glass.Look.Control.Show("clear"));
            rig.Do(() => glass.TintColour.Select("silver"));
            rig.Do(() => glass.Sliders["bezel"].SetValue(20));
            rig.Do(() => glass.Sliders["rim"].SetValue(0.9));
            Assert.False(glass.ResetFineTuning.Dimmed);
            rig.Do(glass.ResetFineTuning.Activate);
            var reset = GlassSettings.Default.At(GlassVariant.Clear) with { TintColour = GlassTintColour.Silver };
            Assert.Equal(reset, rig.Get(() => rig.App.Settings.Glass));
            Assert.Equal(reset, GlassLooks.Tuning);
            Assert.True(glass.ResetFineTuning.Dimmed);
            Assert.False(glass.Reset.Dimmed);
            Assert.Equal("clear", glass.Look.Control.SelectedId);
            Assert.Equal(12, glass.Sliders["bezel"].Value);
            Assert.Equal(0.55, glass.Sliders["rim"].Value!.Value, 9);
            Assert.Equal("{\"transparency\":0,\"tintColour\":\"silver\"}", Stored(rig));
        }
        finally { Restore(rig); }
    });

    [Fact]
    [Trait("Decision", "OD-0147")] // the tint colour and the motion reach the setting, the material and the store
    public void The_tint_colour_and_the_motion_reach_the_material() => headless.OnUiThread(() =>
    {
        using var rig = Rig();
        try
        {
            var glass = rig.Page.Glass!;
            rig.Do(() => glass.TintColour.Select("blue"));
            Assert.Equal(GlassTintColour.Blue, rig.Get(() => rig.App.Settings.Glass.TintColour));
            Assert.Equal((0d, 0.48, 1d, 0.16), (PillGlass(glass).Tuning.TintR, PillGlass(glass).Tuning.TintG, PillGlass(glass).Tuning.TintB, PillGlass(glass).Tuning.TintAmount));
            Assert.Equal("{\"tintColour\":\"blue\"}", Stored(rig));
            rig.Do(() => glass.Motion.Control.Show("reduced"));
            Assert.True(rig.Get(() => rig.App.Settings.Glass.ReducedMotion) is true);
            Assert.Equal(0, PillGlass(glass).Tuning.Motion);
            Assert.Equal("{\"tintColour\":\"blue\",\"motion\":\"reduced\"}", Stored(rig));
            rig.Do(() => glass.Motion.Control.Show("full"));
            Assert.True(rig.Get(() => rig.App.Settings.Glass.ReducedMotion) is false);
            Assert.Equal(1, PillGlass(glass).Tuning.Motion);
            Assert.Equal("{\"tintColour\":\"blue\",\"motion\":\"full\"}", Stored(rig));
        }
        finally { Restore(rig); }
    });

    [Fact]
    [Trait("Decision", "OD-0147")] // one source of truth: Motion, Increase contrast and Reduce transparency show and follow Windows' own until the user sets them; Reset gives them back to Windows
    public void The_switches_follow_windows_until_the_user_sets_them() => headless.OnUiThread(() =>
    {
        using var rig = Rig(system: new FixedGlassSystem(ReduceMotion: true, IncreaseContrast: true, ReduceTransparency: true));
        try
        {
            var glass = rig.Page.Glass!;
            Assert.Equal("reduced", glass.Motion.Control.SelectedId);
            Assert.True(glass.IncreaseContrast.Switch.Checked);
            Assert.True(glass.ReduceTransparency.Switch.Checked);
            Assert.Equal((1d, 1d, 0d), (PillGlass(glass).Tuning.Contrast, PillGlass(glass).Tuning.Reduce, PillGlass(glass).Tuning.Motion));
            rig.Do(glass.IncreaseContrast.Switch.Toggle);
            rig.Do(glass.ReduceTransparency.Switch.Toggle);
            var set = rig.Get(() => rig.App.Settings.Glass);
            Assert.Equal(((bool?)false, (bool?)false), (set.IncreaseContrast, set.ReduceTransparency));
            Assert.False(glass.IncreaseContrast.Switch.Checked);
            Assert.False(glass.ReduceTransparency.Switch.Checked);
            Assert.Equal((0d, 0d), (PillGlass(glass).Tuning.Contrast, PillGlass(glass).Tuning.Reduce));
            Assert.Equal("{\"increaseContrast\":false,\"reduceTransparency\":false}", Stored(rig));
            rig.Do(glass.TranslucentSidebar.Switch.Toggle);
            Assert.False(rig.Get(() => rig.App.Settings.Glass.TranslucentSidebar));
            Assert.Equal(1, GlassLooks.Shown.SidebarOff);
            rig.Do(glass.Reset.Activate);
            Assert.Equal(GlassSettings.Default, rig.Get(() => rig.App.Settings.Glass));
            Assert.True(glass.IncreaseContrast.Switch.Checked, "back to Windows' own");
            Assert.True(glass.ReduceTransparency.Switch.Checked);
            Assert.True(glass.TranslucentSidebar.Switch.Checked);
            Assert.Equal((1d, 1d, 0d), (PillGlass(glass).Tuning.Contrast, PillGlass(glass).Tuning.Reduce, PillGlass(glass).Tuning.SidebarOff));
            Assert.Equal("{}", Stored(rig));
        }
        finally { Restore(rig); }
    });

    [Fact]
    [Trait("Decision", "OD-0147")] // a slider touched mid-glide: the glide lands first, then the slider's value; nothing is left half-way
    public void A_slider_touched_mid_glide_lands_the_glide_first() => headless.OnUiThread(() =>
    {
        using var rig = Rig(reduceMotion: false);
        try
        {
            rig.Advance(500);
            var glass = rig.Page.Glass!;
            rig.Do(() => glass.Look.Control.Show("clear"));
            rig.Advance(100);
            Assert.NotEqual(GlassTuning.Of(GlassSettings.Default.At(GlassVariant.Clear), new FixedGlassSystem()), PillGlass(glass).Tuning);
            rig.Do(() => glass.Sliders["tint"].SetValue(0.3, dragging: true));
            var settings = rig.Get(() => rig.App.Settings.Glass);
            Assert.Equal(GlassSettings.Default.At(GlassVariant.Clear).With("tint", 0.3), settings);
            Assert.Equal(GlassTuning.Of(settings, new FixedGlassSystem()), PillGlass(glass).Tuning);
            rig.Advance(600);
            Assert.Equal(settings, rig.Get(() => rig.App.Settings.Glass));
            Assert.Equal("clear", glass.Look.Control.SelectedId);
        }
        finally { Restore(rig); }
    });

    [Fact]
    [Trait("Decision", "OD-0147")] // a change from elsewhere (another window, a relaunch) shows in every row
    public void A_change_from_elsewhere_shows_in_every_row() => headless.OnUiThread(() =>
    {
        using var rig = Rig();
        try
        {
            var glass = rig.Page.Glass!;
            var elsewhere = GlassSettings.Default.At(80).With("refraction", 2).With("shadow", 1.4) with
            {
                TintColour = GlassTintColour.Green, ReducedMotion = true, IncreaseContrast = true, TranslucentSidebar = false,
            };
            rig.Do(() => rig.App.Settings.Glass = elsewhere);
            Assert.Equal(80, glass.Transparency.Value);
            foreach (var knob in GlassSettings.Knobs) Assert.Equal(elsewhere.Get(knob.Id), glass.Sliders[knob.Id].Value);
            Assert.Equal("", glass.Look.Control.SelectedId);
            Assert.Equal("green", glass.TintColour.SelectedId);
            Assert.Equal("reduced", glass.Motion.Control.SelectedId);
            Assert.True(glass.IncreaseContrast.Switch.Checked);
            Assert.False(glass.ReduceTransparency.Switch.Checked);
            Assert.False(glass.TranslucentSidebar.Switch.Checked);
            Assert.False(glass.ResetFineTuning.Dimmed);
            Assert.False(glass.Reset.Dimmed);
            Assert.Equal(new GlassCanvas(GlassKind.Composer, GlassTuning.Of(elsewhere, new FixedGlassSystem())), PillGlass(glass));
        }
        finally { Restore(rig); }
    });
}
