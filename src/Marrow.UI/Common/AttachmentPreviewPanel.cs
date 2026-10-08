using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Reactive;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.UI.Common;

// Source: DroppyCode/UI/Common/AttachmentPreviewPanel.swift (B3 52bc792), :1-393.
// Records: PR-COMPOSER-069, PR-COMPOSER-070, PR-COMPOSER-071, PR-COMPOSER-072.
// Forms (ED-095): NSPopover is an Avalonia Popup shown through the Marrow.UI.Port.Presentation seam
// (R-VIEW-9); NSView is Control; NSRect/CGRect/CGSize are Rect/Size; the file's NSViewRepresentable
// (AttachmentAnchorCapture) folds into one Control. Popovers.SetFixedContent (Chrome/Popovers.swift:15-41,
// already ported) installs the sized, non-resizing content.

/// A stand-in for AppKit's `NSRectEdge`, restricted to the two cases this file reads (:64, :89, :117).
/// `MaxY` opens the panel above the anchor; `MinY` opens it below (":63 .minY is below in these flipped
/// views" -- Avalonia is always top-left-origin, so both edges keep the same meaning here as in the
/// flipped SwiftUI views this file anchors to).
public enum PreviewPanelEdge
{
    MinY,
    MaxY,
}

/// One attachment preview panel per thumbnail strip. A single Avalonia Popup shared per strip and swapped
/// in place, so several photos in one strip never fight over separate popovers and any photo opens in a
/// single tap (:1-18). MainActor-isolated per R-ISO-1: every member here assumes it runs on the main
/// actor and performs no isolation check of its own.
///
/// The panel anchors to the tapped thumbnail's own view, so the arrow lands on the photo every time.
/// While open, tapping another photo swaps the content and re-shows at the new anchor. The panel is
/// application-defined (:35 `.applicationDefined`, ported as `IsLightDismissEnabled = false`): taps
/// elsewhere still reach their target and the panel closes on its own monitors instead.
public sealed class AttachmentPreviewCoordinator
{
    private readonly Popup popup = new();
    private WeakView? anchor; // :22
    private Rect? anchorRect; // :25
    private Guid? currentID; // :26
    private bool isShown; // Swift: popover.isShown, read at :65, :90, :154
    private bool monitorsActive; // :161 guard monitors.isEmpty else return
    private Window? monitorWindow;
    private IDisposable? pointerMonitor;
    private IDisposable? keyMonitor;
    private RoutedEventArgs? lastMonitoredEvent;

    public AttachmentPreviewCoordinator()
    {
        popup.IsLightDismissEnabled = false; // :35 popover.behavior = .applicationDefined: no automatic dismissal
        // :151-158 popoverDidClose: a defensive resync for a close this coordinator did not itself drive.
        // An owner/anchor can disappear without Close(). Read the actual popup state before resyncing the mirror.
        popup.Closed += (_, _) =>
        {
            if (popup.IsOpen) return; // :154 guard self.popover.isShown else return...
            isShown = false;
            StopMonitors();
            currentID = null;
        };
    }

    /// Test-only readback (InternalsVisibleTo, matching WindowChrome.cs's pattern): the Popup this
    /// coordinator drives, so tests can inspect its placement and content without a real presenter.
    internal Popup PopupForTests => popup;

    internal Guid? CurrentIDForTests => currentID;

    /// :42-44 setAnchor(_:). The strip's own view, captured from the strip's background. One stable view
    /// per strip: never a recycled row, never a stale id.
    public void SetAnchor(Control view) => anchor = new WeakView(view);

    /// :49-51 setAnchor(windowRect:). The strip's frame in the window's content view, for a strip that
    /// must not mount an anchor view of its own. A view anchor, when there is one, still wins.
    public void SetAnchor(Rect windowRect) => anchorRect = windowRect;

    /// :55-57 retire(except:). Forget the panel when its attachment left (e.g. removed from the draft
    /// while previewing).
    public void Retire(IReadOnlySet<Guid> except)
    {
        if (currentID is { } id && !except.Contains(id)) Close();
    }

    /// :64-70 toggle(_:over:edge:). `over` is the tapped thumbnail's own view when it has one.
    public void Toggle(Attachment attachment, Control? over = null, PreviewPanelEdge edge = PreviewPanelEdge.MaxY)
    {
        if (currentID == attachment.Id && isShown) Close(); // :65-66
        else Show(attachment, over, edge); // :67-68
    }

    /// :72-76 close().
    public void Close()
    {
        StopMonitors();
        currentID = null;
        if (!isShown) return;
        isShown = false;
        Presentation.Presenter.Current.SetPresented(popup, false); // :75 popover.performClose(nil)
    }

