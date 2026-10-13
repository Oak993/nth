// PORT-ONLY: Avalonia painter for SwiftTerm's AppleTerminalView cell grid. TextBlock.Render
// is sealed in Avalonia 12.1.2, so the painter composes one real SelectableTextBlock for
// accessible text and Copy; the cell painter owns glyph positions and selection highlights.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Views.Terminal.Port;

internal sealed class TerminalSurface : Decorator
{
    private readonly Func<Size> measureCell;
    private TerminalFrame? frame;
    private int historyOffset;
    private int[] textOffsets = [];
    private bool pressed;
    private TerminalPosition pressedCell;
    private readonly TerminalSelection selection = new();
    private readonly CaretLayer caret;
    private IBrush? cursorBrush;
    private bool hasFocus;
    private readonly OpacityLoopRunner blink;
    private Avalonia.Controls.Window? blinkWindow;
    private bool windowActive = true; // ED-NEW-perf-idle-2: a window that is not the active one shows a steady caret, not a blinking one

    public TerminalSurface(Func<Size> measureCell)
    {
        this.measureCell = measureCell;
        // MacTerminalView.swift:3271-3273 resetCursorRects: the I-beam over the whole body (the catalogue's pointer.ibeam-text).
        Cursor = new Cursor(StandardCursorType.Ibeam);
        // ED-NEW-softscroll-1 opt-out: the terminal takes the wheel itself (OnPointerWheelChanged): it scrolls its ConPTY history by
        // whole lines, or reports the wheel to a program that asked for the mouse (MacTerminalView.swift:3204-3254), so no soft
        // scroll view around it may take its wheel.
        SoftScroll.SetOwnsWheel(this, true);
        TextControl = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            // Keep the text control in the logical/automation tree and arranged in the viewport.
            // It does not paint a second, non-cell-aligned copy or intercept cell hit testing.
            Opacity = 0,
            IsHitTestVisible = false,
            Focusable = false,
        };
        TextControl.PropertyChanged += (_, _) => Redraw();
        Child = TextControl;
        // The caret is a layer of its own over the grid: its opacity is what blinks, which costs the composition one property and not a repaint of every cell.
        caret = new CaretLayer(this);
        VisualChildren.Add(caret);
        blink = new OpacityLoopRunner(caret, BlinkCycle, BlinkKeys, null, 1);
    }

    /// <summary>
    /// SwiftTerm Mac/MacCaretView.swift:104-123 (updateAnimation) as the catalogue measured it on the Mac app (terminal.block-cursor-blink, 2-chat-use.mov S 132.237-134.31):
    /// with the terminal focused the block fades out in 0.64 s, fades back in 0.64 s and holds a moment, a cycle of 1.4 s (the source: a 0.7 s fade there and back, repeating).
    /// </summary>
    internal static double BlinkOpacity(double seconds)
    {
        var phase = seconds % BlinkCycleSeconds;
        if (phase < BlinkFade) return 1 - phase / BlinkFade;
        if (phase < 2 * BlinkFade) return (phase - BlinkFade) / BlinkFade;
        return 1;
    }

    private const double BlinkFade = 0.64;
    private const double BlinkCycleSeconds = 1.4;

    /// <summary>The cycle as the compositor plays it (ED-NEW-perf-idle-2): the one length.</summary>
    internal static readonly TimeSpan BlinkCycle = TimeSpan.FromSeconds(BlinkCycleSeconds);

    /// <summary>
    /// ED-NEW-perf-idle-2: <see cref="BlinkOpacity"/> as the key frames of a composition animation that loops for ever on the
    /// render thread, linear between them: full, out to 0 at 0.64 s, back to full at 1.28 s, held to 1.4 s. The blink used to ask
    /// the frame clock for a frame at every display refresh for as long as the terminal had the keyboard, which kept main awake
    /// (a commit and a render per refresh, 240 a second on a fast panel) and never let the idle warm-up see a calm frame.
    /// </summary>
    internal static readonly IReadOnlyList<OpacityKey> BlinkKeys =
        [new(0f, 1f), new((float)(BlinkFade / BlinkCycleSeconds), 0f), new((float)(2 * BlinkFade / BlinkCycleSeconds), 1f), new(1f, 1f)];

    /// <summary>The caret layer opacity now (for tests).</summary>
    internal double CaretOpacity => caret.Opacity;

    /// <summary>Whether the blink is running (for tests).</summary>
    internal bool IsBlinking => blink.Running;

    public SelectableTextBlock TextControl { get; }
    public string? Text => TextControl.Text;
    public IBrush? Foreground { get => TextControl.Foreground; set => TextControl.Foreground = value; }
    public IBrush? Background { get => TextControl.Background; set => TextControl.Background = value; }
    public IBrush? SelectionBrush { get => TextControl.SelectionBrush; set => TextControl.SelectionBrush = value; }
    private int SelectionStart { get => TextControl.SelectionStart; set => TextControl.SelectionStart = value; }
    private int SelectionEnd { get => TextControl.SelectionEnd; set => TextControl.SelectionEnd = value; }
    private FontFamily FontFamily => TextControl.FontFamily;
    private FontStyle FontStyle => TextControl.FontStyle;
    private FontWeight FontWeight => TextControl.FontWeight;
    private double FontSize => TextControl.FontSize;
    public IBrush? CursorBrush
    {
        get => cursorBrush;
        set
        {
            cursorBrush = value;
            caret.InvalidateVisual();
        }
    }

    internal void ClearSelection()
    {
        selection.SelectNone();
        SelectionStart = SelectionEnd = 0;
    }

    /// <summary>MacTerminalView.swift:1178-1186 <c>allowMouseReporting</c>: whether a program that asked for the mouse is told of it.</summary>
    internal bool AllowMouseReporting { get; set; } = true;

    /// <summary>What the running program asked for (the screen mouse modes), read when an event arrives.</summary>
    internal Func<TerminalMouseState>? MouseState { get; set; }

    /// <summary>The bytes of a mouse event the program asked for, to be written to its input.</summary>
    internal event Action<byte[]>? MouseOutput;

    /// <summary>The selection cell range (for tests): the active range, or null.</summary>
    internal (TerminalPosition Start, TerminalPosition End, TerminalSelectionMode Mode)? SelectedCells =>
        selection.Active ? (selection.Start, selection.End, selection.Mode) : null;

    internal void ScrollPage(bool up)
    {
        if (frame is null) return;
        // SwiftTerm AppleTerminalView.swift:2710-2744: one page is terminal.rows.
        historyOffset = Math.Clamp(historyOffset + (up ? frame.Rows : -frame.Rows), 0, frame.HistoryCount);
        Redraw();
    }

    internal void EnsureCaretVisible()
    {
        if (frame is not { } current) return;
        // MacTerminalView.swift:3437-3446 only returns to the live screen when
        // the caret is outside the scrolled viewport, not on every input byte.
        var first = current.HistoryCount - historyOffset;
        var caret = current.HistoryCount + current.CursorRow;
        if (caret >= first && caret < first + current.Rows) return;
        historyOffset = 0;
        Redraw();
    }

    public void UpdateFrame(TerminalFrame next)
    {
        var changedVisibility = frame?.CursorVisible != next.CursorVisible;
        frame = next;
        historyOffset = Math.Min(historyOffset, next.HistoryCount);
        var lines = next.Lines.Select(line => string.Concat(line.Select(cell => cell.Text ?? " ")).TrimEnd()).ToArray();
        textOffsets = new int[lines.Length];
        for (var row = 1; row < lines.Length; row++)
            textOffsets[row] = textOffsets[row - 1] + lines[row - 1].Length + 1;
        TextControl.Text = string.Join('\n', lines);
        InvalidateMeasure();
        Redraw();
        if (changedVisibility) RestartBlink();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Decorator arranges the semantic child; measure it even though only the grid is painted.
        TextControl.Measure(availableSize);
        var cell = measureCell();
        var size = new Size(Math.Min(availableSize.Width, (frame?.Columns ?? 80) * cell.Width),
            Math.Min(availableSize.Height, (frame?.Rows ?? 25) * cell.Height));
        caret.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        caret.Arrange(new Rect(finalSize));
        return arranged;
    }

    private void Redraw()
    {
        InvalidateVisual();
        caret?.InvalidateVisual(); // (the text control raises changes while the constructor is still building the layer)
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        FocusChanged();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        FocusChanged();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // ED-NEW-perf-idle-2: the caret blinks only in the active window (a deactivated one keeps the block, steady)
        if (TopLevel.GetTopLevel(this) is Avalonia.Controls.Window window && !ReferenceEquals(window, blinkWindow))
        {
            Unwatch();
            blinkWindow = window;
            window.Activated += OnWindowActivated;
            window.Deactivated += OnWindowDeactivated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopBlink();
        Unwatch();
        base.OnDetachedFromVisualTree(e);
    }

    private void Unwatch()
    {
        if (blinkWindow is null) return;
        blinkWindow.Activated -= OnWindowActivated;
        blinkWindow.Deactivated -= OnWindowDeactivated;
        blinkWindow = null;
        windowActive = true;
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        windowActive = true;
        RestartBlink();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        windowActive = false;
        RestartBlink();
    }

    /// <summary>The terminal view (the surface or the host that owns the keyboard for it) gained or lost focus: a focused caret is a block that blinks, an unfocused one an outline.</summary>
    internal void FocusChanged()
    {
        var focused = IsKeyboardFocusWithin || Parent is InputElement { IsKeyboardFocusWithin: true };
        if (focused == hasFocus) return;
        hasFocus = focused;
        caret.InvalidateVisual();
        RestartBlink();
    }

    // MacCaretView.swift:104-123: the animation runs while the caret is shown in a focused terminal (updateCursorStyle's canBlink), from full opacity.
    private void RestartBlink()
    {
        StopBlink();
        caret.Opacity = 1;
        if (!hasFocus || !windowActive || frame is not { CursorVisible: true } || !FrameClock.IsBound) return;
        blink.Start(); // ED-NEW-perf-idle-2: the compositor plays the cycle; nothing on main moves it
    }

    private void StopBlink() => blink.Stop();

    public override void Render(DrawingContext context)
    {
        if (frame is not { } current) return;
        var cellSize = measureCell();
        var first = current.HistoryCount - historyOffset;
        var selectionFrom = Math.Min(SelectionStart, SelectionEnd);
        var selectionTo = Math.Max(SelectionStart, SelectionEnd);
        using var viewport = context.PushClip(new Rect(Bounds.Size));
        for (var row = 0; row < current.Rows && first + row < current.Lines.Length; row++)
        {
            var cells = current.Lines[first + row];
            var textIndex = textOffsets[first + row];
            var textEnd = first + row + 1 < textOffsets.Length
                ? textOffsets[first + row + 1] - 1 : Text?.Length ?? 0;
            for (var column = 0; column < cells.Length; column++)
            {
                var cell = cells[column];
                if (cell.IsContinuation) continue;
                var rect = new Rect(column * cellSize.Width, row * cellSize.Height,
                    Math.Max(1, cell.Width) * cellSize.Width, cellSize.Height);
                var foreground = cell.Foreground is { } fg ? new SolidColorBrush(fg) : Foreground ?? Brushes.White;
                var background = cell.Background is { } bg ? new SolidColorBrush(bg) : Background;
                if (cell.Inverse) (foreground, background) = (background ?? Brushes.Black, foreground);
                if (background is not null) context.FillRectangle(background, rect);
                var text = cell.Text ?? " ";
                if (SelectionBrush is { } selection && textIndex < Math.Min(selectionTo, textEnd)
                    && textIndex + text.Length > selectionFrom)
                    context.FillRectangle(selection, rect);
                if (cell.Text is { Length: > 0 })
                {
                    var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(FontFamily, FontStyle, cell.Bold ? FontWeight.Bold : FontWeight), FontSize, foreground);
                    using var clip = context.PushClip(rect);
                    context.DrawText(formatted, rect.Position);
                    if (cell.Underline)
                        context.DrawLine(new Pen(foreground, 1), new Point(rect.Left, rect.Bottom - 1), new Point(rect.Right, rect.Bottom - 1));
                }
                textIndex += text.Length;
            }
        }
    }

    // The caret as MacCaretView draws it: a block over the cell (its glyph in the background colour) while the terminal is focused, an outline of line width 3 when not.
    private void DrawCaret(DrawingContext context)
    {
        if (frame is not { } current) return;
        var cellSize = measureCell();
        if (historyOffset == 0 && current.CursorVisible && CursorBrush is { } cursor)
        {
            var column = current.CursorColumn;
            var cells = current.Lines[current.HistoryCount + current.CursorRow];
            if (cells[column].IsContinuation && column > 0) column--;
            var rect = new Rect(column * cellSize.Width, current.CursorRow * cellSize.Height,
                Math.Max(1, cells[column].Width) * cellSize.Width, cellSize.Height);
            // SwiftTerm MacCaretView: focused block, unfocused outline (lineWidth 3).
            if (IsKeyboardFocusWithin || Parent is InputElement { IsKeyboardFocusWithin: true })
            {
                context.FillRectangle(cursor, rect);
                if (cells[column].Text is { Length: > 0 } text)
                {
                    var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(FontFamily, FontStyle, cells[column].Bold ? FontWeight.Bold : FontWeight), FontSize, Brushes.Black);
                    using var clip = context.PushClip(rect);
                    context.DrawText(formatted, rect.Position);
                }
            }
            else context.DrawRectangle(null, new Pen(cursor, 3), rect);
        }
    }

    private sealed class CaretLayer(TerminalSurface owner) : Control
    {
        public override void Render(DrawingContext context) => owner.DrawCaret(context);
    }


    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (frame is null || e.Delta.Y == 0) return;
        // Avalonia wheel deltas are line units. SwiftTerm MacTerminalView.swift:3232-3236
        // rounds a non-precise delta, retaining at least one line per nonzero event.
        var lines = (int)Math.Round(e.Delta.Y, MidpointRounding.AwayFromZero);
        if (lines == 0) lines = e.Delta.Y > 0 ? 1 : -1;
        // MacTerminalView.swift:3204 and :3243-3254: a program that asked for the mouse is told of the wheel (button 4 up, 5 down, once per line)
        // instead of the history scrolling.
        var state = Mouse;
        if (AllowMouseReporting && !ShiftBypasses(e.KeyModifiers, state) && state.Mode != TerminalMouseMode.Off)
        {
            var hit = Hit(e.GetPosition(this));
            var flags = TerminalMouse.EncodeButton(state.Mode, lines > 0 ? 4 : 5, release: false, e.KeyModifiers.HasFlag(KeyModifiers.Shift),
                e.KeyModifiers.HasFlag(KeyModifiers.Alt), e.KeyModifiers.HasFlag(KeyModifiers.Control));
            for (var line = 0; line < Math.Abs(lines); line++)
                MouseOutput?.Invoke(TerminalMouse.EncodeEvent(state.Encoding, flags, hit.Cell.Column, hit.ScreenRow, hit.PixelX, hit.PixelY));
            e.Handled = true;
            return;
        }
        historyOffset = Math.Clamp(historyOffset + lines, 0, frame.HistoryCount);
        Redraw();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        HandleSelectionKey(e);
    }

    internal void HandleSelectionKey(KeyEventArgs e)
    {
        if (e.Handled || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        // Same Windows copy chord used by ParagraphTextPresenter; preserve the composed
        // SelectableTextBlock's own Clipboard implementation rather than maintaining another copy.
        if (e.Key == Key.C && TextControl.CanCopy)
        {
            TextControl.Copy();
            e.Handled = true;
        }
        else if (e.Key == Key.A)
        {
            TextControl.SelectAll();
            e.Handled = true;
        }
    }

    private TerminalMouseState Mouse => MouseState?.Invoke() ?? default;

    // MacTerminalView.swift:2991-2993 shiftBypassesMouseReporting: shift selects locally unless the program asked (XTSHIFTESCAPE) for shift-clicks.
    private static bool ShiftBypasses(KeyModifiers modifiers, TerminalMouseState state) => modifiers.HasFlag(KeyModifiers.Shift) && !state.ShiftCapture;

    /// <summary>MacTerminalView.swift:2765-2793 <c>calculateMouseHit</c>: the cell under a point (held to the grid, a row counted from the top of the history), the
    /// screen row for a report, and the pixel.</summary>
    private (TerminalPosition Cell, int ScreenRow, int PixelX, int PixelY) Hit(Point point)
    {
        var current = frame ?? throw new InvalidOperationException("no frame");
        var cell = measureCell();
        var column = Math.Clamp((int)(point.X / cell.Width), 0, current.Columns - 1);
        var first = current.HistoryCount - historyOffset;
        var row = Math.Clamp((int)(point.Y / cell.Height) + first, 0, Math.Max(0, current.Lines.Length - 1));
        var screenRow = Math.Clamp(row - first, 0, current.Rows - 1);
        return (new TerminalPosition(column, row), screenRow, (int)Math.Clamp(point.X, 0, Bounds.Width), (int)Math.Clamp(point.Y, 0, Bounds.Height));
    }

    // MacTerminalView.swift:2753-2760 encodeMouseEvent, 2795-2802 sharedMouseEvent.
    private void Report(TerminalMouseState state, PointerEventArgs e, bool release, bool motion = false)
    {
        var hit = Hit(e.GetPosition(this));
        var flags = TerminalMouse.EncodeButton(state.Mode, 0, release, e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Alt), e.KeyModifiers.HasFlag(KeyModifiers.Control));
        MouseOutput?.Invoke(motion
            ? TerminalMouse.EncodeMotion(state.Encoding, flags, hit.Cell.Column, hit.ScreenRow, hit.PixelX, hit.PixelY)
            : TerminalMouse.EncodeEvent(state.Encoding, flags, hit.Cell.Column, hit.ScreenRow, hit.PixelX, hit.PixelY));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        pressed = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        if (frame is not { } current) return;
        // MacTerminalView.swift:2887-2890: a program that asked for presses is told of this one and nothing is selected.
        var state = Mouse;
        if (AllowMouseReporting && !ShiftBypasses(e.KeyModifiers, state) && state.Mode.SendsButtonPress())
        {
            Report(state, e, release: false);
            return;
        }
        var hit = Hit(e.GetPosition(this)).Cell;
        pressedCell = hit;
        // MacTerminalView.swift:2895-2917: one click drops an active selection (a shift click extends it), two select the word or expression, more the row.
        switch (e.ClickCount)
        {
            case 1:
                if (selection.Active)
                {
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) selection.ShiftExtend(hit, current);
                    else selection.SelectNone();
                }
                break;
            case 2:
                selection.SelectWordOrExpression(hit, current, current.HistoryCount - historyOffset);
                break;
            default:
                selection.SelectRow(hit.Row, current);
                break;
        }
        ShowSelection();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (frame is not { } current) return;
        var state = Mouse;
        if (!pressed)
        {
            // MacTerminalView.swift:3152-3158 mouseMoved: a program that asked for every motion is told of it (a released button, 3, plus the motion bit).
            if (state.Mode.SendsMotion()) Report(state, e, release: true, motion: true);
            return;
        }
        // MacTerminalView.swift:2992-3012 mouseDragged.
        if (AllowMouseReporting && !ShiftBypasses(e.KeyModifiers, state))
        {
            if (state.Mode.SendsButtonTracking())
            {
                Report(state, e, release: false, motion: true);
                e.Handled = true;
                return;
            }
            if (state.Mode != TerminalMouseMode.Off)
            {
                e.Handled = true;
                return;
            }
        }
        var hit = Hit(e.GetPosition(this)).Cell;
        if (!selection.Active)
        {
            // SwiftTerm starts at the first drag event, which lies within a pixel or two of the press; here the pointer may have moved further between
            // two samples, so the start is the pressed cell (ED-332).
            selection.SetSoftStart(pressedCell);
            selection.StartSelection();
        }
        selection.DragExtend(hit, current);
        ShowSelection();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!pressed) return;
        pressed = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        // MacTerminalView.swift:2941-2944 mouseUp: a program that asked for releases is told of it (X10 asks for none but this one, as SwiftTerm does).
        var state = Mouse;
        if (frame is not null && AllowMouseReporting && !ShiftBypasses(e.KeyModifiers, state) && state.Mode.SendsButtonRelease())
            Report(state, e, release: true);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        pressed = false;
        base.OnPointerCaptureLost(e);
    }

    // The cell range as the text control selection (its text is the grid lines, joined by newlines), which paints the highlight and copies.
    private void ShowSelection()
    {
        if (frame is null || !selection.Active || !selection.HasRange)
        {
            SelectionStart = SelectionEnd = 0;
        }
        else
        {
            var forward = TerminalPosition.Compare(selection.Start, selection.End) <= 0;
            SelectionStart = TextIndexOf(forward ? selection.Start : selection.End);
            SelectionEnd = TextIndexOf(forward ? selection.End : selection.Start);
        }
        Redraw();
    }

    private int TextIndexOf(TerminalPosition position)
    {
        if (frame is not { } current) return 0;
        var row = Math.Clamp(position.Row, 0, current.Lines.Length - 1);
        var column = Math.Clamp(position.Column, 0, current.Columns);
        var length = current.Lines[row].Take(column).Sum(value => (value.Text ?? " ").Length);
        var end = row + 1 < textOffsets.Length ? textOffsets[row + 1] - 1 : Text?.Length ?? 0;
        return Math.Min(end, textOffsets[row] + length);
    }
}
