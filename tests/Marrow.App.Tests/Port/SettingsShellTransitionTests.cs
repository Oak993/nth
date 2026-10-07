using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.App.Port.Settings;
using Marrow.Core.Models;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port;

// Locked Settings Rise SPEC §§2,4,5: exterior shadows belong to the glass/open and common close group.
[Collection("Headless")]
public sealed class SettingsShellTransitionTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Fact]
    [Trait("Decision", "ED-249")]
    public void An_unavailable_native_open_keeps_the_immediate_close_fallback() => WithManager((_, manager) =>
    {
        using var platform = Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());
        using var rig = new SettingsRiseRig();
        manager.SettingsCloak = (_, _) => false;
        manager.ShowSettings(SettingsPage.Appearance);
        Dispatcher.UIThread.RunJobs();
        var window = Settings(manager);
        Assert.True(window.IsVisible);
        Assert.Null(manager.SettingsOpening);
        window.Close();
        Assert.False(window.IsVisible);
        Assert.Null(manager.SettingsClosing);
    });

    [Fact]
    [Trait("Decision", "ED-249")]
    public void The_app_shadow_starts_at_zero_fades_with_glass_and_is_inside_the_close_group() => WithManager((_, manager) =>
    {
        using var platform = Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());
        using var rig = new SettingsRiseRig();
        manager.SettingsCloak = rig.Cloak;
        manager.ShowSettings(SettingsPage.Appearance);
        Dispatcher.UIThread.RunJobs();
        var window = Settings(manager);
        var root = Assert.IsAssignableFrom<Control>(window.Content);
        var shadow = Assert.Single(root.GetVisualDescendants().OfType<Border>(), b => b.Name == "SettingsShellShadow");
        Assert.Equal(2, shadow.BoxShadow.Count);
        Assert.Equal(34, shadow.BoxShadow[0].OffsetY);
        Assert.Equal(90, shadow.BoxShadow[0].Blur);
        Assert.Equal(8, shadow.BoxShadow[1].OffsetY);
        Assert.Equal(24, shadow.BoxShadow[1].Blur);
        rig.Frame(0); rig.Frame(0);
        Assert.False(rig.Cloaked);
        Assert.Equal(0, shadow.Opacity);
        rig.At(.040);
        Assert.Equal(SpecRise.Glass(.040), shadow.Opacity, 5);
        rig.At(.260);
        Assert.Equal(1, shadow.Opacity);
        window.Close(); rig.Frame(.080);
        Assert.InRange(root.Opacity, .1, .9);
        Assert.Equal(1, shadow.Opacity); // close alpha comes only from the common parent, never double-fades
        Assert.Contains(root, shadow.GetVisualAncestors());
        manager.ShowSettings();
        Assert.Equal(1, root.Opacity);
        Assert.Null(root.RenderTransform);
        Assert.Equal(1, shadow.Opacity);
        rig.Frame(.200);
        Assert.True(window.IsVisible);
    });
}