    // :78-109 show(_:over:edge:).
    private void Show(Attachment attachment, Control? view, PreviewPanelEdge edge)
    {
        Size? imageSize = attachment.IsImage ? AttachmentLargePreview.ImageDisplaySize(attachment) // :80
            : attachment.IsVideo ? AttachmentLargePreview.VideoDisplaySize(attachment) // :82
            : null; // :84
        var content = new AttachmentLargePreview(attachment, imageSize); // :86
        var size = ContentSize(attachment, imageSize); // :87
        currentID = attachment.Id; // :88, set before the anchor guard: a failed resolve still marks this as current
        if (AnchorTarget(view, edge) is not { } target) return; // :89
        // :90-98 the panel is sized here, once, and setFixedContent's host never publishes a size of its own.
        popup.SetFixedContent(content, size);
        popup.PlacementTarget = target.View;
        popup.PlacementRect = target.Rect;
        popup.Placement = edge == PreviewPanelEdge.MaxY ? PlacementMode.Top : PlacementMode.Bottom;
        // :99-107 ENGINEERING DECISION: the three-way isShown/has-a-window branch exists only to suppress
        // NSPopover's own reposition animation (`popover.animates = false`) when swapping content on an
        // already-materialized popover. Avalonia's Popup has no animation of its own to suppress here
        // (Chrome/Popovers.cs's Popover() shows one with none either), so all three branches collapse to
        // one unconditional presentation; every call still re-shows, matching "re-shows at the new anchor".
        isShown = true;
        Presentation.Presenter.Current.SetPresented(popup, true);
        StartMonitors(target.View); // :108
    }

    // :111-147 anchorTarget(thumbnailView:edge:). The view and rect to anchor the panel to, best proof
    // first: this click's own tap point inside the strip anchor, then the thumbnail's own bounds, then the
    // strip's bounds, then the window-rect anchor (again preferring the tap's x).
    private (Control View, Rect Rect)? AnchorTarget(Control? thumbnailView, PreviewPanelEdge edge)
    {
        if (anchor?.Value is { } strip && TopLevel.GetTopLevel(strip) is { } stripWindow &&
            strip.Bounds.Width >= 8 && strip.Bounds.Height >= 8 && // :119
            AppCurrentEvent.LastLeftMouseUp is { } last && ReferenceEquals(last.Root, stripWindow)) // :120-121
        {
            var point = ((Visual)last.Root).TranslatePoint(last.Position, strip) ?? default; // :122
            var x = Math.Min(Math.Max(point.X, 2), strip.Bounds.Width - 2); // :123
            return (strip, new Rect(x - 1, 0, 2, strip.Bounds.Height)); // :127
        }
        if (thumbnailView is { } tv && TopLevel.GetTopLevel(tv) is not null) // :130
            return (tv, new Rect(tv.Bounds.Size)); // :131
        if (anchor?.Value is { } strip2 && TopLevel.GetTopLevel(strip2) is not null) // :133
            return (strip2, new Rect(strip2.Bounds.Size)); // :134
        if (anchorRect is { } rect && WindowRectAnchor.Target(rect) is { } target) // :136
        {
            if (AppCurrentEvent.LastLeftMouseUp is { } last2 && ReferenceEquals(last2.Root, TopLevel.GetTopLevel(target.View))) // :139
            {
                var point = ((Visual)last2.Root).TranslatePoint(last2.Position, target.View) ?? default; // :140
                var x = Math.Min(Math.Max(point.X, target.Rect.X + 2), target.Rect.Right - 2); // :141
                return (target.View, new Rect(x - 1, target.Rect.Y, 2, target.Rect.Height)); // :142
            }
            return (target.View, target.Rect); // :144
        }
        return null; // :146
    }

    // :160-187 startMonitors(). A local mouse-down monitor, a local key-down monitor and window activation
    // and move notifications. Static Raised streams observe every app top-level (as AppCurrentEvent does);
    // the popup's own top-level is explicitly excluded (:193). NSWindow's three notifications (didResignKey,
    // willMoveNotification, didMoveNotification), which all only call close(), collapse to Avalonia's two
    // (Deactivated, PositionChanged) with the identical effect.
    private void StartMonitors(Control resolvedAnchor)
    {
        if (monitorsActive) return; // :161
        var window = (anchor?.Value is { } strip ? TopLevel.GetTopLevel(strip) : null) as Window // :172
            ?? TopLevel.GetTopLevel(resolvedAnchor) as Window;
        if (window is null) return;
        monitorWindow = window;
        pointerMonitor = InputElement.PointerPressedEvent.Raised.Subscribe(new AnonymousObserver<(object Sender, RoutedEventArgs Args)>(e =>
        {
            if (ReferenceEquals(lastMonitoredEvent, e.Args)) return;
            lastMonitoredEvent = e.Args;
            if (e.Args is PointerPressedEventArgs pointer) OnWindowPointerPressed(e.Sender, pointer);
        })); // :162-166 app-wide, pointer remains unhandled
        keyMonitor = InputElement.KeyDownEvent.Raised.Subscribe(new AnonymousObserver<(object Sender, RoutedEventArgs Args)>(e =>
        {
            if (ReferenceEquals(lastMonitoredEvent, e.Args)) return;
            lastMonitoredEvent = e.Args;
            if (e.Args is KeyEventArgs key) OnWindowKeyDown(e.Sender, key);
        })); // :167-171 Escape is consumed before target routing
        window.Deactivated += OnWindowDeactivatedOrMoved; // :174-177 didResignKeyNotification
        window.PositionChanged += OnWindowDeactivatedOrMoved; // :178-185 willMove/didMoveNotification
        monitorsActive = true;
    }

