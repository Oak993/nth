using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Xunit;

namespace Marrow.App.Tests.Views.Diff;

// web W1 (DESIGN 4.3, ED-NEW-web-w1-7): the Alt diff popover (DiffInspector.swift :520-692, a fixed 640 x 540 at :522-523)
// takes the shared Splitter on its free sides: both side edges, the far edge and the two far corners. Its size is at least
// 420 x 320 and at most the work area less 48, 640 x 540 by default, remembered globally as diffPopoverSize. It is centred
// on its anchor, so a side edge's travel counts twice. The popup seam records (headless cannot open a real popup), so the
// grips are driven through their targets and keys; the shared pointer mechanics are covered in Marrow.UI.Tests.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w1-7")]
public sealed class DiffPopoverResizeTests(HeadlessSession headless)
{
    private static Splitter[] Grips(Popup popup) =>
        LogicalQuery.SelfAndDescendants<Splitter>(Assert.IsAssignableFrom<Control>(popup.Child)).ToArray();

    private static Splitter Side(Splitter[] grips, HorizontalAlignment side) =>
        Assert.Single(grips, grip => grip.Orientation == SplitterOrientation.Vertical && grip.HorizontalAlignment == side);

    private static void Key(Splitter grip, SplitterKey key, bool shift = false) =>
        Assert.True(grip.Execute(SplitterMath.Command(key, shift, false, grip.Orientation)));

    // A roomy window (the largest size is its screen's work area, or without one its client area, less 48) with the app
    // model in its environment, and the popover open above the changes tab.
    private static void OpenRoomy(DiffPopoverRig rig)
    {
        rig.Window.Width = 1600;
        rig.Window.Height = 1200;
        Dispatcher.UIThread.RunJobs();
        AppEnvironment.SetAppModel(rig.Window, rig.App);
        rig.OpenOnFirst();
    }

    [Fact]
    public void The_size_is_held_between_420_by_320_and_the_work_area_less_48() => headless.OnUiThread(() => DiffPopoverRig.Run(rig =>
    {
        Assert.Equal(new Size(420, 320), DiffPopoverCoordinator.MinimumSize);
        Assert.Equal(new Size(double.PositiveInfinity, double.PositiveInfinity), DiffPopoverCoordinator.Largest(new Border()));
        Assert.Equal(new Size(420, 320), DiffPopoverCoordinator.Fit(new Size(100, 50), null));
        Assert.Equal(new Size(5000, 4000), DiffPopoverCoordinator.Fit(new Size(5000, 4000), null)); // nowhere to measure

        var largest = DiffPopoverCoordinator.Largest(rig.Tab);
        var room = rig.Window.Screens?.ScreenFromVisual(rig.Tab) is { Scaling: > 0 } screen
            ? new Size(screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling)
            : rig.Window.ClientSize;
        Assert.Equal(new Size(Math.Max(420, room.Width - 48), Math.Max(320, room.Height - 48)), largest);
        Assert.Equal(largest, DiffPopoverCoordinator.Fit(new Size(100_000, 100_000), rig.Tab));
    }));

    [Fact]
    public void It_opens_at_the_remembered_size_with_grips_on_both_sides_the_far_edge_and_the_far_corners() => headless.OnUiThread(() => DiffPopoverRig.Run(rig =>
    {
        rig.Window.Width = 1600;
        rig.Window.Height = 1200;
        Dispatcher.UIThread.RunJobs();
        AppEnvironment.SetAppModel(rig.Window, rig.App);
        rig.App.Settings.DiffPopoverSize = new Size(560, 400);
        rig.OpenOnFirst();

        Assert.Equal(new Size(560, 400), rig.Coordinator.ShownSize);
        Assert.Equal((560.0, 400.0), (rig.LastPopup.Width, rig.LastPopup.Height));
        var inspector = Assert.Single(LogicalQuery.SelfAndDescendants<DiffInspector>(Assert.IsAssignableFrom<Control>(rig.LastPopup.Child)));
        Assert.Equal((560.0, 400.0), (inspector.Width, inspector.Height));

        var grips = Grips(rig.LastPopup);
        Assert.Equal(5, grips.Length);
        Assert.Equal(8, Side(grips, HorizontalAlignment.Left).Width);
        Assert.Equal(8, Side(grips, HorizontalAlignment.Right).Width);
        var far = Assert.Single(grips, grip => grip.Orientation == SplitterOrientation.Horizontal);
        Assert.Equal(VerticalAlignment.Top, far.VerticalAlignment); // hung above the tab: its top edge is the free one
        Assert.Equal(8, far.Height);
        var corners = grips.Where(grip => grip.Orientation == SplitterOrientation.Both).ToArray();
        Assert.Equal(new[] { SplitterCorner.TopLeft, SplitterCorner.TopRight }, corners.Select(grip => grip.Corner).ToArray());
        Assert.All(corners, corner => Assert.Equal((16.0, 16.0), (corner.Width, corner.Height)));
        Assert.All(grips, grip =>
        {
            Assert.Equal("Double-click for the default size", grip.HelpHint);
            Assert.True(grip.TracksInScreenSpace); // the popup's own window moves as it grows
            Assert.Equal("Resize", Assert.IsType<TextBlock>(grip.TipChip.Child).Text);
        });

        rig.Coordinator.Close();
        rig.App.Settings.DiffPopoverSize = new Size(100, 100); // stored below the floor
        rig.First.IsDiffVisible = true;
        rig.Coordinator.Sync(true, rig.First);
        Assert.Equal(new Size(420, 320), rig.Coordinator.ShownSize);
    }));

