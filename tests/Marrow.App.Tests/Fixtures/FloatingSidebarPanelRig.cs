using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// Rig for the FloatingSidebarPanel tests (App/Views/Sidebar/FloatingSidebarPanel.swift:9-127): the panel built over a chat
// area of a given size in a shown window, with the app model as its environment and as WindowManager.shared's model (the
// launch harness installs it), driven through the pointer the way a user does: the headless mouse for the strip and the
// title, a second (touch) pointer for the grip, so a strip drag and a grip drag can be held at the same time. Helper
// functions only; every test body lives in a test class.
public sealed class FloatingSidebarPanelRig : IDisposable
{
    private readonly LaunchHarness launch;
    private readonly IDisposable frameScope;
    private readonly IDisposable cursorScope;
    private AvaloniaWindow window = null!;
    private readonly Pointer touch = new(Pointer.GetNextFreeId(), PointerType.Touch, false);
    private Point mouseDown;
    private Point touchDown;
    private DragHandleView? touchTarget;

    /// <summary>Builds the panel over a chat area <paramref name="area"/> in a shown window (as large as the area unless
    /// <paramref name="windowSize"/> says otherwise) with the given theme.</summary>
    public FloatingSidebarPanelRig(LaunchHarness launch, Size area, bool canClose = true, ThemeVariant? theme = null,
        Point? origin = null, Size? size = null, Action? close = null, Size? windowSize = null)
    {
        this.launch = launch;
        frameScope = FrameClock.Source.Override(Frames);
        // The drag handle re-reads the real pointer on release.
        cursorScope = DragHandleView.CursorPlatform.Override(new FixedCursorPlatform(null));
        Model = launch.Manager.Model;
        launch.Harness.Main.OnMain(() =>
        {
            Model.Settings.SidebarPanelOrigin = origin;
            Model.Settings.SidebarPanelSize = size;
            Panel = new FloatingSidebarPanel(area, canClose, () =>
            {
                Closed++;
                close?.Invoke();
            });
            var host = new Border { Child = Panel };
            AppEnvironment.SetAppModel(host, Model);
            var shown = windowSize ?? area;
            window = new AvaloniaWindow { Content = host, Width = shown.Width, Height = shown.Height };
            if (theme is not null) window.RequestedThemeVariant = theme;
            window.Show();
            Layout();
        });
    }

    /// <summary>The frame source the panel's hover fades and slides run on, driven by hand.</summary>
    public ManualFrameSource Frames { get; } = new();

    public AppModel Model { get; }

    public FloatingSidebarPanel Panel { get; private set; } = null!;

    /// <summary>How many times the panel's close action ran.</summary>
    public int Closed { get; private set; }

    public AvaloniaWindow Window => window;

    public void Layout()
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    /// <summary>The window's theme variant changes, the way the appearance setting does.</summary>
    public void SetTheme(ThemeVariant theme)
    {
        launch.Harness.Main.OnMain(() =>
        {
            window.RequestedThemeVariant = theme;
            Layout();
        });
    }

    /// <summary>Lets every running animation finish: time moves on, frames are delivered, the layout settles.</summary>
    public void Settle()
    {
        for (var step = 0; step < 5; step++)
        {
            launch.Harness.Main.Advance(TimeSpan.FromSeconds(0.5));
            launch.Harness.Main.OnMain(() => Frames.Advance(TimeSpan.FromSeconds(0.5))); // frames run on the main actor, as in the app
            Layout();
        }
    }

    public T[] All<T>() where T : class => RootViewRig.All<T>(Panel);

    /// <summary>The panel's own glass surface: the rounded, clipped, tinted layer holding the strip and the sidebar.</summary>
    public Border Surface => Assert.Single(All<Border>(), border =>
        border.CornerRadius == new CornerRadius(22) && border.ClipToBounds && Marrow.UI.Port.SwiftGlass.GetEffect(border) is not null);

    /// <summary>The backing plate behind the surface, the one that casts the shadow (its caster's source, ED-358).</summary>
    public Border Plate => Shadow.Source;

    /// <summary>The one shadow the backing plate casts: its SwiftShadow.Caster, drawn under it (no Border carries a box shadow).</summary>
    public Marrow.UI.Port.SwiftShadow.Caster Shadow
    {
        get
        {
            // The panel's own borders carry none; the only box shadows are the shared splitters' grip pills and their Resize
            // and readout chips (web W1, the canvas's `.spl .grip` and `.spl .tip`), each inside its Splitter.
            Assert.All(All<Border>().Where(border => border.BoxShadow.Count > 0), border =>
                Assert.NotNull(Avalonia.LogicalTree.LogicalExtensions.FindLogicalAncestorOfType<Splitter>(border)));
            return Assert.Single(All<Marrow.UI.Port.SwiftShadow.Caster>());
        }
    }

