using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using PortAnimation = Marrow.UI.Port.Animation;

namespace Marrow.UI.Chrome;

// PORT-ONLY, no Swift twin: nth web surfaces, unit W1 (work/design/web-surfaces/DESIGN.md 4.2 and 4.3, canvas
// index.html `function splitter`). The one splitter every resize in nth uses: the sidebar's edge in all three modes, the
// terminal drawer, the floating panels' and the floating sidebar's edges and corners, the Alt diff popover, and (later
// units) every workspace gutter, the App pop-out, the Visus console drawer and the device frame. It wraps the shared
// DragHandleView (the press, drag, release and double-click mechanics, the cursor, the client hit test that never moves
// the window) and adds the look (SplitterGrip, the "Resize" tooltip, the readout), the keyboard, accessibility, right-to-
// left mirroring, Reduce Motion and touch. ED-NEW-web-w1-2.

/// <summary>
/// What a splitter moves. Every member runs on the UI thread. Travel and steps are logical DIP toward the trailing side
/// (x negated in a mirrored right-to-left layout), y down; a one-axis splitter reads only its own axis. Set the delegates,
/// or derive (see <see cref="SplitterValue"/>).
/// </summary>
public class SplitterTarget
{
    public Func<SplitterReport>? OnReport { get; init; }
    public Action? OnSettle { get; init; }
    public Action? OnBegin { get; init; }
    public Action<Vector>? OnMove { get; init; }
    public Action<bool>? OnEnd { get; init; }
    public Action? OnReset { get; init; }
    public Action<Vector>? OnStep { get; init; }
    public Action<bool>? OnStepTo { get; init; }
    public Action<bool>? OnCollapse { get; init; }
    public Action<double>? OnSetValue { get; init; }

    /// <summary>The value, range and texts reported to assistive technology and drawn in the readout.</summary>
    public virtual SplitterReport Report() => OnReport?.Invoke() ?? new SplitterReport(0, 0, 0, false, 0, "", "");

    /// <summary>Pointer down: every spring still in flight lands on its target in this same frame (DESIGN 4.2).</summary>
    public virtual void Settle() => OnSettle?.Invoke();

    /// <summary>A drag starts (past the slop).</summary>
    public virtual void Begin() => OnBegin?.Invoke();

    /// <summary>The pointer's travel since the press.</summary>
    public virtual void Move(Vector travel) => OnMove?.Invoke(travel);

    /// <summary>Release (or a lost capture).</summary>
    public virtual void End(bool moved) => OnEnd?.Invoke(moved);

    /// <summary>Double-click or Enter: spring to the default.</summary>
    public virtual void Reset() => OnReset?.Invoke();

    /// <summary>An arrow key. Without <see cref="OnStep"/> it is a short drag of <paramref name="delta"/>.</summary>
    public virtual void Step(Vector delta)
    {
        if (OnStep is { } step)
        {
            step(delta);
            return;
        }
        Settle();
        Begin();
        Move(delta);
        End(true);
    }

    /// <summary>Home (minimum) or End (maximum).</summary>
    public virtual void StepTo(bool maximum) => OnStepTo?.Invoke(maximum);

    /// <summary>Collapse (true) or restore (false) the collapsible side.</summary>
    public virtual void Collapse(bool collapse) => OnCollapse?.Invoke(collapse);

    /// <summary>Whether assistive technology may set the value directly.</summary>
    public virtual bool CanSetValue => OnSetValue is not null;

    /// <summary>Assistive technology sets the value (in the report's units).</summary>
    public virtual void SetValue(double value) => OnSetValue?.Invoke(value);
}

/// <summary>
/// A one-axis splitter value with the standard behaviour (SplitterModel): 1:1 drags, the rubber band, collapse and
/// restore, keyboard steps, all drawn through <paramref name="apply"/> on the frame clock's spring, and each committed
/// state handed to <paramref name="commit"/>.
/// </summary>
public sealed class SplitterValue : SplitterTarget
{
    private readonly Control owner;
    private readonly SplitterOrientation orientation;
    private readonly Action<double> apply;
    private readonly Action<SplitterState>? commit;
    private Action<TimeSpan>? pendingFrame;
    private IFrameSource? frameSource;
    private ContinuousInstant lastFrame;

