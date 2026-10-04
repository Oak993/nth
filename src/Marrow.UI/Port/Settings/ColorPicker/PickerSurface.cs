// OD-0099 Look Picks, Q-CZ-3 B: one drawn area of Marrow's glass picker: the saturation-brightness square, the hue strip, the opacity
// strip or the row of recent colours (lp-pickers.js, pickerB). Pointer: a press sets the value under it, a drag follows it, the release
// commits (drag()). Keyboard: the design states none; see ED-NEW-f-cz-2 in porting/engineering_decisions.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Marrow.UI.Port.Settings.ColorPicker;

internal sealed class PickerSurface : Control
{
    // ENGINEERING DECISION (the design's stylesheet is not in the repository; lp-pickers.js fixes only the 16 px strip thumb, the 18 px
    // square thumb, the 150 px square and the widths): the strips are 16 px high with a 10 px track, the swatches are 20 px round (CZ-12.6's
    // well size) 8 px apart, the checkerboard under the opacity strip has 4 px cells.
    internal const double SquareHeight = 150;
    internal const double StripHeight = 16;
    internal const double TrackHeight = 10;
    internal const double SwatchSize = 20;
    internal const double SwatchGap = 8;
    private const double SquareRadius = 6;
    private const double CheckCell = 4;

    /// <summary>The step of an arrow key; with Shift, ten times as much. Square: a hundredth of the saturation or value; hue: a degree; opacity: a hundredth.</summary>
    internal const double SquareStep = 0.01, HueStep = 1, AlphaStep = 0.01, ShiftFactor = 10;

    private PickerColor colour;
    private IReadOnlyList<string> recent = [];
    private int cursor;
    private bool keyboardFocus;

