using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.UI.Port;
using PortAnimation = Marrow.UI.Port.Animation;

namespace Marrow.UI.Chrome;

// Source: DroppyCode/UI/Chrome/DragHandle.swift (B3 52bc792), :1-304.
//
// PLATFORM ADAPTATION: this file generalizes the technique WindowChrome.cs's SidebarResizeHandleView already
// established (:461-699) for one fixed vertical strip - an Avalonia Canvas hosting the grip and glow CALayer
// stand-ins as child Border controls positioned with Canvas.Left/Top, a Win32 client hit-test declaration so the
// handle never drags the window (ED-063, PR-WIN-053), and the CursorPlatform seam (Port/CursorPlatform.cs) for a
// scriptable real-pointer-position read on attach and release - to three axes and an optional, caller-configured
// grip instead of one caller. AppKit's NSTrackingArea (:234-245) has no separate Avalonia analogue: pointer-
// entered/exited fire natively for any control in the visual tree without a registered tracking area, so
// `configure(grip:)`'s tracking-area add/remove (:108) has nothing further to do here.
//
// Travel's axis signs (:257-266) are the one place this file's Avalonia coordinates differ textually from the
// Swift source, because AppKit's window-space y runs up while Avalonia's runs down (WindowChrome.cs's own
// SidebarResizeHandleView and TerminalPanel.cs's TerminalResizeHandle already invert per their one fixed axis for
// the same reason). The plan's own row for this unit gives the Avalonia-coordinate rule directly: .free is
// point.Y - origin.Y and .vertical is origin.Y - point.Y (B3_PLAN.md section 3, row N8). This corrects, for the
// shared component, the sign SubagentPanel.cs's own free-axis Travel got backwards at B2 (its :908-913 computes
// pressOrigin.Y - point.Y for .free, the .vertical rule, not the .free one) - a pre-existing port defect (6.2)
// that SubagentPanel.cs's own lane will inherit the fix for once it retargets its PanelDragHandleView onto this
// type; SubagentPanel.cs itself is not touched here.
/// <summary>
/// :6-267 <c>final class DragHandleView: NSView</c>. The AppKit strip behind every drag handle: not a SwiftUI
/// gesture, so the first click works even when the window is not key (AcceptsFirstMouse below), and only a
/// cursor declaration shows the cursor before the press.
/// </summary>
public sealed class DragHandleView : Canvas
{
    /// <summary>:8-15 <c>enum Axis</c>. Which way the handle reports the pointer's travel.</summary>
    public enum Axis
    {
        Horizontal, // :10 Drags on x, with a vertical grip bar (the sidebar's edge).
        Vertical,   // :12 Drags on y, with a horizontal grip bar (the terminal drawer's edge).
        Free,       // :14 Reports both axes, y running down like a panel's offset, and draws no grip.
    }

    /// <summary>:18-30 <c>struct Grip</c> (R-VAL-1): a resize handle's centred bar, which grows on hover.</summary>
    public sealed record Grip(
        double RestLength,                  // :19
        double ShownLength,                 // :20
        double Thickness,                   // :21
        double Alpha = 0.6,                 // :22
        double? GlowInset = null,           // :24. When set, a wider glow layer starts at this inset and lights on hover.
        bool RefreshesHoverOnLayout = false, // :27. The sidebar re-reads hover on every layout; the terminal only snaps on enter/exit.
        double HoverGlow = 0.05,            // :28
        double DragGlow = 0.10);            // :29

    private static class Metrics // :58-61
    {
        public const double GlowRadius = 4;  // :59
        public const double Duration = 0.18; // :60
    }

    /// <summary>The one AppKit read this file makes twice (attach, release): the cursor's actual screen position,
    /// seamed like SidebarResizeHandleView's own <c>CursorPlatform</c> (WindowChrome.cs:483) so a test can script
    /// it instead of moving the real OS pointer. A separate instance from that one: DragHandleView is its own
    /// NSView-equivalent component, independent of the sidebar strip.</summary>
    public static Ambient<ICursorPlatform> CursorPlatform { get; } = new(() => new WindowsCursorPlatform());

    public Action? OnBegin { get; set; } // :32
    public Action<Size>? OnChange { get; set; } // :34. Travel since the press, in the axis's own terms.
    public Action? OnEnd { get; set; } // :35
    public Action? OnDoubleClick { get; set; } // :36

    public Axis HandleAxis { get; set; } = Axis.Horizontal; // :37 Swift: axis (R-NAME-3: collides with the nested Axis type)