    public SplitterValue(Control owner, SplitterOrientation orientation, SplitterLimits limits, SplitterState state, Action<double> apply, Action<SplitterState>? commit = null)
    {
        this.owner = owner;
        this.orientation = orientation;
        this.apply = apply;
        this.commit = commit;
        Model = new SplitterModel(limits, state);
        owner.DetachedFromVisualTree += (_, _) => Finish();
    }

    public SplitterModel Model { get; }

    /// <summary>Raised when a drag, key or reset changes whether the collapsible side is collapsed.</summary>
    public event Action<bool>? CollapsedChanged;

    public bool IsAnimating => pendingFrame is not null;

    public override SplitterReport Report() => Model.Report();

    public override void Settle()
    {
        Model.PointerDown();
        StopFrames();
        apply(Model.Shown.Value);
    }

    public override void Begin()
    {
        StopFrames();
        Model.ReduceMotion = ReduceMotion;
        Model.BeginDrag();
        apply(Model.Shown.Value);
    }

    public override void Move(Vector travel)
    {
        var collapsed = Model.State.IsCollapsed;
        Model.Drag(orientation == SplitterOrientation.Horizontal ? travel.Y : travel.X);
        Draw();
        if (collapsed != Model.State.IsCollapsed) CollapsedChanged?.Invoke(Model.State.IsCollapsed);
    }

    public override void End(bool moved)
    {
        if (!Model.IsDragging) return;
        Model.EndDrag();
        Draw();
        commit?.Invoke(Model.State);
    }

    public override void Reset() => Run(new SplitterCommand(SplitterCommandKind.Reset));

    public override void Step(Vector delta) => Run(new SplitterCommand(SplitterCommandKind.Step, delta.X, delta.Y));

    public override void StepTo(bool maximum) => Run(new SplitterCommand(maximum ? SplitterCommandKind.ToMaximum : SplitterCommandKind.ToMinimum));

    public override void Collapse(bool collapse) => Run(new SplitterCommand(collapse ? SplitterCommandKind.Collapse : SplitterCommandKind.Restore));

    public override bool CanSetValue => true;

    public override void SetValue(double value)
    {
        var limits = Model.Limits;
        var current = Model.State.IsCollapsed ? limits.CollapsedValue : Model.State.Value;
        if (Model.State.IsCollapsed) Run(new SplitterCommand(SplitterCommandKind.Restore));
        var open = Model.State.Value;
        var travel = (value - open) * limits.Growth;
        if (current != value) Run(orientation == SplitterOrientation.Horizontal
            ? new SplitterCommand(SplitterCommandKind.Step, 0, travel)
            : new SplitterCommand(SplitterCommandKind.Step, travel, 0));
    }

    /// <summary>Lands the spring on its target now.</summary>
    public void Finish()
    {
        StopFrames();
        Model.Shown.Settle();
        apply(Model.Shown.Value);
    }

    private void Run(SplitterCommand command)
    {
        var collapsed = Model.State.IsCollapsed;
        Model.ReduceMotion = ReduceMotion;
        if (!Model.Apply(command, orientation)) return;
        Draw();
        commit?.Invoke(Model.State);
        if (collapsed != Model.State.IsCollapsed) CollapsedChanged?.Invoke(Model.State.IsCollapsed);
    }

    private bool ReduceMotion =>
        EnvironmentValues.GetAccessibilityReduceMotion(owner) || Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    private void Draw()
    {
        apply(Model.Shown.Value);
        if (!Model.Shown.IsMoving || pendingFrame is not null) return;
        if (!FrameClock.IsBound || TopLevel.GetTopLevel(owner) is null)
        {
            Model.Shown.Settle();
            apply(Model.Shown.Value);
            return;
        }
        lastFrame = SwiftClock.Now;
        frameSource = FrameClock.Source.Current;
        pendingFrame = OnFrame;
        frameSource.RequestFrame(pendingFrame);
    }