    // :209-217 stopMonitors().
    private void StopMonitors()
    {
        if (!monitorsActive) return;
        if (monitorWindow is { } window)
        {
            window.Deactivated -= OnWindowDeactivatedOrMoved;
            window.PositionChanged -= OnWindowDeactivatedOrMoved;
        }
        monitorWindow = null;
        pointerMonitor?.Dispose(); pointerMonitor = null;
        keyMonitor?.Dispose(); keyMonitor = null;
        lastMonitoredEvent = null;
        monitorsActive = false;
    }

    // :189-201 handleMouseDown(_:). Panel and strip clicks pass through; every other app-window
    // left/right click closes first and still reaches its target. No pointer event is consumed.
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(null).Properties.PointerUpdateKind is not
            (PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed)) return; // :162 only left/right mouse down
        var eventWindow = e.Source is Visual source ? TopLevel.GetTopLevel(source) : null;
        if (popup.Child is Control content && TopLevel.GetTopLevel(content) is { } panelWindow &&
            ReferenceEquals(eventWindow, panelWindow)) return; // :193 panel clicks pass through
        if (anchor?.Value is { } strip && strip.IsEffectivelyVisible &&
            ReferenceEquals(eventWindow, TopLevel.GetTopLevel(strip))) // :194-195 same window and visible
        {
            var point = e.GetPosition(strip);
            if (new Rect(strip.Bounds.Size).Contains(point)) return; // :196
        }
        if (anchorRect is { } rect && WindowRectAnchor.Target(rect) is { } target &&
            ReferenceEquals(eventWindow, TopLevel.GetTopLevel(target.View))) // :197 same window as rect anchor
        {
            var point = e.GetPosition(target.View);
            if (target.Rect.Contains(point)) return; // :198
        }
        Close(); // :199
    }

    // :203-207 handleKeyDown(_:). Escape (key code 53) closes and swallows the key.
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return; // :204
        Close(); // :205
        e.Handled = true; // :206 return nil: the key does not reach the composer
    }

    private void OnWindowDeactivatedOrMoved(object? sender, EventArgs e) => Close();

    // :223-235 contentSize(for:imageSize:). Fixed size per attachment kind, decided before the panel shows.
    private static Size ContentSize(Attachment attachment, Size? imageSize)
    {
        const double chrome = 60; // :224
        if (imageSize is { } size)
            return new Size(AttachmentLargePreview.ImageBounds.Width + 24, Math.Ceiling(size.Height) + chrome); // :226
        if (TextSample(attachment) is { } text)
            return new Size(464, Math.Min(320, Math.Max(120, (double)(SwiftString.Count(text) / 4))) + 60); // :230
        return new Size(344, 100); // :234
    }

    // :237-242 textSample(for:). A readable text file under 500,000 bytes, or null.
    private static string? TextSample(Attachment attachment)
    {
        if (!File.Exists(attachment.Path)) return null;
        long length;
        try { length = new FileInfo(attachment.Path).Length; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        if (length >= 500_000) return null; // :239
        string text;
        try
        {
            var bytes = File.ReadAllBytes(attachment.Path);
            if (SwiftText.DecodeUtf8OrNil(bytes) is not { } decoded) return null; // :240
            text = decoded;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        return text.Length == 0 ? null : text; // :240
    }
}

/// :245-249 A weak box for a Control anchor, so coordinators never keep views alive.
public sealed class WeakView
{
    private WeakReference<Control>? reference;

    public WeakView(Control? value = null)
    {
        if (value is not null) reference = new WeakReference<Control>(value);
    }

    public Control? Value
    {
        get => reference is not null && reference.TryGetTarget(out var value) ? value : null;
        set => reference = value is not null ? new WeakReference<Control>(value) : null;
    }
}

/// :251-295 A row's preview panel, and the view it hangs from, built only once the row needs them (a
/// coordinator made eagerly per row would build an Avalonia Popup for every row on every rebuild, for a
/// panel almost none of them ever shows).
public sealed class AttachmentPreviewSlot
{
    private readonly WeakView anchor = new(); // :261
    private Rect? anchorRect; // :262
    private AttachmentPreviewCoordinator? coordinator; // :263

    // :265-272 panel.
    public AttachmentPreviewCoordinator Panel
    {
        get
        {
            if (coordinator is { } existing) return existing;
            var made = new AttachmentPreviewCoordinator();
            if (anchor.Value is { } view) made.SetAnchor(view);
            if (anchorRect is { } rect) made.SetAnchor(rect);
            coordinator = made;
            return made;
        }
    }

    // :260-261 `let anchor = WeakView()`: the view the panel anchors to, captured while the row is under the pointer.
    // Module-internal in Swift and read by App/Views/Chat/TimelineRows.swift:1691 and :1694, another layer here
    // (R-NAME-6), so public.
    public WeakView Anchor => anchor;

    // :274-277 setAnchor(_:).
    public void SetAnchor(Control view)
    {
        anchor.Value = view;
        coordinator?.SetAnchor(view);
    }

    // :280-283 setAnchor(windowRect:).
    public void SetAnchor(Rect windowRect)
    {
        anchorRect = windowRect;
        coordinator?.SetAnchor(windowRect);
    }

    // :286-288 close(). A slot that never made a panel does nothing.
    public void Close() => coordinator?.Close();

    // :291-294 retire(except:). Nothing to forget until one has been opened.
    public void Retire(IReadOnlySet<Guid> except) => coordinator?.Retire(except);
}

/// :297-323 WindowRectAnchor. A popover anchor with no view of its own: where a SwiftUI view sits in its
/// window, for a popover hung from that rect on the window instead of from a view mounted in the tree.
public static class WindowRectAnchor
{
    // :313-322 target(for:). The window the current event is in, else the key window, else the main
    // window; nil for a zero-sized rect or no window. Avalonia has no `isFlipped` distinction (its
    // coordinate system is always top-left origin, matching NSView.isFlipped == true), so the conditional
    // flip at :318-320 has no counterpart: `rect`, already reported in the window's own top-left-origin
    // coordinates by AttachmentPreviewPanel.WindowRectAnchor(_:) below, is used as-is.
    public static (Control View, Rect Rect)? Target(Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return null; // :315
        if (CurrentOrKeyOrMainWindow() is not { } window) return null; // :316-317
        return (window, rect); // :318-321
    }

    private static Window? CurrentOrKeyOrMainWindow()
    {
        if (AppCurrentEvent.CurrentWindow is Window current) return current; // :316 NSApp.currentEvent?.window
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.Windows.FirstOrDefault(w => w.IsActive) is { } key) return key; // :316 NSApp.keyWindow
            return desktop.MainWindow; // :316 NSApp.mainWindow
        }
        return null;
    }
}

