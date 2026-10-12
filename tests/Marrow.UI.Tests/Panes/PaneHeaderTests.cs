using Avalonia;
using Avalonia.Automation;
using Marrow.Core.Models;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Panes;

namespace Marrow.UI.Tests.Panes;

// nth web surfaces, unit W3a: the workspace pane header (work/design/web-surfaces/DESIGN.md 3.2 "Header overflow", 3.4,
// 3.12, 13.3 "Header overflow: at each pane's minimum width, all header actions are reachable"). Measured on a real
// headless window, never guessed: at every pane's minimum width the title keeps 80 DIP, nothing overlaps it, Close is in
// the row, and each action is either in the row or in the one More menu.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w3a-1")]
public sealed class PaneHeaderTests(HeadlessSession headless)
{
    private static readonly PaneKind[] Panes =
    [
        PaneKind.Browser, PaneKind.Artifacts, PaneKind.App, PaneKind.SideChat, PaneKind.Terminal,
        PaneKind.BackgroundTasks, PaneKind.Changes, PaneKind.Transcript,
    ];

    private static void AssertFits(PaneHeaderRig rig, string context)
    {
        var header = rig.Header;
        var width = header.Bounds.Width;
        var title = rig.TitleBox;
        Assert.True(title.Width >= PaneHeaderParts.TitleMinimum - 0.5, $"{context}: the title got {title.Width}");
        Assert.True(header.CloseButton.IsVisible, $"{context}: Close left the row");
        foreach (var item in rig.TrailingShown())
        {
            var box = rig.BoxOf(item);
            Assert.True(box.X >= title.Right - 0.5, $"{context}: {AutomationProperties.GetName(item)} overlaps the title ({box.X} < {title.Right})");
            Assert.True(box.Right <= width + 0.5, $"{context}: {AutomationProperties.GetName(item)} runs past the header ({box.Right} > {width})");
        }
        var shown = rig.TrailingShown().ToList();
        for (var i = 1; i < shown.Count; i++)
            Assert.True(rig.BoxOf(shown[i]).X >= rig.BoxOf(shown[i - 1]).Right + PaneTokens.HeaderGap - 0.5, $"{context}: trailing items overlap");
        var menu = header.MenuItems;
        foreach (var (action, view) in rig.Actions.Zip(header.ActionViews))
            Assert.True(view.IsVisible ^ menu.Contains(action.Name), $"{context}: {action.Name} is in neither the row nor More, or in both");
        if (header.CanExpand) Assert.True(header.ExpandButton.IsVisible ^ menu.Contains(header.IsExpanded ? "Restore" : "Expand"), $"{context}: Expand is unreachable");
        Assert.Equal(menu.Count > 0, header.MoreButton.IsVisible);
        Assert.Equal(menu, rig.MenuRows());
    }

    [Fact]
    public void At_every_pane_minimum_the_title_keeps_80_nothing_overlaps_and_every_action_is_reachable() => headless.OnUiThread(() =>
    {
        foreach (var kind in Panes)
        {
            // The header spans the card inside its 1 DIP rim.
            var width = PaneKinds.Size(kind).MinWidth - 2;
            using var rig = new PaneHeaderRig(kind, width);
            AssertFits(rig, $"{kind} at {width}");
            Assert.True(rig.Header.MoreButton.IsVisible, $"{kind}: six actions cannot all fit at {width}");
            rig.Header.IsExpanded = true;
            rig.Header.ShowsWorking = true;
            rig.Layout();
            AssertFits(rig, $"{kind} expanded and working at {width}");
            Assert.False(rig.Header.WorkingChip.IsVisible); // the chip is the first to go
        }
    });