    /// <summary>The surface's rectangle in the chat area: where the panel is, as it is drawn.</summary>
    public Rect Frame => RootViewRig.In(Surface, Panel);

    /// <summary>A control's rectangle in the surface's coordinate space.</summary>
    public Rect InSurface(Control control) => RootViewRig.In(control, Surface);

    public TextBlock Title => RootViewRig.Text(Panel, "Threads");

    /// <summary>The handle with the given tooltip (the strip: "Drag to move").</summary>
    public PanelDragHandle HandleTipped(string tip) =>
        Assert.Single(All<PanelDragHandle>(), handle => ToolTip.GetTip(handle) as string == tip);

    public PanelDragHandle StripHandle => HandleTipped("Drag to move");

    /// <summary>The bottom-right corner grip, the shared Splitter (web W1).</summary>
    public Splitter Grip => Assert.Single(All<Splitter>(), splitter => splitter.Orientation == SplitterOrientation.Both);

    /// <summary>The right edge's grip (web W1).</summary>
    public Splitter RightGrip => Assert.Single(All<Splitter>(), splitter => splitter.Orientation == SplitterOrientation.Vertical);

    /// <summary>The bottom edge's grip (web W1).</summary>
    public Splitter BottomGrip => Assert.Single(All<Splitter>(), splitter => splitter.Orientation == SplitterOrientation.Horizontal);

    /// <summary>The handle laid over the title capsule (it has no tooltip of its own).</summary>
    public PanelDragHandle TitleHandle => Assert.Single(All<PanelDragHandle>(), handle => ToolTip.GetTip(handle) is null);

    public Point Centre(Control control)
    {
        var rect = RootViewRig.In(control, window);
        return new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    // ---- the mouse ----

    public void MousePress(Control target)
    {
        mouseDown = Centre(target);
        launch.Harness.Main.OnMain(() =>
        {
            window.MouseDown(mouseDown, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        });
    }

    public void MouseMoveBy(double dx, double dy)
    {
        launch.Harness.Main.OnMain(() =>
        {
            window.MouseMove(new Point(mouseDown.X + dx, mouseDown.Y + dy), RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Layout();
        });
    }

    public void MouseReleaseBy(double dx, double dy)
    {
        launch.Harness.Main.OnMain(() =>
        {
            window.MouseUp(new Point(mouseDown.X + dx, mouseDown.Y + dy), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Layout();
        });
    }

    /// <summary>Press on <paramref name="target"/>, move by (dx, dy) and release there: one whole drag.</summary>
    public void Drag(Control target, double dx, double dy)
    {
        MousePress(target);
        MouseMoveBy(dx, dy);
        MouseReleaseBy(dx, dy);
    }

    /// <summary>A plain click with the mouse at the control's centre.</summary>
    public void Click(Control target)
    {
        var at = Centre(target);
        launch.Harness.Main.OnMain(() =>
        {
            window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        });
    }

    // ---- a second pointer ----

    public void TouchPress(Control target)
    {
        touchTarget = target is Splitter splitter ? splitter.Handle : Assert.IsType<DragHandleView>(Assert.IsType<PanelDragHandle>(target).Child);
        touchDown = Centre(target);
        launch.Harness.Main.OnMain(() =>
        {
            var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            touchTarget.RaiseEvent(new PointerPressedEventArgs(touchTarget, touch, window, touchDown, 0, properties, KeyModifiers.None));
            Dispatcher.UIThread.RunJobs();
        });
    }

    public void TouchMoveBy(double dx, double dy)
    {
        launch.Harness.Main.OnMain(() =>
        {
            var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);
            var at = new Point(touchDown.X + dx, touchDown.Y + dy);
            touchTarget!.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, touchTarget, touch, window, at, 0, properties, KeyModifiers.None));
            Dispatcher.UIThread.RunJobs();
            Layout();
        });
    }

    public void TouchReleaseBy(double dx, double dy)
    {
        launch.Harness.Main.OnMain(() =>
        {
            var properties = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
            var at = new Point(touchDown.X + dx, touchDown.Y + dy);
            touchTarget!.RaiseEvent(new PointerReleasedEventArgs(touchTarget, touch, window, at, 0, properties, KeyModifiers.None, MouseButton.Left));
            Dispatcher.UIThread.RunJobs();
            Layout();
        });
    }

    /// <summary>Press on <paramref name="target"/> with the second pointer, move by (dx, dy) and release there.</summary>
    public void TouchDrag(Control target, double dx, double dy)
    {
        TouchPress(target);
        TouchMoveBy(dx, dy);
        TouchReleaseBy(dx, dy);
    }

    public void Dispose()
    {
        launch.Harness.Main.OnMain(() =>
        {
            window.Hide();
            Dispatcher.UIThread.RunJobs();
        });
        frameScope.Dispose();
        cursorScope.Dispose();
    }
}
