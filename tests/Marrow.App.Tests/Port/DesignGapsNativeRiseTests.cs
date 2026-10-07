using System.Numerics;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Port;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-designgapsfix-2")]
public sealed class DesignGapsNativeRiseTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Fact]
    public void The_native_root_stays_at_zero_during_hide_then_restores_after_hide_returns() => WithManager((_, manager) =>
    {
        using var platform = Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());
        var graph = new DesignGapsFakePlatform();
        using var attach = SettingsNativePresentation.Attach.Override(_ => SettingsNativePresentation.Bind(graph));
        using var rig = new SettingsRiseRig();
        manager.SettingsCloak = rig.Cloak;
        manager.ShowSettings(SettingsPage.Appearance);
        Dispatcher.UIThread.RunJobs();
        rig.Frame(0); rig.Frame(0); rig.At(.260);
        var window = Settings(manager);
        float? duringHide = null;
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Visual.IsVisibleProperty && !window.IsVisible)
                duringHide = graph._glSurface._window._target.Root.Opacity;
        };
        window.Close(); rig.Frame(.160);
        Assert.Equal(0f, duringHide);
        Assert.False(window.IsVisible);
        Assert.Equal(.9f, graph._glSurface._window._target.Root.Opacity);
        Assert.Equal(Vector3.One, graph._glSurface._window._target.Root.Scale);
        Assert.Equal(0, rig.PendingFor(manager.SettingsClosing));
    });

    [Fact]
    public void A_native_surface_created_on_the_sixth_hidden_frame_gets_its_own_hidden_draw_before_G() => WithManager((_, manager) =>
    {
        using var platform = Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());
        var graph = new DesignGapsFakePlatform();
        var attempts = 0;
        using var required = SettingsNativePresentation.RequiresSurface.Override(_ => true);
        using var attach = SettingsNativePresentation.Attach.Override(_ => ++attempts < 6 ? null : SettingsNativePresentation.Bind(graph));
        using var rig = new SettingsRiseRig();
        manager.SettingsCloak = rig.Cloak;
        manager.ShowSettings(SettingsPage.Appearance);
        Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 6; i++) rig.Frame(0);
        Assert.Equal(6, attempts);
        Assert.True(rig.Cloaked);
        Assert.Equal(0, graph._glSurface._window._blur.Opacity);
        rig.Frame(0);
        Assert.False(rig.Cloaked);
        Assert.Equal(0, graph._glSurface._window._blur.Opacity);
        rig.At(.260);
        Assert.Equal(.8f, graph._glSurface._window._blur.Opacity);
    });

    [Fact]
    public void Native_blur_waits_hidden_then_follows_glass_and_close_uses_only_the_common_root() => WithManager((_, manager) =>
    {
        using var platform = Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());
        var graph = new DesignGapsFakePlatform();
        var native = graph._glSurface._window;
        using var attach = SettingsNativePresentation.Attach.Override(_ => SettingsNativePresentation.Bind(graph));
        using var rig = new SettingsRiseRig();
        manager.SettingsCloak = rig.Cloak;
        manager.ShowSettings(SettingsPage.Appearance);
        Dispatcher.UIThread.RunJobs();
        rig.Frame(0);
        Assert.True(rig.Cloaked);
        Assert.Equal(0, native._blur.Opacity);
        rig.Frame(0);
        Assert.False(rig.Cloaked);
        Assert.Equal(0, native._blur.Opacity);
        var root = Assert.IsAssignableFrom<Control>(Settings(manager).Content);
        Assert.Equal(1, root.Opacity); // the captions are whole from G; no window/content fade on open
        Assert.Equal(.9f, native._target.Root.Opacity);
        for (var i = 1; i <= 5; i++)
        {
            rig.At(i / 60.0);
            Assert.Equal(.8 * SpecRise.Glass(i / 60.0), native._blur.Opacity, 5);
            Assert.Equal(.9f, native._target.Root.Opacity);
        }
        rig.At(.260);
        Assert.Equal(.8f, native._blur.Opacity);
        var window = Settings(manager);
        window.Close();
        rig.Frame(.080);
        Assert.True(window.IsVisible);
        Assert.Equal(1, root.Opacity); // the native common root owns the fade, no second framebuffer alpha
        Assert.Null(root.RenderTransform);
        Assert.InRange(native._target.Root.Opacity, .1f, .8f);
        Assert.InRange(native._target.Root.Scale.X, .985f, 1);
        Assert.Equal(native._target.Root.Scale.X, native._target.Root.Scale.Y);
        Assert.Equal(.8f, native._blur.Opacity); // blur retains its own alpha while its parent fades
        manager.ShowSettings();
        Assert.Equal(.9f, native._target.Root.Opacity);
        Assert.Equal(Vector3.One, native._target.Root.Scale);
        Assert.Equal(new Vector3(5, 6, 0), native._target.Root.CenterPoint);
        Assert.Equal(0, rig.PendingFor(manager.SettingsClosing));
        rig.Frame(.200);
        Assert.True(window.IsVisible);
    });
}
