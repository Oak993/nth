using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// web W1 (DESIGN 4.3, ED-NEW-web-w1-3): the sidebar's resize strip (WindowChrome.swift:376-401, B3 52bc792) is a thin
// configuration of the shared Splitter over SidebarLayout, with the sidebar's own numbers unchanged (210 · 268 · 440, a
// release under 110 hides it, PR-WIN-043 to PR-WIN-045 in WindowChromeSecondTests). These replace WindowChromeThirdTests'
// strip tests (PR-WIN-049 to PR-WIN-057): the DragHandle mechanics the source requires are kept (client hit, first
// mouse, inert while inactive, deltas from the press point); the 9 point strip, its 10/22 grip and glow are superseded by
// the shared splitter and grip (SplitterGripTests, SplitterPointerTests).
[Collection("Headless")]
public sealed class SidebarSplitterTests(HeadlessSession headless)
{
    private static (Window Window, Splitter Splitter) Mounted(SidebarResizeHandle config, SidebarLayout layout)
    {
        var splitter = config.MakeSplitter();
        splitter.HorizontalAlignment = HorizontalAlignment.Left;
        splitter.VerticalAlignment = VerticalAlignment.Stretch;
        splitter.Margin = new Thickness(layout.RenderedWidth - Splitter.DividerThickness / 2, 0, 0, 0);
        var window = new Window { Content = new Panel { Children = { splitter } }, Width = 800, Height = 500 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, splitter);
    }

    private static Point Centre(Splitter splitter, Window window) =>
        splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;

    [Fact]
    [Trait("Req", "PR-WIN-053")] // DragHandle.swift:128-130, :323-329: the strip is client area and takes the first click
    public void The_sidebar_splitter_is_client_area_that_never_moves_the_window_and_takes_the_first_click() => headless.OnUiThread(() =>
    {
        var splitter = new SidebarResizeHandle(() => null).MakeSplitter();
        Assert.Equal(Win32Properties.Win32HitTestValue.Client, splitter.Handle.GetValue(Win32Properties.NonClientHitTestResultProperty));
        Assert.False(splitter.Handle.MouseDownCanMoveWindow);
        Assert.True(splitter.Handle.AcceptsFirstMouse(null));
    });

