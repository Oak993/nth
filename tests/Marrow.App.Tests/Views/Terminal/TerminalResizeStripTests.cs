using System.Linq;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/Views/Terminal/TerminalPanel.swift (B3 52bc792) the drawer's top-edge resize strip :84-99 and its
// handle :113-131, now the shared Splitter (web W1, DESIGN 4.3, ED-NEW-web-w1-4): the source's numbers (140 to 760, 260,
// the global terminalHeight written on release), with the shared splitter's 12 DIP strip centred on the edge, its rubber
// band, keys and grip, and one new behaviour: released under 84 DIP the drawer closes. Real panel, real (headless) window
// and pointer. The 8 point strip and its 12/28/2.5 label-coloured grip (UI-TERM-012) are superseded by the shared grip
// (Marrow.UI.Tests SplitterGripTests).
[Collection("Headless")]
public sealed class TerminalResizeStripTests(HeadlessSession headless)
{
    private static Splitter Strip(Control root) => LogicalQuery.SelfAndDescendants<Splitter>(root).Single();

    private static Point Centre(Control control, Visual ancestor) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ancestor)!.Value;

    [Fact]
    [Trait("Decision", "ED-NEW-web-w1-4")]
    public void The_strip_spans_the_drawer_twelve_dip_tall_centred_on_its_top_edge_with_the_up_down_cursor_before_any_press() => TerminalPanelRig.Run(headless, rig =>
    {
        var (host, panel, _) = rig.Show(TerminalPanelRig.Folder("strip geometry"), 520, 400);
        var strip = Strip(host);
        var box = new Rect(strip.TranslatePoint(new Point(), panel)!.Value, strip.Bounds.Size);
        Assert.Equal(new Rect(0, -6, panel.Bounds.Width, 12), box); // half above the drawer, half inside it
        Assert.Equal(520, box.Width, 3);
        Assert.Equal("SizeNorthSouth", strip.Handle.HandleCursor!.ToString()); // :125 .resizeUpDown
        Assert.Equal(new Thickness(0, 1), strip.Handle.Margin); // a mouse hits the middle 10 DIP
    });

    [Fact]
    [Trait("Req", "UI-TERM-011")] // App/Views/Terminal/TerminalPanel.swift:86-96,116-118
    [Trait("Decision", "ED-NEW-web-w1-4")]
    public void Dragging_down_shrinks_and_up_grows_held_at_140_and_760_with_a_rubber_band_and_the_release_commits_the_terminalHeight_key() => TerminalPanelRig.Run(headless, rig =>
    {
        using var cursor = DragHandleView.CursorPlatform.Override(new FixedCursorPlatform(null));
        var (host, panel, window) = rig.Show(TerminalPanelRig.Folder("strip drag"), 500, 900);
        var start = Centre(Strip(host), window);
        rig.Step(() => window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseMove(start + new Vector(0, 50), RawInputModifiers.LeftMouseButton)); // down: shrinks
        Assert.True(rig.Runtime.IsTerminalResizing);
        Assert.Equal(210, panel.Height);
        rig.Step(() => window.MouseMove(start + new Vector(0, 160), RawInputModifiers.LeftMouseButton)); // 40 under 140
        Assert.Equal(128, panel.Height, 6); // the band gives 0.3 of it
        rig.Step(() => window.MouseMove(start + new Vector(0, -800), RawInputModifiers.LeftMouseButton)); // up: grows
        Assert.Equal(784, panel.Height, 6); // 760 and at most 24 more while held
        Assert.Equal(260, rig.App.Settings.TerminalHeight); // the setting is not written while dragging
        Assert.Null(rig.Store.Defaults.Object("terminalHeight"));

        rig.Step(() => window.MouseMove(start + new Vector(0, -800), RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseUp(start + new Vector(0, -800), MouseButton.Left, RawInputModifiers.None));
        Assert.False(rig.Runtime.IsTerminalResizing);
        Assert.Equal(760, rig.App.Settings.TerminalHeight); // the band is let go: the drawer settles at the maximum
        Assert.Equal(760.0, rig.Store.Defaults.Object("terminalHeight"));
        Assert.Equal(760, panel.Height);

        // Release somewhere new without moving first: the release pixel counts.
        var again = Centre(Strip(host), window);
        rig.Step(() => window.MouseDown(again, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseMove(again + new Vector(0, 2), RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseUp(again + new Vector(0, 530), MouseButton.Left, RawInputModifiers.None));
        Assert.Equal(230, rig.App.Settings.TerminalHeight);
        Assert.Equal(230, panel.Height);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w1-4")]
    public void A_click_without_movement_changes_nothing_and_a_stored_height_out_of_range_is_shown_verbatim_until_a_drag() => TerminalPanelRig.Run(headless, rig =>
    {
        using var cursor = DragHandleView.CursorPlatform.Override(new FixedCursorPlatform(null));
        rig.Step(() => rig.App.Settings.TerminalHeight = 100); // below the range: used verbatim (:80)
        var (host, panel, window) = rig.Show(TerminalPanelRig.Folder("strip click"), 500, 400);
        Assert.Equal(100, panel.Height);
        var where = Centre(Strip(host), window);
        rig.Step(() => window.MouseDown(where, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseUp(where, MouseButton.Left, RawInputModifiers.None));
        Assert.Equal(100, rig.App.Settings.TerminalHeight); // the 1 DIP slop: a click is not a drag
        Assert.Equal(100, panel.Height);
        Assert.False(rig.Runtime.IsTerminalResizing);

        var elsewhere = where + new Vector(120, 0); // along the strip, far enough that the press is not a double-click
        rig.Step(() => window.MouseDown(elsewhere, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseMove(elsewhere + new Vector(0, -1), RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseUp(elsewhere + new Vector(0, -1), MouseButton.Left, RawInputModifiers.None));
        Assert.Equal(141, rig.App.Settings.TerminalHeight); // a drag clamps the stored height first (:89)
        Assert.Equal(141, panel.Height);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w1-4")]
    public void Released_under_84_dip_the_drawer_closes_and_keeps_the_height_it_had() => TerminalPanelRig.Run(headless, rig =>
    {
        using var cursor = DragHandleView.CursorPlatform.Override(new FixedCursorPlatform(null));
        rig.Step(() => rig.Runtime.IsTerminalVisible = true);
        var (host, panel, window) = rig.Show(TerminalPanelRig.Folder("strip close"), 500, 600);
        var start = Centre(Strip(host), window);
        rig.Step(() => window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseMove(start + new Vector(0, 176), RawInputModifiers.LeftMouseButton)); // 84: not past it
        Assert.Equal(123.2, panel.Height, 6); // held in the band: 140 less 0.3 of the 56 past it
        rig.Step(() => window.MouseMove(start + new Vector(0, 177), RawInputModifiers.LeftMouseButton)); // 83
        Assert.Equal(0, panel.Height, 6); // collapsed while held
        rig.Step(() => window.MouseMove(start + new Vector(0, 60), RawInputModifiers.LeftMouseButton)); // back up: it opens again
        Assert.Equal(200, panel.Height, 6);
        Assert.True(rig.Runtime.IsTerminalVisible);
        rig.Step(() => window.MouseMove(start + new Vector(0, 200), RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseUp(start + new Vector(0, 200), MouseButton.Left, RawInputModifiers.None));
        Assert.False(rig.Runtime.IsTerminalVisible); // the drawer closed
        Assert.False(rig.Runtime.IsTerminalResizing);
        Assert.Equal(260, rig.App.Settings.TerminalHeight); // the height it opens at again
        Assert.Null(rig.Store.Defaults.Object("terminalHeight"));

        rig.Step(() => rig.Runtime.IsTerminalVisible = true); // the same drawer opened again before it left
        Assert.Equal(260, panel.Height);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w1-4")]
    public void Keys_and_automation_resize_the_drawer_and_ctrl_down_closes_it() => TerminalPanelRig.Run(headless, rig =>
    {
        rig.Step(() => rig.Runtime.IsTerminalVisible = true);
        var (host, panel, _) = rig.Show(TerminalPanelRig.Folder("strip keys"), 500, 600);
        var strip = Strip(host);
        rig.Step(() => strip.Execute(SplitterMath.Command(SplitterKey.Up, false, false, SplitterOrientation.Horizontal)));
        Assert.Equal(268, rig.App.Settings.TerminalHeight); // the edge up 8: the drawer grows
        Assert.Equal(268, panel.Height);
        rig.Step(() => strip.Execute(SplitterMath.Command(SplitterKey.Down, true, false, SplitterOrientation.Horizontal)));
        Assert.Equal(220, rig.App.Settings.TerminalHeight);
        rig.Step(() => strip.Execute(SplitterMath.Command(SplitterKey.End, false, false, SplitterOrientation.Horizontal)));
        Assert.Equal(760, rig.App.Settings.TerminalHeight);
        rig.Step(() => strip.Execute(SplitterMath.Command(SplitterKey.Enter, false, false, SplitterOrientation.Horizontal)));
        Assert.Equal(260, rig.App.Settings.TerminalHeight);

        var range = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(strip));
        Assert.Equal((0.0, 760.0, 260.0), (range.Minimum, range.Maximum, range.Value));
        rig.Step(() => range.SetValue(400));
        Assert.Equal(400, rig.App.Settings.TerminalHeight);
        Assert.Equal(400, panel.Height);

        rig.Step(() => strip.Execute(SplitterMath.Command(SplitterKey.Down, false, true, SplitterOrientation.Horizontal, collapseSide: SplitterSide.Trailing)));
        Assert.False(rig.Runtime.IsTerminalVisible);
        Assert.Equal(400, rig.App.Settings.TerminalHeight);
    });
}
