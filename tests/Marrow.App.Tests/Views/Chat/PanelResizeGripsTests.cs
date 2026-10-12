using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/SubagentPanel.swift (B3 52bc792) PanelResizeGrips :591-638, now the shared Splitter
// (web W1, DESIGN 4.3, ED-NEW-web-w1-5): the free edges are 8 DIP grips and the free corner the 16 DIP grip, all facing
// away from the dock; a drag reports the window-space travel with its axes (:603-607), a double-click resets (:609), and
// the keys and a screen reader move the focused grip within the panel's extent. A real (headless) window and pointer.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w1-5")]
public sealed class PanelResizeGripsTests(HeadlessSession headless)
{
    private static readonly Rect Box = new(50, 40, 300, 200);

    // What the grips told the resizer: each resize, then the count of ends ([0]) and of resets ([1]).
    private static (PanelResizeGrips Grips, Avalonia.Controls.Window Window, List<(Size Travel, PanelResizeAxes Axes)> Resizes, int[] Counts) Show(
        PanelDockCorner dock, Func<PanelResizeExtent>? extent = null)
    {
        var resizes = new List<(Size Travel, PanelResizeAxes Axes)>();
        var counts = new int[2];
        var grips = new PanelResizeGrips(new PanelResize
        {
            Corner = dock,
            OnResize = (travel, axes) => resizes.Add((travel, axes)),
            OnResizeEnd = () => counts[0]++,
            OnReset = () => counts[1]++,
            Extent = extent,
        })
        {
            Width = Box.Width,
            Height = Box.Height,
            Margin = new Thickness(Box.X, Box.Y, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = new Avalonia.Controls.Window { Content = grips, Width = 500, Height = 400 };
        window.Show();
        Settle();
        return (grips, window, resizes, counts);
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static Rect Frame(Control control, Visual ancestor) =>
        new(control.TranslatePoint(new Point(), ancestor)!.Value, control.Bounds.Size);

    private static Point Centre(Control control, Visual ancestor) => Frame(control, ancestor).Center;

    private static void Key(Splitter grip, SplitterKey key, bool shift = false) =>
        Assert.True(grip.Execute(SplitterMath.Command(key, shift, false, grip.Orientation)));

    [Theory]
    [InlineData(PanelDockCorner.TopLeading, 292, 192, 284, 184, SplitterCorner.BottomRight, "BottomRightCorner")]
    [InlineData(PanelDockCorner.TopTrailing, 0, 192, 0, 184, SplitterCorner.BottomLeft, "BottomLeftCorner")]
    [InlineData(PanelDockCorner.BottomLeading, 292, 0, 284, 0, SplitterCorner.TopRight, "BottomLeftCorner")]
    [InlineData(PanelDockCorner.BottomTrailing, 0, 0, 0, 0, SplitterCorner.TopLeft, "BottomRightCorner")]
    public void The_free_edges_and_corner_face_away_from_each_dock_and_follow_it(PanelDockCorner dock, double widthX, double heightY, double cornerX, double cornerY, SplitterCorner corner, string cursor) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var cursors = DragHandleView.CursorPlatform.Override(new FixedCursorPlatform(null));
        var (grips, window, _, _) = Show(dock == PanelDockCorner.TopLeading ? PanelDockCorner.BottomTrailing : PanelDockCorner.TopLeading);
        grips.SetDock(dock); // a move to another corner moves the grips with it
        Settle();

        Assert.Equal(new Rect(widthX, 0, 8, 200), Frame(grips.WidthGrip, grips)); // the whole free vertical edge
        Assert.Equal(new Rect(0, heightY, 300, 8), Frame(grips.HeightGrip, grips)); // the whole free horizontal edge
        Assert.Equal(new Rect(cornerX, cornerY, 16, 16), Frame(grips.CornerGrip, grips)); // :597
        Assert.Equal(corner, grips.CornerGrip.Corner);
        Assert.Equal(cursor, grips.CornerGrip.Handle.HandleCursor!.ToString());
        Assert.Equal("SizeWestEast", grips.WidthGrip.Handle.HandleCursor!.ToString());
        Assert.Equal("SizeNorthSouth", grips.HeightGrip.Handle.HandleCursor!.ToString());
        Assert.Equal(
            new[] { "Resize panel width", "Resize panel height", "Resize panel" },
            new[] { grips.WidthGrip.Label, grips.HeightGrip.Label, grips.CornerGrip.Label });
        Assert.All(new[] { grips.WidthGrip, grips.HeightGrip, grips.CornerGrip }, grip =>
        {
            Assert.Equal(PanelResizeGrips.ResetHint, grip.HelpHint);
            Assert.Equal("Resize", Assert.IsType<TextBlock>(grip.TipChip.Child).Text);
            Assert.Null(ToolTip.GetTip(grip));
        });
        var children = Assert.IsType<Grid>(grips.Child).Children;
        Assert.Equal(2, children.IndexOf(grips.CornerGrip)); // the corner wins where the edges meet
        window.Close();
    });

    [Fact]
    public void A_drag_reports_the_window_space_travel_with_its_axes_and_a_double_click_resets() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var cursors = DragHandleView.CursorPlatform.Override(new FixedCursorPlatform(null));
        var (grips, window, resizes, counts) = Show(PanelDockCorner.TopTrailing);

        var start = Centre(grips.WidthGrip, window);
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(resizes); // a press is not a resize
        window.MouseMove(start + new Vector(-30, 20), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        // :603-607: the window-space travel along the grip's own axis (the resizer reads only that axis; the shared splitter
        // passes nothing across it).
        Assert.Equal((new Size(-30, 0), PanelResizeAxes.Width), resizes.Last());
        window.MouseUp(start + new Vector(-30, 20), MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, counts[0]);

        var corner = Centre(grips.CornerGrip, window);
        window.MouseDown(corner, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseMove(corner + new Vector(-12, 18), RawInputModifiers.LeftMouseButton);
        window.MouseUp(corner + new Vector(-12, 18), MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((new Size(-12, 18), PanelResizeAxes.Both), resizes.Last());
        Assert.Equal(2, counts[0]);

        var count = resizes.Count;
        var edge = Centre(grips.HeightGrip, window) + new Vector(60, 0); // away from the corner
        window.MouseDown(edge, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(edge, MouseButton.Left, RawInputModifiers.None);
        window.MouseDown(edge, MouseButton.Left, RawInputModifiers.LeftMouseButton); // :609 the second press of a double-click
        window.MouseUp(edge, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, counts[1]);
        Assert.Equal(count, resizes.Count); // a click is never a resize
        Assert.Equal(2, counts[0]);
        window.Close();
    });

    [Fact]
    public void Keys_and_automation_move_the_free_edge_within_the_extent() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var extent = new PanelResizeExtent(new Size(300, 200), new Size(240, 120), new Size(600, 500));
        var (grips, window, resizes, counts) = Show(PanelDockCorner.TopTrailing, () => extent); // free edges: leading and bottom

        Key(grips.WidthGrip, SplitterKey.Right); // the edge moves right: a trailing panel narrows
        Assert.Equal((new Size(8, 0), PanelResizeAxes.Width), resizes.Last());
        Assert.Equal(1, counts[0]);
        Key(grips.HeightGrip, SplitterKey.Down, shift: true);
        Assert.Equal((new Size(0, 48), PanelResizeAxes.Height), resizes.Last());
        Key(grips.CornerGrip, SplitterKey.Left);
        Assert.Equal((new Size(-8, 0), PanelResizeAxes.Both), resizes.Last());
        Key(grips.WidthGrip, SplitterKey.End); // the largest width: 300 more, the leading edge travels left
        Assert.Equal((new Size(-300, 300), PanelResizeAxes.Width), resizes.Last());
        Key(grips.HeightGrip, SplitterKey.Home); // the smallest height: 80 less, the bottom edge travels up
        Assert.Equal((new Size(60, -80), PanelResizeAxes.Height), resizes.Last());
        Assert.Equal(5, counts[0]);

        var width = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(grips.WidthGrip));
        Assert.Equal((240.0, 600.0, 300.0, false), (width.Minimum, width.Maximum, width.Value, width.IsReadOnly));
        width.SetValue(400);
        Assert.Equal((new Size(-100, 0), PanelResizeAxes.Width), resizes.Last());
        width.SetValue(9999); // clamped to the extent
        Assert.Equal((new Size(-300, 0), PanelResizeAxes.Width), resizes.Last());
        var height = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(grips.HeightGrip));
        Assert.Equal((120.0, 500.0, 200.0), (height.Minimum, height.Maximum, height.Value));
        height.SetValue(150);
        Assert.Equal((new Size(0, -50), PanelResizeAxes.Height), resizes.Last());

        var corner = ControlAutomationPeer.CreatePeerForElement(grips.CornerGrip);
        Assert.True(Assert.IsAssignableFrom<IRangeValueProvider>(corner).IsReadOnly); // two values: no single number to set
        Assert.Equal("300 × 200", Assert.IsAssignableFrom<IValueProvider>(corner).Value);
        Assert.Throws<InvalidOperationException>(() => Assert.IsAssignableFrom<IRangeValueProvider>(corner).SetValue(10));
        window.Close();
    });

    [Fact]
    public void Without_an_extent_home_and_end_go_as_far_as_the_resizer_allows_and_the_value_is_read_only() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var (grips, window, resizes, counts) = Show(PanelDockCorner.BottomLeading); // free edges: trailing and top

        Key(grips.WidthGrip, SplitterKey.End);
        Assert.Equal((new Size(100_000, -100_000), PanelResizeAxes.Width), resizes.Last()); // the resizer clamps
        Key(grips.HeightGrip, SplitterKey.Home);
        Assert.Equal((new Size(-100_000, 100_000), PanelResizeAxes.Height), resizes.Last());
        Key(grips.HeightGrip, SplitterKey.Up);
        Assert.Equal((new Size(0, -8), PanelResizeAxes.Height), resizes.Last()); // the top edge rises: the panel grows
        Assert.Equal(3, counts[0]);

        var width = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(grips.WidthGrip));
        Assert.True(width.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => width.SetValue(300));
        Assert.Equal(3, resizes.Count);
        window.Close();
    });
}
