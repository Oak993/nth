using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/MessagePopoverMenu.swift (B3 52bc792), :1-83.
// Record: PR-CHAT-087.

/// <summary>:4-21 RightClickPopoverModifier: a ViewModifier that wraps content with a RightClickCatcher,
/// shows a PopoverMenu when the catch point is set, and anchors the popover to a 1 by 1 rect at the pointer.
/// ED-183: RightClickCatcher is now a tunnel-phase event handler on the content, not an overlay.</summary>
internal sealed class RightClickPopoverModifier
{
    // :5 let actions: () -> [RowAction]
    private readonly Func<System.Collections.Generic.IReadOnlyList<RowAction>> actions;

    public RightClickPopoverModifier(Func<System.Collections.Generic.IReadOnlyList<RowAction>> actions)
    {
        this.actions = actions;
    }

    public Control Apply(Control content)
    {
        // :10-16 .popover(isPresented: Binding, attachmentAnchor: .rect(.rect(CGRect(...))), arrowEdge: .top)
        var popover = new Popup
        {
            Placement = PlacementMode.Top, // :16 arrowEdge: .top
        };

        var shown = false;

        // ED-183: Attach the RightClickCatcher event handler directly to the content as a tunnel-phase handler.
        // No overlay, no custom hit test.
        RightClickCatcher.AttachTo(content, point =>
        {
            // :9 anchor = point
            // :9,11 Every click replaces the anchor and re-evaluates actions, including while open.
            popover.PlacementRect = new Rect(point, new Size(1, 1));
            popover.PlacementTarget = content;
            popover.Child = new PopoverMenu(() => new RowActionItems(actions()));
            if (!shown)
            {
                shown = true;
                Presentation.Presenter.Current.SetPresented(popover, true);
            }
        });

        // :14 .popover isPresented: anchor == nil closes the popover
        popover.Closed += (_, _) =>
        {
            shown = false;
            popover.Child = null;
            Presentation.Presenter.Current.SetPresented(popover, false);
        };

        return content;
    }
}

/// <summary>Extension method on Control to apply RightClickPopoverModifier.</summary>
public static class RightClickPopoverExtensions
{
    /// <summary>:25-27 rightClickPopover(actions:) -> some View: applies RightClickPopoverModifier.</summary>
    public static Control RightClickPopover(this Control view, Func<System.Collections.Generic.IReadOnlyList<RowAction>> actions)
    {
        var modifier = new RightClickPopoverModifier(actions);
        return modifier.Apply(view);
    }
}

/// <summary>:30-40 RightClickCatcher: implements the tunnel-phase event handler behavior from RightClickCatcherView :42-83.
/// ED-183: No longer an overlay Control. Instead, a static helper that attaches a tunnel-phase PointerPressedEvent
/// handler directly to the content. The handler calls onRightClick only for a right mouse button press or a left
/// mouse button press with the Control modifier, both inside content bounds, and sets e.Handled = true. All other
/// presses pass through unhandled, so text selection, links, buttons and drags underneath behave exactly as if it
/// were absent. :82 rightMouseUp is also swallowed (handled).</summary>
internal static class RightClickCatcher
{
    /// <summary>Attaches a tunnel-phase event handler to the given content that calls onRightClick for secondary clicks.
    /// Secondary clicks are: right mouse button, or left mouse button with Control modifier. The handler only claims
    /// events that occur inside the content's bounds. Also swallows the corresponding release events.</summary>
    public static void AttachTo(Control content, Action<Point> onRightClick)
    {
        // PR-CHAT-087 windows_path: the Windows context-menu key is a secondary trigger.
        content.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Apps) return;
            onRightClick(new Point(content.Bounds.Width / 2, content.Bounds.Height / 2));
            e.Handled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        // :30-40 RightClickCatcher NSViewRepresentable wrapping RightClickCatcherView
        // ED-183: Attach the handler as a tunnel-phase PointerPressedEvent handler on the content.
        content.AddHandler(InputElement.PointerPressedEvent, (sender, e) =>
        {
            HandlePointerPressed(content, e, onRightClick);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        // :82 rightMouseUp: also swallow the release event
        content.AddHandler(InputElement.PointerReleasedEvent, (sender, e) =>
        {
            HandlePointerReleased(content, e);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>:62-68 hitTest: returns self only for secondary clicks inside bounds, nil for all other events.
    /// ED-183: Implemented as the event handler logic. Returns true (event is handled) only for secondary clicks
    /// inside bounds; otherwise returns false (event passes through).</summary>
    private static void HandlePointerPressed(Control content, PointerPressedEventArgs e, Action<Point> onRightClick)
    {
        var properties = e.GetCurrentPoint(content).Properties;

        // :64-65 isSecondary = event.type == .rightMouseDown || (event.type == .leftMouseDown && event.modifierFlags.contains(.control))
        var isRightButton = properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed;
        var isLeftWithControl = properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed
            && e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var isSecondary = isRightButton || isLeftWithControl;

        // :66 guard isSecondary, bounds.contains(convert(point, from: superview)) else { return nil }
        if (!isSecondary)
        {
            // Plain left click, middle click, shift+left, etc. all pass through unhandled.
            return;
        }

        // Check if the click is inside the content's bounds
        var point = e.GetPosition(content);
        if (!content.Bounds.Contains(point))
        {
            // Click is outside the content bounds; let it pass through
            return;
        }

        // :70-71 rightMouseDown: onRightClick(convert(event.locationInWindow, from: nil))
        // :90-95 mouseDown with control: also calls onRightClick
        onRightClick(point);

        // ED-183: Set e.Handled = true to prevent the event from reaching the underlying content
        e.Handled = true;
    }

    /// <summary>:82 rightMouseUp: swallowed (no-op). Also swallows the release from control+click.</summary>
    private static void HandlePointerReleased(Control content, PointerReleasedEventArgs e)
    {
        var properties = e.GetCurrentPoint(content).Properties;

        // Swallow right mouse up event (and control+left release if we're tracking it)
        if (properties.PointerUpdateKind == PointerUpdateKind.RightButtonReleased
            || (properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased
                && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            e.Handled = true;
        }
    }
}
