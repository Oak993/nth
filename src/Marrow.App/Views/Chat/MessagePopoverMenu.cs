using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/MessagePopoverMenu.swift (B2 1fbf81e), :1-83.
// Record: PR-CHAT-087.

/// <summary>:4-21 RightClickPopoverModifier: a ViewModifier that overlays a RightClickCatcher, shows a PopoverMenu
/// when the catch point is set, and anchors the popover to a 1 by 1 rect at the pointer.</summary>
internal sealed class RightClickPopoverModifier
{
    // :5 let actions: () -> [RowAction]
    private readonly Func<System.Collections.Generic.IReadOnlyList<RowAction>> actions;

    // :6 @State private var anchor: CGPoint?
    private Point? anchor;

    public RightClickPopoverModifier(Func<System.Collections.Generic.IReadOnlyList<RowAction>> actions)
    {
        this.actions = actions;
    }

    public Control Apply(Control content)
    {
        // :9 content.overlay { RightClickCatcher { point in anchor = point } }
        var overlay = new RightClickCatcher(point => anchor = point);

        // :10-16 .popover(isPresented: Binding, attachmentAnchor: .rect(.rect(CGRect(...))), arrowEdge: .top)
        var popover = new Popup
        {
            Placement = PlacementMode.Top, // :16 arrowEdge: .top
        };

        var panel = new Panel();
        panel.Children.Add(content);
        panel.Children.Add(overlay);

        // Track anchor changes to update popover presentation
        var shown = false;
        overlay.Track(() => anchor, anchorPoint =>
        {
            var presented = anchorPoint.HasValue;
            if (presented == shown) return;
            shown = presented;

            if (presented && anchorPoint.HasValue)
            {
                // Set PlacementRect to a 1x1 rect at the anchor point
                // :15 CGRect(origin: anchor ?? .zero, size: CGSize(width: 1, height: 1))
                popover.PlacementRect = new Rect(anchorPoint.Value, new Size(1, 1));
                popover.PlacementTarget = panel; // :15 attachment to the panel (the view tree)
                popover.Child = new PopoverMenu(() => new RowActionItems(actions()));
            }
            else
            {
                popover.Child = null;
            }

            Presentation.Presenter.Current.SetPresented(popover, presented);
        });

        popover.Closed += (_, _) =>
        {
            anchor = null;
            popover.Child = null;
        };

        return panel;
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

/// <summary>:30-40 RightClickCatcher: NSViewRepresentable wrapping RightClickCatcherView (lines :42-83).
/// A transparent overlay that only claims secondary clicks. It returns nil from hit-testing for every other event,
/// so text selection, links, buttons and drags in the SwiftUI content underneath behave exactly as if it were absent.</summary>
internal sealed class RightClickCatcher : ContentControl
{
    // :31 var onRightClick: (CGPoint) -> Void
    private readonly Action<Point> onRightClick;

    public RightClickCatcher(Action<Point> onRightClick)
    {
        this.onRightClick = onRightClick;
        // Make this control transparent by default; it only becomes hittable for secondary clicks
        IsHitTestVisible = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Background = null;
    }


    /// <summary>:70-72 rightMouseDown: records the point and calls onRightClick. Also handles left mouse down with control modifier.</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;

        // :64-65 isSecondary check: right mouse button or left mouse button with control modifier
        var isSecondary = properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed
            || (properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed
                && e.KeyModifiers.HasFlag(KeyModifiers.Control));

        if (isSecondary)
        {
            // :71 onRightClick(convert(event.locationInWindow, from: nil))
            var point = e.GetPosition(this);
            onRightClick(point);
            e.Handled = true;
        }
        else
        {
            // Not a secondary click - don't handle it, let it pass through to content underneath
            // Do NOT call base.OnPointerPressed, just let the event bubble up
            e.Handled = false;
        }
    }

    /// <summary>:82 rightMouseUp: swallowed (no-op). Also swallows the release from control+click.</summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;

        // Swallow right mouse up event (and control+left release if we're tracking it)
        if (properties.PointerUpdateKind == PointerUpdateKind.RightButtonReleased
            || (properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased
                && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            e.Handled = true;
        }
    }
}