    private void OnFrame(TimeSpan _)
    {
        pendingFrame = null;
        var now = SwiftClock.Now;
        var dt = Math.Min(0.034, Math.Max(0, (now - lastFrame).TotalSeconds));
        lastFrame = now;
        var moving = Model.Shown.Step(dt);
        apply(Model.Shown.Value);
        if (!moving) return;
        pendingFrame = OnFrame;
        frameSource?.RequestFrame(pendingFrame);
    }

    private void StopFrames()
    {
        if (pendingFrame is { } frame) frameSource?.CancelFrame(frame);
        pendingFrame = null;
    }
}

/// <summary>
/// The shared splitter (DESIGN 4.2). The host places it over the divider (centred on it), the edge or the corner, and
/// sizes it: <see cref="DividerThickness"/> across a divider (a fine pointer hits its middle 10 DIP, touch and pen all
/// 12), <see cref="EdgeThickness"/> on a floating panel's edge, <see cref="CornerSize"/> square on a corner.
/// </summary>
public sealed class Splitter : Panel
{
    /// <summary>A divider's layout thickness: the 12 DIP a coarse pointer (touch, pen) hits.</summary>
    public const double DividerThickness = 12;

    /// <summary>A fine pointer (mouse) hits <see cref="DividerThickness"/> minus this on each side: 10 DIP.</summary>
    public const double FinePointerInset = 1;

    public const double EdgeThickness = 8;
    public const double CornerSize = 16;
    public const double HoverDelaySeconds = 0.15;
    public const double TipDelaySeconds = 0.4;
    public const double LongPressSeconds = 0.5;

    /// <summary>A divider longer than this keeps its pill beside the pointer instead of at its centre.</summary>
    public const double FollowSpan = 480;

    public const double MouseSlop = 1;
    public const double CoarseSlop = 4;
    public const string TipText = "Resize";

    private static readonly Cursor WestEast = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor NorthSouth = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor NorthWestSouthEast = new(StandardCursorType.BottomRightCorner);
    private static readonly Cursor NorthEastSouthWest = new(StandardCursorType.BottomLeftCorner);

    private readonly DragHandleView handle;
    private readonly SplitterGrip grip;
    private readonly Popup tip;
    private readonly Popup readout;
    private readonly Border tipChip;
    private readonly Border readoutChip;
    private readonly TextBlock readoutText;
    private readonly AnimatedScalar tipOpacity;
    private readonly AnimatedScalar tipLift;
    private TaskHandle? hoverTask;
    private TaskHandle? tipTask;
    private TaskHandle? longPressTask;
    private bool pointerOver;
    private bool hovered;
    private bool pressed;
    private bool dragging;
    private bool coarse;
    private bool focusVisible;
    private bool isActive = true;
    private double fineInset = FinePointerInset;
    private SplitterReport lastReport;
    private TopLevel? hookedTop;

    public Splitter(SplitterOrientation orientation, SplitterTarget target, SplitterCorner corner = SplitterCorner.BottomRight)
    {
        Orientation = orientation;
        Target = target;
        Corner = corner;
        Focusable = true;
        IsTabStop = true;
        ClipToBounds = false;
        Background = null;
        handle = new DragHandleView
        {
            HandleAxis = DragHandleView.Axis.Free,
            Slop = MouseSlop,
            OnBegin = OnHandleBegin,
            OnChange = OnHandleChange,
            OnEnd = OnHandleEnd,
            OnDoubleClick = OnHandleDoubleClick,
        };
        handle.Configure(null); // the grip is SplitterGrip's, not DragHandleView's own
        grip = new SplitterGrip(orientation, corner);
        Children.Add(handle);
        Children.Add(grip);

        tipChip = Chip(TipText, 11, SwiftFont.Weight.Medium, monospaced: false, new Thickness(8, 3), 8, out _);
        tip = NewPopup(tipChip);
        readoutChip = Chip("", 10.5, SwiftFont.Weight.Medium, monospaced: true, new Thickness(7, 2), 7, out readoutText);
        readout = NewPopup(readoutChip);
        Children.Add(tip);
        Children.Add(readout);
        tipOpacity = new AnimatedScalar(tipChip, 0, value => tipChip.Opacity = value);
        tipLift = new AnimatedScalar(tipChip, 3, value => tipChip.RenderTransform = new TranslateTransform(0, value));

        ApplyCursor();
        ApplyFineInset();
        AddHandler(PointerPressedEvent, OnPressTunnel, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPressBubble, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnReleaseAny, RoutingStrategies.Bubble, handledEventsToo: true);
        handle.PointerCaptureLost += (_, _) => LoseDrag();
        AutomationProperties.SetName(this, "Resize");
    }