    /// <summary>:38 <c>var grip: Grip?</c>. Settable only through <see cref="Configure"/>, exactly as this file's
    /// only caller (the <see cref="DragHandle"/> wrapper's <c>apply(to:)</c>, :294) reaches it.</summary>
    public Grip? HandleGrip { get; private set; } // Swift: grip

    private Cursor configuredCursor = Cursor.Default; // :39 var cursor: NSCursor = .arrow

    /// <summary>:39-44 <c>var cursor: NSCursor</c>. Named to avoid shadowing the inherited <see
    /// cref="Control.Cursor"/>, which this property drives while <see cref="IsActive"/>.</summary>
    public Cursor HandleCursor
    {
        get => configuredCursor;
        set
        {
            if (ReferenceEquals(value, configuredCursor)) return; // :41 guard cursor !== oldValue else { return }
            configuredCursor = value;
            if (IsActive) Cursor = configuredCursor; // :42 window?.invalidateCursorRects(for: self)
        }
    }

    public Cursor? DragCursor { get; set; } // :46. Pushed while the pointer is held; null leaves the cursor in place.
    public double Slop { get; set; } // :48. How far the pointer must travel before a press becomes a drag.

    /// <summary>PORT-ONLY (nth web surfaces W1, ED-NEW-web-w1-2): measure travel in screen DIP from any top level, not
    /// in a Window's own space. A grip inside a popup needs it: a popup is no Window, and it moves as it is resized.</summary>
    public bool TracksInScreenSpace { get; set; }

    private bool isActive = true; // :49 var isActive = true { didSet { ... } }
    public bool IsActive
    {
        get => isActive;
        set
        {
            if (value == isActive) return; // :51 guard isActive != oldValue
            isActive = value;
            IsHitTestVisible = value; // :194-196 hitTest returns nil (the whole subtree) when inactive
            Cursor = value ? configuredCursor : null; // :52 invalidateCursorRects; :188-192 resetCursorRects' guard
            RefreshHover(); // :53
            UpdateGrip(animated: false); // :54
        }
    }

    private Point pressOrigin; // :63 private var pressOrigin: NSPoint = .zero
    private bool hasConfigured; // :65
    private bool isHovering; // :66
    private bool isDragging; // :67
    private bool isPressed; // PORT-ONLY: Avalonia raises PointerMoved on every hover move, unlike AppKit's
                             // mouseDragged, which only ever fires while this view holds the press (:210). This
                             // flag tells the two apart so a slop > 0 handle can test travel before isDragging
                             // itself goes true, without reacting to an unrelated hover move.
    private readonly Border gripLayer; // :68 private let gripLayer = CALayer()
    private readonly Border glowLayer; // :69 private let glowLayer = CALayer()

    /// <summary>:71-89 <c>init(frame:)</c>/<c>init?(coder:)</c>, both routing through <c>configureLayers()</c>.</summary>
    public DragHandleView()
    {
        // :186 override var mouseDownCanMoveWindow: Bool { false }, realized the way SidebarResizeHandleView's
        // own :521-522 already is (ED-063, PR-WIN-053): declared to the platform once, not queried per event.
        Win32Properties.SetNonClientHitTestResult(this, Win32Properties.Win32HitTestValue.Client);
        // Takes the hit across the whole strip, like an NSView's default opaque event area (matches
        // WindowChrome.cs:520's identical fix for the same Avalonia Panel/Canvas hit-testing gap).
        Background = Brushes.Transparent;
        Cursor = configuredCursor; // isActive starts true (:49); realizes :188-192 while active.

        // :82-88. gripLayer gets no fixed corner radius here: its thickness (and so its radius, :167) is
        // configured per grip, unlike SidebarResizeHandleView's fixed 2.5pt strip.
        glowLayer = new Border { CornerRadius = new CornerRadius(Metrics.GlowRadius), Opacity = 0, IsHitTestVisible = false }; // :84-85
        gripLayer = new Border { Opacity = 0, IsHitTestVisible = false }; // :83
        ApplyColors(); // :86
        Children.Add(glowLayer); // :87 layer?.addSublayer(glowLayer)
        Children.Add(gripLayer); // :88 layer?.addSublayer(gripLayer), drawn on top of the glow
    }

