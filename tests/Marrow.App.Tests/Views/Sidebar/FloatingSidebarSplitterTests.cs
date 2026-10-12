using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Xunit;

namespace Marrow.App.Tests.Views.Sidebar;

// web W1 (DESIGN 4.3, ED-NEW-web-w1-6): the floating sidebar (Floating and Panel only modes share it) gets the shared
// Splitter on its free edges, right and bottom (8 DIP), beside the 16 DIP bottom-right corner, with the source's numbers:
// at least 240 x 320, at most the area past the resting origin less the margin, 300 x 560 by default, saved globally. A
// double-click, a key, Home, End or a screen reader's value moves the size under PanelSlide from the size drawn now.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w1-6")]
public sealed class FloatingSidebarSplitterTests(HeadlessSession headless)
{
    [Fact]
    public void The_right_and_bottom_edges_are_8_dip_grips_along_the_whole_side_with_the_corner_on_top() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));
        Assert.Equal(new Rect(300 - 8, 0, 8, 560), RootViewRig.In(rig.RightGrip, rig.Surface));
        Assert.Equal(new Rect(0, 560 - 8, 300, 8), RootViewRig.In(rig.BottomGrip, rig.Surface));
        Assert.Equal("Resize sidebar width", rig.RightGrip.Label);
        Assert.Equal("Resize sidebar height", rig.BottomGrip.Label);
        Assert.Equal("SizeWestEast", rig.RightGrip.Handle.HandleCursor!.ToString());
        Assert.Equal("SizeNorthSouth", rig.BottomGrip.Handle.HandleCursor!.ToString());
        Assert.Equal(3, rig.All<Splitter>().Length);
        var children = Assert.IsAssignableFrom<Avalonia.Controls.Panel>(rig.Grip.Parent).Children;
        Assert.True(children.IndexOf(rig.Grip) > children.IndexOf(rig.RightGrip)); // the corner wins where they meet
        Assert.True(children.IndexOf(rig.Grip) > children.IndexOf(rig.BottomGrip));
    });

    [Fact]
    public void Dragging_an_edge_changes_only_its_own_dimension_and_writes_the_size_on_every_tick() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));
        rig.MousePress(rig.RightGrip);
        rig.MouseMoveBy(40, 25);
        Assert.Equal(new Size(340, 560), rig.Model.Settings.SidebarPanelSize);
        Assert.Equal(new Size(340, 560), rig.Frame.Size);
        rig.MouseReleaseBy(40, 25);

        rig.Drag(rig.BottomGrip, 30, 30);
        Assert.Equal(new Size(340, 590), rig.Model.Settings.SidebarPanelSize);
        rig.Drag(rig.BottomGrip, 0, -600); // the floor
        Assert.Equal(new Size(340, 320), rig.Model.Settings.SidebarPanelSize);
        rig.Drag(rig.RightGrip, 900, 0); // the area's cap: 800 - 12 - 12
        Assert.Equal(new Size(776, 320), rig.Model.Settings.SidebarPanelSize);
        Assert.Equal(new Point(12, 72), rig.Frame.TopLeft); // resizing never moves the panel
    });

    [Fact]
    public void A_double_click_on_a_grip_eases_back_to_300_by_560_and_a_press_lands_it_at_once() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700), size: new Size(500, 400));
        Assert.Equal(new Size(500, 400), rig.Frame.Size);
        launch.Harness.Main.OnMain(() =>
        {
            rig.Grip.Target.Reset();
            Dispatcher.UIThread.RunJobs();
        });
        rig.Layout();
        Assert.Equal(new Size(500, 400), rig.Frame.Size); // it has not jumped
        launch.Harness.Main.Advance(TimeSpan.FromSeconds(0.08));
        rig.Frames.Advance(TimeSpan.Zero);
        rig.Layout();
        var midway = rig.Frame.Size;
        Assert.InRange(midway.Width, 300.5, 499.5);
        Assert.InRange(midway.Height, 400.5, 559.5);
        Assert.Equal(new Size(500, 400), rig.Model.Settings.SidebarPanelSize); // written when it lands

        rig.MousePress(rig.Grip); // pointer-down settling
        Assert.Equal(new Size(300, 560), rig.Model.Settings.SidebarPanelSize);
        rig.Layout();
        Assert.Equal(new Size(300, 560), rig.Frame.Size);
        rig.MouseReleaseBy(0, 0);
    });

    [Fact]
    public void Keys_and_automation_resize_within_the_floor_and_the_areas_cap() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));
        void Key(Splitter grip, SplitterKey key, bool shift = false)
        {
            launch.Harness.Main.OnMain(() => grip.Execute(SplitterMath.Command(key, shift, false, grip.Orientation)));
            rig.Settle();
        }

        Key(rig.RightGrip, SplitterKey.Right);
        Assert.Equal(new Size(308, 560), rig.Model.Settings.SidebarPanelSize);
        Key(rig.BottomGrip, SplitterKey.Up, shift: true);
        Assert.Equal(new Size(308, 512), rig.Model.Settings.SidebarPanelSize);
        Key(rig.Grip, SplitterKey.Left);
        Key(rig.Grip, SplitterKey.Down);
        Assert.Equal(new Size(300, 520), rig.Model.Settings.SidebarPanelSize);
        Key(rig.RightGrip, SplitterKey.End);
        Assert.Equal(new Size(776, 520), rig.Model.Settings.SidebarPanelSize);
        Key(rig.BottomGrip, SplitterKey.Home);
        Assert.Equal(new Size(776, 320), rig.Model.Settings.SidebarPanelSize);
        Key(rig.Grip, SplitterKey.Enter);
        Assert.Equal(new Size(300, 560), rig.Model.Settings.SidebarPanelSize);
        Assert.Equal(new Size(300, 560), rig.Frame.Size);

        var width = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(rig.RightGrip));
        Assert.Equal((240.0, 776.0, 300.0), (width.Minimum, width.Maximum, width.Value));
        launch.Harness.Main.OnMain(() => width.SetValue(420));
        rig.Settle();
        Assert.Equal(new Size(420, 560), rig.Model.Settings.SidebarPanelSize);
        var height = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(rig.BottomGrip));
        Assert.Equal((320.0, 616.0, 560.0), (height.Minimum, height.Maximum, height.Value));
        Assert.True(Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(rig.Grip)).IsReadOnly);
    });
}
