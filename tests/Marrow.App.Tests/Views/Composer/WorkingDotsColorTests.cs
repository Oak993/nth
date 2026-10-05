using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Composer;

// customization-SPEC CZ-10.4 (CZ-T53): the colour the working dots wear. Accent is the user's accent, Model is the maker's mid stop toned for the window
// (the locked cz.js's wdColour, in dots.json), Custom is the swatch's hex for the theme, and a brand with no maker (Purple) wears its own mid, #8C57F7.
[Collection("Headless")]
public sealed class WorkingDotsColorTests(HeadlessSession headless)
{
    private static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    private static ThemeVariant Of(bool dark) => dark ? ThemeVariant.Dark : ThemeVariant.Light;

    private static WorkingDotsSettings Mode(WorkingDotsMode mode, string custom = "blue") => new() { Mode = mode, Custom = custom };

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T53, CZ-10.4: accent is Chrome.Accent, whatever the maker and the custom pick
    public void Accent_is_the_chrome_accent_in_both_themes() => headless.OnUiThread(() =>
    {
        var (theme, choice) = (ThemeManager.Current, AccentChoice.Current);
        try
        {
            ThemeManager.Current = AppTheme.System;
            foreach (var id in new[] { "windows", "orchid", "teal" })
            {
                AccentChoice.Current = id;
                foreach (var dark in new[] { true, false })
                    Assert.Equal(Chrome.Accent.Resolve(Of(dark)), WorkingDotsColor.Resolve(Mode(WorkingDotsMode.Accent, "red"), "OpenAI", Of(dark)));
            }
        }
        finally { AccentChoice.Current = choice; ThemeManager.Current = theme; }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T53, CZ-10.4: model is czTone(mid, dark, title) of the maker, for each of the 30 makers in both themes, as the design's wdColour gives it
    public void Model_is_the_toned_mid_of_every_maker_in_both_themes()
    {
        using var fixture = DotsFixture.Open();
        var checkedCount = 0;
        foreach (var maker in MakerLiquids.All.Select(m => m["id"]))
            foreach (var dark in new[] { true, false })
            {
                Assert.Equal(DotsFixture.Model(fixture, maker, dark), Hex(WorkingDotsColor.Resolve(Mode(WorkingDotsMode.Model), maker, Of(dark))));
                checkedCount++;
            }
        Assert.Equal(60, checkedCount);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T53, CZ-10.4: the composer's maker is its thread's provider brand mapped through JewelPalettes; every brand lands on its maker's colour (Purple on its own mid)
    public void Every_brand_the_app_has_wears_its_makers_colour()
    {
        using var fixture = DotsFixture.Open();
        var brands = Enum.GetValues<EffortBrand>();
        Assert.Equal(15, brands.Length);
        foreach (var brand in brands)
        {
            var maker = WorkingDotsColor.MakerOf(brand);
            Assert.Equal(JewelPalettes.For(brand).Maker, maker);
            foreach (var dark in new[] { true, false })
            {
                var expected = brand == EffortBrand.Purple ? "#8c57f7" : DotsFixture.Model(fixture, maker, dark);
                Assert.Equal(expected, Hex(WorkingDotsColor.Resolve(Mode(WorkingDotsMode.Model), maker, Of(dark))));
            }
        }
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T53, CZ-10.4 "Purple rule": no maker (null, or a name MakerLiquids does not hold) uses JewelPalettes Purple's mid #8C57F7 with no title colour, in both themes
    public void The_purple_rule_gives_a_brand_with_no_maker_its_own_mid()
    {
        Assert.Equal("#8C57F7", JewelPalettes.For(EffortBrand.Purple).High[1]);
        foreach (var maker in new string?[] { null, "Purple", "", "Nobody" })
            foreach (var dark in new[] { true, false })
                Assert.Equal("#8c57f7", Hex(WorkingDotsColor.Resolve(Mode(WorkingDotsMode.Model), maker, Of(dark))));
        Assert.Equal("Purple", WorkingDotsColor.MakerOf(EffortBrand.Create((ProviderKind?)null)));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T53, CZ-10.4: custom is the picked swatch's hex for the theme, for all 22 (the Windows accent draws the lifted system accent as the accent picker does)
    public void Custom_is_the_swatchs_hex_in_both_themes() => headless.OnUiThread(() =>
    {
        using var fixture = DotsFixture.Open();
        foreach (var swatch in AccentPalette.All)
            foreach (var dark in new[] { true, false })
            {
                var got = WorkingDotsColor.Resolve(Mode(WorkingDotsMode.Custom, swatch.Id), "Claude", Of(dark));
                if (swatch.Id == AccentPalette.WindowsId) Assert.Equal(SwiftColor.ControlAccentColor.Resolve(Of(dark)), got);
                else
                {
                    Assert.Equal(swatch.Hex(dark).ToLowerInvariant(), Hex(got));
                    Assert.Equal(DotsFixture.Custom(fixture, swatch.Id, dark), Hex(got));
                }
            }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T53: an id the palette does not hold reads as the default pick (blue), as the stored setting does
    public void An_unknown_custom_id_wears_the_default_pick()
    {
        foreach (var dark in new[] { true, false })
            Assert.Equal(WorkingDotsColor.Resolve(Mode(WorkingDotsMode.Custom, "blue"), "Claude", Of(dark)), WorkingDotsColor.Resolve(Mode(WorkingDotsMode.Custom, "mauve"), "Claude", Of(dark)));
    }

    [Fact]
    [Trait("Decision", "ED-253")] // CZ-10.4: the colour a composer draws is one object for one answer, so a field that is handed it again does not start a blend
    public void The_colour_a_composer_wears_is_one_object_for_one_answer()
    {
        var model = Mode(WorkingDotsMode.Model);
        Assert.Same(WorkingDotsColor.Of(model, "Claude"), WorkingDotsColor.Of(model, "Claude"));
        Assert.NotSame(WorkingDotsColor.Of(model, "Claude"), WorkingDotsColor.Of(model, "OpenAI"));
        Assert.Same(WorkingDotsColor.Of(Mode(WorkingDotsMode.Accent, "red"), "Claude"), WorkingDotsColor.Of(Mode(WorkingDotsMode.Accent, "teal"), "OpenAI"));
        Assert.Same(WorkingDotsColor.Of(Mode(WorkingDotsMode.Custom, "teal"), "Claude"), WorkingDotsColor.Of(Mode(WorkingDotsMode.Custom, "teal"), "OpenAI"));
    }
}