    // :91-97 applyColors(). SwiftColor.LabelColor.Brush(control) installs a binding that re-resolves on the
    // control's own theme change, exactly what :99-102 viewDidChangeEffectiveAppearance exists to do by hand in
    // AppKit (WindowChrome.cs:530-533 makes the same observation for SidebarResizeHandleView): installing it once
    // here needs no separate override.
    private void ApplyColors()
    {
        var brush = SwiftColor.LabelColor.Brush(this); // :93 NSColor.labelColor resolved in effectiveAppearance
        gripLayer.Background = brush; // :94
        glowLayer.Background = brush; // :95
    }

    /// <summary>:104-114 <c>func configure(grip:)</c>. Sets the grip and, on a first configuration or a change in
    /// whether one is present, snaps the grip to it unanimated - a re-render must never interrupt a hover or drag
    /// animation already in flight.</summary>
    public void Configure(Grip? grip)
    {
        var hadGrip = HandleGrip is not null; // :106
        HandleGrip = grip; // :107
        // :108. updateTrackingAreas() has nothing to do on Avalonia (top-of-file note); the presence change itself
        // needs no separate registration step here.
        if (!hasConfigured || hadGrip != (grip is not null)) // :110
        {
            hasConfigured = true; // :111
            UpdateGrip(animated: false); // :112
        }
    }

