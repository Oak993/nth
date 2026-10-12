using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.Core.Models;

// PORT-ONLY: web surfaces W4, the terminal's drawer lifted into the grid (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.1 "Terminal |
// drawer | under the chat column | stays the chat's drawer by default; can be dragged into the grid", 3.6 "The terminal can be
// dragged out of its drawer into the grid and back"; ED-NEW-web-w4-1). No Swift counterpart. W3b's re-dock lifts a card of the
// grid (BeginRedock, PaneRedock.Targets); a grid terminal goes back to its drawer through one more target of that re-dock
// (PaneDropKind.Drawer). The other direction starts here: the drawer has its place in the chat column and no card, so it lifts as a
// ghost (a card with the terminal's header and an empty body) that follows the pointer like a lifted card, over the same outlines
// (PaneRedock.DrawerTargets); a release springs the ghost into the cell the real pane then fades in at, and a release over nothing
// or Esc springs it back and away. The lift, the outlines, the hot target and Esc are W3b's (Lift, Follow, OnRedockKey).

namespace Marrow.App.Views.Panes;

public sealed partial class PaneWorkspace
{
    private CardMotion? ghost;

    /// <summary>The terminal drawer's stored height, where a drop back to the drawer is outlined. Defaults to the drawer's own 260.</summary>
    public Func<double>? DrawerHeight { get; set; }

    private double DrawerHeightNow => DrawerHeight?.Invoke() ?? PaneRedock.DefaultDrawerHeight;

    /// <summary>The ghost of a drawer being lifted (or springing away), or null.</summary>
    internal PaneCard? DrawerGhost => ghost?.Card;

    /// <summary>
    /// Whether the terminal drawer of <paramref name="from"/> (the thread's workspace state) can open the grid in this workspace's
    /// size (below it the workspace would be narrow tabs).
    /// </summary>
    internal bool CanDockTerminalToGrid(PaneWorkspaceState from) => PaneRedock.CanOpenInGrid(from, SolverWidth, SolverHeight);

