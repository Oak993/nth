using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Settings;
using Marrow.UI.Port;

namespace Marrow.App.Tests;

// ED-NEW-glasslook-2 (OD-0107; owner 2026-10-11: glass settings in Customization, persisted): the glassLook key, stored as GlassSettings writes it,
// read back after a relaunch, held to its ranges, and mirrored into the material's tuning (GlassLooks.Tuning) as the theme is into ThemeManager.
[Collection("AppSettings")]
public sealed class AppSettingsGlassTests
{
    private static string Text(object? stored) => Encoding.UTF8.GetString((byte[])stored!);

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // nothing stored is every surface's own look, mirrored, and a launch writes nothing
    public void Nothing_stored_is_the_default_and_a_launch_writes_nothing()
    {
        using var scope = AppSettingsScope.Create();
        try
        {
            scope.OnMain(() =>
            {
                var settings = new AppSettings();
                Assert.Equal(GlassSettings.Default, settings.Glass);
                Assert.Equal(GlassSettings.Default, GlassLooks.Tuning);
            });
            Assert.DoesNotContain(scope.Defaults.Writes, w => w.Key == "glassLook");
        }
        finally { GlassLooks.Tuning = GlassSettings.Default; }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // persistence: the setting stores its JSON under glassLook, reads back after a relaunch and is the material's tuning from the start
    public void The_glass_setting_is_stored_read_back_after_a_relaunch_and_mirrored()
    {
        using var scope = AppSettingsScope.Create();
        var picked = new GlassSettings { Look = GlassVariant.Clear, Refraction = 1.6, Clarity = 0.7 }.With("tint", 0.7).With("shadow", 1.4);
        try
        {
            scope.OnMain(() =>
            {
                var settings = new AppSettings { Glass = picked };
                settings.FlushPendingWrites();
                Assert.Equal(Encoding.UTF8.GetString(picked.ToJson()), Text(scope.Defaults.Object("glassLook")));
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
                Assert.Equal("{}", Text(scope.Defaults.Object("glassLook")));
                Assert.Equal(GlassSettings.Default, GlassLooks.Tuning);
            });
        }
        finally { GlassLooks.Tuning = GlassSettings.Default; }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // clamping: a value past a range is held before it is kept, stored or drawn; unreadable data is the default
    public void A_value_past_its_range_is_held_and_unreadable_data_is_the_default()
    {
        using var scope = AppSettingsScope.Create();
        try
        {
            scope.OnMain(() =>
            {
                var settings = new AppSettings { Glass = new GlassSettings { Tint = 9, Frost = 0, Refraction = -4 } };
                Assert.Equal((1.0, 0.5, 0.0), (settings.Glass.Tint, settings.Glass.Frost, settings.Glass.Refraction));
                Assert.Equal(settings.Glass, GlassLooks.Tuning);
                settings.FlushPendingWrites();
                Assert.Equal("{\"refraction\":0,\"frost\":0.5}", Text(scope.Defaults.Object("glassLook")));
                scope.Defaults.Set("glassLook", Encoding.UTF8.GetBytes("{oops"));
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