    // :116-125 override func layout()
    // PLATFORM ADAPTATION: AppKit's layout() (:116-125) is only ever invoked when the NSView's own frame actually
    // needs recomputing; setting a CALayer sublayer's frame from inside layout() itself never re-triggers it. On
    // Avalonia, changing gripLayer/glowLayer's Width/Height/Canvas.Left/Top from UpdateGrip below can invalidate
    // this control's own arrange, so ArrangeOverride would otherwise re-enter itself with the SAME size and (in
    // the else branch, grip.refreshesHoverOnLayout == false) unconditionally re-apply animated: false, stomping
    // an animated transition this same call just installed a moment earlier from a press or a hover change. This
    // guard - re-run the else branch's snap only when the size actually changed, matching WindowChrome.cs's own
    // PlaceTrafficLights "changed" check for the identical reason - restores AppKit's actual invocation frequency
    // without touching the if branch, which must keep re-checking hover on every layout regardless of size
    // (PR-WIN-056's rule for a grip whose refreshesHoverOnLayout is true, e.g. a sidebar-style caller: its own
    // panel can move around it while its own bounds size stays the same).
    private Size? lastArrangedSize;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        if (HandleGrip?.RefreshesHoverOnLayout == true) // :118
        {
            var hovered = isHovering; // :119
            RefreshHover(); // :120
            UpdateGrip(animated: hovered != isHovering, result); // :121
        }
        else if (lastArrangedSize != result) // :123, guarded against the re-entrant arrange noted above
        {
            UpdateGrip(animated: false, result); // :123
        }
        lastArrangedSize = result;
        return result;
    }

    // :127-138 override func viewDidMoveToWindow(). Avalonia splits "the view's window changed" into attach and
    // detach callbacks; the Swift method's two independent checks (the drag clear gated on window == nil, the
    // grip refresh unconditional either way) are ported to whichever of the two this is, each keeping its own
    // gate exactly as the source states it.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // :130-133 (PR-WIN-233). Taken out mid-drag, the handle lets go of the cursor with it - but does NOT call
        // onEnd or onChange, unlike a real release.
        if (isDragging)
        {
            isDragging = false; // :131
            isPressed = false;
            if (DragCursor is not null) Cursor = IsActive ? configuredCursor : null; // :132 NSCursor.pop()
        }
        if (HandleGrip is not null) // :134, runs on every viewDidMoveToWindow call, attach or detach alike
        {
            RefreshHover(); // :135. The window is now nil, so this resets isHovering to false via its own guard.
            UpdateGrip(animated: false); // :136
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (HandleGrip is not null) // :134 (PR-WIN-232)
        {
            RefreshHover(); // :135. Re-reads the real pointer position now a window exists to read it from.
            UpdateGrip(animated: false); // :136
        }
    }

    private bool ShowsGrip => IsActive && (isHovering || isDragging); // :140

    /// <summary>:142-151 <c>refreshHover()</c>. Reads the cursor's actual place, so isHovering never outlives the
    /// cursor being over the strip; the entered/exited pair only keeps it true while the mouse itself moves.</summary>
    private void RefreshHover()
    {
        // `let window` (:145) holds only while the window itself is still in a visual tree. When a Window closes,
        // Avalonia detaches it (its PresentationSource cleared) before it detaches its descendants, whose visual
        // parent chain stays intact, so GetTopLevel still finds the closing window from this handle's own detach
        // callback, and PointToClient below would throw on it. A closing window is no window here (:146-147).
        if (TopLevel.GetTopLevel(this) is not Window window || !window.IsAttachedToVisualTree() ||
            !IsActive || !IsEffectivelyVisible) // :145 guard isActive, let window, !isHiddenOrHasHiddenAncestor
        {
            isHovering = false; // :146
            return;
        }
        if (CursorPlatform.Current.ScreenPosition is not { } screen) // :149 window.mouseLocationOutsideOfEventStream
        {
            isHovering = false;
            return;
        }
        var client = window.PointToClient(screen); // window-space, as `from: nil` means window space in AppKit
        var local = window.TranslatePoint(client, this); // :149 convert(_:from:)
        isHovering = local is { } point && new Rect(Bounds.Size).Contains(point); // :150 bounds.contains(point)
    }

    /// <summary>:153-181 <c>updateGrip(animated:)</c>.</summary>
    private void UpdateGrip(bool animated) => UpdateGrip(animated, Bounds.Size);

    private void UpdateGrip(bool animated, Size size)
    {
        if (HandleGrip is not { } grip) // :154 guard let grip else
        {
            gripLayer.Opacity = 0; // :155
            glowLayer.Opacity = 0; // :156
            return; // :157
        }
        var length = ShowsGrip ? grip.ShownLength : grip.RestLength; // :159
        // :160-166 CATransaction.begin()/setAnimationDuration/setAnimationTimingFunction or setDisableActions(true).
        ApplyTransitions(gripLayer, animated);
        ApplyTransitions(glowLayer, animated);

        gripLayer.CornerRadius = new CornerRadius(grip.Thickness / 2); // :167
        if (HandleAxis == Axis.Horizontal) // :168
        {
            gripLayer.Width = grip.Thickness; // :169
            gripLayer.Height = length;
            SetLeft(gripLayer, (size.Width - grip.Thickness) / 2);
            SetTop(gripLayer, (size.Height - length) / 2);
        }
        else // :170-172. Vertical and free share this branch, as the Swift `if axis == .horizontal ... else` does.
        {
            gripLayer.Width = length;
            gripLayer.Height = grip.Thickness;
            SetLeft(gripLayer, (size.Width - length) / 2);
            SetTop(gripLayer, (size.Height - grip.Thickness) / 2);
        }
        gripLayer.Opacity = ShowsGrip ? grip.Alpha : 0; // :173

        if (grip.GlowInset is { } inset) // :174
        {
            SetLeft(glowLayer, 0.5); // :175 bounds.minX + 0.5, always 0 for a control's own local bounds
            SetTop(glowLayer, inset);
            glowLayer.Width = Math.Max(0, size.Width - 1); // :175
            glowLayer.Height = Math.Max(0, size.Height - inset);
            glowLayer.Opacity = ShowsGrip ? (isDragging ? grip.DragGlow : grip.HoverGlow) : 0; // :176
        }
        else
        {
            glowLayer.Opacity = 0; // :178
        }
    }

    private static void ApplyTransitions(Border layer, bool animated)
    {
        if (!animated) // :165 CATransaction.setDisableActions(true)
        {
            layer.Transitions = null;
            return;
        }
        var duration = TimeSpan.FromSeconds(Metrics.Duration); // :162 CATransaction.setAnimationDuration
        var easing = PortAnimation.EaseOut(Metrics.Duration).ToEasing(); // :163 CAMediaTimingFunction(name: .easeOut)
        layer.Transitions =
        [
            new Avalonia.Animation.DoubleTransition { Property = WidthProperty, Duration = duration, Easing = easing },
            new Avalonia.Animation.DoubleTransition { Property = HeightProperty, Duration = duration, Easing = easing },
            new Avalonia.Animation.DoubleTransition { Property = LeftProperty, Duration = duration, Easing = easing },
            new Avalonia.Animation.DoubleTransition { Property = TopProperty, Duration = duration, Easing = easing },
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = duration, Easing = easing },
        ];
    }

    // :183 override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }. The file's own top comment
    // (:5-6) states why: the first click must work even when the window is not key.
    public bool AcceptsFirstMouse(PointerEventArgs? @for) => true;

    // :185-186 override var mouseDownCanMoveWindow: Bool { false }. Declared through the Win32 client hit test
    // set in the constructor instead of a queried property (matches SidebarResizeHandleView's identical choice).
    public bool MouseDownCanMoveWindow => false;

    // :198-208 override func mouseDown(with event: NSEvent)
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        pressOrigin = PointerPosition(e) ?? default; // :199 event.locationInWindow
        if (e.ClickCount == 2) OnDoubleClick?.Invoke(); // :200
        isPressed = true;
        // AppKit's mouseDown/Dragged/Up trio is delivered to this view unconditionally once it gets the press,
        // regardless of slop; Avalonia needs an explicit capture for the same guarantee, taken here rather than
        // only once isDragging goes true (matches SidebarResizeHandleView:649's identical windows_path).
        e.Pointer.Capture(this);
        CapturedPointerForTest = e.Pointer;
        e.Handled = true;
        if (Slop != 0) // :201 guard slop == 0 else
        {
            isDragging = false; // :202
            return; // :203
        }
        isDragging = true; // :205
        UpdateGrip(animated: true); // :206
        OnBegin?.Invoke(); // :207
    }

    // :210-220 override func mouseDragged(with event: NSEvent)
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!isPressed) return; // see the isPressed field comment: hover moves must not reach here.
        if (PointerPosition(e) is not { } position) return;
        var travel = Travel(position); // :211
        if (!isDragging) // :212
        {
            if (Math.Abs(travel.Width) < Slop && Math.Abs(travel.Height) < Slop) return; // :213
            isDragging = true; // :214
            if (DragCursor is { } dragCursor) Cursor = dragCursor; // :215 dragCursor?.push()
            UpdateGrip(animated: true); // :216
            OnBegin?.Invoke(); // :217
        }
        OnChange?.Invoke(travel); // :219
    }

    // :222-232 override func mouseUp(with event: NSEvent)
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        isPressed = false;
        if (isDragging) // :223 guard isDragging else return
        {
            var travel = PointerPosition(e) is { } position ? Travel(position) : new Size(0, 0); // :224
            isDragging = false; // :225
            if (DragCursor is not null) Cursor = IsActive ? configuredCursor : null; // :226 NSCursor.pop()
            OnChange?.Invoke(travel); // :227
            OnEnd?.Invoke(); // :228
            // :229-231. No exit event arrives while the button is held; the release decides from the real cursor.
            RefreshHover(); // :230
            UpdateGrip(animated: true); // :231
        }
        // Releasing our own capture can itself raise PointerCaptureLost on this element (Avalonia notifies
        // whoever held capture whenever it changes, including a self-initiated release, not only a theft or a
        // detach) - done last, after the isDragging-dependent work above, so that reentrant call finds isDragging
        // already false and its own guard no-ops instead of firing OnPointerCaptureLost's clearing logic first
        // and making this method's own `if (isDragging)` check above see a false it never actually earned.
        e.Pointer.Capture(null);
    }

    // PR-WIN-233's rule, generalized: Avalonia can drop a capture without a matching release (another element
    // steals it, or the platform revokes it) where AppKit has no separate notion of losing an implicit mouseDown
    // capture. This is the closest Avalonia gets to :127-133's mid-drag detach, so it clears state the same way -
    // without invoking onEnd or onChange.
    //
    // ENGINEERING DECISION: :134-137's grip-refresh clause has no literal analogue here either - this event has
    // no Swift original at all, unlike :127-133's drag-clear, which does. But the component contract (PR-WIN-234)
    // ties the glow's opacity to isDragging (:176 `isDragging ? dragGlow : hoverGlow`) every time UpdateGrip runs.
    // When capture moves to another element that Avalonia can still hit-test, that element's own arrival already
    // raises PointerExited on this view (ED-202), and since isDragging is false by the time that fires, nothing
    // here gates it, so OnPointerExited's own RefreshHover-free `isHovering = false; UpdateGrip(animated: true)`
    // settles things regardless of this method. But when capture is simply revoked without moving to a live,
    // hit-tested element - the platform drops it, or a caller does `pointer.Capture(null)` outside the normal
    // release path - no such trailing exit follows, and nothing else will ever re-run UpdateGrip; the grip/glow
    // would stay rendered at their mid-drag values indefinitely, even with the pointer still over the strip. Settle
    // them the same way :134-137 itself does on an equally abrupt, non-user-driven state change (animated: false -
    // a snap, not a transition): safe to do unconditionally, since the ED-202 path above simply re-does the same
    // work a moment later on the sub-case it already covers.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        isPressed = false;
        if (!isDragging) return;
        isDragging = false;
        if (DragCursor is not null) Cursor = IsActive ? configuredCursor : null;
        RefreshHover();
        UpdateGrip(animated: false);
    }

    // :234-245 override func updateTrackingAreas(). See the top-of-file note: nothing to port, no Avalonia
    // registration step exists for pointer-entered/exited to depend on.

    // :247-250 override func mouseEntered(with event: NSEvent)
    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        // ED-202: Avalonia raises this on the captured element's own chain during a drag (every press captures
        // implicitly), where AppKit's tracking area is frozen while the button is held (:229) - "no exit arrives
        // while the button is held; the release decides from the cursor." A mid-drag toggle here has no AppKit
        // counterpart, so it is absorbed rather than ported.
        if (isDragging) return;
        isHovering = IsActive; // :248
        UpdateGrip(animated: true); // :249
    }

    // :252-255 override func mouseExited(with event: NSEvent)
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (isDragging) return; // ED-202, the same reasoning as OnPointerEntered above.
        isHovering = false; // :253
        UpdateGrip(animated: true); // :254
    }

    // :199, :211, :224 event.locationInWindow: the pointer in its Window's space, or (TracksInScreenSpace) in screen DIP.
    private Point? PointerPosition(PointerEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (!TracksInScreenSpace) return top is Window window ? e.GetPosition(window) : null;
        if (top is null) return null;
        var screen = top.PointToScreen(e.GetPosition(top));
        var scale = top.RenderScaling;
        return new Point(screen.X / scale, screen.Y / scale);
    }

    /// <summary>:257-266 <c>travel(to:)</c>. AppKit's window-space y runs up; a free handle reports the panel's
    /// offset, which runs down. Avalonia's window-space y runs down already, so <c>.free</c> needs no inversion
    /// here while <c>.vertical</c> needs one it did not need in AppKit (top-of-file note).</summary>
    private Size Travel(Point point)
    {
        return HandleAxis switch
        {
            Axis.Horizontal => new Size(point.X - pressOrigin.X, 0), // :262
            Axis.Vertical => new Size(0, pressOrigin.Y - point.Y), // :263, Avalonia coordinates: origin.Y - point.Y
            Axis.Free => new Size(point.X - pressOrigin.X, point.Y - pressOrigin.Y), // :264, Avalonia: point.Y - origin.Y
            _ => throw new UnreachableException(),
        };
    }

    // Test-only readback (InternalsVisibleTo), matching WindowChrome.cs's identical pattern.
    internal bool IsHoveringForTest => isHovering;
    internal bool IsDraggingForTest => isDragging;
    internal Border GripLayerForTest => gripLayer;
    internal Border GlowLayerForTest => glowLayer;
    internal IPointer? CapturedPointerForTest { get; private set; }
}

