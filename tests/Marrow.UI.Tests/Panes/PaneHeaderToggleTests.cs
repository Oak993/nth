using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.UI.Panes;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Panes;

// nth web surfaces, unit W6: a header action can be a toggle (Side chat's Can act): a chip that is lit while on, whose accessible name says
// on or off, and that fits Side chat's narrowest header together with Clear (work/design/web-surfaces/DESIGN.md 3.9, 13.3 "Header overflow").
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w6-1")]
public sealed class PaneHeaderToggleTests(HeadlessSession headless)
{
    private static PaneHeaderRig Rig(double width, bool pressed, List<string> invoked)
    {
        var rig = new PaneHeaderRig(PaneKind.SideChat, width, withActions: false);
        rig.Header.SetActions([
            new PaneHeaderAction("Can act", () => invoked.Add("Can act"), Text: "Can act", Pressed: pressed),
            new PaneHeaderAction("Clear", () => invoked.Add("Clear"), Text: "Clear"),
        ]);
        rig.Layout();
        return rig;
    }

    [Fact]
    public void A_toggle_names_its_state_a_plain_action_does_not() => headless.OnUiThread(() =>
    {
        var invoked = new List<string>();
        using var off = Rig(278, false, invoked);
        Assert.Equal(["Can act, off", "Clear"], off.Header.ActionViews.Select(AutomationProperties.GetName));
        using var on = Rig(278, true, invoked);
        Assert.Equal(["Can act, on", "Clear"], on.Header.ActionViews.Select(AutomationProperties.GetName));
    });

    [Fact]
    public void A_toggle_that_is_on_is_lit_and_one_that_is_off_is_not() => headless.OnUiThread(() =>
    {
        var invoked = new List<string>();
        using var off = Rig(400, false, invoked);
        using var on = Rig(400, true, invoked);
        static Avalonia.Media.IBrush? Fill(PaneHeaderRig rig) => ((Border)rig.Header.ActionViews[0].Content!).Background;
        Assert.Same(Avalonia.Media.Brushes.Transparent, Fill(off));
        Assert.NotNull(Fill(on));
        Assert.NotSame(Avalonia.Media.Brushes.Transparent, Fill(on));
    });

    [Fact]
    public void A_click_invokes_the_action_and_the_chip_row_fits_the_narrowest_side_chat_header() => headless.OnUiThread(() =>
    {
        var invoked = new List<string>();
        var width = PaneKinds.Size(PaneKind.SideChat).MinWidth - 2;
        using var rig = Rig(width, true, invoked);
        var header = rig.Header;
        Assert.True(rig.TitleBox.Width >= PaneHeaderParts.TitleMinimum - 0.5, $"the title got {rig.TitleBox.Width}");
        var menu = header.MenuItems;
        foreach (var (action, view) in new[] { "Can act", "Clear" }.Zip(header.ActionViews))
            Assert.True(view.IsVisible ^ menu.Contains(action), $"{action} is in neither the row nor More, or in both");
        PaneHeaderRig.Click(header.ActionViews[0]);
        Assert.Equal(["Can act"], invoked);
    });
}