    [Fact]
    public void Actions_leave_the_row_from_the_last_one_back_and_More_lists_them_in_their_order() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Artifacts, 900);
        AssertFits(rig, "900");
        Assert.All(rig.Header.ActionViews, view => Assert.True(view.IsVisible));
        Assert.False(rig.Header.MoreButton.IsVisible);
        var names = rig.Actions.Select(action => action.Name).ToList();
        var previous = names.Count;
        for (var width = 900; width >= 318; width -= 7)
        {
            rig.SetWidth(width);
            AssertFits(rig, $"{width}");
            var visible = rig.Header.Fit.VisibleActions;
            Assert.True(visible <= previous, $"{width}: an action came back as the header narrowed");
            Assert.Equal(names.Skip(visible).Concat(rig.Header.Fit.ShowsExpand ? Array.Empty<string>() : new[] { "Expand" }), rig.Header.MenuItems);
            previous = visible;
        }
        Assert.True(previous < names.Count);
    });

    [Fact]
    public void A_More_menu_row_runs_its_action_and_Expand_runs_from_the_menu_when_it_left_the_row() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Changes, 230);
        var expanded = 0;
        rig.Header.ExpandRequested += () => expanded++;
        Assert.Contains("Export", rig.Header.MenuItems);
        rig.RunMenuItem("Export");
        Assert.Equal(new[] { "Export" }, rig.Invoked);
        if (!rig.Header.Fit.ShowsExpand)
        {
            rig.RunMenuItem("Expand");
            Assert.Equal(1, expanded);
        }
        var closed = 0;
        rig.Header.CloseRequested += () => closed++;
        PaneHeaderRig.Click(rig.Header.CloseButton);
        Assert.Equal(1, closed);
    });

    [Fact]
    public void While_held_the_set_of_buttons_stays_and_nothing_overlaps_the_title_then_release_refits() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Artifacts, 900);
        var before = rig.Header.Fit;
        Assert.Equal(6, before.VisibleActions);
        rig.Header.IsHeld = true;
        foreach (var width in new[] { 700.0, 520, 400, 330 })
        {
            rig.SetWidth(width);
            Assert.Equal(before, rig.Header.Fit); // no flicker of buttons during a spring or a drag
            var title = rig.TitleBox;
            Assert.True(title.Width >= PaneHeaderParts.TitleMinimum - 0.5, $"held at {width}: the title got {title.Width}");
            foreach (var item in rig.TrailingShown())
            {
                // Items that do not fit for the moment are clipped at the title's minimum, never drawn over it.
                var box = rig.BoxOf(item);
                var host = item.Parent as Avalonia.Controls.Control;
                var clip = host is null ? box : rig.BoxOf(host);
                Assert.True(clip.X >= title.Right - 0.5, $"held at {width}: the trailing items reach over the title");
            }
        }
        rig.Header.IsHeld = false;
        rig.Layout();
        Assert.True(rig.Header.Fit.VisibleActions < 6);
        AssertFits(rig, "released at 330");
    });

    [Fact]
    public void Expanded_the_button_is_Restore_and_the_working_chip_shows_when_there_is_room() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Browser, 1100);
        Assert.Equal("Expand", AutomationProperties.GetName(rig.Header.ExpandButton));
        rig.Header.IsExpanded = true;
        rig.Header.ShowsWorking = true;
        rig.Layout();
        Assert.Equal("Restore", AutomationProperties.GetName(rig.Header.ExpandButton));
        Assert.True(rig.Header.WorkingChip.IsVisible);
        AssertFits(rig, "expanded at 1100");
        var clicked = 0;
        rig.Header.WorkingChipClicked += () => clicked++;
        PaneHeaderRig.Click(rig.Header.WorkingChip);
        Assert.Equal(1, clicked);
        rig.Header.ShowsWorking = false;
        rig.Layout();
        Assert.False(rig.Header.WorkingChip.IsVisible);
    });

    [Fact]
    public void A_cell_with_tabs_shows_its_tabs_where_the_title_was_and_choosing_one_raises_TabChosen() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Changes, 520, withActions: false);
        rig.Header.SetTabs(new[] { new PaneHeaderTab(PaneKind.Changes, "Changes", true), new PaneHeaderTab(PaneKind.Terminal, "Terminal", false) });
        rig.Layout();
        Assert.True(rig.Header.TabStrip.IsVisible);
        Assert.False(rig.Header.TitleBlock.IsVisible);
        var tabs = rig.Header.TabStrip.Children.OfType<Avalonia.Controls.Button>().ToList();
        Assert.Equal(new[] { "Changes", "Terminal" }, tabs.Select(tab => AutomationProperties.GetName(tab)));
        Assert.All(tabs, tab => Assert.Equal(Avalonia.Automation.Peers.AutomationControlType.TabItem, tab.GetValue(AutomationProperties.ControlTypeOverrideProperty)));
        PaneKind? chosen = null;
        rig.Header.TabChosen += kind => chosen = kind;
        PaneHeaderRig.Click(tabs[1]);
        Assert.Equal(PaneKind.Terminal, chosen);
        AssertFits(rig, "tabs at 520");
        rig.Header.SetTabs([]);
        rig.Layout();
        Assert.False(rig.Header.TabStrip.IsVisible);
        Assert.True(rig.Header.TitleBlock.IsVisible);
    });
}
