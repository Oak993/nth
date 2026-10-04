// OD-0099 Look Picks, Q-CZ-3 B: one drawn area of Marrow's glass picker: the saturation-brightness square, the hue strip, the opacity
// strip or the row of recent colours (lp-pickers.js, pickerB). Pointer: a press sets the value under it, a drag follows it, the release
// commits (drag()). Keyboard: the design states none; see ED-NEW-f-cz-2 in porting/engineering_decisions.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Marrow.UI.Port.Settings.ColorPicker;

internal sealed class PickerSurface : Control
{
    // SOURCE FACT (design-src/look-picks/lp-page.css, variant B): .mg-sv is 150 px high, corner 10, an inner .5 px edge (--rim);
    // .mg-strip is 14 px high, corner 7, the same edge; the thumbs are 18 px with a 2 px white ring and a soft shadow, a strip's
    // thumb 2 px above its track and 1 px left of u * (w - 16) (top: -2px; margin-left: -1px); .sws is a 2 px gap of 24 px buttons,
    // each with a 16 px dot, the chosen one ringed (.sw.on::after: inset 1 px, 1.5 px --ring); .chk is a conic checker on an 8 px cell.
    internal const double SquareHeight = 150;
    internal const double SquareRadius = 10;
    internal const double StripHeight = 14;
    internal const double StripRadius = 7;
    internal const double ThumbSize = 18;
    internal const double ThumbRing = 2;
    internal const double StripThumbTop = -2;
    internal const double StripThumbLeft = -1;
    internal const double SwatchBox = 24;
    internal const double SwatchGap = 2;
    internal const double SwatchDot = 16;
    internal const double CheckCell = 8;
    internal const double RimWidth = 0.5;
    internal const double RingInset = 1;
    internal const double RingWidth = 1.5;