    public SplitterOrientation Orientation { get; }

    public SplitterCorner Corner { get; private set; }

    public SplitterTarget Target { get; set; }

    /// <summary>The accessible name ("Resize sidebar", "Resize Chat and Browser").</summary>
    public string Label
    {
        get => AutomationProperties.GetName(this) ?? "Resize";
        set => AutomationProperties.SetName(this, value);
    }

    /// <summary>A hint appended to the accessible help text ("Double-click for the automatic size").</summary>
    public string? HelpHint { get; set; }

    /// <summary>The side whose pane collapses, for Space and Ctrl+Arrow (logical; mirrored in right-to-left).</summary>
    public SplitterSide CollapseSide { get; set; }

    /// <summary>Whether a drag shows the size readout beside the pill (the workspace gutters; keyboard focus always shows it).</summary>
    public bool ShowsReadout { get; set; }

    /// <summary>Whether a right-to-left layout mirrors this splitter's x travel and arrows (dividers yes; grips placed in window coordinates no).</summary>
    public bool MirrorsInRightToLeft { get; set; } = true;

    /// <summary>The fine-pointer inset on each side across the divider (1 for a 12 DIP divider; 0 for edges and corners).</summary>
    public double FineInset
    {
        get => fineInset;
        set
        {
            fineInset = value;
            ApplyFineInset();
        }
    }

    /// <summary>Inactive: no hit, no cursor, no focus, no grip.</summary>
    public bool IsActive
    {
        get => isActive;
        set
        {
            if (value == isActive) return;
            isActive = value;
            handle.IsActive = value;
            Focusable = value;
            IsTabStop = value;
            if (!value)
            {
                CancelTimers();
                pointerOver = hovered = pressed = false;
                if (dragging) LoseDrag();
                UpdateLook();
            }
        }
    }

    /// <summary>The mechanics underneath.</summary>
    public DragHandleView Handle => handle;

    public SplitterGrip Grip => grip;

    public bool IsMirrored => MirrorsInRightToLeft && FlowDirection == FlowDirection.RightToLeft && Orientation != SplitterOrientation.Horizontal;

    public bool IsHovered => hovered;

    public bool IsDragging => dragging;

    public bool IsFocusVisible => focusVisible;

    public bool IsTipOpen => tip.IsOpen;

    public bool IsReadoutOpen => readout.IsOpen;

    public string ReadoutText => readoutText.Text ?? "";

    public SplitterReport LastReport => lastReport;

    internal Popup TipPopup => tip;

    internal Popup ReadoutPopup => readout;

    internal Border TipChip => tipChip;

    public void SetCorner(SplitterCorner corner)
    {
        if (corner == Corner) return;
        Corner = corner;
        grip.SetCorner(corner);
        ApplyCursor();
    }

