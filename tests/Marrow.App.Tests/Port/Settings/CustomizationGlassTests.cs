using System.Globalization;
using Avalonia.Automation;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Settings;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Settings;

// ED-NEW-glasslook-2 (OD-0107; owner 2026-10-11: "add glass UI settings inside Customization, so the user can set it as he wishes"; the lead: the
// group is planned as Apple's Liquid Glass setting, iOS 27 the target): the Glass section of Settings > Customization. A live preview tile, the Look
// (Clear, Regular, Tinted), a fine-tuning slider per knob of the two repos' live demos and Reset to defaults; every control writes the app's
// glassLook setting, the material follows at once, a new Look crossfades every surface, Reset glides, and the store is written when a drag or a
// glide ends.
[Collection("Headless")]
public sealed class CustomizationGlassTests(HeadlessSession headless)
{
    private static void Restore(CustomizationPageRig rig)
    {
        rig.Do(() => rig.App.Settings.Glass = GlassSettings.Default);
        GlassLooks.Blend(GlassVariant.Regular, 1);
        GlassLooks.Tuning = GlassSettings.Default;
    }

    private static string Stored(CustomizationPageRig rig) => System.Text.Encoding.UTF8.GetString((byte[])rig.Defaults.Object("glassLook")!);

    // The look the preview's composer pill is drawn with now.
    private static GlassLook PillLook(GlassSections glass) => GlassLooks.Of(SwiftGlass.GetEffect(glass.Preview.Pill)!);

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // the section: after the composer's pill, the preview tile, the Look, a slider per knob in its range, Reset and the note
    public void The_glass_section_has_the_preview_the_look_a_slider_per_knob_and_reset() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var glass = rig.Page.Glass!;
        Assert.Same(glass.Section, rig.Page.Rest.Children[^1]);
        Assert.Equal("Glass", glass.Section.Title);
        Assert.Equal(["Clear", "Regular", "Tinted"], glass.Look.Control.Buttons.Select(b => AutomationProperties.GetName(b)));
        Assert.Equal("regular", glass.Look.Control.SelectedId);
        Assert.Equal(GlassSettings.Knobs.Select(k => k.Id), glass.Sliders.Keys);
        Assert.Equal(["Refraction", "Bezel width", "Clarity", "Frost", "Colour fringe", "Rim light", "Tint", "Saturation", "Shadow depth"],
            glass.Sliders.Values.Select(s => AutomationProperties.GetName(s)));
        foreach (var knob in GlassSettings.Knobs)
        {
            var slider = glass.Sliders[knob.Id];
            Assert.Equal((knob.Min, knob.Max), (slider.Minimum, slider.Maximum));
            Assert.Equal(GlassSettings.Default.Get(knob.Id), slider.Value);
        }
        Assert.True(glass.Sliders["clarity"].IsAuto, "clarity starts on Auto: each surface's own");
        Assert.Equal((double?)GlassLooks.For(GlassSurface.Composer, GlassVariant.Regular).Clarity, glass.Sliders["clarity"].AutoValue);
        Assert.True(glass.Reset.Dimmed, "Reset has nothing to do at the defaults");
        // the preview tile: a composer pill and a glass button of the app's material, on their surfaces
        Assert.Equal(GlassSurface.Composer, GlassLooks.SurfaceOf(SwiftGlass.GetEffect(glass.Preview.Pill)!));
        Assert.Equal(GlassSurface.Button, GlassLooks.SurfaceOf(SwiftGlass.GetEffect(glass.Preview.Button)!));
    });

    [Theory]
    [Trait("Decision", "ED-NEW-glasslook-2")] // each control reaches the setting and the material live; the drag's end writes the store; the Look stays
    [InlineData("refraction", 1.7)]
    [InlineData("bezel", 0.8)]
    [InlineData("clarity", 0.35)]
    [InlineData("frost", 1.35)]
    [InlineData("fringe", 0.25)]
    [InlineData("rim", 1.15)]
    [InlineData("tint", 0.45)]
    [InlineData("saturation", 1.3)]
    [InlineData("shadow", 0.6)]
    public void Each_slider_writes_the_setting_and_the_material_follows_live(string id, double value) => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        try
        {
            var glass = rig.Page.Glass!;
            rig.Do(() => glass.Sliders[id].SetValue(value, dragging: true));
            var settings = rig.Get(() => rig.App.Settings.Glass);
            Assert.Equal(value, settings.Get(id)!.Value, 9);
            Assert.Equal(settings, GlassLooks.Tuning);
            Assert.Equal(GlassLooks.For(GlassSurface.Composer).Under(settings), PillLook(glass));
            Assert.Equal("regular", glass.Look.Control.SelectedId);
            Assert.False(glass.Reset.Dimmed);
            rig.Do(() => rig.App.Settings.FlushPendingWrites());
            Assert.Equal(System.Text.Encoding.UTF8.GetString(settings.ToJson()), Stored(rig));
        }
        finally { Restore(rig); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // Auto on the clarity slider gives each surface its own clarity back, and shows the composer's own in the Look picked
    public void Clarity_on_auto_is_each_surfaces_own() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        try
        {
            var glass = rig.Page.Glass!;
            var clarity = glass.Sliders["clarity"];
            rig.Do(() => clarity.SetValue(0.8));
            Assert.Equal(0.8, rig.Get(() => rig.App.Settings.Glass.Clarity));
            rig.Do(clarity.ToggleAuto);
            Assert.Null(rig.Get(() => rig.App.Settings.Glass.Clarity));
            Assert.Equal(GlassSettings.Default, GlassLooks.Tuning);
            rig.Do(() => glass.Look.Control.Show("clear"));
            Assert.True(clarity.IsAuto);
            Assert.Equal((double?)GlassLooks.For(GlassSurface.Composer, GlassVariant.Clear).Clarity, clarity.AutoValue);
        }
        finally { Restore(rig); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // a new Look crossfades every surface (no pop), the knobs staying where they are; Reset glides the knobs and the Look back to the defaults
    public void A_new_look_crossfades_and_reset_glides_back() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: false);
        try
        {
            rig.Advance(500);
            var glass = rig.Page.Glass!;
            rig.Do(() => glass.Sliders["refraction"].SetValue(1.7));
            var tuned = GlassSettings.Default.With("refraction", 1.7);
            Assert.Equal(tuned, rig.Get(() => rig.App.Settings.Glass));
            var regular = PillLook(glass);
            var tinted = GlassLook.TintedCandidate.Under(tuned);
            rig.Do(() => glass.Look.Control.Show("tinted"));
            var tints = new List<double>();
            for (var frame = 0; frame < 12; frame++)
            {
                rig.Advance(1000.0 / 60);
                var now = rig.Get(() => rig.App.Settings.Glass);
                Assert.Equal(tuned with { Look = GlassVariant.Tinted }, now); // the knobs stay
                Assert.Equal(now, GlassLooks.Tuning);
                Assert.Equal(1.7, glass.Sliders["refraction"].Value!.Value, 9);
                tints.Add(PillLook(glass).Tint);
            }
            var shown = string.Join(", ", tints.Select(t => t.ToString("F3", CultureInfo.InvariantCulture)));
            Assert.True(tints[0] > regular.Tint && tints[^1] < tinted.Tint, "between the two Looks: " + shown);
            Assert.True(tints.Zip(tints.Skip(1)).All(p => p.Second >= p.First), "and rising steadily: " + shown);
            Assert.True(tints.Zip(tints.Skip(1)).Max(p => p.Second - p.First) <= 0.25 * (tinted.Tint - regular.Tint), "with no jump between two frames: " + shown);
            rig.Advance(600);
            Assert.Equal(tuned with { Look = GlassVariant.Tinted }, rig.Get(() => rig.App.Settings.Glass));
            Assert.Equal(tinted, PillLook(glass));
            Assert.Equal("tinted", glass.Look.Control.SelectedId);
            Assert.Equal("{\"look\":\"tinted\",\"refraction\":1.7}", Stored(rig));
            Assert.False(glass.Reset.Dimmed);

            rig.Do(glass.Reset.Activate);
            rig.Advance(100);
            var midway = rig.Get(() => rig.App.Settings.Glass);
            var midLook = PillLook(glass);
            Assert.True(midway.Refraction > 1 && midway.Refraction < 1.7, "Reset glides the knobs: " + midway.Refraction.ToString("F3", CultureInfo.InvariantCulture));
            Assert.True(midLook.Tint > GlassLook.Release.Tint && midLook.Tint < tinted.Tint, "and crossfades the Look: " + midLook.Tint.ToString("F3", CultureInfo.InvariantCulture));
            rig.Advance(600);
            Assert.Equal(GlassSettings.Default, rig.Get(() => rig.App.Settings.Glass));
            Assert.Equal(GlassSettings.Default, GlassLooks.Tuning);
            Assert.Equal(GlassLook.Release, PillLook(glass));
            Assert.True(glass.Reset.Dimmed);
            Assert.Equal("regular", glass.Look.Control.SelectedId);
            Assert.Equal("{}", Stored(rig));
        }
        finally { Restore(rig); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // a slider touched mid-crossfade: the crossfade lands first, then the slider's value; nothing is left half-way
    public void A_slider_touched_mid_crossfade_lands_the_look_first() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: false);
        try
        {
            rig.Advance(500);
            var glass = rig.Page.Glass!;
            rig.Do(() => glass.Look.Control.Show("clear"));
            rig.Advance(100);
            Assert.NotEqual(GlassLook.Premium, PillLook(glass));
            rig.Do(() => glass.Sliders["tint"].SetValue(0.5, dragging: true));
            var settings = rig.Get(() => rig.App.Settings.Glass);
            Assert.Equal(new GlassSettings { Look = GlassVariant.Clear, Tint = 0.5 }, settings);
            Assert.Equal(GlassLook.Premium.Under(settings), PillLook(glass));
            rig.Advance(600);
            Assert.Equal(settings, rig.Get(() => rig.App.Settings.Glass));
            Assert.Equal("clear", glass.Look.Control.SelectedId);
        }
        finally { Restore(rig); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // under Reduce motion a Look is taken at once
    public void Under_reduce_motion_a_look_is_taken_at_once() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        try
        {
            var glass = rig.Page.Glass!;
            rig.Do(() => glass.Look.Control.Show("clear"));
            var clear = GlassSettings.Default with { Look = GlassVariant.Clear };
            Assert.Equal(clear, rig.Get(() => rig.App.Settings.Glass));
            Assert.Equal(clear, GlassLooks.Tuning);
            Assert.Equal(GlassLook.Premium, PillLook(glass));
            Assert.Equal("{\"look\":\"clear\"}", Stored(rig));
        }
        finally { Restore(rig); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // a change from elsewhere (another window, a relaunch) shows in every row
    public void A_change_from_elsewhere_shows_in_every_row() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        try
        {
            var glass = rig.Page.Glass!;
            var elsewhere = new GlassSettings { Look = GlassVariant.Tinted, Refraction = 1.4, Clarity = 0.3, Tint = 0.6, Shadow = 1.2 };
            rig.Do(() => rig.App.Settings.Glass = elsewhere);
            foreach (var knob in GlassSettings.Knobs) Assert.Equal(elsewhere.Get(knob.Id), glass.Sliders[knob.Id].Value);
            Assert.Equal("tinted", glass.Look.Control.SelectedId);
            Assert.False(glass.Reset.Dimmed);
            Assert.Equal(GlassLook.TintedCandidate.Under(elsewhere), PillLook(glass));
        }
        finally { Restore(rig); }
    });
}