    /// <summary>
    /// Lifts the terminal's <paramref name="drawer"/> (3.6) with the pointer at <paramref name="pointer"/> (workspace coordinates). False
    /// when nothing can be lifted now: a narrow or expanded workspace, no layout yet, the terminal not in its drawer, or a lift already
    /// running.
    /// </summary>
    internal bool BeginDrawerRedock(Point pointer, Control drawer)
    {
        if (redock is not null || dragging || solved is not { } layout || !PaneRedock.CanLiftFromDrawer(Drawn(), layout)) return false;
        if (drawer.TranslatePoint(default, this) is not { } corner || drawer.Bounds is not { Width: > 0, Height: > 0 } size) return false;
        var targets = PaneRedock.DrawerTargets(Drawn(), layout);
        if (targets.Count == 0) return false; // nowhere to drop it (a window too tight for the terminal beside the chat): nothing to lift
        SettleAll();
        RemoveGhost();
        var card = new PaneCard(PaneKind.Terminal, new PaneContent(new Border(), PaneKinds.Title(PaneKind.Terminal))) { IsHitTestVisible = false };
        card.Header.CanExpand = false;
        AutomationProperties.SetAccessibilityView(card, AccessibilityView.Raw); // a copy of the drawer's chrome: the drawer is what a screen reader has
        var motion = new CardMotion(card) { Placed = true, Visible = true };
        motion.SnapRect(new Rect(corner, size.Size));
        motion.Opacity.Snap(1);
        motion.Scale.Snap(1);
        ghost = motion;
        Children.Add(card);
        var current = redock = new Redock(PaneKind.Terminal, motion, pointer) { Pointer = pointer, FromDrawer = true };
        redocking = true;
        var top = TopBand;
        foreach (var target in targets)
        {
            var visual = new PaneDropVisual(target);
            var slot = new DropSlot(target, new Rect(target.Bounds.X, target.Bounds.Y + top, target.Bounds.Width, target.Bounds.Height), visual);
            Canvas.SetLeft(visual, slot.Bounds.X);
            Canvas.SetTop(visual, slot.Bounds.Y);
            dropSlots.Add(slot);
            current.Slots.Add(slot);
            dropLayer.Children.Add(visual);
        }
        UpdateHeld();
        keyTop?.AddHandler(KeyDownEvent, OnRedockKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        var ready = BeginWebGesture();
        if (ready.IsCompleted) Lift(current);
        else _ = ready.ContinueWith(_ => Dispatcher.UIThread.Post(() => Lift(current)), TaskScheduler.Default);
        return true;
    }

    /// <summary>The pointer moved with the drawer lifted: the ghost follows it 1:1 and the target under it lights.</summary>
    internal void MoveDrawerRedock(Point pointer)
    {
        if (redock is not { FromDrawer: true } current || current.Ended) return;
        current.Pointer = pointer;
        if (current.Lifted) Follow(current);
    }

    /// <summary>The pointer was released (or lost): over a target the drawer's terminal opens there, over nothing the ghost goes home.</summary>
    internal void ReleaseDrawerRedock(bool drop)
    {
        if (redock is { FromDrawer: true }) EndRedock(PaneKind.Terminal, drop);
    }

    // The ghost springs on from where it was let go to the cell the real pane fades in at, or home when nothing took it.
    private void EndDrawerRedock(Redock current, bool drop)
    {
        var reduce = ReduceMotion;
        foreach (var slot in current.Slots)
        {
            slot.Fading = true;
            slot.Appear.SetTarget(0, reduce);
            slot.HotSpring.SetTarget(0, reduce);
        }
        var motion = current.Motion;
        var dropped = false;
        if (current.Lifted && drop && current.Hot is { } hot)
        {
            var layout = Shown(motion);
            motion.SnapRect(new Rect(layout.X + motion.DragX.Value, layout.Y + motion.DragY.Value, layout.Width, layout.Height));
            motion.DragX.Snap(0);
            motion.DragY.Snap(0);
            redocking = false;
            dropped = Commit(hot.Target.Result);
            if (dropped && PaneLayout.Solve(Drawn(), SolverWidth, SolverHeight).BoundsOf(PaneKind.Terminal) is { } cell)
            {
                motion.SetRect(new Rect(cell.X, cell.Y + TopBand, cell.Width, cell.Height), reduce);
                motion.Scale.SetTarget(1, reduce);
                motion.Opacity.SetTarget(0, reduce);
            }
            else
            {
                dropped = false;
                motion.SnapRect(layout);
            }
        }
        if (!dropped)
        {
            redocking = false;
            motion.DragX.SetTarget(0, reduce);
            motion.DragY.SetTarget(0, reduce);
            motion.Scale.SetTarget(1, reduce);
            motion.Opacity.SetTarget(0, reduce);
        }
        dirty = true;
        InvalidateMeasure();
        StartFrames();
        UpdateHeld();
        PostRemoval();
    }

    // The ghost's looks with the cards' (opacity, scale, where the pointer has taken it), above everything.
    private void ApplyGhostLooks()
    {
        if (ghost is not { } motion) return;
        motion.Card.Opacity = Math.Clamp(motion.Opacity.Value, 0, 1);
        motion.Card.Scale = Math.Clamp(motion.Scale.Value, 0.9, 1.05);
        motion.Card.Offset = new Vector(motion.DragX.Value, motion.DragY.Value);
        motion.Card.ZIndex = 40;
    }

    // Everything has landed: a ghost that has finished springing, with no lift running, goes.
    private void FinishGhost()
    {
        if (ghost is not null && redock is not { FromDrawer: true } && !ghost.IsMoving) RemoveGhost();
    }

    private void RemoveGhost()
    {
        if (ghost is not { } motion) return;
        ghost = null;
        motion.Card.Detach();
        Children.Remove(motion.Card);
    }
}