    [Fact]
    public void A_drag_grows_from_the_centre_held_inside_the_limits_and_the_release_remembers_the_size() => headless.OnUiThread(() => DiffPopoverRig.Run(rig =>
    {
        OpenRoomy(rig);
        Assert.Equal(new Size(640, 540), rig.Coordinator.ShownSize); // :522-523 by default
        var grips = Grips(rig.LastPopup);

        var right = Side(grips, HorizontalAlignment.Right);
        right.Target.Begin();
        right.Target.Move(new Vector(30, 7));
        Assert.Equal(new Size(700, 540), rig.Coordinator.ShownSize); // both sides move: twice the travel, the height untouched
        Assert.Equal(700, rig.LastPopup.Width);
        Assert.Null(rig.App.Settings.DiffPopoverSize); // not written while held
        right.Target.Move(new Vector(25, 0));
        Assert.Equal(690, rig.Coordinator.ShownSize.Width); // from the press, not from the last move
        right.Target.End(true);
        Assert.Equal(new Size(690, 540), rig.App.Settings.DiffPopoverSize);

        var top = Assert.Single(grips, grip => grip.Orientation == SplitterOrientation.Horizontal);
        top.Target.Begin();
        top.Target.Move(new Vector(5, -60)); // the top edge rises: taller by the travel, the width untouched
        top.Target.End(true);
        Assert.Equal(new Size(690, 600), rig.App.Settings.DiffPopoverSize);

        var corner = Assert.Single(grips, grip => grip.Corner == SplitterCorner.TopLeft && grip.Orientation == SplitterOrientation.Both);
        corner.Target.Begin();
        corner.Target.Move(new Vector(-100_000, 100_000));
        corner.Target.End(true);
        Assert.Equal(new Size(DiffPopoverCoordinator.Largest(rig.Tab).Width, 320), rig.Coordinator.ShownSize);
        Assert.Equal(rig.Coordinator.ShownSize, rig.App.Settings.DiffPopoverSize);
    }));

    [Fact]
    public void Keys_a_double_click_and_automation_resize_it_and_each_change_is_remembered() => headless.OnUiThread(() => DiffPopoverRig.Run(rig =>
    {
        OpenRoomy(rig);
        var grips = Grips(rig.LastPopup);
        var left = Side(grips, HorizontalAlignment.Left);
        var right = Side(grips, HorizontalAlignment.Right);
        var top = Assert.Single(grips, grip => grip.Orientation == SplitterOrientation.Horizontal);
        var corner = Assert.Single(grips, grip => grip.Corner == SplitterCorner.TopRight && grip.Orientation == SplitterOrientation.Both);
        var largest = DiffPopoverCoordinator.Largest(rig.Tab);

        Key(right, SplitterKey.Right);
        Assert.Equal(new Size(648, 540), rig.App.Settings.DiffPopoverSize);
        Key(top, SplitterKey.Up, shift: true);
        Assert.Equal(new Size(648, 588), rig.App.Settings.DiffPopoverSize);
        Key(left, SplitterKey.Right); // the left edge moves in: narrower
        Assert.Equal(new Size(640, 588), rig.App.Settings.DiffPopoverSize);
        Key(right, SplitterKey.Home);
        Assert.Equal(new Size(420, 588), rig.App.Settings.DiffPopoverSize);
        Key(top, SplitterKey.End);
        Assert.Equal(new Size(420, largest.Height), rig.App.Settings.DiffPopoverSize);
        Key(corner, SplitterKey.Enter);
        Assert.Equal(new Size(640, 540), rig.App.Settings.DiffPopoverSize);
        Assert.Equal(new Size(640, 540), rig.Coordinator.ShownSize);
        corner.Target.Reset(); // the double-click
        Assert.Equal(new Size(640, 540), rig.Coordinator.ShownSize);

        var width = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(right));
        Assert.Equal((420.0, largest.Width, 640.0, false), (width.Minimum, width.Maximum, width.Value, width.IsReadOnly));
        width.SetValue(800);
        Assert.Equal(new Size(800, 540), rig.App.Settings.DiffPopoverSize);
        var height = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(top));
        Assert.Equal((320.0, largest.Height, 540.0), (height.Minimum, height.Maximum, height.Value));
        Assert.True(Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(corner)).IsReadOnly);
    }));
}
