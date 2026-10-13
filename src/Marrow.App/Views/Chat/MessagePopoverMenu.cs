using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
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
            IsLightDismissEnabled = true,
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

        // :6 @State private var anchor and :13-18 .popover(isPresented:) both belong to the row's own view: when the row
        // leaves the hierarchy (a block of the timeline going, a thread closing) its state goes, and the popover with it.
        // The popover must not hold the row as its target after that, or an open menu would pin the row, its entry and its
        // text (TimelineRows.swift:56-62, :165-178; PR-CHAT-161). ENGINEERING DECISION ED-256: a row that stays in the timeline
        // keeps its @State (the 1.7.0 timeline is an eager VStack, ThreadTimeline.swift:384-391), so a row that is merely
        // moved is not gone. ThreadTimeline.Draw takes every kept row out of its old stack and puts it into a new one on
        // each change of the thread, inside one synchronous pass: the row is gone only if it is still out of the tree once
        // that pass is over (the same reading of onDisappear as BadgePopoverCoordinator.Disappear).
        content.DetachedFromVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (content.IsAttachedToVisualTree()) return;
            popover.PlacementTarget = null;
            if (!shown) return;
            shown = false;
            popover.Child = null;
            Presentation.Presenter.Current.SetPresented(popover, false);
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

        // PORT-ONLY (web surfaces W10, DESIGN 5.11, ED-NEW-web-w10-2): a Ctrl+click on a link in the prose belongs to the link ("Ctrl opens the
        // other browser"), not to the message menu; the menu stays for a right click and for a Ctrl+click anywhere else in the message.
        if (isLeftWithControl && OverChatLink(e)) return;

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

        // :62-67 hitTest gave the press to the catcher view, and AppKit sends the rest of that click (its drags and its
        // release, swallowed at :81) to the same view. The mouse device has already captured the pointer for the element
        // under it, where a SelectableTextBlock extends a selection from a stale start on the next move with the left
        // button down; capturing it for the catcher routes the moves and the release to the catcher instead.
        e.Pointer.Capture(content);
    }

    /// <summary>:82 rightMouseUp: swallowed (no-op). Also swallows the release from control+click.</summary>
    private static void HandlePointerReleased(Control content, PointerReleasedEventArgs e)
    {
        var properties = e.GetCurrentPoint(content).Properties;

        // Swallow right mouse up event (and control+left release if we're tracking it)
        if (properties.PointerUpdateKind == PointerUpdateKind.RightButtonReleased
            || (properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased
                && e.KeyModifiers.HasFlag(KeyModifiers.Control) && !OverChatLink(e)))
        {
            e.Handled = true;
        }
    }

    // PORT-ONLY (web surfaces W10): the event is over a link of chat prose, where the chat has a link hook (set above its timeline).
    private static bool OverChatLink(PointerEventArgs e) =>
        e.Source is Visual source && source.FindAncestorOfType<Marrow.UI.Markdown.LinkTextView>(includeSelf: true) is { } view
        && Marrow.UI.Markdown.MarkdownView.GetChatLinkAction(view) is not null && view.IsOverLink(e);
}