/// :325-331 `extension View { func windowRectAnchor(_:) }`, and :333-366 `AttachmentAnchorCapture` folded
/// into one Control per ED-095: reports this view's frame in its window (top-left-origin coordinates)
/// whenever it changes, and captures the strip's own Control so the preview panel can anchor to it.
public static class AttachmentPreviewPanel
{
    // :328-330 windowRectAnchor(_:). `.global` here is this control's frame relative to its window's root
    // visual, which Avalonia already reports top-left-origin (see WindowRectAnchor.Target above).
    public static T WindowRectAnchor<T>(this T control, Action<Rect> onChange) where T : Control
    {
        control.LayoutUpdated += (_, _) =>
        {
            if (TopLevel.GetTopLevel(control) is not Visual root) return;
            var origin = control.TranslatePoint(new Point(0, 0), root) ?? default;
            onChange(new Rect(origin, control.Bounds.Size));
        };
        return control;
    }
}

/// :335-366 AttachmentAnchorCapture. Captures the strip's own Control so the preview panel can anchor to
/// it; mounted as the strip's background, so it fills exactly the strip. `updateNSView`'s call to
/// `onResolve` on every SwiftUI re-render (:342-345) has no counterpart in a control built once (R-VIEW-1):
/// the one attach event below is the single occasion this control's identity is ever reported.
public sealed class AttachmentAnchorCapture : Control
{
    private readonly Action<Control> onResolve;

    public AttachmentAnchorCapture(Action<Control> onResolve)
    {
        this.onResolve = onResolve;
        IsHitTestVisible = false; // :359 hitTest returns nil: only an anchor, never in the way of a click
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        onResolve(this); // :361-364 viewDidMoveToWindow, window != nil
    }
}
