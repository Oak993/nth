using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Marrow.App.Views.Terminal.Port;

namespace Marrow.App.Tests.Views.Terminal;

// The per-cell painting TerminalSurface.Render did before ED-NEW-perf-motion-1 (a FormattedText, a clip and new brushes for every cell),
// verbatim, reading the surface's own state: the oracle the row painter must match pixel for pixel.
internal static class LegacyTerminalPaint
{
    public static void Render(TerminalSurface surface, DrawingContext context)
    {
        if (Field(surface, "frame") is not TerminalFrame current) return;
        var cellSize = ((Func<Size>)Field(surface, "measureCell")!)();
        var historyOffset = (int)Field(surface, "historyOffset")!;
        var textOffsets = (int[])Field(surface, "textOffsets")!;
        var control = surface.TextControl;
        var first = current.HistoryCount - historyOffset;
        var selectionFrom = Math.Min(control.SelectionStart, control.SelectionEnd);
        var selectionTo = Math.Max(control.SelectionStart, control.SelectionEnd);
        using var viewport = context.PushClip(new Rect(surface.Bounds.Size));
        for (var row = 0; row < current.Rows && first + row < current.Lines.Length; row++)
        {
            var cells = current.Lines[first + row];
            var textIndex = textOffsets[first + row];
            var textEnd = first + row + 1 < textOffsets.Length
                ? textOffsets[first + row + 1] - 1 : control.Text?.Length ?? 0;
            for (var column = 0; column < cells.Length; column++)
            {
                var cell = cells[column];
                if (cell.IsContinuation) continue;
                var rect = new Rect(column * cellSize.Width, row * cellSize.Height,
                    Math.Max(1, cell.Width) * cellSize.Width, cellSize.Height);
                var foreground = cell.Foreground is { } fg ? new SolidColorBrush(fg) : control.Foreground ?? Brushes.White;
                var background = cell.Background is { } bg ? new SolidColorBrush(bg) : control.Background;
                if (cell.Inverse) (foreground, background) = (background ?? Brushes.Black, foreground);
                if (background is not null) context.FillRectangle(background, rect);
                var text = cell.Text ?? " ";
                if (control.SelectionBrush is { } selection && textIndex < Math.Min(selectionTo, textEnd)
                    && textIndex + text.Length > selectionFrom)
                    context.FillRectangle(selection, rect);
                if (cell.Text is { Length: > 0 })
                {
                    var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(control.FontFamily, control.FontStyle, cell.Bold ? FontWeight.Bold : control.FontWeight), control.FontSize, foreground);
                    using var clip = context.PushClip(rect);
                    context.DrawText(formatted, rect.Position);
                    if (cell.Underline)
                        context.DrawLine(new Pen(foreground, 1), new Point(rect.Left, rect.Bottom - 1), new Point(rect.Right, rect.Bottom - 1));
                }
                textIndex += text.Length;
            }
        }
    }

    private static object? Field(TerminalSurface surface, string name) =>
        typeof(TerminalSurface).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(surface);
}
