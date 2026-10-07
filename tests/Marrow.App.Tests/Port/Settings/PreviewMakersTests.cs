using System.Text.Json;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Settings;
using Marrow.UI.Port;
using Avalonia.Styling;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-4.5: the preview's makers, the stepping, the palette rule and the chip's text. The expected values come from stage.json,
// which tests/fixtures/customization/stage.generate.cjs measures from the locked cz.js, never from the port.
// GAP-2 light-pair cases instead freeze the approved real-slot maker stops; see the numeric InlineData source citations.
[Collection("Headless")]
public sealed class PreviewMakersTests(HeadlessSession headless)
{
    private static JsonDocument Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir!.FullName, "tests/fixtures/customization/stage.json")));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T34, CZ-4.5.3
    [Trait("Spec", "CZ-T34")]
    public void The_initial_maker_is_the_default_providers_and_a_provider_without_a_maker_falls_to_Claude()
    {
        var expected = new Dictionary<ProviderKind, string>
        {
            [ProviderKind.Claude] = "Claude", [ProviderKind.Codex] = "OpenAI", [ProviderKind.Cursor] = "Cursor", [ProviderKind.Opencode] = "OpenCode",
            [ProviderKind.OpencodeGo] = "OpenCodeGo", [ProviderKind.Grok] = "Grok", [ProviderKind.Deepseek] = "DeepSeek", [ProviderKind.Meta] = "Meta",
            [ProviderKind.Zai] = "Zai", [ProviderKind.Devin] = "Devin", [ProviderKind.Antigravity] = "Gemini", [ProviderKind.Copilot] = "Copilot",
            [ProviderKind.Commandcode] = "CommandCode", [ProviderKind.Pi] = "Pi",
            [ProviderKind.OpenRouter] = "Claude", // Purple has no maker
        };
        foreach (var provider in Enum.GetValues<ProviderKind>()) Assert.Equal(expected[provider], PreviewMakers.Initial(provider));
        Assert.Equal(Enum.GetValues<ProviderKind>().Length, expected.Count);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T32, CZ-4.5.1, CZ-4.5.4
    public void The_list_is_the_thirty_makers_in_order_and_the_steppers_wrap_around()
    {
        using var fixture = Fixture();
        var makers = fixture.RootElement.GetProperty("makers").EnumerateArray().ToArray();
        Assert.Equal(makers.Select(m => m.GetProperty("id").GetString()), PreviewMakers.All.Select(m => m.Id));
        Assert.Equal(makers.Select(m => m.GetProperty("name").GetString()), PreviewMakers.All.Select(m => m.Name));
        Assert.Equal(makers.Select(m => m.GetProperty("model").GetString()), PreviewMakers.All.Select(m => m.Model));
        Assert.Equal("OpenAI", PreviewMakers.Step("Claude", 1));
        Assert.Equal("Arcee", PreviewMakers.Step("Claude", -1));
        Assert.Equal("Claude", PreviewMakers.Step("Arcee", 1));
        var id = "Claude";
        for (var i = 0; i < 30; i++) id = PreviewMakers.Step(id, 1);
        Assert.Equal("Claude", id);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-4.5.1: the chip's accessible name, cz.js modelSwitch().aria
    public void The_chips_accessible_name_names_the_maker_and_its_model_when_it_has_one()
    {
        using var fixture = Fixture();
        foreach (var maker in fixture.RootElement.GetProperty("makers").EnumerateArray())
            Assert.Equal(maker.GetProperty("aria").GetString(), PreviewMakers.ChipName(maker.GetProperty("id").GetString()!));
        Assert.Equal("Preview model: Arcee", PreviewMakers.ChipName("Arcee"));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-4.5.5: the chip and menu dots are czTone(deep, dark, title of the theme)
    public void The_dots_are_the_makers_deep_stop_toned_for_the_window()
    {
        using var fixture = Fixture();
        foreach (var maker in fixture.RootElement.GetProperty("makers").EnumerateArray())
        {
            var id = maker.GetProperty("id").GetString()!;
            Assert.Equal(maker.GetProperty("dotDark").GetString(), PreviewMakers.Dot(id, dark: true), ignoreCase: true);
            Assert.Equal(maker.GetProperty("dotLight").GetString(), PreviewMakers.Dot(id, dark: false), ignoreCase: true);
        }
    }

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T31, CZ-4.5.5: the preview's palette rule is the composer slot's: dark (mid, light), light (deep, mid), plus the accent
    [Trait("Spec", "CZ-T31")]
    public void The_preview_palette_takes_the_stops_the_same_way_the_composers_slot_does_for_all_fifteen_brands() => headless.OnUiThread(() =>
    {
        var brands = Enum.GetValues<EffortBrand>();
        Assert.Equal(15, brands.Length);
        foreach (var brand in brands)
        {
            var stops = brand.LiquidStops.Select(c => c.Resolve(ThemeVariant.Dark)).Select(x => $"#{x.R:X2}{x.G:X2}{x.B:X2}").ToArray();
            var slot = ComposerLightSlot.PaletteFor(brand);
            var preview = ComposerLightPalettes.FromStops((stops[0], stops[1], stops[2]), (stops[0], stops[1], stops[2]), slot.AccentDark, slot.AccentLight);
            Assert.Equal(slot, preview);
            Assert.Equal((stops[1], stops[2], stops[0], stops[1]), (slot.MakerHeartDark, slot.MakerSecondDark, slot.MakerHeartLight, slot.MakerSecondLight));
        }
    });

    [Theory]
    [Trait("Decision", "ED-251")]
    [Trait("Spec", "CZ-T31")]
    // GAP-2 ruling: keep the real composer unchanged. OD-0089 maker pair (logic.js:483-484),
    // with the approved 14-maker stops (effort-liquid-approved-v15.dc.html:360-373).
    // gen.py:462 embeds logic.js; its literal four-model table is not a 14-maker oracle.
    [InlineData("OpenAI", true, "#EEF0F5", "#D9DBE3")] // approved canvas:360
    [InlineData("OpenAI", false, "#FFFFFF", "#EEF0F5")] // approved canvas:360
    [InlineData("Claude", true, "#CC6B47", "#E0875F")] // approved canvas:361
    [InlineData("Claude", false, "#B85A38", "#CC6B47")] // approved canvas:361
    [InlineData("DeepSeek", true, "#3D5CFA", "#6A82FF")] // approved canvas:362
    [InlineData("DeepSeek", false, "#2A45E0", "#3D5CFA")] // approved canvas:362
    [InlineData("Meta", true, "#0070EC", "#2B95FF")] // approved canvas:363
    [InlineData("Meta", false, "#0054C4", "#0070EC")] // approved canvas:363
    [InlineData("Gemini", true, "#9C73CC", "#D96670")] // approved canvas:364
    [InlineData("Gemini", false, "#4285F5", "#9C73CC")] // approved canvas:364
    [InlineData("Cursor", true, "#F54E00", "#FF8040")] // approved canvas:365
    [InlineData("Cursor", false, "#D93F00", "#F54E00")] // approved canvas:365
    [InlineData("Grok", true, "#121218", "#26262E")] // approved canvas:366
    [InlineData("Grok", false, "#030304", "#121218")] // approved canvas:366
    [InlineData("OpenCode", true, "#25B34F", "#45DC6C")] // approved canvas:367
    [InlineData("OpenCode", false, "#168A3A", "#25B34F")] // approved canvas:367
    [InlineData("OpenCodeGo", true, "#00A4CD", "#3CCBEC")] // approved canvas:368
    [InlineData("OpenCodeGo", false, "#007EA3", "#00A4CD")] // approved canvas:368
    [InlineData("Zai", true, "#14306E", "#2F6BFF")] // approved canvas:369
    [InlineData("Zai", false, "#0A1630", "#14306E")] // approved canvas:369
    [InlineData("Devin", true, "#3CBC9B", "#6FDCBF")] // approved canvas:370
    [InlineData("Devin", false, "#239377", "#3CBC9B")] // approved canvas:370
    [InlineData("Copilot", true, "#D25AB4", "#F4A876")] // approved canvas:371
    [InlineData("Copilot", false, "#8534F3", "#D25AB4")] // approved canvas:371
    [InlineData("CommandCode", true, "#2E1B9C", "#6145DC")] // approved canvas:372
    [InlineData("CommandCode", false, "#24157E", "#2E1B9C")] // approved canvas:372
    [InlineData("Pi", true, "#C8102E", "#EC5560")] // approved canvas:373
    [InlineData("Pi", false, "#A30C25", "#C8102E")] // approved canvas:373
    public void Every_locked_makers_preview_and_real_slot_have_the_exact_approved_pair_in_each_appearance(string maker, bool dark, string c1, string c2) => headless.OnUiThread(() =>
    {
        static (string, string) Pair(ComposerLightPalette palette, bool isDark) => isDark
            ? (palette.MakerHeartDark, palette.MakerSecondDark) : (palette.MakerHeartLight, palette.MakerSecondLight);
        var expected = (c1, c2);
        var preview = PreviewMakers.Palette(maker, "#4B9FFC", "#0067C0");
        Assert.Equal(expected, Pair(preview, dark));
        Assert.Equal(("#4B9FFC", "#0067C0"), (preview.AccentDark, preview.AccentLight));
        var brand = Enum.GetValues<EffortBrand>().Single(b => b != EffortBrand.Purple && JewelPalettes.For(b).Maker == maker);
        Assert.Equal(expected, Pair(ComposerLightSlot.PaletteFor(brand), dark));
        using var rig = new CustomizationPageRig(light: !dark);
        rig.Do(() => rig.Page.Session!.Pick(maker));
        rig.Advance(1500);
        Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, rig.Get(() => rig.Window.ActualThemeVariant));
        Assert.Equal(expected, Pair(rig.Get(() => rig.Page.Stage!.Preview.Light.Palette), dark));
    });

    [Fact]
    [Trait("Decision", "ED-251")]
    public void Gallery_makers_without_a_fixed_composer_brand_keep_the_existing_pair_rule_and_accents()
    {
        var locked = Enum.GetValues<EffortBrand>().Where(b => b != EffortBrand.Purple).Select(b => JewelPalettes.For(b).Maker).ToHashSet();
        Assert.Equal(14, locked.Count);
        var gallery = MakerLiquids.All.Where(m => !locked.Contains(m["id"])).ToArray();
        Assert.Equal(16, gallery.Length);
        foreach (var maker in gallery)
            Assert.Equal(new ComposerLightPalette(maker["dark.mid"], maker["dark.light"], maker["light.deep"], maker["light.mid"], "#4B9FFC", "#0067C0"),
                PreviewMakers.Palette(maker["id"], "#4B9FFC", "#0067C0"));
    }
}
