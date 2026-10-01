using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/Views/Terminal/TerminalPanel.swift (B3 52bc792) the drawer's top-edge resize strip :84-99, its
// handle :113-131 and the shared grip DragHandle.swift:18-30,153-181. Real panel, real (headless) window and pointer.
[Collection("Headless")]
public sealed class TerminalResizeStripTests(HeadlessSession headless)
{
    private static DragHandleView Handle(Control root) => LogicalQuery.SelfAndDescendants<DragHandleView>(root).Single();

    private static Border Grip(DragHandleView handle) => handle.Children.OfType<Border>().Last(); // the glow is added first, the grip on top

    private static Point Centre(Control control, Visual ancestor) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ancestor)!.Value;

    // The grip animates for 0.18 s on the render clock: pump it until the layer shows what is asked of it.
    private static bool SettlesAt(Border grip, double width, double opacity)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (Math.Abs(grip.Width - width) < 0.01 && Math.Abs(grip.Opacity - opacity) < 0.01) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    private static Color Solid(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [Fact]
    [Trait("Req", "UI-TERM-011")] // App/Views/Terminal/TerminalPanel.swift:84-99
    public void The_strip_is_overlaid_across_the_top_eight_points_and_shows_the_resize_cursor_before_any_press() => TerminalPanelRig.Run(headless, rig =>
    {
        var (host, panel, _) = rig.Show(TerminalPanelRig.Folder("strip geometry"), 520, 400);
        var handle = Handle(host);
        var box = new Rect(handle.TranslatePoint(new Point(), panel)!.Value, handle.Bounds.Size);
        Assert.Equal(new Rect(0, 0, panel.Bounds.Width, 8), box); // .overlay(alignment: .top), full width, .frame(height: 8)
        Assert.Equal(520, box.Width, 3);
        Assert.Same(TerminalResizeHandleView.ResizeCursor, handle.Cursor); // set on the strip itself, before any press (:125)
        Assert.Equal("SizeNorthSouth", handle.Cursor!.ToString()); // ns-resize: .resizeUpDown
        Assert.Equal(Colors.Transparent, Solid(handle.Background)); // transparent: only the grip bar is drawn
    });

    [Fact]
    [Trait("Req", "UI-TERM-011")] // App/Views/Terminal/TerminalPanel.swift:86-96,116-118
    public void Dragging_down_shrinks_and_dragging_up_grows_clamped_to_140_and_760_and_the_release_pixel_commits_the_height_to_the_terminalHeight_key() => TerminalPanelRig.Run(headless, rig =>
    {
        using var cursor = DragHandleView.CursorPlatform.Override(new FixedCursorPlatform(null));
        var (host, panel, window) = rig.Show(TerminalPanelRig.Folder("strip drag"), 500, 900);
        var handle = Handle(host);
        var start = Centre(handle, window);
        rig.Step(() => window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        Assert.True(rig.Runtime.IsTerminalResizing);

        rig.Step(() => window.MouseMove(start + new Vector(0, 50), RawInputModifiers.LeftMouseButton)); // down: shrinks
        Assert.Equal(210, panel.Height);
        rig.Step(() => window.MouseMove(start + new Vector(0, 600), RawInputModifiers.LeftMouseButton));
        Assert.Equal(140, panel.Height); // clamped at the bottom
        rig.Step(() => window.MouseMove(start + new Vector(0, -800), RawInputModifiers.LeftMouseButton)); // up: grows
        Assert.Equal(760, panel.Height); // clamped at the top
        Assert.Equal(260, rig.App.Settings.TerminalHeight); // the setting is not written while dragging
        Assert.Null(rig.Store.Defaults.Object("terminalHeight"));

        // Release somewhere new without moving first: the release pixel counts (one final change before the end).
        rig.Step(() => window.MouseUp(start + new Vector(0, 30), MouseButton.Left, RawInputModifiers.None));
        Assert.False(rig.Runtime.IsTerminalResizing);
        Assert.Equal(230, rig.App.Settings.TerminalHeight);
        Assert.Equal(230.0, rig.Store.Defaults.Object("terminalHeight"));
        Assert.Equal(230, panel.Height);
    });

    [Fact]
    [Trait("Req", "UI-TERM-011")] // App/Views/Terminal/TerminalPanel.swift:87-96; UI/Chrome/DragHandle.swift:222-232
    public void A_click_with_no_movement_still_commits_the_height_clamped_to_the_range() => TerminalPanelRig.Run(headless, rig =>
    {
        using var cursor = DragHandleView.CursorPlatform.Override(new FixedCursorPlatform(null));
        rig.Step(() => rig.App.Settings.TerminalHeight = 100); // below the range: used verbatim until a drag touches it
        var (host, panel, window) = rig.Show(TerminalPanelRig.Folder("strip click"), 500, 400);
        Assert.Equal(100, panel.Height);
        var where = Centre(Handle(host), window);

        rig.Step(() => window.MouseDown(where, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseUp(where, MouseButton.Left, RawInputModifiers.None));

        Assert.Equal(140, rig.App.Settings.TerminalHeight); // the press-release is a drag of zero: it commits clamp(100 + 0)
        Assert.Equal(140.0, rig.Store.Defaults.Object("terminalHeight"));
        Assert.Equal(140, panel.Height);
        Assert.False(rig.Runtime.IsTerminalResizing);

        // An in-range height is committed unchanged.
        rig.Step(() => rig.App.Settings.TerminalHeight = 400);
        var again = Centre(Handle(host), window);
        rig.Step(() => window.MouseDown(again, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseUp(again, MouseButton.Left, RawInputModifiers.None));
        Assert.Equal(400, rig.App.Settings.TerminalHeight);
        Assert.Equal(400, panel.Height);
    });

    [Fact]
    [Trait("Req", "UI-TERM-012")] // App/Views/Terminal/TerminalPanel.swift:147-153; UI/Chrome/DragHandle.swift:153-181
    public void The_grip_bar_is_a_two_and_a_half_point_label_coloured_pill_twelve_wide_and_invisible_at_rest_centred_in_the_strip() => TerminalPanelRig.Run(headless, rig =>
    {
        using var cursor = DragHandleView.CursorPlatform.Override(new FixedCursorPlatform(null));
        var (host, panel, _) = rig.Show(TerminalPanelRig.Folder("grip rest"), 500, 400);
        var handle = Handle(host);
        var grip = Grip(handle);

        Assert.Equal(2.5, grip.Height, 3);
        Assert.Equal(1.25, grip.CornerRadius.TopLeft, 3); // height / 2
        Assert.Equal(12, grip.Width, 3);
        Assert.Equal(0, grip.Opacity, 3);
        Assert.Equal((panel.Bounds.Width - 12) / 2, Canvas.GetLeft(grip), 3); // centred horizontally ...
        Assert.Equal((8 - 2.5) / 2, Canvas.GetTop(grip), 3); // ... and vertically in the 8-point strip
        Assert.Equal(SwiftColor.LabelColor.Resolve(grip.ActualThemeVariant), Solid(grip.Background)); // NSColor.labelColor
        Assert.Null(grip.Transitions); // layout-driven updates are applied with animations disabled
    });

    [Fact]
    [Trait("Req", "UI-TERM-012")] // App/Views/Terminal/TerminalPanel.swift:124; UI/Chrome/DragHandle.swift:60,153-181,247-255
    public void Hovering_widens_the_grip_to_28_at_0_6_over_0_18_seconds_and_a_drag_keeps_it_shown_even_when_the_pointer_leaves() => TerminalPanelRig.Run(headless, rig =>
    {
        var cursor = new FixedCursorPlatform(null);
        using var scope = DragHandleView.CursorPlatform.Override(cursor);
        var (host, panel, window) = rig.Show(TerminalPanelRig.Folder("grip hover"), 500, 400);
        var handle = Handle(host);
        var grip = Grip(handle);
        var over = Centre(handle, window);
        var away = Centre(panel, window) + new Vector(0, 40);

        rig.Step(() => window.MouseMove(over, RawInputModifiers.None));
        var transitions = Assert.IsType<Transitions>(grip.Transitions); // an animated change: 0.18 s ease-out
        Assert.NotEmpty(transitions);
        Assert.All(transitions, transition => Assert.Equal(TimeSpan.FromSeconds(0.18), Assert.IsType<DoubleTransition>(transition).Duration));
        Assert.True(SettlesAt(grip, 28, 0.6), $"hover left the grip at {grip.Width} wide, opacity {grip.Opacity}");
        Assert.Equal(2.5, grip.Height, 3);

        rig.Step(() => window.MouseMove(away, RawInputModifiers.None));
        Assert.True(SettlesAt(grip, 12, 0), $"leaving left the grip at {grip.Width} wide, opacity {grip.Opacity}");

        // While dragging the grip stays shown even though the pointer is far from the strip.
        rig.Step(() => window.MouseMove(over, RawInputModifiers.None));
        rig.Step(() => window.MouseDown(over, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseMove(away, RawInputModifiers.LeftMouseButton));
        Assert.True(SettlesAt(grip, 28, 0.6), $"dragging away left the grip at {grip.Width} wide, opacity {grip.Opacity}");
        rig.Step(() => window.MouseUp(away, MouseButton.Left, RawInputModifiers.None));
        // Released away from the strip: the cursor is read, found elsewhere, and the grip returns to rest.
        Assert.True(SettlesAt(grip, 12, 0), $"after release the grip stayed at {grip.Width} wide, opacity {grip.Opacity}");
    });
}
