using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :714-716,737,757,759 (the ring's popover and its
// UsagePanel); DroppyCode/App/Views/Usage/UsagePanel.swift (B3 52bc792), :9,27 (`@Environment(AppModel.self)`).
//
// SwiftUI hands a popover's content the environment of the view that presents it. In Avalonia a Popup that is not itself
// a visual child hosts its content in a window of its own, outside the main window's tree, so the content reads no
// inherited AppModel: the presenting view has to give it one (ModelEffortButton does, EffortSlider.cs:97). The headless
// presenter never hosts the popup, so these tests carry the popup's content into a window that has no AppModel above it,
// which is what the real popup window is.
[Collection("Headless")]
public sealed class ContextMeterPopoverEnvironmentTests(HeadlessSession headless)
{
    private static ThreadRuntimePart12Harness RuntimeHarness()
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var h = new ThreadRuntimePart12Harness();
        h.Main.OnMain(() => { h.App.Settings.HydraQueueHeads = false; h.App.Settings.HydraAlwaysHeads = false; });
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        return h;
    }

    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 5; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }

    // A pointer press and release at the middle of the control, in the window's coordinates.
    private static void PointerClick(AvaloniaWindow window, Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);
        Settle(window);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-009")] // App/Views/Composer/ComposerView.swift:757; App/Views/Usage/UsagePanel.swift:9,27
    [Trait("Req", "UI-USAGE-003")] // App/Views/Composer/ComposerView.swift:743-757
    public void Clicking_the_usage_ring_opens_a_popover_whose_panel_still_reads_the_model_outside_the_window_tree() => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        var presenter = new ContentRecordingPresenter();
        using var presentation = Presentation.Presenter.Override(presenter);
        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Usage(new ContextUsage { UsedTokens = 310, WindowTokens = 1000 })));
        ContextMeter meter = null!; AvaloniaWindow window = null!; AvaloniaWindow popupWindow = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                meter = new ContextMeter(h.Runtime, ProviderKind.Codex, null);
                var host = new Border { Child = meter, Width = 200, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
                AppEnvironment.SetAppModel(host, h.App);
                window = new AvaloniaWindow { Content = host, Width = 1400, Height = 900 };
                window.Show(); Settle(window);
            });
            for (var round = 0; round < 4; round++) h.Main.OnMain(() => Settle(window));
            h.Main.OnMain(() => PointerClick(window, meter.GetVisualDescendants().OfType<Button>().Single()));
            for (var round = 0; round < 4; round++) h.Main.OnMain(() => Settle(window));

            var popup = Assert.Single(presenter.Shown()).Popup;
            var content = Assert.IsAssignableFrom<Control>(popup.Child);
            // The popup's own window has no AppModel above it: the content must carry one itself (SwiftUI hands the popover the
            // presenter's environment).
            Assert.Same(h.App, content.GetValue(AppEnvironment.AppModelProperty));
            popup.Child = null;
            h.Main.OnMain(() =>
            {
                popupWindow = new AvaloniaWindow { Content = content, Width = 400, Height = 400 };
                popupWindow.Show(); // UsagePanel reads the model when it attaches (UsagePanel.swift:27); this threw before.
                Settle(popupWindow);
                Assert.NotEmpty(content.GetVisualDescendants().OfType<UsagePanel>());
            });
        }
        finally
        {
            h.Main.OnMain(() => { popupWindow?.Close(); window?.Close(); });
        }
    });
}
