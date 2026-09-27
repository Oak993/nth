using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Window;

[Collection("Headless")]
public sealed class ForEachEnvironmentTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Req", "PR-WIN-017")] // App/Window/WindowManager.swift:113-140,170-172
    public void App_live_resize_environment_is_read_live_by_UI_without_an_App_dependency() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        harness.Main.OnMain(() =>
        {
            var read = typeof(EnvironmentValues).GetMethod("GetWindowIsLiveResizing"); Assert.NotNull(read);
            var leaf = new Border(); var parent = new Border { Child = leaf };
            var resize = new WindowLiveResize(new Avalonia.Controls.Window());
            AppEnvironment.SetWindowLiveResize(parent, resize);
            Assert.False((bool)read.Invoke(null, [leaf])!);
            resize.IsPaneResizing = true; Assert.True((bool)read.Invoke(null, [leaf])!);
            resize.IsPaneResizing = false; Assert.False((bool)read.Invoke(null, [leaf])!);
        });
    });
}
