// PORT-ONLY: web surfaces W16b (DESIGN.md 7.2 "chart (line, bar, area, pie, scatter)", 7.4 "Charts animate their marks in once",
// 7.5 accent only for the current value and focus, 10 keyboard and Reduce Motion; ED-NEW-web-w16b-1). No Swift counterpart. A
// pie drawn as a ring cut into slices in the monochrome ramp, with the total (or the slice the reader is on) in the middle.
// Each slice edge is a spring, so a slider that changes the shares swings the ring round to its new cut, and the whole ring is
// swept on once from the top. The slice under the pointer or the keyboard takes the accent and steps out from the ring.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi.Charts;

/// <summary>The rings and slices of a pie, as pure geometry.</summary>
public static class NthUiPieGeometry
{
    /// <summary>The point at <paramref name="angle"/> (radians, clockwise from twelve o'clock) on a circle.</summary>
    public static Point At(Point center, double radius, double angle) => new(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));

    /// <summary>The angle of <paramref name="point"/> from twelve o'clock, clockwise, in 0 to 2 pi.</summary>
    public static double AngleOf(Point center, Point point)
    {
        var angle = Math.Atan2(point.X - center.X, center.Y - point.Y);
        return angle < 0 ? angle + 2 * Math.PI : angle;
    }

    /// <summary>The slice between <paramref name="from"/> and <paramref name="to"/> (a ring piece; a whole ring when it spans a full turn).</summary>
    public static StreamGeometry Slice(Point center, double outer, double inner, double from, double to)
    {
        var geometry = new StreamGeometry();
        using var c = geometry.Open();
        if (to - from >= 2 * Math.PI - 1e-4)
        {
            Piece(c, center, outer, inner, from, from + Math.PI);
            Piece(c, center, outer, inner, from + Math.PI, from + 2 * Math.PI);
        }
        else Piece(c, center, outer, inner, from, to);
        return geometry;
    }

    private static void Piece(StreamGeometryContext c, Point center, double outer, double inner, double from, double to)
    {
        var large = to - from > Math.PI;
        c.BeginFigure(At(center, outer, from), true);
        c.ArcTo(At(center, outer, to), new Size(outer, outer), 0, large, SweepDirection.Clockwise);
        c.LineTo(At(center, inner, to));
        c.ArcTo(At(center, inner, from), new Size(inner, inner), 0, large, SweepDirection.CounterClockwise);
        c.EndFigure(true);
    }

    /// <summary>The slice that holds <paramref name="angle"/>, given the cumulative edges (0 to 1) of the slices.</summary>
    public static int SliceAt(IReadOnlyList<double> edges, double angle)
    {
        var turn = angle / (2 * Math.PI);
        for (var i = 0; i + 1 < edges.Count; i++)
        {
            if (turn >= edges[i] && turn < edges[i + 1]) return i;
        }
        return -1;
    }
}

/// <summary>The ring of a pie chart.</summary>
public sealed class NthUiPieChart : Control, INthUiAnimated
{
    /// <summary>The ring's size.</summary>
    public const double Diameter = 176;

    private const double Gap = 2.5;
    private const double RevealSeconds = .8;
    private static readonly NthCurve Linear = new(0, 0, 1, 1);

    private readonly NthUiContext context;
    private readonly NthUiChartText text = new();
    private CultureInfo culture;
    private NthUiChartModel model;
    private NthSpring[] edges = [];
    private NthSpring[] pops = [];
    private NthUiTween reveal = NthUiTween.At(1);
    private int? hover;
    private int? cursor;
    private int? emphasised;
    private bool started;

