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
    public void The_initial_maker_is_the_default_providers_and_a_provider_without_a_maker_falls_to_Claude()
    {
        var expected = new Dictionary<ProviderKind, string>
        {
            [ProviderKind.Claude] = "Claude", [ProviderKind.Codex] = "OpenAI", [ProviderKind.Cursor] = "Cursor", [ProviderKind.Opencode] = "OpenCode",
            [ProviderKind.OpencodeGo] = "OpenCodeGo", [ProviderKind.Grok] = "Grok", [ProviderKind.Deepseek] = "DeepSeek", [ProviderKind.Meta] = "Meta",
            [ProviderKind.Zai] = "Zai", [ProviderKind.Devin] = "Devin", [ProviderKind.Antigravity] = "Gemini", [ProviderKind.Copilot] = "Copilot",
            [ProviderKind.Commandcode] = "CommandCode", [ProviderKind.Pi] = "Pi",
            [ProviderKind.OpenRouter] = "Claude", // Purple has no maker
            [ProviderKind.Local] = "Claude", // ED-410: existing no-maker fallback
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

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-4.5.5: a maker's palette from liquid-palettes.json's own dark and light stops, by that rule
    public void A_makers_palette_is_its_dark_mid_and_light_and_its_light_deep_and_mid_with_the_accent()
    {
        foreach (var maker in MakerLiquids.All)
        {
            var palette = PreviewMakers.Palette(maker["id"], "#4B9FFC", "#0067C0");
            Assert.Equal(new ComposerLightPalette(maker["dark.mid"], maker["dark.light"], maker["light.deep"], maker["light.mid"], "#4B9FFC", "#0067C0"), palette);
        }
        Assert.Equal(("#B95D3A", "#D47E58", "#A14827", "#B95D3A"), (PreviewMakers.Palette("Claude", "#000000", "#000000").MakerHeartDark, PreviewMakers.Palette("Claude", "#000000", "#000000").MakerSecondDark, PreviewMakers.Palette("Claude", "#000000", "#000000").MakerHeartLight, PreviewMakers.Palette("Claude", "#000000", "#000000").MakerSecondLight));
    }
}
