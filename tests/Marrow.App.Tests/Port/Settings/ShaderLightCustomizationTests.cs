using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// PORT-ONLY, owner-ruled addition (OD-0139 Composer Light v2, v3 and v4; ED-NEW-lightv2-4, -5, -6): the shader lights in
// Settings > Customization. Every light is a card between the Uiverse light and Off, and picking one persists it; its rows
// are the ones the catalogue declares and edit that light only; Custom's colours, Text and light, and the wallpaper's
// dimming persist; Reset returns the light and those settings to how they ship.
[Collection("Headless")]
public sealed class ShaderLightCustomizationTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-lightv2-4")]
    public void Every_light_is_a_card_and_picking_each_persists_it() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var cards = rig.Page.StyleCards!;
        Assert.Equal(["beam", "aura", .. ShaderLightCatalog.All.Select(d => d.Id), "off"], cards.Buttons.Select(b => b.Id));
        foreach (var def in ShaderLightCatalog.All)
        {
            rig.Do(() => cards.Select(def.Id));
            Assert.Equal((ComposerLightStyle.Shader, def.Id), (rig.Light.Style, rig.Light.ShaderId));
            var stored = ComposerLightSettings.FromJson(rig.Light.ToJson());
            Assert.Equal(def.Id, stored.ActiveShader?.Id);
            Assert.True(rig.Get(() => rig.Page.Shader!.Look.Open));
            Assert.Equal(def.Id, rig.Get(() => rig.Page.Shader!.Light.Id));
        }
        rig.Do(() => cards.Select("beam"));
        Assert.Equal(ComposerLightStyle.Beam, rig.Light.Style);
        Assert.False(rig.Get(() => rig.Page.Shader!.Look.Open));
        Assert.Equal(ComposerLightStyle.Beam, ComposerLightSettings.Default.Style); // the default stays Border beam
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lightv2-4")]
    public void The_rows_edit_the_light_picked_and_custom_colours_persist() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s => s.ComposerLight = ComposerLightSettings.Default with { Style = ComposerLightStyle.Shader, ShaderId = "aurum" });
        var shader = rig.Page.Shader!;
        Assert.Equal(ShaderLightCatalog.Packs.Select(p => p.Id), shader.Packs.Buttons.Select(b => b.Id)); // every pack, Custom last
        Assert.Equal("gold", shader.Packs.SelectedId);
        rig.Do(() => shader.Spread.Slider.SetValue(180));
        rig.Do(() => shader.Intensity.Slider.SetValue(1.6));
        rig.Do(() => shader.Speed.Slider.SetValue(.5));
        rig.Do(() => shader.Packs.Select("custom"));
        Assert.True(rig.Get(() => shader.YourColoursSlot.Open));
        rig.Do(() => shader.Wells[2].SetColor("#123456"));
        var aurum = rig.Light.ShaderOptions("aurum");
        Assert.Equal(("custom", 180.0, 1.6, .5), (aurum.Pack, aurum.Spread, aurum.Intensity, aurum.Speed));
        Assert.Equal("#123456", aurum.Custom[2]);
        Assert.Equal(ShaderLightCatalog.Find("lumen")!.Defaults, rig.Light.ShaderOptions("lumen")); // the others keep theirs
        var stored = ComposerLightSettings.FromJson(rig.Light.ToJson());
        Assert.Equal(aurum, stored.ShaderOptions("aurum"));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lightv2-5")]
    public void Text_and_light_and_the_wallpaper_dimming_persist_and_reset_restores_them() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s => s.ComposerLight = ComposerLightSettings.Default with { Style = ComposerLightStyle.Shader, ShaderId = "velum" });
        var shader = rig.Page.Shader!;
        Assert.Equal(["Text above light", "Light above text", "Difference"], ShaderSections.TextModes.Select(m => m.Text));
        Assert.Equal("above", shader.TextMode.Control.SelectedId);
        Assert.False(rig.Get(() => shader.Halo.Row.Dimmed));
        rig.Do(() => shader.TextMode.Control.Select("diff"));
        Assert.Equal(ComposerLightTextMode.Difference, rig.Light.TextMode);
        Assert.True(rig.Get(() => shader.Halo.Row.Dimmed)); // the halo is Text above light's (the page disables it otherwise)
        rig.Do(() => shader.Behind.Slider.SetValue(.8));
        rig.Do(() => shader.Dim.Control.Select("off"));
        Assert.True(rig.Get(() => shader.DimStrength.Row.Dimmed));
        rig.Do(() => shader.Dim.Control.Select("auto"));
        rig.Do(() => shader.DimStrength.Slider.SetValue(.75));
        rig.Do(() => shader.Intensity.Slider.SetValue(2));
        var stored = ComposerLightSettings.FromJson(rig.Light.ToJson());
        Assert.Equal((ComposerLightTextMode.Difference, .8, WallpaperDimMode.Auto, .75), (stored.TextMode, stored.TextBehind, stored.WallpaperDim, stored.WallpaperDimStrength));
        rig.Do(() => shader.ResetButton.Activate());
        Assert.Equal(ShaderLightCatalog.Find("velum")!.Defaults, rig.Light.ShaderOptions("velum"));
        Assert.True(rig.Light.TextAndDimAtDefaults);
        Assert.Equal((ComposerLightStyle.Shader, "velum"), (rig.Light.Style, rig.Light.ShaderId));
        Assert.Equal("above", rig.Get(() => shader.TextMode.Control.SelectedId));
    });
}
