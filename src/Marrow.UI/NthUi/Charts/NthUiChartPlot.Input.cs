// PORT-ONLY: web surfaces W16b (DESIGN.md 7.2 "lets the user drag a line or bar point ... Keyboard: focus a point, Up and Down change
// it by the axis step", 10 keyboard and assistive technology; ED-NEW-web-w16b-1). No Swift counterpart. The pointer and the
// keyboard of the plot: hovering or pressing finds the nearest column or point and shows its readout; on an editable chart a
// press picks the column and a drag moves its point; a plot that cannot be edited still takes the keyboard, so Left and Right
// walk its points and the readout (and the assistive technology's item status) follows.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Input;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi.Charts;

public sealed partial class NthUiChartPlot
{
    /// <summary>The nearest point or column to <paramref name="at"/>, for the pointer (null outside the plot).</summary>
    public (int Series, int Index)? Pick(Point at) => Pick(at, Now);

    private (int Series, int Index)? Pick(Point at, double now)
    {
        if (model.IsEmpty || Bounds.Width < 24) return null;
        var f = MakeFrame(now);
        if (model.Kind == NthUiChartKind.Scatter)
        {
            (int, int)? best = null;
            var distance = 28.0 * 28.0;
            for (var s = 0; s < model.Series.Count; s++)
            {
                for (var i = 0; i < model.Series[s].Values.Count; i++)
                {
                    if (!Present(s, i)) continue;
                    var dx = f.X(DispX(s, i, now)) - at.X;
                    var dy = f.Y(Disp(s, i, now)) - at.Y;
                    var d = dx * dx + dy * dy;
                    if (d > distance) continue;
                    distance = d;
                    best = (s, i);
                }
            }
            return best;
        }
        if (at.X < f.Plot.Left - 6 || at.X > f.Plot.Right + 6 || at.Y < f.Plot.Top - 12 || at.Y > Bounds.Height) return null;
        var column = Math.Clamp((int)Math.Floor((at.X - f.Plot.Left) / Math.Max(1, f.Band)), 0, model.Count - 1);
        int? chosen = null;
        var gap = double.PositiveInfinity;
        var running = 0.0;
        for (var s = 0; s < model.Series.Count; s++)
        {
            if (!Present(s, column)) continue;
            var value = Disp(s, column, now);
            double y;
            if (model.Stacked)
            {
                var from = running;
                running += Math.Max(0, value);
                y = (f.Y(from) + f.Y(running)) / 2;
            }
            else y = f.Y(value);
            var d = Math.Abs(y - at.Y);
            if (d < gap)
            {
                gap = d;
                chosen = s;
            }
        }
        return chosen is int found ? (found, column) : null;
    }

    private void SetHover((int Series, int Index)? at)
    {
        if (hover == at) return;
        hover = at;
        Refresh();
    }

    private void SetCursor(int series, int index)
    {
        if (cursor == (series, index)) return;
        cursor = (series, index);
        Refresh();
    }

    private void ClearCursor()
    {
        if (cursor is null) return;
        cursor = null;
        Refresh();
    }

    /// <summary>The active point changed: move the readout, repaint, and keep the item status for assistive technology.</summary>
    private void Refresh()
    {
        var now = Now;
        Track(now);
        InvalidateVisual();
        Wake(now);
        AutomationProperties.SetItemStatus(this, Readout?.Spoken ?? "");
    }

