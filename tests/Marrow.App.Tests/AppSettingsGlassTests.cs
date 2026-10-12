using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Settings;
using Marrow.UI.Port;

namespace Marrow.App.Tests;

// OD-0147 (the owner approved the Glass UI canvas, 2026-10-11: ship its defaults, every setting the user's) with ED-NEW-glasslook-2 (persisted): the
// glass key, stored as GlassSettings writes it, read back after a relaunch, held to its ranges, and mirrored into the material's tuning
// (GlassLooks.Tuning) as the theme is into ThemeManager. The earlier glassLook key (the release look's knobs) is not read.
[Collection("AppSettings")]
public sealed class AppSettingsGlassTests
{
    private static string Text(object? stored) => Encoding.UTF8.GetString((byte[])stored!);

    [Fact]
    [Trait("Decision", "OD-0147")] // nothing stored is the canvas's shipped glass (Regular, every value its own), mirrored, and a launch writes nothing
    public void Nothing_stored_is_the_shipped_glass_and_a_launch_writes_nothing()
    {
        using var scope = AppSettingsScope.Create();
        try
        {
            scope.OnMain(() =>
            {
                var settings = new AppSettings();
                Assert.Equal(GlassSettings.Default, settings.Glass);
                Assert.Equal((50d, GlassVariant.Regular, 1.30, 12d, 32d, 0.4, 0.50, 0.12, 1.22, 1d), (settings.Glass.Transparency, settings.Glass.Look!.Value,
                    settings.Glass.Refraction, settings.Glass.Bezel, settings.Glass.Frost, settings.Glass.Fringe, settings.Glass.Rim, settings.Glass.Tint,
                    settings.Glass.Saturation, settings.Glass.Shadow));
                Assert.Equal(GlassSettings.Default, GlassLooks.Tuning);
            });
            Assert.DoesNotContain(scope.Defaults.Writes, w => w.Key is "glass" or "glassLook");
        }
        finally { GlassLooks.Tuning = GlassSettings.Default; }
    }

    [Fact]
    [Trait("Decision", "OD-0147")] // persistence: every value, the tint colour and every switch store their JSON under glass, read back after a relaunch and are the material's tuning from the start
    public void The_glass_setting_is_stored_read_back_after_a_relaunch_and_mirrored()
    {
        using var scope = AppSettingsScope.Create();
        var picked = GlassSettings.Default.At(20).With("refraction", 1.85).With("shadow", 1.6) with
        {
            TintColour = GlassTintColour.Pink, IncreaseContrast = true, ReduceTransparency = false, ReducedMotion = true, TranslucentSidebar = false,
        };
        try
        {
            scope.OnMain(() =>
            {
                var settings = new AppSettings { Glass = picked };
                settings.FlushPendingWrites();
                Assert.Equal("{\"transparency\":20,\"refraction\":1.85,\"shadow\":1.6,\"tintColour\":\"pink\",\"motion\":\"reduced\",\"increaseContrast\":true,"
                    + "\"reduceTransparency\":false,\"translucentSidebar\":false}", Text(scope.Defaults.Object("glass")));
                Assert.Equal(picked, GlassLooks.Tuning);
            });
            GlassLooks.Tuning = GlassSettings.Default;
            scope.Relaunch();
            scope.OnMain(() =>
            {
                var again = new AppSettings();
                Assert.Equal(picked, again.Glass);
                Assert.Equal(picked, GlassLooks.Tuning);
                again.Glass = GlassSettings.Default;
                again.FlushPendingWrites();
                Assert.Equal("{}", Text(scope.Defaults.Object("glass")));
                Assert.Equal(GlassSettings.Default, GlassLooks.Tuning);
            });
        }
        finally { GlassLooks.Tuning = GlassSettings.Default; }
    }

    [Fact]
    [Trait("Decision", "OD-0147")] // clamping: a value past a range is held before it is kept, stored or drawn; unreadable data, and the earlier glassLook key, are the shipped glass
    public void A_value_past_its_range_is_held_and_unreadable_data_is_the_default()
    {
        using var scope = AppSettingsScope.Create();
        try
        {
            scope.OnMain(() =>
            {
                var settings = new AppSettings { Glass = GlassSettings.Default.At(GlassVariant.Clear) with { Transparency = -3, Tint = 9, Frost = 0, Refraction = -4 } };
                Assert.Equal((0d, 0.5, 20d, 0.4), (settings.Glass.Transparency, settings.Glass.Tint, settings.Glass.Frost, settings.Glass.Refraction));
                Assert.Equal(settings.Glass, GlassLooks.Tuning);
                settings.FlushPendingWrites();
                Assert.Equal("{\"transparency\":0,\"refraction\":0.4,\"frost\":20,\"tint\":0.5}", Text(scope.Defaults.Object("glass")));
                scope.Defaults.Set("glass", Encoding.UTF8.GetBytes("{oops"));
                scope.Defaults.Set("glassLook", Encoding.UTF8.GetBytes("{\"look\":\"clear\",\"refraction\":1.6}"));
            });
            scope.Relaunch();
            scope.OnMain(() =>
            {
                Assert.Equal(GlassSettings.Default, new AppSettings().Glass);
                Assert.Equal(GlassSettings.Default, GlassLooks.Tuning);
            });
        }
        finally { GlassLooks.Tuning = GlassSettings.Default; }
    }
}
