using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.Core.Models;

// PORT-ONLY: web surfaces W4, the pointer side of lifting the terminal's drawer (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.6 "Press
// on a header's empty area and move 4 DIP: the pane lifts"; 4.2 touch and pen: "pointer capture on press ... slop 4 DIP so a tap is
// not a drag"; ED-NEW-web-w4-1). No Swift counterpart. A grid pane's header lifts it through PaneHeader's own drag events (W3b); the
// drawer's header is the terminal panel's and has none, so this watches it: a press on its empty area (not a button, a tab or a scroll
// bar) arms it, travel past the slop lifts the drawer in the workspace (PaneWorkspace.BeginDrawerRedock) and captures the pointer,
// every move follows, and the release lands it. A lift that cannot start (a narrow or expanded workspace) never does, so the press
// stays an ordinary press.

namespace Marrow.App.Views.Panes;

/// <summary>Lifts the terminal's drawer from its header's empty area.</summary>
internal sealed class DrawerLiftGesture
{
    private readonly Control header;
    private readonly Func<PaneWorkspace?> workspace;
    private readonly Func<Control> drawer;
    private PaneWorkspace? armedIn;
    private IPointer? pointer;
    private Point press;
    private bool lifted;

    private DrawerLiftGesture(Control header, Func<PaneWorkspace?> workspace, Func<Control> drawer)
    {
        this.header = header;
        this.workspace = workspace;
        this.drawer = drawer;
    }

    /// <summary>
    /// Watches <paramref name="header"/>, the drawer\'s header row. <paramref name="workspace"/> finds the workspace to lift in (null: the
    /// drawer is not in one); <paramref name="drawer"/> is the drawer itself, whose rectangle the ghost starts from.
    /// </summary>
    public static IDisposable Attach(Control header, Func<PaneWorkspace?> workspace, Func<Control> drawer)
    {
        var gesture = new DrawerLiftGesture(header, workspace, drawer);
        header.AddHandler(InputElement.PointerPressedEvent, gesture.OnPressed, RoutingStrategies.Bubble);
        header.AddHandler(InputElement.PointerMovedEvent, gesture.OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        header.AddHandler(InputElement.PointerReleasedEvent, gesture.OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        header.AddHandler(InputElement.PointerCaptureLostEvent, gesture.OnCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        return new Detach(() =>
        {
            header.RemoveHandler(InputElement.PointerPressedEvent, gesture.OnPressed);
            header.RemoveHandler(InputElement.PointerMovedEvent, gesture.OnMoved);
            header.RemoveHandler(InputElement.PointerReleasedEvent, gesture.OnReleased);
            header.RemoveHandler(InputElement.PointerCaptureLostEvent, gesture.OnCaptureLost);
        });
    }

    /// <summary>Whether a press that began at <paramref name="source"/> is on the header\'s empty area (nothing that takes a press itself).</summary>
    internal static bool IsEmptyArea(Control header, object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, header)) return true;
            if (visual is Button or ScrollBar or Thumb or TextBox or ToggleButton || visual.GetType().Name == "TerminalTab") return false;
        }
        return true;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        Reset();
        var point = e.GetCurrentPoint(header);
        if (!point.Properties.IsLeftButtonPressed || e.KeyModifiers != KeyModifiers.None || !IsEmptyArea(header, e.Source)) return;
        if (workspace() is not { } host) return;
        armedIn = host;
        pointer = e.Pointer;
        press = e.GetPosition(host);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (armedIn is not { } host || !ReferenceEquals(e.Pointer, pointer)) return;
        var position = e.GetPosition(host);
        if (!lifted)
        {
            if (!e.GetCurrentPoint(header).Properties.IsLeftButtonPressed)
            {
                Reset();
                return;
            }
            var travel = position - press;
            if (Math.Sqrt(travel.X * travel.X + travel.Y * travel.Y) < PaneRedock.LiftDistance) return;
            if (!host.BeginDrawerRedock(press, drawer()))
            {
                Reset();
                return;
            }
            lifted = true;
            e.Pointer.Capture(header);
        }
        host.MoveDrawerRedock(position);
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (armedIn is not { } host || !ReferenceEquals(e.Pointer, pointer)) return;
        var wasLifted = lifted;
        if (wasLifted)
        {
            host.MoveDrawerRedock(e.GetPosition(host));
            host.ReleaseDrawerRedock(drop: true);
            e.Handled = true;
        }
        Reset();
        if (wasLifted) e.Pointer.Capture(null);
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (armedIn is not { } host || !ReferenceEquals(e.Pointer, pointer)) return;
        var wasLifted = lifted;
        Reset();
        if (wasLifted) host.ReleaseDrawerRedock(drop: false);
    }

    private void Reset()
    {
        armedIn = null;
        pointer = null;
        lifted = false;
    }

    private sealed class Detach(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
