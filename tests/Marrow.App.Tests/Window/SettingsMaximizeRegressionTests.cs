using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using System.Reflection;
using System.Numerics;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Window;

[Collection("Headless")]
public sealed class SettingsMaximizeRegressionTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Theory]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(150)]
    [Trait("Decision", "ED-063")]
    public void Settings_maximize_and_restore_fill_the_viewport_and_keep_zoom_inside_it(int percent) => WithManager((_, manager) =>
    {
        manager.ShowSettings(SettingsPage.Shortcuts);
        var window = Settings(manager);
        Drain(window);
        Assert.True(window.CanMaximize); // T45 owner override: every caption cell activates, Settings included.
        var shell = Assert.IsType<SettingsShell>(window.Content);
        var geometry = new SettingsClipGeometry();
        using var clip = new SettingsAcrylicClip(window, geometry, new object(), 0, new Size(780, 580));
        shell.AcrylicClip = clip;
        var view = View(manager);
        manager.Model.Settings.AppZoom = percent;
        var handled = false;
        manager.HandleNativeWindowMessage(window, 0, 0xa1, 9, 0, ref handled);
        manager.HandleNativeWindowMessage(window, 0, 0xa2, 9, 0, ref handled);
        Drain(window);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        // Headless does not have a monitor work area; deliver the backend's maximized client size explicitly.
        var resized = Assert.IsType<Action<Size, WindowResizeReason>>(typeof(ITopLevelImpl)
            .GetProperty("Resized", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(window.PlatformImpl));
        resized(new Size(1600, 900), WindowResizeReason.Unspecified);
        Drain(window);
        Assert.Equal(new Size(1600, 900), shell.Client.Bounds.Size);
        Assert.Equal(1600, shell.Shadow.Width);
        Assert.Equal(900, shell.Shadow.Height);
        var end = view.TranslatePoint(new Point(view.Bounds.Width, view.Bounds.Height), window)!.Value;
        Assert.InRange(end.X, 1599.99, 1600.01);
        Assert.InRange(end.Y, 899.99, 900.01);
        Assert.Equal(0, shell.Inset);
        Assert.Equal(Vector2.Zero, geometry.Offset);
        Assert.Equal(new Vector2(1600, 900), geometry.Size);
        Assert.Equal(Vector2.Zero, geometry.Radius);
        manager.HandleNativeWindowMessage(window, 0, 0xa1, 9, 0, ref handled);
        manager.HandleNativeWindowMessage(window, 0, 0xa2, 9, 0, ref handled);
        Drain(window);
        Assert.Equal(WindowState.Normal, window.WindowState);
        resized(new Size(780, 580), WindowResizeReason.Unspecified);
        Drain(window);
        Assert.Equal(new Size(780, 580), shell.Client.Bounds.Size);
        end = view.TranslatePoint(new Point(view.Bounds.Width, view.Bounds.Height), window)!.Value;
        Assert.InRange(end.X, 779.99, 780.01);
        Assert.InRange(end.Y, 579.99, 580.01);
        Assert.False(shell.Shadow.IsVisible); // Headless has no exterior shadow surface.
        Assert.Equal(780, window.Width);
        Assert.Equal(580, window.Height);
        clip.Update(1.5);
        Assert.Equal(new Vector2(1170, 870), geometry.Size);
        Assert.Equal(new Vector2(12), geometry.Radius);
    });
}