    public NthUiPieChart(NthUiContext context, NthUiChartModel model, CultureInfo culture)
    {
        this.context = context;
        this.culture = culture;
        this.model = model;
        Focusable = true;
        ActualThemeVariantChanged += (_, _) =>
        {
            text.Clear();
            InvalidateVisual();
        };
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Image);
        Show(model);
    }

    /// <summary>The slice the reader is on changed (the key highlights the same row).</summary>
    public event Action<int?>? ActiveChanged;

    public NthUiChartModel Model => model;

    /// <summary>The slice under the pointer, else under the keyboard, else the one the key points at.</summary>
    public int? Active => hover ?? cursor ?? emphasised;

    /// <summary>The share of the whole slice <paramref name="slice"/> is drawn with now (the edges spring to a new cut).</summary>
    public double ShownShare(int slice) => slice >= 0 && slice + 1 < edges.Length ? Math.Max(0, edges[slice + 1].Rest(Now) - edges[slice].Rest(Now)) : 0;

    /// <summary>How far round the ring has been swept on, 0 to 1 (1 once it has drawn in).</summary>
    public double Sweep => Math.Clamp(reveal.Value(Now), 0, 1);

    /// <summary>How far slice <paramref name="slice"/> has stepped out, 0 to 1.</summary>
    public double Pop(int slice) => slice >= 0 && slice < pops.Length ? Math.Clamp(pops[slice].Rest(Now), 0, 1) : 0;

    /// <summary>The ring's centre and radii as drawn.</summary>
    public (Point Center, double Outer, double Inner) Geometry => Rings();

    private double Now => NthUiMotion.Now;

    /// <summary>Takes new data: the slice edges spring to their new cut; the ring sweeps on the first time.</summary>
    public void Show(NthUiChartModel next)
    {
        model = next;
        var now = Now;
        var shares = model.Shares;
        var n = shares.Count;
        var cumulative = new double[n + 1];
        for (var i = 0; i < n; i++) cumulative[i + 1] = cumulative[i] + shares[i];
        var animate = started && context.Motion.CanAnimate;
        var calm = animate && context.IsCalm(this);
        if (edges.Length != n + 1)
        {
            var old = edges;
            edges = new NthSpring[n + 1];
            for (var i = 0; i <= n; i++) edges[i] = i < old.Length && animate ? old[i].Retarget(cumulative[i], now, calm) : NthSpring.At(cumulative[i]);
            pops = new NthSpring[n];
            for (var i = 0; i < n; i++) pops[i] = NthSpring.At(0);
        }
        else
        {
            for (var i = 0; i <= n; i++) edges[i] = animate ? edges[i].Retarget(cumulative[i], now, calm) : NthSpring.At(cumulative[i]);
        }
        if (!started)
        {
            started = true;
            reveal = context.Motion.CanAnimate && !model.IsEmpty
                ? NthUiTween.At(0).Retarget(1, now, context.IsCalm(this) ? NthUiTokens.CalmFadeSeconds : RevealSeconds, Linear)
                : NthUiTween.At(1);
        }
        if (cursor is { } c && (c >= n || shares[c] <= 0)) cursor = null;
        if (hover is { } h && (h >= n || shares[h] <= 0)) hover = null;
        AutomationProperties.SetName(this, model.Data.Title is { Length: > 0 } title ? title : NthUiStrings.KindName(NthUiChartKind.Pie));
        AutomationProperties.SetHelpText(this, NthUiChartSummary.Describe(model, culture));
        Touch(now);
    }

    /// <summary>Highlights a slice for the key (a row the pointer is on), without moving the pointer's own hover.</summary>
    public void Emphasise(int? slice)
    {
        if (emphasised == slice) return;
        emphasised = slice;
        Touch(Now);
    }

    private void SetHover(int? slice)
    {
        if (hover == slice) return;
        hover = slice;
        Touch(Now);
        ActiveChanged?.Invoke(Active);
    }

    private void SetCursor(int? slice)
    {
        if (cursor == slice) return;
        cursor = slice;
        Touch(Now);
        ActiveChanged?.Invoke(Active);
    }

    private void Touch(double now)
    {
        var active = Active;
        var calm = context.IsCalm(this);
        for (var i = 0; i < pops.Length; i++)
        {
            var target = active == i ? 1 : 0;
            pops[i] = context.Motion.CanAnimate ? pops[i].Retarget(target, now, calm) : NthSpring.At(target);
        }
        AutomationProperties.SetItemStatus(this, active is { } a && a < model.Shares.Count ? NthUiChartSummary.Readout(model, a, culture).Spoken : "");
        InvalidateVisual();
        if (Moving(now)) context.Motion.Start(this);
    }

    private bool Moving(double now)
    {
        if (!reveal.Settled(now)) return true;
        foreach (var edge in edges)
        {
            if (!edge.Settled(now)) return true;
        }
        foreach (var pop in pops)
        {
            if (!pop.Settled(now)) return true;
        }
        return false;
    }

    bool INthUiAnimated.Step(double now)
    {
        InvalidateVisual();
        return Moving(now);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(Diameter, double.IsFinite(availableSize.Width) ? availableSize.Width : Diameter), Diameter);

    private (Point Center, double Outer, double Inner) Rings()
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        var outer = Math.Max(10, side / 2 - 7);
        return (new Point(Bounds.Width / 2, Bounds.Height / 2), outer, outer * .64);
    }

    // ---- paint -----------------------------------------------------------------------------------------------------------

    public override void Render(DrawingContext drawing)
    {
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (Bounds.Width < 24) return;
        var now = Now;
        var theme = ActualThemeVariant;
        var strong = NthUiTokens.Strong.Resolve(theme);
        var accent = NthUiTokens.Accent.Resolve(theme);
        var (center, outer, inner) = Rings();
        var n = model.Shares.Count;
        var calmFade = context.IsCalm(this);
        var progress = reveal.Value(now);
        var sweep = calmFade || progress >= 1 ? 1 : NthEase.Out.Ease(progress);
        var live = Enumerable.Range(0, n).Count(i => model.Shares[i] > 0);
        var active = Active;
        if (model.IsEmpty || live == 0)
        {
            drawing.DrawEllipse(null, new ImmutablePen(new ImmutableSolidColorBrush(NthUiTokens.Track.Resolve(theme)), outer - inner), center, (outer + inner) / 2, (outer + inner) / 2);
        }
        using (calmFade && progress < 1 ? drawing.PushOpacity(progress) : default)
        {
            var shown = 0;
            for (var i = 0; i < n; i++)
            {
                if (model.Shares[i] <= 0) continue;
                shown++;
                var from = edges[i].Rest(now) * 2 * Math.PI * sweep;
                var to = edges[i + 1].Rest(now) * 2 * Math.PI * sweep;
                if (to - from < 1e-3) continue;
                var gap = live > 1 ? Gap / (2 * outer) : 0;
                if (to - from > 2 * gap + 1e-3)
                {
                    from += gap;
                    to -= gap;
                }
                var pop = Math.Clamp(pops[i].Rest(now), 0, 1);
                var middle = (from + to) / 2;
                var origin = NthUiPieGeometry.At(center, 4 * pop, middle);
                var color = NthUiChartPlot.Mix(Color.FromArgb((byte)Math.Round(255 * NthUiChartInk.FillTone(shown - 1, live)), strong.R, strong.G, strong.B), accent, pop);
                var geometry = NthUiPieGeometry.Slice(origin, outer, inner, from, to);
                drawing.DrawGeometry(new ImmutableSolidColorBrush(color), null, geometry);
            }
        }
        DrawCenter(drawing, center, inner, theme, active);
    }

    private void DrawCenter(DrawingContext drawing, Point center, double inner, Avalonia.Styling.ThemeVariant theme, int? active)
    {
        if (model.IsEmpty) return;
        string caption, value;
        if (active is { } a && a < model.Shares.Count)
        {
            var read = NthUiChartSummary.Readout(model, a, culture);
            caption = read.Heading;
            value = read.Rows.Count > 0 ? read.Rows[0].Value : "";
        }
        else
        {
            var total = Enumerable.Range(0, model.Shares.Count).Sum(i => model.Value(0, i) is double v && v > 0 ? v : 0);
            caption = NthUiStrings.TotalCaption;
            value = NthUiNumbers.Format(total, culture, model.Data.Unit);
        }
        var room = inner * 2 - 14;
        var font = NthUiChartInk.CenterFont;
        var layout = text.Get(value, font, NthUiTokens.Text1.Resolve(theme));
        if (layout.WidthIncludingTrailingWhitespace > room)
        {
            font = NthUiTokens.BodyStrong.MonospacedDigit();
            layout = text.Get(value, font, NthUiTokens.Text1.Resolve(theme));
        }
        var small = text.Get(caption, NthUiChartInk.AxisFont, NthUiTokens.Text2.Resolve(theme));
        var captionWidth = small.WidthIncludingTrailingWhitespace;
        if (captionWidth > room)
        {
            small = text.Get(Trim(caption, room), NthUiChartInk.AxisFont, NthUiTokens.Text2.Resolve(theme));
            captionWidth = small.WidthIncludingTrailingWhitespace;
        }
        var height = small.Height + 2 + layout.Height;
        var top = center.Y - height / 2;
        small.Draw(drawing, new Point(center.X - captionWidth / 2, top));
        layout.Draw(drawing, new Point(center.X - Math.Min(layout.WidthIncludingTrailingWhitespace, room) / 2, top + small.Height + 2));
    }

    private string Trim(string value, double room)
    {
        for (var length = value.Length - 1; length > 1; length--)
        {
            var cut = value[..length].TrimEnd() + "…";
            if (text.Measure(cut, NthUiChartInk.AxisFont).Width <= room) return cut;
        }
        return value;
    }

    // ---- pointer and keyboard ----------------------------------------------------------------------------------------------

    /// <summary>The slice under <paramref name="at"/> (null off the ring).</summary>
    public int? SliceAt(Point at)
    {
        var (center, outer, inner) = Rings();
        var dx = at.X - center.X;
        var dy = at.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < inner * .85 || distance > outer + 8) return null;
        var edgeValues = new double[edges.Length];
        for (var i = 0; i < edges.Length; i++) edgeValues[i] = edges[i].Target;
        var slice = NthUiPieGeometry.SliceAt(edgeValues, NthUiPieGeometry.AngleOf(center, at));
        return slice >= 0 && model.Shares[slice] > 0 ? slice : null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHover(SliceAt(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        SetHover(SliceAt(e.GetPosition(this)));
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional or NavigationMethod.Unspecified && cursor is null) SetCursor(Next(-1, 1));
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        SetCursor(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        var n = model.Shares.Count;
        int? next = e.Key switch
        {
            Key.Right or Key.Down => Next(cursor ?? -1, 1),
            Key.Left or Key.Up => Next(cursor ?? n, -1),
            Key.Home => Next(-1, 1),
            Key.End => Next(n, -1),
            _ => null,
        };
        if (e.Key == Key.Escape)
        {
            SetCursor(null);
            return;
        }
        if (next is null) return;
        e.Handled = true;
        SetCursor(next);
    }

    private int? Next(int from, int direction)
    {
        var shares = model.Shares;
        for (var i = from + direction; i >= 0 && i < shares.Count; i += direction)
        {
            if (shares[i] > 0) return i;
        }
        return null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        context.Motion.Remove(this);
        hover = null;
    }
}
