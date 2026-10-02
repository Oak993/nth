using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Xunit;
using static Marrow.App.Tests.Fixtures.HydraSettingsPageRig;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/HydraSettingsPage.swift (B3 52bc792), :208-218 (the cookbook popover),
// :249-276 (the pair editor popover); HydraCookbookPanel.swift :6-7,73; HydraSettingsPage.swift :343,368 (the
// `@Environment(AppModel.self)` the popover content reads).
//
// SwiftUI hands a popover's content the environment of the view that presents it. Here each of these two popups is
// created beside its button and never joins the page's tree, so its content lands in a window of its own with no AppModel
// above it; the button has to give the content the model. The recording presenter never hosts the popup, so each test
// carries the opened content into a window that has no model, which is what the real popup window is.
[Collection("Headless")]
public sealed class HydraSettingsPopoverEnvironmentTests(HeadlessSession headless)
{
    // The content the popup holds once it is open, lifted into a window with no AppModel anywhere above it.
    private static AvaloniaWindow HostOutsideTheWindowTree(Popup popup)
    {
        var content = Assert.IsAssignableFrom<Control>(popup.Child);
        popup.Child = null;
        var window = new AvaloniaWindow { Content = content, Width = 600, Height = 700 };
        window.Show(); // the content reads AppEnvironment.GetAppModel as it attaches; this threw before.
        Sync();
        return window;
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-176")] // HydraSettingsPage.swift:204-220 the cookbook popover; HydraCookbookPanel.swift:6-7
    public void The_cookbook_popover_content_reads_the_model_from_a_window_of_its_own() => Run(headless, _ => new HydraSettingsPage(), scene =>
    {
        Click(Of<Button>(Of<HydraCookbookButton>(scene.View).Single()).Single());
        var popup = LastPresented(scene);
        RaiseOpened(popup);
        Assert.Same(scene.Model, popup.Child!.GetValue(AppEnvironment.AppModelProperty));
        var window = HostOutsideTheWindowTree(popup);
        try { Assert.Single(window.GetVisualDescendants().OfType<HydraCookbookPanel>()); }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-165")] // HydraSettingsPage.swift:249-276 the pair editor popover; :343,368,510-513
    public void The_pair_editor_popover_content_reads_the_model_from_a_window_of_its_own() => PairRow(pair => Run(headless,
        _ => new HydraPairRow(pair), scene =>
        {
            Click(Of<Button>(scene.View).First());
            var popup = LastPresented(scene);
            RaiseOpened(popup);
            Assert.Same(scene.Model, popup.Child!.GetValue(AppEnvironment.AppModelProperty));
            var window = HostOutsideTheWindowTree(popup);
            try { Assert.Single(window.GetVisualDescendants().OfType<HydraPairEditor>()); }
            finally { window.Close(); }
        }, 900, model => model.Settings.AddHydraPair(pair)));

    private static void PairRow(Action<HydraPair> run) => run(new HydraPair(ProviderKind.Claude));
}