    public PickerSurface(PickerSurfaceKind kind)
    {
        Kind = kind;
        Focusable = true;
        Cursor = kind == PickerSurfaceKind.Recent ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Cross);
    }

    internal PickerSurfaceKind Kind { get; }

    /// <summary>A colour edit: the new colour and whether it is committed (a release, a hex entry, a recent click).</summary>
    internal Action<PickerColor, bool>? Edit { get; set; }

    internal PickerColor Colour
    {
        get => colour;
        set { colour = value; InvalidateVisual(); }
    }

    internal IReadOnlyList<string> Recent
    {
        get => recent;
        set { recent = value; cursor = Math.Clamp(cursor, 0, Math.Max(0, value.Count - 1)); InvalidateVisual(); }
    }

    /// <summary>The recent swatch the keyboard stands on.</summary>
    internal int RecentCursor => cursor;

    internal static double HeightFor(PickerSurfaceKind kind) => kind switch
    {
        PickerSurfaceKind.Square => SquareHeight,
        PickerSurfaceKind.Recent => SwatchSize,
        _ => StripHeight,
    };

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, HeightFor(Kind));

    // ---- pointer (drag(): a press edits, a move edits, the release edits and commits)
    private bool dragging;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(this);
        dragging = true;
        keyboardFocus = false;
        Focus();
        Point(e.GetPosition(this), Kind == PickerSurfaceKind.Recent);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragging && Kind != PickerSurfaceKind.Recent) Point(e.GetPosition(this), false);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!dragging) return;
        dragging = false;
        e.Pointer.Capture(null);
        if (Kind != PickerSurfaceKind.Recent) Point(e.GetPosition(this), true);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        dragging = false;
    }

    /// <summary>The pointer at <paramref name="p"/> (this area's own pixels): what drag()'s callback does.</summary>
    internal void Point(Point p, bool commit)
    {
        double w = Bounds.Width, h = Bounds.Height, c = colour.A;
        switch (Kind)
        {
            case PickerSurfaceKind.Square:
                var (s, v) = PickerMath.SquareAt(p.X, p.Y, w, h);
                Edit?.Invoke(colour with { S = s, V = v }, commit);
                break;
            case PickerSurfaceKind.Hue:
                Edit?.Invoke(colour with { H = PickerMath.HueAt(p.X, w) }, commit);
                break;
            case PickerSurfaceKind.Opacity:
                Edit?.Invoke(colour with { A = PickerMath.AlphaAt(p.X, w) }, commit);
                break;
            default:
                var i = (int)Math.Floor(p.X / (SwatchSize + SwatchGap));
                if (i >= 0 && i < recent.Count && p.X - i * (SwatchSize + SwatchGap) <= SwatchSize && p.Y >= 0 && p.Y <= SwatchSize)
                {
                    cursor = i;
                    Edit?.Invoke(PickerMath.FromHex(recent[i], c), true);   // a recent swatch keeps the current alpha
                }
                break;
        }
    }

    // ---- keyboard
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        keyboardFocus = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        keyboardFocus = false;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var k = e.Key;
        var big = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var f = big ? ShiftFactor : 1;
        switch (Kind)
        {
            case PickerSurfaceKind.Square:
                // arrows move the thumb the way it moves on screen: right is more saturated, up is brighter
                var s = colour.S + (k == Key.Right ? SquareStep * f : k == Key.Left ? -SquareStep * f : 0);
                var v = colour.V + (k == Key.Up ? SquareStep * f : k == Key.Down ? -SquareStep * f : 0);
                if (k is Key.Home) s = 0;
                if (k is Key.End) s = 1;
                if (k is Key.PageUp) v = 1;
                if (k is Key.PageDown) v = 0;
                if (k is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)) return;
                Edit?.Invoke(colour with { S = PickerMath.Clamp(Round(s), 0, 1), V = PickerMath.Clamp(Round(v), 0, 1) }, false);
                break;
            case PickerSurfaceKind.Hue:
                if (!Along(k, out var dh)) return;
                Edit?.Invoke(colour with { H = k == Key.Home ? 0 : k == Key.End ? PickerMath.HueMax : PickerMath.Clamp(colour.H + dh * HueStep * f, 0, PickerMath.HueMax) }, false);
                break;
            case PickerSurfaceKind.Opacity:
                if (!Along(k, out var da)) return;
                Edit?.Invoke(colour with { A = k == Key.Home ? 0 : k == Key.End ? 1 : PickerMath.Clamp(Round(colour.A + da * AlphaStep * f), 0, 1) }, false);
                break;
            default:
                if (recent.Count == 0) return;
                if (k == Key.Left) cursor = Math.Max(0, cursor - 1);
                else if (k == Key.Right) cursor = Math.Min(recent.Count - 1, cursor + 1);
                else if (k is Key.Enter or Key.Space) Edit?.Invoke(PickerMath.FromHex(recent[cursor], colour.A), true);
                else return;
                InvalidateVisual();
                break;
        }
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        // the release of a key that moved a value commits it, as the pointer's release does
        if (Kind == PickerSurfaceKind.Recent) return;
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            Edit?.Invoke(colour, true);
            e.Handled = true;
        }
    }

    private static bool Along(Key k, out int d)
    {
        d = k is Key.Right or Key.Up ? 1 : k is Key.Left or Key.Down ? -1 : 0;
        return d != 0 || k is Key.Home or Key.End;
    }

    private static double Round(double x) => Math.Round(x, 6);

    // ---- drawing
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        switch (Kind)
        {
            case PickerSurfaceKind.Square: RenderSquare(context, w); break;
            case PickerSurfaceKind.Recent: RenderRecent(context); break;
            default: RenderStrip(context, w); break;
        }
        if (keyboardFocus && Kind != PickerSurfaceKind.Recent)
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1), new Rect(0.5, 0.5, w - 1, Bounds.Height - 1), SquareRadius, SquareRadius);
    }

    private static Color Rgb(double[] c, double a = 1) => Color.FromArgb((byte)Math.Round(a * 255), (byte)PickerMath.Clamp(PickerMath.JsRound(c[0]), 0, 255), (byte)PickerMath.Clamp(PickerMath.JsRound(c[1]), 0, 255), (byte)PickerMath.Clamp(PickerMath.JsRound(c[2]), 0, 255));

    private static LinearGradientBrush Gradient(RelativePoint from, RelativePoint to, params (Color Colour, double At)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = from, EndPoint = to };
        foreach (var (c, at) in stops) brush.GradientStops.Add(new GradientStop(c, at));
        return brush;
    }

    private static readonly RelativePoint Left = new(0, 0, RelativeUnit.Relative), Right = new(1, 0, RelativeUnit.Relative),
        Top = new(0, 0, RelativeUnit.Relative), Bottom = new(0, 1, RelativeUnit.Relative);

    private void RenderSquare(DrawingContext context, double w)
    {
        var h = Bounds.Height;
        var box = new RoundedRect(new Rect(0, 0, w, h), SquareRadius);
        using (context.PushClip(box))
        {
            // .hue (the pure hue), .wh (white to clear across), .bk (clear to black down)
            context.DrawRectangle(new SolidColorBrush(Rgb(PickerMath.HsvRgb(colour.H, 1, 1))), null, new Rect(0, 0, w, h));
            context.DrawRectangle(Gradient(Left, Right, (Colors.White, 0), (Color.FromArgb(0, 255, 255, 255), 1)), null, new Rect(0, 0, w, h));
            context.DrawRectangle(Gradient(Top, Bottom, (Color.FromArgb(0, 0, 0, 0), 0), (Colors.Black, 1)), null, new Rect(0, 0, w, h));
        }
        var (x, y) = PickerMath.SquareThumbAt(colour.S, colour.V, w, h);
        Thumb(context, new Point(x + PickerMath.SquareThumb / 2, y + PickerMath.SquareThumb / 2), PickerMath.SquareThumb / 2, Rgb(PickerMath.HsvRgb(colour.H, colour.S, colour.V)));
    }

    private void RenderStrip(DrawingContext context, double w)
    {
        var track = new Rect(0, (StripHeight - TrackHeight) / 2, w, TrackHeight);
        var rounded = new RoundedRect(track, TrackHeight / 2);
        Color thumb;
        double u;
        if (Kind == PickerSurfaceKind.Hue)
        {
            // linear-gradient(90deg,#f00,#ff0 17%,#0f0 33%,#0ff 50%,#00f 67%,#f0f 83%,#f00)
            context.DrawRectangle(Gradient(Left, Right, (Color.FromRgb(255, 0, 0), 0), (Color.FromRgb(255, 255, 0), 0.17), (Color.FromRgb(0, 255, 0), 0.33), (Color.FromRgb(0, 255, 255), 0.5),
                (Color.FromRgb(0, 0, 255), 0.67), (Color.FromRgb(255, 0, 255), 0.83), (Color.FromRgb(255, 0, 0), 1)), null, rounded);
            u = colour.H / PickerMath.HueMax;
            thumb = Rgb(PickerMath.HsvRgb(colour.H, 1, 1));
        }
        else
        {
            using (context.PushClip(rounded))
            {
                for (var row = 0; row * CheckCell < TrackHeight; row++)
                    for (var col = 0; col * CheckCell < w; col++)
                        context.DrawRectangle(new SolidColorBrush((row + col) % 2 == 0 ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0xCC, 0xCC, 0xCC)), null,
                            new Rect(col * CheckCell, track.Y + row * CheckCell, CheckCell, Math.Min(CheckCell, TrackHeight - row * CheckCell)));
            }
            var rgb = PickerMath.HsvRgb(colour.H, colour.S, colour.V);
            context.DrawRectangle(Gradient(Left, Right, (Rgb(rgb, 0), 0), (Rgb(rgb, 1), 1)), null, rounded);
            u = colour.A;
            thumb = Rgb(rgb, colour.A);
        }
        var left = PickerMath.StripThumbAt(u, w);
        Thumb(context, new Point(left + PickerMath.StripThumb / 2, StripHeight / 2), PickerMath.StripThumb / 2, thumb);
    }

    private static void Thumb(DrawingContext context, Point centre, double radius, Color fill)
    {
        context.DrawEllipse(new SolidColorBrush(fill), new Pen(Brushes.White, 2), centre, radius - 1, radius - 1);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1), centre, radius, radius);
    }

    private void RenderRecent(DrawingContext context)
    {
        var now = PickerMath.HsvHex(colour);
        for (var i = 0; i < recent.Count; i++)
        {
            var c = new Point(i * (SwatchSize + SwatchGap) + SwatchSize / 2, SwatchSize / 2);
            var rgb = PickerMath.HexRgb(recent[i]);
            context.DrawEllipse(new SolidColorBrush(Rgb(rgb)), new Pen(new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)), 1), c, SwatchSize / 2 - 1, SwatchSize / 2 - 1);
            if (recent[i] == now) context.DrawEllipse(null, new Pen(Brushes.White, 2), c, SwatchSize / 2 + 1, SwatchSize / 2 + 1);   // .sw.on
            if (keyboardFocus && i == cursor) context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1), c, SwatchSize / 2 + 3, SwatchSize / 2 + 3);
        }
    }
}
