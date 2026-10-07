using Avalonia.Automation;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-7 (the Uiverse light's rows), CZ-12.6 (the colour wells and the glass picker of CZ-2). What each row says is the locked cz.js's own auraRows()
// (tests/fixtures/customization/page.json, made by page.generate.cjs): every fixture case is put into the app's settings and the page's rows are read.
[Collection("Headless")]
public sealed class CustomizationAuraRowsTests(HeadlessSession headless)
{
    private static (CzRow Row, CzSliderRow? Slider) RowOf(AuraSections a, string key) => key switch
    {
        "colours" => (a.Colours.Row, null),
        "custom" => (a.YourColours, null),
        "speed" => (a.Speed.Row, a.Speed),
        "breath" => (a.Breathing.Row, null),
        "rest" => (a.AtRest.Row, a.AtRest),
        "typing" => (a.WhileTyping.Row, a.WhileTyping),
        "running" => (a.WhileRunning.Row, a.WhileRunning),
        "rim" => (a.RimLight.Row, null),
        "dots" => (a.Halftone.Row, null),
        "hs" => (a.HaloSize.Row, a.HaloSize),
        "hb" => (a.HaloSoftness.Row, a.HaloSoftness),
        "rw" => (a.RimWidth.Row, a.RimWidth),
        "dc" => (a.DotSpacing.Row, a.DotSpacing),
        "ds" => (a.DotStrength.Row, a.DotStrength),
        _ => throw new ArgumentException(key),
    };

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T18, CZ-T19, CZ-T20, CZ-7.1 to CZ-7.4: in every fixture case each row's detail, shown value and dimmed state are the design's (the maker is the preview's, the accent the user's)
    [Trait("Spec", "CZ-T18")]
    [Trait("Spec", "CZ-T19")]
    [Trait("Spec", "CZ-T20")]
    public void Every_uiverse_row_says_what_the_design_says_in_every_fixture_case() => headless.OnUiThread(() =>
    {
        using var fixture = PageFixture.Open();
        // the design's preview maker is OpenAI here, as the default provider (Codex) gives; its accent is Blue
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s => s.Accent = "blue");
        Assert.Equal(fixture.RootElement.GetProperty("makerName").GetString(), PreviewMakers.For(rig.Page.Session!.Maker).Name);
        var cases = 0;
        foreach (var c in fixture.RootElement.GetProperty("auras").EnumerateArray())
        {
            var name = c.GetProperty("name").GetString()!;
            rig.SetLight(PageFixture.Settings("aura", null, c.GetProperty("aura")));
            var aura = rig.Page.Aura!;
            foreach (var pair in c.GetProperty("rows").EnumerateObject())
            {
                var (row, slider) = RowOf(aura, pair.Name);
                var expected = pair.Value;
                if (pair.Name == "custom")
                {
                    Assert.True(expected.GetProperty("fold").GetBoolean() == !aura.YourColoursSlot.Open, $"{name}: Your colours folds open only for Custom");
                    Assert.Equal("One for each side of the glow", row.Detail.Text);
                    continue;
                }
                Assert.True(expected.GetProperty("d").GetString() == row.Detail.Text, $"{name} / {pair.Name}: detail '{row.Detail.Text}'");
                Assert.True(expected.GetProperty("dim").GetBoolean() == row.Dimmed, $"{name} / {pair.Name}: dimmed");
                if (slider is not null) Assert.True(expected.GetProperty("vt").GetString() == slider.Slider.ValueText.Text, $"{name} / {pair.Name}: value '{slider.Slider.ValueText.Text}'");
            }
            cases++;
        }
        Assert.Equal(17, cases);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T18, CZ-7.1: the Model detail names the preview's maker and follows the maker switch; the Accent detail names the user's accent and follows it
    [Trait("Spec", "CZ-T18")]
    public void The_colours_detail_follows_the_preview_maker_and_the_accent() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s => s.ComposerLight = ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura, Aura = UiverseAuraOptions.Default with { Scheme = AuraScheme.Maker } });
        var colours = rig.Page.Aura!.Colours.Row;
        Assert.Equal("The model in use: OpenAI", colours.Detail.Text);
        rig.Do(() => rig.Page.Session!.Pick("Claude"));
        Assert.Equal("The model in use: Claude", colours.Detail.Text);
        rig.Do(() => rig.Page.Session!.Step(1));
        Assert.Equal("The model in use: OpenAI", colours.Detail.Text);
        rig.SetLight(rig.Light with { Aura = rig.Light.Aura with { Scheme = AuraScheme.Accent } });
        Assert.Equal("Your accent: Windows accent", colours.Detail.Text);
        rig.Do(() => rig.App.Settings.Accent = "teal");
        Assert.Equal("Your accent: Teal", colours.Detail.Text);
        rig.SetLight(rig.Light with { Aura = rig.Light.Aura with { Scheme = AuraScheme.Custom } });
        Assert.Equal("Two colours you pick", colours.Detail.Text);
        rig.SetLight(rig.Light with { Aura = rig.Light.Aura with { Scheme = AuraScheme.Original } });
        Assert.Equal("Orange and blue", colours.Detail.Text);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T18, CZ-7.1: a Colours pick writes the scheme; Your colours folds open for Custom only
    [Trait("Spec", "CZ-T18")]
    public void A_colours_pick_writes_the_scheme_and_Custom_opens_the_wells() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s => s.ComposerLight = ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura });
        var aura = rig.Page.Aura!;
        Assert.Equal(["Original", "Model", "Accent", "Custom"], aura.Colours.Control.Buttons.Select(b => AutomationProperties.GetName(b)));
        Assert.False(aura.YourColoursSlot.Open);
        foreach (var (id, scheme) in new[] { ("maker", AuraScheme.Maker), ("accent", AuraScheme.Accent), ("custom", AuraScheme.Custom), ("original", AuraScheme.Original) })
        {
            rig.Do(() => aura.Colours.Control.Select(id));
            Assert.Equal(scheme, rig.Light.Aura.Scheme);
            Assert.Equal(scheme == AuraScheme.Custom, aura.YourColoursSlot.Open);
        }
        rig.SetLight(rig.Light with { Aura = rig.Light.Aura with { Scheme = AuraScheme.Custom } });
        Assert.True(aura.YourColoursSlot.Open);
        Assert.Equal("custom", aura.Colours.Control.SelectedId);
        Assert.Equal("First colour", AutomationProperties.GetName(aura.FirstWell));
        Assert.Equal("Second colour", AutomationProperties.GetName(aura.SecondWell));
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T20, CZ-7.4: Rim width and the two dot rows dim (and go inert) with their switches, and their details swap
    [Trait("Spec", "CZ-T20")]
    public void The_rim_and_dot_rows_dim_with_their_switches() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s => s.ComposerLight = ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura });
        rig.Do(() => rig.Page.Advanced.Open = true);
        var aura = rig.Page.Aura!;
        Assert.False(aura.RimWidth.Row.Dimmed);
        Assert.Equal("Thickness of the rim light", aura.RimWidth.Row.Detail.Text);
        rig.Do(aura.RimLight.Switch.Toggle);
        Assert.False(rig.Light.Aura.Rim);
        Assert.True(aura.RimWidth.Row.Dimmed);
        Assert.False(aura.RimWidth.Slider.IsEnabled);
        Assert.False(aura.RimWidth.Slider.Rail.Focusable);
        Assert.Equal("Turn on Rim light to use it", aura.RimWidth.Row.Detail.Text);
        Assert.False(aura.DotSpacing.Row.Dimmed);
        rig.Do(aura.Halftone.Switch.Toggle);
        Assert.False(rig.Light.Aura.Dots);
        Assert.True(aura.DotSpacing.Row.Dimmed);
        Assert.True(aura.DotStrength.Row.Dimmed);
        Assert.Equal("Turn on Halftone to use it", aura.DotSpacing.Row.Detail.Text);
        Assert.Equal("Turn on Halftone to use it", aura.DotStrength.Row.Detail.Text);
        var before = rig.Light.Aura.DotCell;
        rig.Do(() => aura.DotSpacing.Slider.SetValue(6)); // inert
        Assert.Equal(before, rig.Light.Aura.DotCell);
        rig.Do(aura.Halftone.Switch.Toggle);
        Assert.False(aura.DotSpacing.Row.Dimmed);
        Assert.Equal("Finer or coarser dots", aura.DotSpacing.Row.Detail.Text);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T19, CZ-7.2, CZ-7.3: the Uiverse sliders' ranges, steps and formats; Speed is inverted 3..40 in 0.5
    [Trait("Spec", "CZ-T19")]
    public void The_uiverse_sliders_have_the_designs_ranges_and_write_what_they_show() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s => s.ComposerLight = ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura });
        rig.Do(() => rig.Page.Advanced.Open = true);
        var aura = rig.Page.Aura!;
        (CzSliderRow Row, double Min, double Max, double Step, bool Inverted)[] table =
        [
            (aura.Speed, 3, 40, 0.5, true), (aura.AtRest, 0, 100, 1, false), (aura.WhileTyping, 0, 100, 1, false), (aura.WhileRunning, 0, 100, 1, false),
            (aura.HaloSize, 0, 40, 1, false), (aura.HaloSoftness, 4, 64, 1, false), (aura.RimWidth, 0.5, 5, 0.25, false), (aura.DotSpacing, 2, 8, 0.5, false), (aura.DotStrength, 0, 100, 1, false),
        ];
        foreach (var (row, min, max, step, inverted) in table)
        {
            Assert.Equal((min, max, step, inverted), (row.Slider.Minimum, row.Slider.Maximum, row.Slider.Step, row.Slider.Inverted));
            Assert.Null(row.Slider.AutoValue);
        }
        rig.Do(() => aura.Speed.Slider.SetValue(7.26));
        Assert.Equal(7.5, rig.Light.Aura.Speed);
        Assert.Equal("7.5 s", aura.Speed.Slider.ValueText.Text);
        rig.Do(() => aura.WhileRunning.Slider.SetValue(88.4));
        Assert.Equal(88, rig.Light.Aura.Running);
        Assert.Equal("88%", aura.WhileRunning.Slider.ValueText.Text);
        rig.Do(() => aura.RimWidth.Slider.SetValue(2.3));
        Assert.Equal(2.25, rig.Light.Aura.RimWidth);
        Assert.Equal("2.3 px", aura.RimWidth.Slider.ValueText.Text); // fmt(2.25, 1): the design's own rounding of a half
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T18, CZ-7.1, CZ-12.6, OD-0099 Q-CZ-3 B: a well opens the glass colour picker below it (a popover with the arrow tip) on its colour; a pick writes #RRGGBBAA and the well follows
    public void A_well_opens_the_glass_picker_and_a_pick_is_stored_with_its_alpha() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s => s.ComposerLight = ComposerLightSettings.Default with
        {
            Style = ComposerLightStyle.Aura,
            Aura = UiverseAuraOptions.Default with { Scheme = AuraScheme.Custom },
        });
        var aura = rig.Page.Aura!;
        Assert.Equal("#8E7CFF", aura.FirstWell.Hex);
        Assert.Equal("#38C9B4", aura.SecondWell.Hex);
        rig.Do(aura.FirstWell.Activate);
        var popup = Assert.Single(rig.Overlay.Popups);
        Assert.True(popup.IsOpen);
        Assert.Same(aura.FirstWell, aura.Picker.OpenWell);
        Assert.Same(aura.FirstWell, popup.PlacementTarget);
        Assert.Equal(Avalonia.Controls.PlacementMode.Bottom, popup.Placement);
        Assert.Equal("#8E7CFFFF", aura.Picker.Picker.Value);
        // a pick in the picker: the hex field, with an alpha of its own
        rig.Do(() => { aura.Picker.Picker.HexBox.Text = "#11223380"; aura.Picker.Picker.EnterHex(); });
        Assert.Equal("#11223380", rig.Light.Aura.Color1);
        Assert.Equal("#38C9B4", rig.Light.Aura.Color2);
        Assert.Equal("#11223380", aura.FirstWell.Hex);
        // the other well: the same picker moves to it
        rig.Do(aura.SecondWell.Activate);
        Assert.Same(aura.SecondWell, aura.Picker.OpenWell);
        Assert.Equal("#38C9B4FF", aura.Picker.Picker.Value);
        rig.Do(() => { aura.Picker.Picker.HexBox.Text = "#ABCDEF"; aura.Picker.Picker.EnterHex(); });
        Assert.Equal("#ABCDEFFF", rig.Light.Aura.Color2);
        // and it closes
        rig.Do(() => aura.Picker.Close());
        Assert.Null(aura.Picker.OpenWell);
        rig.Advance(600); // the popover shrinks back into its tip before it goes
        Assert.False(popup.IsOpen);
        // the settings can move the wells too
        rig.SetLight(rig.Light with { Aura = rig.Light.Aura with { Color1 = "#00FF00", Color2 = "#FF0000" } });
        Assert.Equal("#00FF00", aura.FirstWell.Hex);
        Assert.Equal("#FF0000", aura.SecondWell.Hex);
    });
}