    /// <summary>Re-reads the target: the accessible value (raising property changes) and the readout text.</summary>
    public void Refresh()
    {
        var report = Target.Report();
        var previous = lastReport;
        lastReport = report;
        readoutText.Text = report.Readout;
        if (ControlAutomationPeer.FromElement(this) is { } peer && previous != report)
        {
            if (previous.Value != report.Value) peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, previous.Value, report.Value);
            if (previous.Minimum != report.Minimum) peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.MinimumProperty, previous.Minimum, report.Minimum);
            if (previous.Maximum != report.Maximum) peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.MaximumProperty, previous.Maximum, report.Maximum);
            if (previous.Text != report.Text) peer.RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, previous.Text, report.Text);
        }
        PlaceChips();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SplitterAutomationPeer(this);

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children) child.Arrange(new Rect(finalSize));
        PlaceChips();
        return finalSize;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        hookedTop = TopLevel.GetTopLevel(this);
        if (hookedTop is not null)
        {
            SplitterPointer.Hook(hookedTop);
            hookedTop.PropertyChanged += OnTopPropertyChanged;
            coarse = SplitterPointer.GetIsCoarse(hookedTop);
            ApplyFineInset();
        }
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (hookedTop is not null) hookedTop.PropertyChanged -= OnTopPropertyChanged;
        hookedTop = null;
        CancelTimers();
        if (dragging) LoseDrag();
        pointerOver = hovered = pressed = false;
        focusVisible = false;
        tip.IsOpen = false;
        readout.IsOpen = false;
        grip.SetShown(false);
        grip.SetDragging(false);
        grip.SetFocusRing(false);
        grip.Settle();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FlowDirectionProperty) ApplyCursor();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (!isActive) return;
        pointerOver = true;
        if (e.Pointer.Type != PointerType.Mouse || dragging) return;
        Follow(e);
        CancelTimers();
        hoverTask = After(HoverDelaySeconds, () =>
        {
            hovered = true;
            UpdateLook();
        });
        tipTask = After(HoverDelaySeconds + TipDelaySeconds, () =>
        {
            if (pointerOver && !pressed && !dragging) ShowTip(true);
        });
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!isActive || dragging || e.Pointer.Type != PointerType.Mouse) return;
        Follow(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        pointerOver = false;
        if (dragging || pressed) return;
        CancelTimers();
        hovered = false;
        ShowTip(false);
        UpdateLook();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        focusVisible = isActive && e.NavigationMethod != NavigationMethod.Pointer;
        Refresh();
        UpdateLook();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        focusVisible = false;
        UpdateLook();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!isActive || e.Handled || dragging) return;
        SplitterKey? key = e.Key switch
        {
            Key.Left => SplitterKey.Left,
            Key.Right => SplitterKey.Right,
            Key.Up => SplitterKey.Up,
            Key.Down => SplitterKey.Down,
            Key.Home => SplitterKey.Home,
            Key.End => SplitterKey.End,
            Key.Enter => SplitterKey.Enter,
            Key.Space => SplitterKey.Space,
            _ => null,
        };
        if (key is not { } pressedKey) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var command = SplitterMath.Command(pressedKey, shift, control, Orientation, IsMirrored, CollapseSide);
        if (!Execute(command)) return;
        e.Handled = true;
        focusVisible = true;
        Refresh();
        UpdateLook();
    }

    /// <summary>Runs a keyboard command on the target; returns whether it was one.</summary>
    public bool Execute(SplitterCommand command)
    {
        switch (command.Kind)
        {
            case SplitterCommandKind.Step:
                Target.Step(new Vector(command.Dx, command.Dy));
                return true;
            case SplitterCommandKind.ToMinimum:
                Target.StepTo(false);
                return true;
            case SplitterCommandKind.ToMaximum:
                Target.StepTo(true);
                return true;
            case SplitterCommandKind.Reset:
                Target.Reset();
                return true;
            case SplitterCommandKind.Collapse:
                Target.Collapse(true);
                return true;
            case SplitterCommandKind.Restore:
                Target.Collapse(false);
                return true;
            case SplitterCommandKind.Toggle:
                Target.Collapse(!Target.Report().IsCollapsed);
                return true;
            default:
                return false;
        }
    }

    // Pointer down settles everything first (DESIGN 4.2), then the drag starts from the settled layout. A second press
    // of a double-click keeps the reset's spring (the reset runs on that same press, after this). Focus is not taken
    // by a press: the splitter stays out of the pointer's way, reached by Tab or F6 only.
    private void OnPressTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (!isActive || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var fine = e.Pointer.Type == PointerType.Mouse;
        handle.Slop = fine ? MouseSlop : CoarseSlop;
        if (e.ClickCount < 2) Target.Settle();
        grip.Settle();
        pressed = true;
        CancelTimers();
        ShowTip(false);
        Focusable = false;
        if (!fine)
        {
            hovered = true;
            longPressTask = After(LongPressSeconds, () =>
            {
                if (pressed && !dragging) ShowTip(true);
            });
        }
        else
        {
            Follow(e);
            hovered = true;
        }
        UpdateLook();
    }

    private void OnPressBubble(object? sender, PointerPressedEventArgs e) => Focusable = isActive;

    private void OnReleaseAny(object? sender, PointerReleasedEventArgs e)
    {
        if (!pressed) return;
        pressed = false;
        longPressTask?.Cancel();
        if (e.Pointer.Type != PointerType.Mouse)
        {
            hovered = false;
            ShowTip(false);
        }
        else
        {
            hovered = pointerOver;
        }
        UpdateLook();
    }

    private void OnHandleBegin()
    {
        if (!isActive) return;
        dragging = true;
        longPressTask?.Cancel();
        ShowTip(false);
        Target.Begin();
        Refresh();
        UpdateLook();
    }

    private void OnHandleChange(Size travel)
    {
        if (!dragging) return;
        var dx = IsMirrored ? -travel.Width : travel.Width;
        Target.Move(Orientation switch
        {
            SplitterOrientation.Vertical => new Vector(dx, 0),
            SplitterOrientation.Horizontal => new Vector(0, travel.Height),
            _ => new Vector(dx, travel.Height),
        });
        Refresh();
    }

    private void OnHandleEnd()
    {
        if (!dragging) return;
        dragging = false;
        Target.End(true);
        Refresh();
        UpdateLook();
    }

    private void OnHandleDoubleClick()
    {
        if (!isActive) return;
        Target.Reset();
        Refresh();
    }

    // A capture taken away mid-drag (or the splitter leaving the tree) ends the target's drag where it is.
    private void LoseDrag()
    {
        if (!dragging) return;
        dragging = false;
        pressed = false;
        Target.End(true);
        Refresh();
        UpdateLook();
    }

    private void UpdateLook()
    {
        var showsGrip = isActive && (hovered || pressed || dragging || focusVisible);
        grip.SetShown(showsGrip);
        grip.SetDragging(isActive && dragging);
        grip.SetFocusRing(isActive && focusVisible);
        var showsReadout = isActive && ((dragging && ShowsReadout) || (focusVisible && !dragging));
        if (showsReadout) PlaceChips();
        readout.IsOpen = showsReadout && TopLevel.GetTopLevel(this) is not null;
    }

    private void ShowTip(bool show)
    {
        if (!show)
        {
            tipOpacity.Set(0, null);
            tipLift.Set(3, null);
            tip.IsOpen = false;
            return;
        }
        if (!isActive || TopLevel.GetTopLevel(this) is null) return;
        PlaceChips();
        tip.IsOpen = true;
        tipOpacity.Set(1, PortAnimation.EaseOut(0.2));
        tipLift.Set(0, PortAnimation.EaseOut(0.3));
    }

    private void Follow(PointerEventArgs e)
    {
        if (Orientation == SplitterOrientation.Both) return;
        var span = Orientation == SplitterOrientation.Vertical ? Bounds.Height : Bounds.Width;
        if (span <= FollowSpan)
        {
            grip.Follow(null);
            return;
        }
        var point = e.GetPosition(this);
        grip.Follow(Orientation == SplitterOrientation.Vertical ? point.Y : point.X);
        PlaceChips();
    }

    // Canvas `.spl .tip` and `.spl .rd`: beside the pill, on the trailing side of a vertical divider (leading when
    // mirrored), to the right of a horizontal divider's pill on its line, outside a corner; never over the pill.
    private void PlaceChips()
    {
        var size = Bounds.Size;
        var along = grip.Along;
        var mirrored = IsMirrored;
        (Point Tip, Point Readout) at = Orientation switch
        {
            SplitterOrientation.Vertical => (new Point(size.Width / 2 + (mirrored ? -10 : 10), along - 10), new Point(size.Width / 2 + (mirrored ? -10 : 10), along + 16)),
            SplitterOrientation.Horizontal => (new Point(along + 24, size.Height / 2 - 10), new Point(along + 24, size.Height / 2 - 9)),
            _ => Corner switch
            {
                SplitterCorner.BottomRight => (new Point(14, 14), new Point(14, -4)),
                SplitterCorner.BottomLeft => (new Point(size.Width - 14, 14), new Point(size.Width - 14, -4)),
                SplitterCorner.TopRight => (new Point(14, size.Height - 14), new Point(14, size.Height + 4)),
                _ => (new Point(size.Width - 14, size.Height - 14), new Point(size.Width - 14, size.Height + 4)),
            },
        };
        var leftward = Orientation == SplitterOrientation.Vertical ? mirrored : Orientation == SplitterOrientation.Both && Corner is SplitterCorner.BottomLeft or SplitterCorner.TopLeft;
        var upward = Orientation == SplitterOrientation.Both && Corner is SplitterCorner.TopLeft or SplitterCorner.TopRight;
        Place(tip, at.Tip, leftward, upward);
        Place(readout, at.Readout, leftward, upward);

        static void Place(Popup popup, Point point, bool leftward, bool upward)
        {
            popup.PlacementRect = new Rect(point, new Size(0.01, 0.01));
            popup.PlacementGravity = (leftward, upward) switch
            {
                (false, false) => PopupGravity.BottomRight,
                (true, false) => PopupGravity.BottomLeft,
                (false, true) => PopupGravity.TopRight,
                _ => PopupGravity.TopLeft,
            };
            popup.PlacementAnchor = PopupAnchor.TopLeft;
        }
    }

    private Popup NewPopup(Control child) => new()
    {
        Child = child,
        PlacementTarget = this,
        Placement = PlacementMode.AnchorAndGravity,
        IsLightDismissEnabled = false,
        IsHitTestVisible = false,
        Focusable = false,
    };

    // Canvas `.spl .tip` and `.spl .rd`: a quiet dark chip with a hairline and a soft shadow; never focusable, never hit.
    private static Border Chip(string text, double size, SwiftFont.Weight weight, bool monospaced, Thickness padding, double radius, out TextBlock label)
    {
        label = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEE)),
            TextWrapping = TextWrapping.NoWrap,
        };
        (monospaced ? SwiftFont.MonospacedSystemFont(size, weight) : SwiftFont.System(size, weight)).Apply(label);
        return new Border
        {
            Child = label,
            Padding = padding,
            CornerRadius = new CornerRadius(radius),
            Background = new SolidColorBrush(Color.FromArgb(0xDB, 0x10, 0x10, 0x13)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0.5),
            BoxShadow = BoxShadows.Parse("0 4 14 0 #73000000"),
            IsHitTestVisible = false,
            Focusable = false,
        };
    }

    private void ApplyCursor()
    {
        var mirrored = MirrorsInRightToLeft && FlowDirection == FlowDirection.RightToLeft;
        handle.HandleCursor = Orientation switch
        {
            SplitterOrientation.Vertical => WestEast,
            SplitterOrientation.Horizontal => NorthSouth,
            _ => (Corner is SplitterCorner.TopLeft or SplitterCorner.BottomRight) != mirrored ? NorthWestSouthEast : NorthEastSouthWest,
        };
        handle.DragCursor = handle.HandleCursor;
    }

    private void ApplyFineInset()
    {
        var inset = coarse ? 0 : fineInset;
        handle.Margin = Orientation switch
        {
            SplitterOrientation.Vertical => new Thickness(inset, 0),
            SplitterOrientation.Horizontal => new Thickness(0, inset),
            _ => default,
        };
    }

    private void OnTopPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != SplitterPointer.IsCoarseProperty) return;
        coarse = e.GetNewValue<bool>();
        ApplyFineInset();
    }

    private void CancelTimers()
    {
        hoverTask?.Cancel();
        tipTask?.Cancel();
        longPressTask?.Cancel();
        hoverTask = tipTask = longPressTask = null;
    }

    // The hover and tooltip delays run on the main actor's clock, so a test advances them; with no main actor bound
    // (a bare control in a test) the delayed step runs at once.
    private static TaskHandle? After(double seconds, Action action)
    {
        try
        {
            return MainActor.Spawn(async () =>
            {
                if (!await SwiftTask.TrySleep(TimeSpan.FromSeconds(seconds))) return;
                action();
            });
        }
        catch (SwiftTrapException)
        {
            action();
            return null;
        }
    }

    /// <summary>Separator semantics (DESIGN 4.2): a thumb with a name, a range value always inside its range (collapsed
    /// positions included), the state's own value text, and the percentage in the help text.</summary>
    private sealed class SplitterAutomationPeer(Splitter owner) : ControlAutomationPeer(owner), IRangeValueProvider, IValueProvider
    {
        private SplitterReport Current => owner.Target.Report();

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Thumb;

        protected override string GetLocalizedControlTypeCore() => owner.Orientation switch
        {
            SplitterOrientation.Vertical => "vertical splitter",
            SplitterOrientation.Horizontal => "horizontal splitter",
            _ => "corner splitter",
        };

        protected override string? GetNameCore() => owner.Label;

        protected override string? GetHelpTextCore()
        {
            var percent = $"{Current.Percent} %";
            return owner.HelpHint is { Length: > 0 } hint ? $"{percent}. {hint}" : percent;
        }

        protected override bool IsKeyboardFocusableCore() => owner.isActive;

        public bool IsReadOnly => !owner.Target.CanSetValue;

        public double Minimum => Current.Minimum;

        public double Maximum => Current.Maximum;

        public double Value => Current.Value;

        public double SmallChange => SplitterLimits.DefaultStep;

        public double LargeChange => SplitterLimits.DefaultLargeStep;

        public void SetValue(double value)
        {
            if (!owner.Target.CanSetValue) throw new InvalidOperationException("This splitter's value is read-only.");
            owner.Target.SetValue(SplitterMath.Clamp(value, Minimum, Maximum));
            owner.Refresh();
        }

        string? IValueProvider.Value => Current.Text;

        bool IValueProvider.IsReadOnly => true;

        void IValueProvider.SetValue(string? value) => throw new InvalidOperationException("Set the range value instead.");
    }
}

/// <summary>Whether a window's last pointer was coarse (touch or pen): its splitters then take their full 12 DIP.</summary>
public static class SplitterPointer
{
    public static readonly AttachedProperty<bool> IsCoarseProperty =
        AvaloniaProperty.RegisterAttached<TopLevel, bool>("IsCoarse", typeof(SplitterPointer));

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TopLevel, object> Hooked = new();

    public static bool GetIsCoarse(TopLevel top) => top.GetValue(IsCoarseProperty);

    public static void SetIsCoarse(TopLevel top, bool value) => top.SetValue(IsCoarseProperty, value);

    internal static void Hook(TopLevel top)
    {
        if (Hooked.TryGetValue(top, out _)) return;
        Hooked.Add(top, new object());
        void Note(object? sender, PointerEventArgs e)
        {
            var isCoarse = e.Pointer.Type != PointerType.Mouse;
            if (GetIsCoarse(top) != isCoarse) SetIsCoarse(top, isCoarse);
        }
        top.AddHandler(InputElement.PointerPressedEvent, Note, RoutingStrategies.Tunnel, handledEventsToo: true);
        top.AddHandler(InputElement.PointerMovedEvent, Note, RoutingStrategies.Tunnel, handledEventsToo: true);
    }
}
