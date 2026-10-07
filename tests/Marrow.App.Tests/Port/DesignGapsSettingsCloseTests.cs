// RR-37, locked settings-rise/SPEC.md sections 5 and 7; no native calls and no wall clock.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.Core.Port;
using Marrow.App.Port.Settings;
using Marrow.App.Window;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.UI.Port;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Port;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-designgaps-5")]
public sealed class DesignGapsSettingsCloseTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Fact]
    public void Close_fades_one_layer_and_scales_about_the_centre_for_exactly_160_ms() => WithManager((_, manager) =>
    {
        using var platform = Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());
        using var rig = new SettingsRiseRig();
        manager.SettingsCloak = rig.Cloak;
        manager.ShowSettings(SettingsPage.Appearance);
        Dispatcher.UIThread.RunJobs();
        rig.Frame(0); rig.Frame(0); rig.At(.260);
        var window = Settings(manager);
        var content = Assert.IsAssignableFrom<Control>(window.Content);
        var card = View(manager).RisePage!.GetVisualDescendants().OfType<ChromeCard>().First();
        window.Close();
        Assert.True(window.IsVisible);
        Assert.Equal(RelativePoint.Center, content.RenderTransformOrigin);
        for (var index = 1; index <= 9; index++)
        {
            rig.Frame(1.0 / 60);
            // The rig advances TimeSpan ticks (100 ns), rounding each 1/60 s input before accumulation.
            var t = index * Math.Round(TimeSpan.TicksPerSecond / 60.0) / TimeSpan.TicksPerSecond;
            var opacity = 1 - CloseEase(t / .160);
            Assert.Equal(opacity, content.Opacity, 5);
            var scale = Assert.IsType<ScaleTransform>(content.RenderTransform);
            Assert.Equal(1 - .015 * (1 - opacity), scale.ScaleX, 5);
            Assert.Equal(scale.ScaleX, scale.ScaleY);
            Assert.Equal(opacity, card.Opacity, 5); // under the group: the card's effective fade is squared
            Assert.True(window.IsVisible);
        }
        rig.Frame(.010);
        Assert.False(window.IsVisible);
        Assert.Equal(1, content.Opacity);
        Assert.Null(content.RenderTransform);
        Assert.Equal(1, card.Opacity);
        Assert.Equal(0, rig.PendingFor(manager.SettingsClosing));
    });

    [Fact]
    public void A_new_show_cancels_close_and_restores_every_property() => WithManager((_, manager) =>
    {
        using var platform = Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());
        using var rig = new SettingsRiseRig();
        manager.SettingsCloak = rig.Cloak;
        manager.ShowSettings(SettingsPage.Appearance);
        Dispatcher.UIThread.RunJobs();
        rig.Frame(0); rig.Frame(0); rig.At(.260);
        var window = Settings(manager);
        var content = Assert.IsAssignableFrom<Control>(window.Content);
        window.Close(); rig.Frame(.080);
        Assert.InRange(content.Opacity, 0.01, 0.99);
        manager.ShowSettings();
        Assert.True(window.IsVisible);
        Assert.Equal(1, content.Opacity);
        Assert.Null(content.RenderTransform);
        Assert.Equal(0, rig.PendingFor(manager.SettingsClosing));
        rig.Frame(.200);
        Assert.True(window.IsVisible);
    });

    [Fact]
    public void Reduce_motion_hides_at_once_with_no_scale_or_frame_request() => WithManager((_, manager) =>
    {
        using var platform = Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        using var rig = new SettingsRiseRig();
        manager.SettingsCloak = rig.Cloak;
        manager.ShowSettings(SettingsPage.Appearance);
        Dispatcher.UIThread.RunJobs();
        rig.Frame(0); rig.Frame(0); rig.At(.260);
        var window = Settings(manager);
        EnvironmentValues.SetAccessibilityReduceMotion(window, true);
        window.Close();
        Assert.False(window.IsVisible);
        Assert.Equal(0, rig.PendingFor(manager.SettingsClosing));
    });

    private static double CloseEase(double x)
    {
        double low = 0, high = 1;
        for (var i = 0; i < 80; i++)
        {
            var u = (low + high) / 2;
            var at = 3 * .4 * u * (1 - u) * (1 - u) + 3 * u * u * (1 - u) + u * u * u;
            if (at < x) low = u; else high = u;
        }
        var v = (low + high) / 2;
        return 3 * v * v * (1 - v) + v * v * v;
    }
}
