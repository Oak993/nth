using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// Source under test: DroppyCode/UI/Chrome/DragHandle.swift (B3 52bc792). The records PR-TERM-017 and PR-TERM-018 cite
// TerminalPanel.swift lines of an earlier pin (B1); the same behaviour lives in DragHandle.swift in the pinned source,
// so those are the lines cited here: applyColors and viewDidChangeEffectiveAppearance (:91-102), acceptsFirstMouse and
// mouseDownCanMoveWindow (:183-186).
[Collection("Headless")]
public sealed class DragHandleSecondTests(HeadlessSession headless)
{
    private static (Window Window, DragHandleView View) Attached(ThemeVariant theme)
    {
        var view = new DragHandleView
        {
            Width = 20,
            Height = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        view.Configure(new DragHandleView.Grip(RestLength: 40, ShownLength: 80, Thickness: 4, GlowInset: 10));
        var window = new Window { Content = view, Width = 400, Height = 300, RequestedThemeVariant = theme };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static void AssertLabelColour(IBrush? brush, byte channel)
    {
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        // NSColor.labelColor at 0.85 (ED-051): black in a light appearance, white in a dark one.
        Assert.Equal((channel, channel, channel), (solid.Color.R, solid.Color.G, solid.Color.B));
        Assert.InRange(solid.Color.A, 216, 218);
    }

    [Fact]
    [Trait("Req", "PR-TERM-017")] // DragHandle.swift:91-102 the grip and glow take the label colour of the effective appearance and follow it
    public void The_grip_and_glow_are_dark_on_light_and_light_on_dark_and_repaint_when_the_appearance_changes() => headless.OnUiThread(() =>
    {
        using var cursor = DragHandleView.CursorPlatform.Override(new FakeCursorPlatform(null)); // nothing reads the real pointer
        var (window, view) = Attached(ThemeVariant.Light);
        try
        {
            AssertLabelColour(view.GripLayerForTest.Background, 0);
            AssertLabelColour(view.GlowLayerForTest.Background, 0);

            window.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            AssertLabelColour(view.GripLayerForTest.Background, 255);
            AssertLabelColour(view.GlowLayerForTest.Background, 255);

            window.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            AssertLabelColour(view.GripLayerForTest.Background, 0);
            AssertLabelColour(view.GlowLayerForTest.Background, 0);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-TERM-018")] // DragHandle.swift:183-186 the strip answers the first click and never acts as window-drag background
    public void The_strip_takes_the_first_click_and_is_declared_client_area_so_it_never_moves_the_window() => headless.OnUiThread(() =>
    {
        using var cursor = DragHandleView.CursorPlatform.Override(new FakeCursorPlatform(null)); // nothing reads the real pointer
        var (window, view) = Attached(ThemeVariant.Light);
        try
        {
            Assert.True(view.AcceptsFirstMouse(null));
            Assert.False(view.MouseDownCanMoveWindow);
            // Windows drags a window only from a region declared as caption; the strip is declared as plain client area.
            Assert.Equal(Win32Properties.Win32HitTestValue.Client, Win32Properties.GetNonClientHitTestResult(view));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-TERM-018")] // DragHandle.swift:183, :198-208, :210-232 one press-and-drag motion both begins and carries the drag, with no earlier click (a headless window is always active, so the unfocused window itself cannot be set up)
    public void A_single_press_and_drag_motion_on_the_strip_begins_and_reports_the_drag() => headless.OnUiThread(() =>
    {
        using var cursor = DragHandleView.CursorPlatform.Override(new FakeCursorPlatform(null)); // nothing reads the real pointer
        var (window, view) = Attached(ThemeVariant.Light);
        try
        {
            var begun = 0;
            var ended = 0;
            var travel = new List<Size>();
            view.OnBegin = () => begun++;
            view.OnChange = size => travel.Add(size);
            view.OnEnd = () => ended++;

            window.MouseDown(new Point(10, 100), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, begun); // the press itself begins the drag: no second click is needed
            window.MouseMove(new Point(35, 100), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(35, 100), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, begun);
            Assert.Equal(1, ended);
            Assert.Equal(25, travel[0].Width);
            Assert.Equal(25, travel[^1].Width);
        }
        finally { window.Close(); }
    });
}