    [Fact]
    [Trait("Req", "PR-WIN-054")] // DragHandle.swift:100-112, :140-151: an inactive strip is inert
    public void An_inactive_sidebar_splitter_takes_no_hit_shows_no_cursor_or_grip_and_cannot_be_focused() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var cursor = DragHandleView.CursorPlatform.Override(new FakeCursorPlatform(null));
        runtime.Defaults.Set("sidebarStartsOpen", true);
        var layout = new SidebarLayout();
        var config = new SidebarResizeHandle(() => layout);
        var (window, splitter) = Mounted(config, layout);
        try
        {
            Assert.True(splitter.Handle.IsHitTestVisible);
            Assert.NotNull(splitter.Handle.Cursor);
            config.IsActive = false;
            config.Update(splitter);
            Assert.False(splitter.IsActive);
            Assert.False(splitter.Handle.IsHitTestVisible);
            Assert.Null(splitter.Handle.Cursor);
            Assert.False(splitter.Focusable);
            window.MouseMove(Centre(splitter, window), RawInputModifiers.None);
            runtime.AdvanceMilliseconds(600);
            Dispatcher.UIThread.RunJobs();
            Assert.False(splitter.Grip.IsShown);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-WIN-049")] // WindowChrome.swift:377,394: a fixed hit width with the shared column-resize cursor (9 there, 10 here)
    [Trait("Decision", "ED-NEW-web-w1-3")]
    public void The_splitter_is_twelve_dip_centred_on_the_edge_hits_ten_for_a_mouse_and_shares_one_cursor() => headless.OnUiThread(() =>
    {
        var config = new SidebarResizeHandle(() => null);
        var first = config.MakeSplitter();
        var second = new SidebarResizeHandle(() => null).MakeSplitter();
        Assert.Equal(10, SidebarResizeHandle.HitWidth); // was 9 (WindowChrome.swift:377)
        Assert.Equal(12, first.Width);
        Assert.Equal(SplitterOrientation.Vertical, first.Orientation);
        Assert.Equal("Resize sidebar", first.Label);
        Assert.Equal(SplitterSide.Leading, first.CollapseSide);
        Assert.Equal("SizeWestEast", first.Handle.HandleCursor!.ToString()); // :394 .columnResize
        Assert.Same(first.Handle.HandleCursor, second.Handle.HandleCursor); // one shared cursor, not one per view
    });

    [Fact]
    [Trait("Req", "PR-WIN-055")] // DragHandle.swift:198-232: the travel from the press point, the final one before the end
    [Trait("Decision", "ED-NEW-web-w1-3")]
    public void A_drag_moves_the_edge_by_the_travel_from_the_press_point_and_the_release_keeps_the_sources_numbers() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var cursor = DragHandleView.CursorPlatform.Override(new FakeCursorPlatform(null));
        runtime.Defaults.Set("sidebarStartsOpen", true);
        var layout = new SidebarLayout();
        Assert.Equal(268, layout.Width);
        var (window, splitter) = Mounted(new SidebarResizeHandle(() => layout), layout);
        try
        {
            var press = Centre(splitter, window);
            runtime.OnMain(() => window.MouseDown(press, MouseButton.Left, RawInputModifiers.LeftMouseButton));
            runtime.OnMain(() => window.MouseMove(press + new Vector(30, 0), RawInputModifiers.LeftMouseButton));
            Assert.Equal(298, layout.Width); // :341 anchorWidth + translation
            runtime.OnMain(() => window.MouseMove(press + new Vector(-20, 0), RawInputModifiers.LeftMouseButton));
            Assert.Equal(248, layout.Width); // from the press point, not the last move
            runtime.OnMain(() => window.MouseMove(press + new Vector(-120, 0), RawInputModifiers.LeftMouseButton));
            Assert.Equal(148, layout.Width); // under the minimum while held (PR-WIN-043)
            runtime.OnMain(() => window.MouseUp(press + new Vector(-120, 0), MouseButton.Left, RawInputModifiers.None));
            runtime.RunUntilIdle();
            Assert.True(layout.IsVisible);
            Assert.Equal(210, layout.Width); // :350 released above 110: back to the minimum
            Assert.Equal(210.0, runtime.Defaults.Object("sidebarWidth"));

            // The splitter was not moved here. The second press lands 40 DIP along the edge, so it is not a double-click.
            var again = press + new Vector(0, 40);
            runtime.OnMain(() => window.MouseDown(again, MouseButton.Left, RawInputModifiers.LeftMouseButton));
            runtime.OnMain(() => window.MouseMove(again + new Vector(-101, 0), RawInputModifiers.LeftMouseButton)); // 210 - 101 = 109
            runtime.OnMain(() => window.MouseUp(again + new Vector(-101, 0), MouseButton.Left, RawInputModifiers.None));
            runtime.RunUntilIdle();
            Assert.False(layout.IsVisible); // :345 released under 110: hidden
        }
        finally { runtime.OnMain(window.Close); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w1-3")]
    public void Keys_resize_hide_and_show_the_sidebar_and_its_value_reads_hidden_at_zero() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("sidebarStartsOpen", true);
        var layout = new SidebarLayout();
        var config = new SidebarResizeHandle(() => layout);
        var splitter = config.MakeSplitter();
        var peer = ControlAutomationPeer.CreatePeerForElement(splitter);
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(peer);
        Assert.Equal((0.0, 440.0, 268.0), (range.Minimum, range.Maximum, range.Value));

        runtime.OnMain(() => splitter.Execute(SplitterMath.Command(SplitterKey.Right, false, false, SplitterOrientation.Vertical)));
        Assert.Equal(276, layout.Width);
        runtime.OnMain(() => splitter.Execute(SplitterMath.Command(SplitterKey.Right, true, false, SplitterOrientation.Vertical)));
        Assert.Equal(324, layout.Width);
        runtime.OnMain(() => splitter.Execute(SplitterMath.Command(SplitterKey.End, false, false, SplitterOrientation.Vertical)));
        Assert.Equal(440, layout.Width);
        runtime.OnMain(() => splitter.Execute(SplitterMath.Command(SplitterKey.Home, false, false, SplitterOrientation.Vertical)));
        Assert.Equal(210, layout.Width);
        runtime.OnMain(() => splitter.Execute(SplitterMath.Command(SplitterKey.Enter, false, false, SplitterOrientation.Vertical)));
        Assert.Equal(268, layout.Width);
        Assert.Equal(268.0, runtime.Defaults.Object("sidebarWidth"));

        runtime.OnMain(() => splitter.Execute(SplitterMath.Command(SplitterKey.Space, false, false, SplitterOrientation.Vertical, collapseSide: SplitterSide.Leading)));
        Assert.False(layout.IsVisible);
        splitter.Refresh();
        Assert.Equal(0, range.Value);
        Assert.Equal("Sidebar hidden", ((IValueProvider)peer).Value);
        runtime.OnMain(() => splitter.Execute(SplitterMath.Command(SplitterKey.Right, false, true, SplitterOrientation.Vertical, collapseSide: SplitterSide.Leading)));
        Assert.True(layout.IsVisible);
        Assert.Equal(268, layout.Width);

        runtime.OnMain(() => range.SetValue(60)); // a screen reader setting it under 110 hides it
        Assert.False(layout.IsVisible);
        runtime.OnMain(() => range.SetValue(300));
        Assert.True(layout.IsVisible);
        Assert.Equal(300, layout.Width);
    });
}
