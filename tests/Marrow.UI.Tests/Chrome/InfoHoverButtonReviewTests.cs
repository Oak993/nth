using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

[Collection("Headless")]
public sealed class InfoHoverButtonReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-SETTINGS-030")] // UI/Chrome/InfoHoverButton.swift:21,24,35
    public void Review_actual_button_is_not_focusable_and_popup_has_bottom_placement() => headless.OnUiThread(() =>
    {
        var view = new InfoHoverButton("Help", () => new TextBlock { Text = "Body" });
        var window = new Window { Width = 600, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var button = Assert.Single(view.GetVisualDescendants().OfType<Button>());
        Assert.False(button.Focusable);
        Assert.False(KeyboardNavigation.GetIsTabStop(button));
        Assert.Equal(AutomationControlType.Button, ControlAutomationPeer.CreatePeerForElement(button).GetAutomationControlType());
        var popup = Assert.Single(view.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.Popup>());
        Assert.Equal(PlacementMode.Bottom, popup.Placement);
        window.Close();
    });
}
