// PORT-ONLY: Avalonia painter for SwiftTerm's AppleTerminalView cell grid. TextBlock.Render
// is sealed in Avalonia 12.1.2, so the painter composes one real SelectableTextBlock for
// accessible text and Copy; the cell painter owns glyph positions and selection highlights.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Marrow.App.Views.Terminal.Port;

internal sealed class TerminalSurface : Decorator
{
    private readonly Func<Size> measureCell;
    private TerminalFrame? frame;
    private int historyOffset;
    private int[] textOffsets = [];
    private bool selecting;

    public TerminalSurface(Func<Size> measureCell)
    {
        this.measureCell = measureCell;
        TextControl = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            // Keep the text control in the logical/automation tree and arranged in the viewport.
            // It does not paint a second, non-cell-aligned copy or intercept cell hit testing.
            Opacity = 0,
            IsHitTestVisible = false,
            Focusable = false,
        };
        TextControl.PropertyChanged += (_, _) => InvalidateVisual();
        Child = TextControl;
    }

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
    public IBrush? CursorBrush { get; set; }

    public void UpdateFrame(TerminalFrame next)
    {
        frame = next;
        historyOffset = Math.Min(historyOffset, next.HistoryCount);
        var lines = next.Lines.Select(line => string.Concat(line.Select(cell => cell.Text ?? " ")).TrimEnd()).ToArray();
        textOffsets = new int[lines.Length];
        for (var row = 1; row < lines.Length; row++)
            textOffsets[row] = textOffsets[row - 1] + lines[row - 1].Length + 1;
        TextControl.Text = string.Join('\n', lines);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Decorator arranges the semantic child; measure it even though only the grid is painted.
        TextControl.Measure(availableSize);
        var cell = measureCell();
        return new Size(Math.Min(availableSize.Width, (frame?.Columns ?? 80) * cell.Width),
            Math.Min(availableSize.Height, (frame?.Rows ?? 25) * cell.Height));
    }

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

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (frame is null || e.Delta.Y == 0) return;
        // Avalonia wheel deltas are line units. SwiftTerm MacTerminalView.swift:3232-3236
        // rounds a non-precise delta, retaining at least one line per nonzero event.
        var lines = (int)Math.Round(e.Delta.Y, MidpointRounding.AwayFromZero);
        if (lines == 0) lines = e.Delta.Y > 0 ? 1 : -1;
        historyOffset = Math.Clamp(historyOffset + lines, 0, frame.HistoryCount);
        InvalidateVisual();
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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        SelectionStart = SelectionEnd = TextIndexAt(e.GetPosition(this));
        selecting = true;
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!selecting) return;
        SelectionEnd = TextIndexAt(e.GetPosition(this));
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!selecting) return;
        selecting = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        selecting = false;
        base.OnPointerCaptureLost(e);
    }

    private int TextIndexAt(Point point)
    {
        if (frame is not { } current) return 0;
        var cell = measureCell();
        var row = Math.Clamp((int)Math.Floor(point.Y / cell.Height), 0, current.Rows - 1)
            + current.HistoryCount - historyOffset;
        var column = Math.Clamp((int)Math.Floor(point.X / cell.Width), 0, current.Columns);
        var length = current.Lines[row].Take(column).Sum(value => (value.Text ?? " ").Length);
        var end = row + 1 < textOffsets.Length ? textOffsets[row + 1] - 1 : Text?.Length ?? 0;
        return Math.Min(end, textOffsets[row] + length);
    }
}