    // ---- pointer ---------------------------------------------------------------------------------------------------------

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var at = e.GetPosition(this);
        if (IsDragging)
        {
            Drag(at);
            e.Handled = true;
            return;
        }
        SetHover(Pick(at));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!IsDragging) SetHover(null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var at = e.GetPosition(this);
        if (!model.Editable || model.IsEmpty)
        {
            SetHover(Pick(at));
            return;
        }
        BeginDrag(at, e.Pointer);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!IsDragging) return;
        EndDrag(e.GetPosition(this));
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (IsDragging) EndDrag(null);
    }

    /// <summary>Presses at <paramref name="at"/>: picks the column under it and holds the axis range still.</summary>
    public void BeginDrag(Point at, IPointer? pointer = null)
    {
        if (!model.Editable || model.IsEmpty) return;
        var now = Now;
        var f = MakeFrame(now);
        var index = Math.Clamp((int)Math.Floor((at.X - f.Plot.Left) / Math.Max(1, f.Band)), 0, model.Count - 1);
        if (!Present(0, index)) return;
        yLo = NthSpring.At(f.YLo);
        yHi = NthSpring.At(f.YHi);
        hover = null;
        cursor = null;
        DragIndex = index;
        dragValue = series[0].Y[index].Value(now);
        pointer?.Capture(this);
        if (index < handles.Count)
        {
            tabStop = index;
            handles[index].Focus(NavigationMethod.Pointer);
        }
        Drag(at);
    }

    /// <summary>Moves the dragged point to the pointer's height; the runtime snaps and clamps what it keeps.</summary>
    public void Drag(Point at)
    {
        if (!IsDragging) return;
        var f = MakeFrame(Now);
        dragValue = Math.Clamp(f.ValueAt(at.Y), f.YLo, f.YHi);
        PointRequested?.Invoke(DragIndex, dragValue);
        Refresh();
    }

    /// <summary>Lets go: the dot settles onto the value the state keeps, and the axis range follows the data again.</summary>
    public void EndDrag(Point? at)
    {
        if (!IsDragging) return;
        var now = Now;
        var index = DragIndex;
        DragIndex = -1;
        if (model.Value(0, index) is double kept && index < series[0].Y.Length)
            series[0].Y[index] = Animate ? new NthSpring(dragValue, 0, kept, now, Calm) : NthSpring.At(kept);
        yLo = yLo.Retarget(model.YLo, now, Calm);
        yHi = yHi.Retarget(model.YHi, now, Calm);
        hover = at is { } p ? Pick(p, now) : null;
        Refresh();
        if (Moving(now)) context.Motion.Start(this);
    }

    // ---- keyboard on a plot that cannot be edited ------------------------------------------------------------------------

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (model.Editable || model.IsEmpty) return;
        if (e.NavigationMethod is not (NavigationMethod.Tab or NavigationMethod.Directional or NavigationMethod.Unspecified)) return;
        if (cursor is null && First() is { } first) SetCursor(first.Series, first.Index);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        if (!model.Editable) ClearCursor();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || model.Editable || model.IsEmpty) return;
        if (e.Key == Key.Escape)
        {
            ClearCursor();
            return;
        }
        // With no point chosen yet a navigation key chooses the first (the last for End) instead of stepping from it.
        var navigating = e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End;
        if (navigating && cursor is null && First() is { } start)
        {
            e.Handled = true;
            var landing = e.Key == Key.End ? Edge(start, true) ?? start : start;
            SetCursor(landing.Series, landing.Index);
            return;
        }
        if (cursor is not { } c) return;
        var ltr = FlowDirection == Avalonia.Media.FlowDirection.LeftToRight;
        (int Series, int Index)? next = e.Key switch
        {
            Key.Left => Step(c, ltr ? -1 : 1),
            Key.Right => Step(c, ltr ? 1 : -1),
            Key.Home => Edge(c, false),
            Key.End => Edge(c, true),
            Key.Up => Across(c, -1),
            Key.Down => Across(c, 1),
            _ => null,
        };
        if (next is not { } n) return;
        e.Handled = true;
        SetCursor(n.Series, n.Index);
    }

    private (int Series, int Index)? First()
    {
        for (var s = 0; s < model.Series.Count; s++)
        {
            for (var i = 0; i < model.Series[s].Values.Count; i++)
            {
                if (Present(s, i)) return (s, i);
            }
        }
        return null;
    }

    private (int Series, int Index)? Step((int Series, int Index) from, int direction)
    {
        for (var i = from.Index + direction; i >= 0 && i < model.Series[from.Series].Values.Count; i += direction)
        {
            if (Present(from.Series, i)) return (from.Series, i);
        }
        return null;
    }

    private (int Series, int Index)? Edge((int Series, int Index) from, bool end)
    {
        var count = model.Series[from.Series].Values.Count;
        for (var k = 0; k < count; k++)
        {
            var i = end ? count - 1 - k : k;
            if (Present(from.Series, i)) return (from.Series, i);
        }
        return null;
    }

    private (int Series, int Index)? Across((int Series, int Index) from, int direction)
    {
        for (var s = from.Series + direction; s >= 0 && s < model.Series.Count; s += direction)
        {
            if (Present(s, Math.Min(from.Index, model.Series[s].Values.Count - 1))) return (s, Math.Min(from.Index, model.Series[s].Values.Count - 1));
        }
        return null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        context.Motion.Remove(this);
        if (IsDragging) EndDrag(null);
        hover = null;
    }
}