/// <summary>:269-304 <c>struct DragHandle: NSViewRepresentable</c>. One configuration for every call site; not a
/// caller-specific type, unlike WindowChrome.cs's <see cref="SidebarResizeHandle"/> and TerminalPanel.cs's
/// TerminalResizeHandle, both of which their own lanes retarget onto this wrapper and <see
/// cref="DragHandleView"/>.</summary>
public sealed class DragHandle
{
    public DragHandleView.Axis HandleAxis { get; set; } = DragHandleView.Axis.Horizontal; // :271
    public DragHandleView.Grip? HandleGrip { get; set; } // :272
    public Cursor HandleCursor { get; set; } = Cursor.Default; // :273 = .arrow
    public Cursor? DragCursor { get; set; } // :274
    public double Slop { get; set; } // :275
    public bool IsActive { get; set; } = true; // :276
    public Action? OnBegin { get; set; } // :277
    public Action<Size>? OnChange { get; set; } // :278
    public Action? OnEnd { get; set; } // :279
    public Action? OnDoubleClick { get; set; } // :280

    // :282-286 func makeNSView(context:) -> DragHandleView
    public DragHandleView MakeNSView()
    {
        var view = new DragHandleView();
        Apply(view); // :284
        return view;
    }

    // :288-290 func updateNSView(_:context:)
    public void UpdateNSView(DragHandleView view) => Apply(view);

    // :292-303 private func apply(to view:)
    private void Apply(DragHandleView view)
    {
        view.HandleAxis = HandleAxis; // :293
        view.Configure(HandleGrip); // :294 view.configure(grip: grip)
        view.HandleCursor = HandleCursor; // :295
        view.DragCursor = DragCursor; // :296
        view.Slop = Slop; // :297
        view.IsActive = IsActive; // :298
        view.OnBegin = OnBegin; // :299
        view.OnChange = OnChange; // :300
        view.OnEnd = OnEnd; // :301
        view.OnDoubleClick = OnDoubleClick; // :302
    }
}
