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
    [Trait("Rule", "R-VIEW-6")] // App/Window/WindowManager.swift:113-140,170-172; inherited environment bridge only
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

    // CLOSED STOP ForEach transitions #2 (2026-10-04)
    // Kind: partial record verification (was). Swift: WindowManager.swift:118-140. Records: PR-WIN-017.
    // Was: this bridge test toggles a pane flag; it did not exercise native resizing of two windows or a move-only drag.
    // Closed by WindowManagerNativeTests: a real edge drag on the main factory window sets only its own flag and clears on
    // release (Req PR-WIN-017, "...real_edge_drag..."), and a real caption move publishes nothing on either window; and by
    // WindowManagerHighRiskTests' real user32 window. ED-218 and R-VIEW-6 describe this subset; all bridge assertions are retained.
}
