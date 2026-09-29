using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Window;

// Source: WindowManager.swift:24, :67-70. The Windows frame store uses pixel positions and client sizes in points.
// Input frames are deliberately asymmetric and outside every edge of the headless display.
[Collection("Headless")]
public sealed class WindowManagerFrameMutationTests(HeadlessSession headless)
{
    private const string FrameName = "DroppyCodeMainWindow";

    private static Dictionary<string, object> Frame(long x, long y, double width, double height) => new()
    {
        ["x"] = x, ["y"] = y, ["width"] = width, ["height"] = height,
    };

    [Fact]
    [Trait("Req", "PR-WIN-006")]
    public void A_positive_offscreen_oversized_saved_frame_is_applied_before_presentation_without_clamping() => WindowManagerMutationScope.Run(headless, (harness, manager) =>
    {
        // :67 applies the saved frame unconditionally. No production save helper is used to seed it.
        harness.Defaults.Set(FrameName, Frame(6007, 4509, 4096, 3072));
        harness.Defaults.Writes.Clear();
        var make = typeof(WindowManager).GetMethod("MakeMainWindow", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(make);
        var window = Assert.IsType<ThreadWindow>(make.Invoke(manager, null));
        try
        {
            Assert.Equal(new PixelPoint(6007, 4509), window.Position);
            Assert.Equal(4096d, window.Width);
            Assert.Equal(3072d, window.Height);
            Assert.False(window.IsVisible);
            Assert.DoesNotContain(FrameName, harness.Defaults.Writes); // :70 follows restoration, not before it.
        }
        finally { window.Content = null; window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-WIN-006")]
    public void Moving_the_main_window_persists_all_four_independent_frame_values_under_the_exact_name() => WindowManagerMutationScope.Run(headless, (harness, manager) =>
    {
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        window.Width = 1453;
        window.Height = 973;
        WindowManagerMutationScope.Settle(window);
        harness.Defaults.Writes.Clear();
        window.Position = new PixelPoint(2345, 6789);
        WindowManagerMutationScope.Settle(window);
        Assert.NotEmpty(harness.Defaults.Writes);
        Assert.All(harness.Defaults.Writes, key => Assert.Equal(FrameName, key));
        var saved = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(harness.Defaults.Object(FrameName));
        Assert.Equal(2345L, saved["x"]);
        Assert.Equal(6789L, saved["y"]);
        Assert.Equal(1453d, saved["width"]);
        Assert.Equal(973d, saved["height"]);
        Assert.Equal(4, saved.Count);
    });

    [Fact]
    [Trait("Req", "PR-WIN-006")]
    public void Resizing_without_moving_persists_both_client_dimensions_and_preserves_the_position() => WindowManagerMutationScope.Run(headless, (harness, manager) =>
    {
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        window.Position = new PixelPoint(131, 257);
        WindowManagerMutationScope.Settle(window);
        harness.Defaults.Writes.Clear();
        window.Width = 1583;
        window.Height = 997;
        WindowManagerMutationScope.Settle(window);
        Assert.Contains(FrameName, harness.Defaults.Writes);
        var saved = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(harness.Defaults.Object(FrameName));
        Assert.Equal(131L, saved["x"]);
        Assert.Equal(257L, saved["y"]);
        Assert.Equal(1583d, saved["width"]);
        Assert.Equal(997d, saved["height"]);
    });

    [Fact]
    [Trait("Req", "PR-WIN-006")]
    public void Minimized_and_maximized_placements_never_replace_the_saved_normal_frame() => WindowManagerMutationScope.Run(headless, (harness, manager) =>
    {
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        foreach (var state in new[] { WindowState.Minimized, WindowState.Maximized })
        {
            window.WindowState = state;
            WindowManagerMutationScope.Settle(window);
            harness.Defaults.Writes.Clear();
            window.Position = state == WindowState.Minimized ? new PixelPoint(-32000, -32000) : new PixelPoint(0, 0);
            WindowManagerMutationScope.Settle(window);
            Assert.DoesNotContain(FrameName, harness.Defaults.Writes);
        }
        window.WindowState = WindowState.Normal;
        harness.Defaults.Writes.Clear();
        window.Position = new PixelPoint(413, 719);
        WindowManagerMutationScope.Settle(window);
        Assert.Contains(FrameName, harness.Defaults.Writes);
    });

    [Fact]
    [Trait("Req", "PR-WIN-006")]
    public void An_absent_saved_frame_centers_the_new_main_window_and_then_installs_autosave() => WindowManagerMutationScope.Run(headless, (harness, manager) =>
    {
        Assert.Null(harness.Defaults.Object(FrameName));
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        // ED-225 on the headless 1920x1280 work area: x=(1920-1320)/2, y=(1280-860)/3.
        Assert.Equal(new PixelPoint(300, 140), window.Position);
        harness.Defaults.Writes.Clear();
        window.Position = new PixelPoint(501, 303);
        WindowManagerMutationScope.Settle(window);
        Assert.Contains(FrameName, harness.Defaults.Writes);
    });
}
