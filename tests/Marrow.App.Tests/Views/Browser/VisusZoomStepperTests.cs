using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.UI.Panes;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: DESIGN 5.1 "zoom chip (shown when not 100 %)" and 5.3 "Zoom ... and the chip"; ED-NEW-web-w8c-9. The menu's Zoom row opens a
// stepper; at 100 % the chip is hidden, so the stepper hangs from the address field, which is always shown.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w8c-9")]
public sealed class VisusZoomStepperTests(HeadlessSession headless)
{
    private static Func<PaneKind, PaneContent?> Contents(VisusRig visus) => kind => kind == PaneKind.Browser
        ? VisusPane.Create(visus.Session, null)
        : new PaneContent(new Border { Child = new TextBox { Text = PaneKinds.Title(kind) } }, PaneKinds.Title(kind));

    private static (PaneWorkspaceRig Rig, VisusPaneBody Body, VisusTab Tab) Open(VisusRig visus)
    {
        var rig = new PaneWorkspaceRig(width: 1400, contents: Contents(visus), popupLayers: true);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var body = Assert.IsType<VisusPaneBody>(rig.Card(PaneKind.Browser).Content.Body);
        visus.Session.Go("https://example.test/");
        var tab = visus.Session.Selected!;
        VisusRig.Live(tab).RaiseNavigationCompleted(tab.Url);
        rig.Layout();
        return (rig, body, tab);
    }

    private static Popup StepperPopup(VisusPaneBody body) =>
        Assert.Single(body.Children.OfType<Popup>(), popup => ReferenceEquals(popup.PlacementTarget, body.Toolbar.Address) && popup.Placement == PlacementMode.BottomEdgeAlignedRight);

    private static List<Button> Buttons(Popup popup) =>
        Assert.IsType<StackPanel>(Assert.IsType<PopoverCard>(popup.Child).Child).Children.OfType<Button>().ToList();

    private static string Percent(Button reset) => Assert.IsType<TextBlock>(Assert.IsType<Border>(reset.Content).Child).Text!;

    [Fact]
    public void At_100_percent_the_stepper_opens_under_the_address_field_because_the_chip_is_hidden() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, _) = Open(visus);
        using var _ = rig;
        Assert.False(body.Toolbar.Address.ZoomChip.IsVisible);
        Assert.True(visus.Session.Perform(VisusCommand.ShowZoom));
        rig.Layout();
        var popup = StepperPopup(body);
        Assert.True(popup.IsOpen);
        var anchor = Assert.IsAssignableFrom<Control>(popup.PlacementTarget);
        Assert.True(anchor.IsEffectivelyVisible);
        Assert.True(anchor.Bounds.Width > 0 && anchor.Bounds.Height > 0);
        var tip = Assert.IsType<Rect>(popup.PlacementRect);
        Assert.Equal(anchor.Bounds.Width, tip.Right);
        Assert.InRange(tip.Width, 1, 56);
    });

    [Fact]
    public void The_three_buttons_are_named_step_the_zoom_and_show_it() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, tab) = Open(visus);
        using var _ = rig;
        visus.Session.Perform(VisusCommand.ShowZoom);
        rig.Layout();
        var buttons = Buttons(StepperPopup(body));
        Assert.Equal(new[] { "Zoom out", "Reset zoom", "Zoom in" }, buttons.Select(button => AutomationProperties.GetName(button)));
        Assert.All(buttons, button => Assert.True(button.Focusable));
        Assert.Equal("100 %", Percent(buttons[1]));
        buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1.1, tab.Zoom);
        Assert.Equal("110 %", Percent(buttons[1]));
        buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0.9, tab.Zoom);
        buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, tab.Zoom);
        Assert.Equal("100 %", Percent(buttons[1]));
    });

    [Fact]
    public void The_percent_follows_a_zoom_chord_while_the_stepper_is_open() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, tab) = Open(visus);
        using var _ = rig;
        visus.Session.Perform(VisusCommand.ShowZoom);
        rig.Layout();
        var reset = Buttons(StepperPopup(body))[1];
        visus.Session.Perform(VisusCommand.ZoomIn);
        visus.Session.Perform(VisusCommand.ZoomIn);
        Assert.Equal(1.25, tab.Zoom);
        Assert.Equal("125 %", Percent(reset));
    });

    [Fact]
    public void The_menu_row_names_the_tabs_zoom() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, tab) = Open(visus);
        using var _ = rig;
        Assert.Contains("Zoom 100 %", new VisusToolbar(visus.Session).MenuEntries().OfType<VisusMenuEntry>().Select(row => row.Title));
        tab.SetZoom(1.5);
        Assert.Contains("Zoom 150 %", body.Toolbar.MenuEntries().OfType<VisusMenuEntry>().Select(row => row.Title));
    });
}
