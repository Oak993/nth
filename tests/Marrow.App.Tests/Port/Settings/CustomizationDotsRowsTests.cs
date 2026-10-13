using Avalonia.Automation;
using Avalonia.Media;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Settings;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-10.1 to CZ-10.3 (CZ-T51, CZ-T52): the Working dots section. What it says is the locked cz.js's own dotsSec() (tests/fixtures/customization/dots.json,
// made by dots.generate.cjs): every fixture case is put into the app's settings and the preview's maker, and the page's rows are read.
[Collection("Headless")]
public sealed class CustomizationDotsRowsTests(HeadlessSession headless)
{
    private static string?[] Names(CzRadioGroup group) => group.Buttons.Select(b => AutomationProperties.GetName(b)).ToArray();

    private static void Show(CustomizationPageRig rig, string wd, string wdc)
    {
        var mode = wd switch { "model" => WorkingDotsMode.Model, "custom" => WorkingDotsMode.Custom, _ => WorkingDotsMode.Accent };
        rig.Do(() => rig.App.Settings.WorkingDots = new WorkingDotsSettings { Mode = mode, Custom = wdc });
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T51, CZ-10.1: in every fixture case the heading, the Dot colour row (label, options, pick, detail), the Custom row's fold and its swatch lines (names, order, pick) and the note are the design's
    public void Every_row_says_what_the_design_says_in_every_fixture_case() => headless.OnUiThread(() =>
    {
        using var fixture = DotsFixture.Open();
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var dots = rig.Page.Dots!;
        var cases = 0;
        foreach (var c in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var (wd, wdc, maker) = (c.GetProperty("wd").GetString()!, c.GetProperty("wdc").GetString()!, c.GetProperty("maker").GetString()!);
            var name = $"{wd}/{wdc}/{maker}";
            rig.Do(() => rig.Page.Session!.Pick(maker));
            Show(rig, wd, wdc);
            Assert.True(c.GetProperty("title").GetString() == dots.Section.Title, name + ": heading");
            var segment = c.GetProperty("segment");
            Assert.True(segment.GetProperty("t").GetString() == dots.DotColour.Row.Title.Text, name + ": row title");
            Assert.True(segment.GetProperty("label").GetString() == AutomationProperties.GetName(dots.DotColour.Control), name + ": accessible name");
            Assert.True(segment.GetProperty("d").GetString() == dots.DotColour.Row.Detail.Text, $"{name}: detail '{dots.DotColour.Row.Detail.Text}'");
            var options = segment.GetProperty("opts").EnumerateArray().ToArray();
            Assert.Equal(options.Select(o => o.GetProperty("t").GetString()), Names(dots.DotColour.Control));
            Assert.Equal(options.Single(o => o.GetProperty("on").GetString() == "true").GetProperty("t").GetString(), AutomationProperties.GetName(dots.DotColour.Control.Buttons.Single(b => b.Picked)));
            var swatches = c.GetProperty("swatches");
            Assert.True(swatches.GetProperty("t").GetString() == "Custom dot colour", name + ": custom row name");
            Assert.True(swatches.GetProperty("fold").GetBoolean() == !dots.CustomSlot.Open, name + ": the Custom row folds open only for Custom");
            foreach (var (key, group, label) in new[] { ("classic", dots.Swatches.Classic, "Classic dot colours"), ("signature", dots.Swatches.Signature, "Signature dot colours") })
            {
                var expected = swatches.GetProperty(key).EnumerateArray().ToArray();
                Assert.Equal(label, AutomationProperties.GetName(group));
                Assert.Equal(expected.Select(e => e.GetProperty("name").GetString()), Names(group));
                var on = expected.SingleOrDefault(e => e.GetProperty("on").GetString() == "true");
                Assert.Equal(on.ValueKind == System.Text.Json.JsonValueKind.Undefined ? "" : AccentPalette.All.Single(a => a.Name == on.GetProperty("name").GetString()).Id, group.SelectedId);
            }
            Assert.True(c.GetProperty("note").GetString() == dots.Note.Text.Text, name + ": note");
            cases++;
        }
        Assert.Equal(48, cases);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T51, CZ-10.1: Classic shows 10 swatches and Signature 12, in the palette's order, drawn in the theme's hex (the Windows accent as the lifted system accent)
    public void Classic_shows_ten_swatches_and_Signature_twelve_in_palette_order_in_the_themes_hex() => headless.OnUiThread(() =>
    {
        foreach (var light in new[] { false, true })
        {
            using var rig = new CustomizationPageRig(reduceMotion: true, light: light);
            var dots = rig.Page.Dots!;
            Assert.Equal(AccentPalette.All.Take(10).Select(a => a.Id), dots.Swatches.Classic.Buttons.Select(b => b.Id));
            Assert.Equal(AccentPalette.All.Skip(10).Select(a => a.Id), dots.Swatches.Signature.Buttons.Select(b => b.Id));
            Assert.Equal((10, 12), (dots.Swatches.Classic.Buttons.Count, dots.Swatches.Signature.Buttons.Count));
            foreach (var swatch in AccentPalette.All)
            {
                var button = dots.Swatches.Classic.Buttons.Concat(dots.Swatches.Signature.Buttons).Single(b => b.Id == swatch.Id);
                var fill = Assert.IsAssignableFrom<ISolidColorBrush>(button.SwatchFill).Color;
                var expected = swatch.Id == AccentPalette.WindowsId
                    ? SwiftColor.ControlAccentColor.Resolve(light ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark)
                    : Color.Parse(swatch.Hex(!light));
                Assert.True(expected == fill, $"{(light ? "light" : "dark")} {swatch.Id}: {fill}, wanted {expected}");
                Assert.Equal(24, button.Width);
            }
            Assert.Equal(62, dots.Swatches.LabelColumn);
        }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T51, CZ-10.1: the Custom row opens when Custom is picked (by the setting) and folds away when another mode is
    public void The_custom_row_folds_open_for_Custom_and_shut_for_the_other_modes() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: false);
        var dots = rig.Page.Dots!;
        Assert.False(dots.CustomSlot.Open);
        Assert.Equal(0, dots.CustomSlot.Fraction);
        rig.Do(() => rig.App.Settings.WorkingDots = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Custom });
        Assert.True(dots.CustomSlot.Open);
        Assert.InRange(dots.CustomSlot.Fraction, 0, 0.99); // folding open over 0.34 s, not cut
        rig.Advance(400);
        Assert.Equal(1, dots.CustomSlot.Fraction);
        rig.Do(() => rig.App.Settings.WorkingDots = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Model });
        Assert.False(dots.CustomSlot.Open);
        rig.Advance(400);
        Assert.Equal(0, dots.CustomSlot.Fraction);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T52, CZ-10.2, CZ-4.6: a pick of the mode writes the setting, switches the preview to Running and cancels the voice demo
    public void Picking_a_mode_writes_the_setting_switches_the_preview_to_Running_and_cancels_the_voice_demo() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var session = rig.Page.Session!;
        var cancels = 0;
        session.VoiceDemoCancelRequested += () => cancels++;
        Assert.Equal(PreviewState.Rest, session.State);
        rig.Do(() => rig.Page.Dots!.DotColour.Control.Select("model"));
        Assert.Equal(WorkingDotsMode.Model, rig.App.Settings.WorkingDots.Mode);
        Assert.Equal((PreviewState.Running, 1), (session.State, cancels));
        Assert.Equal("running", rig.Page.PreviewStatePick.SelectedId);
        rig.Do(() => rig.Page.PreviewStatePick.Select("typing"));
        rig.Do(() => rig.Page.Dots!.DotColour.Control.Select("custom"));
        Assert.Equal((WorkingDotsMode.Custom, PreviewState.Running, 2), (rig.App.Settings.WorkingDots.Mode, session.State, cancels));
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T52, CZ-10.2: a pick of a swatch (Classic or Signature) writes the colour, keeps the mode, switches the preview to Running and cancels the voice demo; the other line lets go
    public void Picking_a_swatch_writes_the_colour_and_does_what_a_pick_of_the_mode_does() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s => s.WorkingDots = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Custom });
        var (dots, session) = (rig.Page.Dots!, rig.Page.Session!);
        var cancels = 0;
        session.VoiceDemoCancelRequested += () => cancels++;
        Assert.Equal("blue", dots.Swatches.Classic.SelectedId);
        rig.Do(() => dots.Swatches.Signature.Select("indigo"));
        Assert.Equal(new WorkingDotsSettings { Mode = WorkingDotsMode.Custom, Custom = "indigo" }, rig.App.Settings.WorkingDots);
        Assert.Equal(("indigo", "", PreviewState.Running, 1), (dots.Swatches.Signature.SelectedId, dots.Swatches.Classic.SelectedId, session.State, cancels));
        Assert.Equal("Indigo", dots.DotColour.Row.Detail.Text);
        rig.Do(() => dots.Swatches.Classic.Select("windows"));
        Assert.Equal("windows", rig.App.Settings.WorkingDots.Custom);
        Assert.Equal(("windows", "", 2), (dots.Swatches.Classic.SelectedId, dots.Swatches.Signature.SelectedId, cancels));
        Assert.Equal("Windows accent", dots.DotColour.Row.Detail.Text);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-10.1, CZ-13.1: a change of the setting from elsewhere shows in the rows without touching the preview's state or the voice demo
    public void A_change_from_elsewhere_shows_in_the_rows_and_does_not_touch_the_preview() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var (dots, session) = (rig.Page.Dots!, rig.Page.Session!);
        var cancels = 0;
        session.VoiceDemoCancelRequested += () => cancels++;
        rig.Do(() => rig.App.Settings.WorkingDots = new WorkingDotsSettings { Mode = WorkingDotsMode.Custom, Custom = "mint" });
        Assert.Equal(("custom", "mint", "Mint"), (dots.DotColour.Control.SelectedId, dots.Swatches.Signature.SelectedId, dots.DotColour.Row.Detail.Text));
        Assert.Equal(("", PreviewState.Rest, 0), (dots.Swatches.Classic.SelectedId, session.State, cancels));
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-10.1: the Model colour detail names the preview's maker and follows the maker switch
    public void The_model_detail_follows_the_preview_maker() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true, seed: s => s.WorkingDots = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Model });
        var detail = rig.Page.Dots!.DotColour.Row.Detail;
        Assert.Equal("The colour of the model in use: OpenAI", detail.Text);
        rig.Do(() => rig.Page.Session!.Pick("Claude"));
        Assert.Equal("The colour of the model in use: Claude", detail.Text);
        rig.Do(() => rig.Page.Session!.Pick("OpenCodeGo"));
        Assert.Equal("The colour of the model in use: OpenCode Go", detail.Text);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-3.3: the Working dots section follows the style's sections inside the sticky container, before the Speech section's place
    public void The_section_sits_at_the_end_of_the_sticky_container_after_the_style_sections() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var page = rig.Page;
        var sections = page.Sticky.Children.OfType<CzSection>().ToList();
        Assert.Same(page.Off, sections[^3]);
        Assert.Same(page.Voice!.Section, sections[^2]);
        Assert.Equal("Voice orb", sections[^2].Title);
        Assert.Same(page.Dots!.Section, sections[^1]);
        Assert.Equal("Working dots", sections[^1].Title);
        Assert.Equal(1, sections[^1].Fraction);
        Assert.Collection(page.Rest.Children, // CZ-8: the Speech section is first after the sticky container, then "Pill in the composer" (multi-account step 3a)
            child => Assert.Same(page.Speech!.Section, child),
            child => Assert.IsType<Marrow.App.Port.MultiAccount.Capsule.PillCapsuleSection>(child),
            child => Assert.Same(page.Glass!.Section, child)); // then Glass (ED-NEW-glasslook-2, owner-requested: the user's Liquid Glass Look and tuning)
    });
}
