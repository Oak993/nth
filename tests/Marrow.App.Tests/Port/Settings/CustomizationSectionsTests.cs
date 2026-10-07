using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-5 (Style), CZ-6.4 (Advanced), CZ-6.5 and CZ-7.5 (Reset), CZ-8 (Off), CZ-12.8 (folds and dividers). The outline of each style is the locked cz.js's own `sections()`
// (tests/fixtures/customization/page.json, made by page.generate.cjs), written line by line in the same form as the page's.
[Collection("Headless")]
public sealed class CustomizationSectionsTests(HeadlessSession headless)
{
    private static List<string> Outline(CustomizationSettingsPage page) => PageOutline.Of(page.Beam!.Sections.Concat(page.Aura!.Sections).Append(page.Off!));

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T16, CZ-T39, CZ-12.8: for every style, every Advanced state and the Line and Pulse outside shapes, the page's rows, folds and dividers are the design's
    [Trait("Spec", "CZ-T16")]
    [Trait("Spec", "CZ-T39")]
    public void The_sections_rows_folds_and_dividers_are_the_designs_in_every_state() => headless.OnUiThread(() =>
    {
        using var fixture = PageFixture.Open();
        var outlines = fixture.RootElement.GetProperty("sections");
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var beam = BorderBeamOptions.Default;
        var cases = new (string Key, ComposerLightSettings Light, bool Advanced)[]
        {
            ("beam", ComposerLightSettings.Default, false),
            ("beamAdvanced", ComposerLightSettings.Default, true),
            ("aura", ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura }, false),
            ("auraAdvanced", ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura }, true),
            ("off", ComposerLightSettings.Default with { Style = ComposerLightStyle.Off }, false),
            ("beamLine", ComposerLightSettings.Default with { Beam = beam.WithType(BeamType.Line) }, true),
            ("beamPulseOutside", ComposerLightSettings.Default with { Beam = beam.WithType(BeamType.PulseOutside) }, true),
        };
        foreach (var (key, light, advanced) in cases)
        {
            rig.Do(() => { rig.App.Settings.ComposerLight = light; rig.Page.Advanced.Open = advanced; });
            Assert.Equal(PageOutline.OfDesign(outlines.GetProperty(key)), Outline(rig.Page));
        }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T23, CZ-5.1: three cards in a radio group named Light with the design's texts; a pick writes `style` and keeps the other styles' settings (CZ-5.3)
    [Trait("Spec", "CZ-T23")]
    public void The_style_cards_are_a_radio_group_and_a_pick_writes_the_style_keeping_the_others_settings() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s => s.ComposerLight = ComposerLightSettings.Default with
        {
            Beam = BorderBeamOptions.Default with { Duration = 6.5, Palette = BeamPalette.Ocean },
            Aura = UiverseAuraOptions.Default with { Speed = 20.5, Scheme = AuraScheme.Accent },
        });
        var cards = rig.Page.StyleCards!;
        Assert.Equal("beam", cards.SelectedId);
        Assert.Equal(["Border beam", "Uiverse light", "Off"], cards.Buttons.Select(b => Avalonia.Automation.AutomationProperties.GetName(b)));
        var before = rig.Light;
        rig.Do(() => cards.Select("aura"));
        Assert.Equal(ComposerLightStyle.Aura, rig.Light.Style);
        Assert.Equal(before.Beam, rig.Light.Beam);
        Assert.Equal(before.Aura, rig.Light.Aura);
        rig.Do(() => cards.Select("off"));
        Assert.Equal(ComposerLightStyle.Off, rig.Light.Style);
        rig.Do(() => cards.Select("beam"));
        Assert.Equal(ComposerLightStyle.Beam, rig.Light.Style);
        Assert.Equal(6.5, rig.Light.Beam.Duration);
        Assert.Equal(20.5, rig.Light.Aura.Speed);
        // and the cards follow the settings the other way
        rig.SetLight(rig.Light with { Style = ComposerLightStyle.Aura });
        Assert.Equal("aura", cards.SelectedId);
        Assert.Single(cards.Buttons, b => b.Focusable);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T22, CZ-8: Off shows the note card only; switching back keeps both styles' settings
    [Trait("Spec", "CZ-T22")]
    public void Off_shows_the_note_alone_and_both_styles_keep_their_settings() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        rig.Do(() => rig.Page.Beam!.Speed.Slider.SetValue(7.25));
        rig.Do(() => rig.Page.StyleCards!.Select("aura"));
        rig.Do(() => rig.Page.Aura!.Speed.Slider.SetValue(22.5));
        rig.Do(() => rig.Page.StyleCards!.Select("off"));
        var open = rig.Get(() => rig.Page.Sticky.Children.OfType<CzSection>().Where(s => s.Open).ToList());
        Assert.Equal([rig.Page.Off!, rig.Page.Voice!.Section, rig.Page.Dots!.Section], open);
        Assert.Null(rig.Page.Off!.Title);
        var note = Assert.IsType<CzNoteRow>(Assert.Single(rig.Page.Off.Rows.Slots).Row);
        Assert.Equal("The composer has no light. Your Border beam and Uiverse light settings are kept for when you turn one back on.", note.Text.Text);
        Assert.Equal(7.25, rig.Light.Beam.Duration);
        Assert.Equal(22.5, rig.Light.Aura.Speed);
        rig.Do(() => rig.Page.StyleCards!.Select("beam"));
        Assert.Equal(7.25, rig.Page.Beam!.Speed.Slider.Value);
        Assert.Equal(22.5, rig.Page.Aura!.Speed.Slider.Value);
        Assert.Equal(ComposerLightStyle.Beam, rig.Light.Style);
        Assert.False(rig.Page.Off.Open);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T24, CZ-6.4, CZ-7.4: the Advanced open state is shared by both styles' Advanced rows, and it is session state (a new page starts shut)
    [Trait("Spec", "CZ-T24")]
    public void The_advanced_open_state_is_shared_between_the_styles_and_is_session_only() => headless.OnUiThread(() =>
    {
        using (var rig = new CustomizationPageRig(reduceMotion: true))
        {
            var beam = rig.Page.Beam!;
            var aura = rig.Page.Aura!;
            Assert.False(beam.AdvancedButton.Open);
            Assert.False(beam.AdvancedNest.Open);
            Assert.Equal("Show", beam.AdvancedButton.Label.Text);
            rig.Do(() => beam.AdvancedButton.Open = true);
            Assert.True(beam.AdvancedNest.Open);
            Assert.True(aura.AdvancedButton.Open);
            Assert.True(aura.AdvancedNest.Open);
            Assert.Equal("Hide", aura.AdvancedButton.Label.Text);
            rig.Do(() => aura.AdvancedButton.Open = false);
            Assert.False(beam.AdvancedButton.Open);
            Assert.False(beam.AdvancedNest.Open);
            rig.Do(() => beam.AdvancedButton.Open = true);
            Assert.DoesNotContain(rig.Defaults.Writes, key => key.Contains("dvanced", StringComparison.OrdinalIgnoreCase));
        }
        using var second = new CustomizationPageRig(reduceMotion: true);
        Assert.False(second.Page.Beam!.AdvancedButton.Open);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T17, CZ-6.5: Reset is idle (0.42, inert, not focusable) at the defaults, active otherwise, and resets family, shape and palette too
    [Trait("Spec", "CZ-T17")]
    public void Border_beam_reset_is_idle_at_the_defaults_and_resets_everything() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var beam = rig.Page.Beam!;
        Assert.Equal("Defaults", beam.Defaults.Title.Text);
        Assert.Equal("Border beam as Marrow ships it", beam.Defaults.Detail.Text);
        Assert.False(beam.ResetButton.IsEnabled);
        Assert.False(beam.ResetButton.Focusable);
        Assert.Equal(.42, beam.ResetButton.Opacity);
        Assert.Equal("Reset to defaults", Avalonia.Automation.AutomationProperties.GetName(beam.ResetButton));
        // the keys the design leaves out of the test do not wake it
        rig.SetLight(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default with { Fitted = false, Faithful = true } });
        Assert.False(beam.ResetButton.IsEnabled);
        rig.SetLight(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default.WithType(BeamType.PulseOutside) with { Palette = BeamPalette.Gold, Duration = 5, Respond = true } });
        Assert.True(beam.ResetButton.IsEnabled);
        Assert.True(beam.ResetButton.Focusable);
        Assert.Equal(1, beam.ResetButton.Opacity);
        Assert.Equal("Return every Border beam setting to how it ships", beam.Defaults.Detail.Text);
        rig.Do(beam.ResetButton.Activate);
        Assert.Equal(BorderBeamOptions.Default, rig.Light.Beam);
        Assert.False(beam.ResetButton.IsEnabled);
        Assert.Equal("Border beam as Marrow ships it", beam.Defaults.Detail.Text);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T21, CZ-7.5: the Uiverse reset, including the Custom colours folded shut while it happens
    [Trait("Spec", "CZ-T21")]
    public void Uiverse_reset_returns_every_setting_and_the_hidden_wells_follow() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s => s.ComposerLight = ComposerLightSettings.Default with
        {
            Style = ComposerLightStyle.Aura,
            Aura = UiverseAuraOptions.Default with { Scheme = AuraScheme.Custom, Color1 = "#FF5500FF", Color2 = "#0055FF80", Speed = 30, Rim = false },
        });
        var aura = rig.Page.Aura!;
        Assert.Equal("Return every Uiverse light setting to how it ships", aura.Defaults.Detail.Text);
        Assert.True(aura.ResetButton.IsEnabled);
        Assert.Equal("#FF5500FF", aura.FirstWell.Hex);
        rig.Do(aura.ResetButton.Activate);
        Assert.Equal(UiverseAuraOptions.Default, rig.Light.Aura);
        Assert.Equal("The Uiverse light as Marrow ships it", aura.Defaults.Detail.Text);
        Assert.False(aura.ResetButton.IsEnabled);
        Assert.False(aura.YourColoursSlot.Open);
        // the wells were folded shut when the colours came back, and still show them
        Assert.Equal("#8E7CFF", aura.FirstWell.Hex);
        Assert.Equal("#38C9B4", aura.SecondWell.Hex);
        Assert.Equal(12, aura.Speed.Slider.Value);
    });
}