    /// <summary>The step of an arrow key (ED-NEW-f-cz-2: the design states none); with Shift, ten times as much. Square: a hundredth of the saturation or value; hue: a degree; opacity: a hundredth.</summary>
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
        AutomationProperties.SetName(this, kind switch
        {
            PickerSurfaceKind.Square => "Saturation and brightness",
            PickerSurfaceKind.Hue => "Hue",
            PickerSurfaceKind.Opacity => "Opacity",
            _ => "Recent colours",
        });
    }

    /// <summary>What a screen reader hears as the value (ED-NEW-f-cz-2: the design names only its hex field).</summary>
    internal string ValueText => Kind switch
    {
        PickerSurfaceKind.Square => $"Saturation {PickerMath.JsRound(colour.S * 100)} %, brightness {PickerMath.JsRound(colour.V * 100)} %",
        PickerSurfaceKind.Hue => $"{PickerMath.JsRound(colour.H)} degrees",
        PickerSurfaceKind.Opacity => $"{PickerMath.JsRound(colour.A * 100)} %",
        _ => recent.Count == 0 ? "" : recent[Math.Clamp(cursor, 0, recent.Count - 1)],
    };

    /// <summary>The range a hue or opacity strip covers, and its value; null for the square and the recent row.</summary>
    internal (double Minimum, double Maximum, double Small, double Large, double Value)? Range => Kind switch
    {
        PickerSurfaceKind.Hue => (0, PickerMath.HueMax, HueStep, HueStep * ShiftFactor, colour.H),
        PickerSurfaceKind.Opacity => (0, 1, AlphaStep, AlphaStep * ShiftFactor, colour.A),
        _ => null,
    };

    /// <summary>A screen reader's set-value: a hue or opacity strip takes it as a committed edit.</summary>
    internal void SetRange(double value)
    {
        if (Kind == PickerSurfaceKind.Hue) Edit?.Invoke(colour with { H = PickerMath.Clamp(value, 0, PickerMath.HueMax) }, true);
        else if (Kind == PickerSurfaceKind.Opacity) Edit?.Invoke(colour with { A = PickerMath.Clamp(Round(value), 0, 1) }, true);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => Range is null ? new PickerSurfacePeer(this) : new PickerRangePeer(this);

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
        PickerSurfaceKind.Recent => SwatchBox,
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
                var i = (int)Math.Floor(p.X / (SwatchBox + SwatchGap));
                if (i >= 0 && i < recent.Count && p.X - i * (SwatchBox + SwatchGap) <= SwatchBox && p.Y >= 0 && p.Y <= SwatchBox)
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
        if (keyboardFocus && Kind != PickerSurfaceKind.Recent)   // ED-NEW-f-cz-2: the design draws no keyboard focus
            context.DrawRectangle(null, new Pen(new SolidColorBrush(PickerTheme.Ring(this)), 1), new Rect(-1.5, -1.5, w + 3, Bounds.Height + 3), Kind == PickerSurfaceKind.Square ? SquareRadius + 1.5 : StripRadius + 1.5, Kind == PickerSurfaceKind.Square ? SquareRadius + 1.5 : StripRadius + 1.5);
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

    /// <summary>The inner .5 px edge (box-shadow: inset 0 0 0 .5px var(--rim)).</summary>
    private void Rim(DrawingContext context, Rect box, double radius) =>
        context.DrawRectangle(null, new Pen(new SolidColorBrush(PickerTheme.Rim(this)), RimWidth), box.Deflate(RimWidth / 2), Math.Max(0, radius - RimWidth / 2), Math.Max(0, radius - RimWidth / 2));

    private void RenderSquare(DrawingContext context, double w)
    {
        var h = Bounds.Height;
        var bounds = new Rect(0, 0, w, h);
        using (context.PushClip(new RoundedRect(bounds, SquareRadius)))
        {
            // .hue (the pure hue), .wh (white to clear across), .bk (black to clear, from the bottom up)
            context.DrawRectangle(new SolidColorBrush(Rgb(PickerMath.HsvRgb(colour.H, 1, 1))), null, bounds);
            context.DrawRectangle(Gradient(Left, Right, (Colors.White, 0), (Color.FromArgb(0, 255, 255, 255), 1)), null, bounds);
            context.DrawRectangle(Gradient(Bottom, Top, (Colors.Black, 0), (Color.FromArgb(0, 0, 0, 0), 1)), null, bounds);
        }
        Rim(context, bounds, SquareRadius);
        var (x, y) = PickerMath.SquareThumbAt(colour.S, colour.V, w, h);
        Thumb(context, new Rect(x, y, ThumbSize, ThumbSize), Rgb(PickerMath.HsvRgb(colour.H, colour.S, colour.V)), 0.45);
    }

    private void RenderStrip(DrawingContext context, double w)
    {
        var track = new Rect(0, 0, w, StripHeight);
        var rounded = new RoundedRect(track, StripRadius);
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
            // .chk: repeating-conic-gradient(var(--chk) 0 25%, transparent 0 50%) on an 8 px cell: the top right and bottom left quarters.
            // --chk is not defined anywhere in the design sources; a neutral grey is used (ED-NEW-f-cz-2).
            using (context.PushClip(rounded))
            {
                var chk = new SolidColorBrush(Color.FromArgb(0x59, 128, 128, 128));
                for (var row = 0; row * CheckCell < StripHeight; row++)
                    for (var col = 0; col * CheckCell < w; col++)
                    {
                        var x = col * CheckCell; var y = row * CheckCell;
                        context.DrawRectangle(chk, null, new Rect(x + CheckCell / 2, y, CheckCell / 2, CheckCell / 2));
                        context.DrawRectangle(chk, null, new Rect(x, y + CheckCell / 2, CheckCell / 2, CheckCell / 2));
                    }
            }
            var rgb = PickerMath.HsvRgb(colour.H, colour.S, colour.V);
            context.DrawRectangle(Gradient(Left, Right, (Rgb(rgb, 0), 0), (Rgb(rgb, 1), 1)), null, rounded);
            u = colour.A;
            thumb = Rgb(rgb, colour.A);
        }
        Rim(context, track, StripRadius);
        Thumb(context, new Rect(PickerMath.StripThumbAt(u, w) + StripThumbLeft, StripThumbTop, ThumbSize, ThumbSize), thumb, 0.4);
    }

    /// <summary>The 18 px thumb: box-shadow 0 0 0 2px #fff, 0 1px 4px rgba(0,0,0,.4 or .45).</summary>
    private static void Thumb(DrawingContext context, Rect box, Color fill, double shadow) =>
        context.DrawRectangle(new SolidColorBrush(fill), null, new RoundedRect(box, ThumbSize / 2),
            new BoxShadows(new BoxShadow { Spread = ThumbRing, Color = Colors.White }, [new BoxShadow { OffsetY = 1, Blur = 4, Color = Color.FromArgb((byte)Math.Round(shadow * 255), 0, 0, 0) }]));

    private void RenderRecent(DrawingContext context)
    {
        var now = PickerMath.HsvHex(colour);
        for (var i = 0; i < recent.Count; i++)
        {
            var box = new Rect(i * (SwatchBox + SwatchGap), 0, SwatchBox, SwatchBox);
            var c = box.Center;
            context.DrawEllipse(new SolidColorBrush(Rgb(PickerMath.HexRgb(recent[i]))), new Pen(new SolidColorBrush(PickerTheme.Rim(this)), RimWidth), c, SwatchDot / 2 - RimWidth / 2, SwatchDot / 2 - RimWidth / 2);
            var r = SwatchBox / 2 - RingInset - RingWidth / 2;
            if (recent[i] == now) context.DrawEllipse(null, new Pen(new SolidColorBrush(PickerTheme.Ring(this)), RingWidth), c, r, r);   // .sw.on::after
            if (keyboardFocus && i == cursor) context.DrawEllipse(null, new Pen(new SolidColorBrush(PickerTheme.Ring(this)), 1), c, SwatchBox / 2 + 1.5, SwatchBox / 2 + 1.5);
        }
    }
}
