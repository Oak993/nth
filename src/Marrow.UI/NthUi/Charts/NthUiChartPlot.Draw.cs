// PORT-ONLY: web surfaces W16b (DESIGN.md 7.4, 7.5, 10 Reduce Motion; ED-NEW-web-w16b-1). No Swift counterpart. The painting half
// of the plot: the gridlines and labels, the marks of each chart type, the cursor, the point the reader is on and the readout.
// Everything is read from springs at the time of the frame, so a frame is a pure function of the clock. The reader's attention
// is one moving focus (a spring on where it is, a fade on whether it is there): the column band, the cursor line, the
// enlarged dots and the readout all follow it, so hovering from one column to the next glides instead of jumping, and a mark
// takes the accent in proportion to how near the focus is.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi.Charts;

public sealed partial class NthUiChartPlot
{
    private readonly Dictionary<(int Series, int Count, int Kind), IBrush> brushes = [];
    private (int Series, int Index)? hover;
    private (int Series, int Index)? cursor;
    private NthUiReadout? readout;
    private bool readoutShown;
    private NthUiTween readoutAlpha = NthUiTween.At(0);
    private NthUiGlide readoutX = NthUiGlide.At(0);
    private NthUiGlide readoutY = NthUiGlide.At(0);
    private NthUiGlide focusX = NthUiGlide.At(0);
    private NthUiGlide focusY = NthUiGlide.At(0);
    private NthUiTween focusAlpha = NthUiTween.At(0);
    private int focusSeries;
    private Size readoutSize;
    private double dragValue;

    private readonly record struct Inks(Color Strong, Color Accent, Color Text1, Color Text2, Color Text3, Color Face, Color Grid, Color Axis, Color Band, Color Menu, Color MenuRim, Color Shadow);

    /// <summary>Where the reader's attention is: the point, how much it is there, and which series it is on.</summary>
    private readonly record struct Attention(double X, double Y, double Alpha, int Series)
    {
        /// <summary>How much of the reader's attention a mark at (<paramref name="x"/>, <paramref name="y"/>) holds, 0 to 1.</summary>
        public double Hot(double x, double y, double reach, bool both)
        {
            if (Alpha <= 0) return 0;
            var dx = x - X;
            var dy = both ? y - Y : 0;
            var t = Math.Clamp(1 - Math.Sqrt(dx * dx + dy * dy) / reach, 0, 1);
            return Alpha * t * t * (3 - 2 * t);
        }
    }

    private Inks Resolve()
    {
        var theme = ActualThemeVariant;
        return new Inks(NthUiTokens.Strong.Resolve(theme), NthUiTokens.Accent.Resolve(theme), NthUiTokens.Text1.Resolve(theme), NthUiTokens.Text2.Resolve(theme),
            NthUiTokens.Text3.Resolve(theme), NthUiChartInk.Face.Resolve(theme), NthUiChartInk.Grid.Resolve(theme), NthUiChartInk.Axis.Resolve(theme),
            NthUiChartInk.Band.Resolve(theme), NthUiTokens.Menu.Resolve(theme), NthUiTokens.ControlRim.Resolve(theme), NthUiTokens.CardShadow.Resolve(theme));
    }

    private static Color Alpha(Color color, double alpha) => Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(alpha, 0, 1)), color.R, color.G, color.B);

    /// <summary>The colour <paramref name="amount"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    internal static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        byte Lerp(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromArgb(Lerp(from.A, to.A), Lerp(from.R, to.R), Lerp(from.G, to.G), Lerp(from.B, to.B));
    }

    private static IBrush Solid(Color color) => new ImmutableSolidColorBrush(color);

    /// <summary>Device pixels to a DIP: a hairline is one device pixel wide and sits on the pixel grid at any display scaling.</summary>
    private double Pixel => 1 / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);

    /// <summary>The coordinate of a one-pixel line near <paramref name="at"/>, on the middle of a device pixel.</summary>
    private double Hairline(double at)
    {
        var pixel = Pixel;
        return (Math.Round(at / pixel) + .5) * pixel;
    }

    private static IPen Line(Color color, double thickness, double[]? dash = null, PenLineCap cap = PenLineCap.Round) =>
        new ImmutablePen(new ImmutableSolidColorBrush(color), thickness, dash is null ? null : new ImmutableDashStyle(dash, 0), cap, PenLineJoin.Round);

    private bool Present(int s, int i) => s < series.Count && i < series[s].Present.Length && series[s].Present[i];

    /// <summary>The value point <paramref name="i"/> of series <paramref name="s"/> is drawn at: the pointer's while it is dragged.</summary>
    private double Disp(int s, int i, double now) => s == 0 && i == DragIndex ? dragValue : series[s].Y[i].Rest(now);

    private double DispX(int s, int i, double now) => series[s].X[i].Rest(now);

    /// <summary>How much of a mark is drawn: marks grow from their base one after another, left to right, once.</summary>
    private double Grown(int s, int i, double now)
    {
        var progress = series[s].Reveal.Value(now);
        if (progress >= 1) return 1;
        var n = Math.Max(1, model.Series[s].Values.Count - 1);
        return NthEase.Out.Ease(Math.Clamp(progress * 1.35 - .35 * i / n, 0, 1));
    }

    /// <summary>The strength the whole plot is drawn at while its series draw in under Reduce Motion (a plain fade).</summary>
    private double CalmAlpha(double now)
    {
        if (series.Count == 0) return 1;
        var least = 1.0;
        foreach (var state in series) least = Math.Min(least, state.Reveal.Value(now));
        return least;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(Brushes.Transparent, null, bounds);
        if (bounds.Width < 24 || bounds.Height < 24) return;
        var now = Now;
        var ink = Resolve();
        var frame = MakeFrame(now);
        var focus = new Attention(focusX.Rest(now), focusY.Rest(now), Math.Clamp(focusAlpha.Value(now), 0, 1), focusSeries);
        DrawAxes(context, frame, ink, now, focus);
        if (model.IsEmpty) return;
        var calm = Calm && series.Any(s => !s.Reveal.Settled(now));
        using (calm ? context.PushOpacity(CalmAlpha(now)) : default)
        {
            switch (model.Kind)
            {
                case NthUiChartKind.Bar:
                    DrawBars(context, frame, ink, now, calm, focus);
                    break;
                case NthUiChartKind.Scatter:
                    DrawScatter(context, frame, ink, now, calm, focus);
                    break;
                default:
                    DrawLines(context, frame, ink, now, calm, focus);
                    break;
            }
        }
        DrawReadout(context, ink, now);
    }

    // ---- axes ------------------------------------------------------------------------------------------------------------

    private void DrawAxes(DrawingContext context, Frame f, Inks ink, double now, Attention focus)
    {
        var plot = f.Plot;
        if (HasUnit)
        {
            var unit = text.Get(model.Data.Unit!, NthUiChartInk.AxisFont, ink.Text3);
            unit.Draw(context, new Point(0, 0));
        }
        var rests = model.Kind is NthUiChartKind.Bar or NthUiChartKind.Area;
        foreach (var (value, alpha) in yTicks.Visible(now))
        {
            var y = f.Y(value);
            if (y < plot.Top - .5 || y > plot.Bottom + .5) continue;
            var baseline = rests && Math.Abs(value) < 1e-9 || Math.Abs(y - plot.Bottom) < .5;
            using (alpha < .999 ? context.PushOpacity(alpha) : default)
            {
                var yLine = Hairline(y);
                context.DrawLine(Line(baseline ? ink.Axis : ink.Grid, Pixel, null, PenLineCap.Flat), new Point(plot.Left, yLine), new Point(plot.Right, yLine));
                var label = text.Get(YLabel(value), NthUiChartInk.AxisFont, ink.Text3);
                label.Draw(context, new Point(plot.Left - 8 - label.WidthIncludingTrailingWhitespace, y - label.Height / 2));
            }
        }
        var rule = Hairline(plot.Bottom);
        context.DrawLine(Line(ink.Axis, Pixel, null, PenLineCap.Flat), new Point(plot.Left, rule), new Point(plot.Right, rule));
        if (model.Kind == NthUiChartKind.Scatter)
        {
            foreach (var (value, alpha) in xTicks.Visible(now))
            {
                var x = f.X(value);
                if (x < plot.Left - .5 || x > plot.Right + .5) continue;
                using (alpha < .999 ? context.PushOpacity(alpha) : default)
                {
                    var xLine = Hairline(x);
                    context.DrawLine(Line(ink.Grid, Pixel, null, PenLineCap.Flat), new Point(xLine, plot.Top), new Point(xLine, plot.Bottom));
                    var label = text.Get(XLabel(value), NthUiChartInk.AxisFont, ink.Text3);
                    var left = Math.Clamp(x - label.WidthIncludingTrailingWhitespace / 2, 0, Math.Max(0, Bounds.Width - label.WidthIncludingTrailingWhitespace));
                    label.Draw(context, new Point(left, plot.Bottom + 6));
                }
            }
            return;
        }
        var n = model.Count;
        if (n == 0) return;
        var widest = 0.0;
        for (var i = 0; i < n; i++) widest = Math.Max(widest, text.Measure(model.Label(i), NthUiChartInk.AxisFont).Width);
        var stride = Math.Max(1, (int)Math.Ceiling((widest + 10) / Math.Max(1, f.Band)));
        for (var i = 0; i < n; i += stride)
        {
            var hot = focus.Hot(f.CatX(i), 0, Math.Max(1, f.Band * .5), false);
            var label = text.Get(model.Label(i), NthUiChartInk.AxisFont, Mix(ink.Text3, ink.Text1, hot > .5 ? 1 : 0));
            var x = Math.Clamp(f.CatX(i) - label.WidthIncludingTrailingWhitespace / 2, 0, Math.Max(0, Bounds.Width - label.WidthIncludingTrailingWhitespace));
            label.Draw(context, new Point(x, plot.Bottom + 6));
        }
    }

    // ---- bars ------------------------------------------------------------------------------------------------------------

    private IBrush BarBrush(int s, Inks ink)
    {
        var key = (s, model.Series.Count, 0);
        if (brushes.TryGetValue(key, out var brush)) return brush;
        var tone = NthUiChartInk.Series(s).Tone;
        brush = new ImmutableLinearGradientBrush(
            [new ImmutableGradientStop(0, Alpha(ink.Strong, tone)), new ImmutableGradientStop(1, Alpha(ink.Strong, tone * .6))],
            startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
        brushes[key] = brush;
        return brush;
    }

    private void DrawBars(DrawingContext context, Frame f, Inks ink, double now, bool calm, Attention focus)
    {
        var n = model.Count;
        var k = model.Series.Count;
        var together = model.Stacked || k == 1;
        var cluster = Math.Min(f.Band * .7, 72);
        var gap = together ? 0 : 3.0;
        var width = together ? cluster : Math.Max(2, (cluster - gap * (k - 1)) / k);
        if (focus.Alpha > .01)
        {
            var column = new Rect(Math.Max(f.Plot.Left, focus.X - f.Band / 2) + 1, f.Plot.Top, Math.Min(f.Band, f.Plot.Width) - 2, f.Plot.Height);
            using (context.PushOpacity(focus.Alpha)) context.DrawRectangle(Solid(ink.Band), null, new RoundedRect(column, 8));
        }
        var below = new double[n];
        var above = new double[n];
        for (var s = 0; s < k; s++)
        {
            for (var i = 0; i < n; i++)
            {
                if (!Present(s, i)) continue;
                var value = Disp(s, i, now);
                double low, high;
                if (model.Stacked)
                {
                    if (value >= 0)
                    {
                        low = above[i];
                        high = above[i] += value;
                    }
                    else
                    {
                        high = below[i];
                        low = below[i] += value;
                    }
                }
                else (low, high) = (Math.Min(0, value), Math.Max(0, value));
                // A bar grows from its base (the zero line, or the top of the bar under it) toward its end.
                var baseValue = value >= 0 ? low : high;
                var endValue = baseValue + ((value >= 0 ? high : low) - baseValue) * (calm ? 1 : Grown(s, i, now));
                var yBase = Math.Clamp(f.Y(baseValue), f.Plot.Top, f.Plot.Bottom);
                var yEnd = Math.Clamp(f.Y(endValue), f.Plot.Top, f.Plot.Bottom);
                var x = f.Plot.Left + i * f.Band + (f.Band - cluster) / 2 + (together ? 0 : s * (width + gap));
                var rect = new Rect(x, Math.Min(yBase, yEnd), width, Math.Abs(yBase - yEnd));
                if (rect.Height < .5) continue;
                var radius = Math.Min(4, Math.Min(width, rect.Height) / 2);
                var radii = value >= 0 || model.Stacked ? new CornerRadius(radius, radius, 0, 0) : new CornerRadius(0, 0, radius, radius);
                if (model.Stacked && s < k - 1) radii = default;
                var shape = new RoundedRect(rect, radii);
                var fill = model.Stacked ? Solid(Alpha(ink.Strong, NthUiChartInk.FillTone(s, k))) : BarBrush(s, ink);
                context.DrawRectangle(fill, model.Stacked ? new ImmutablePen(new ImmutableSolidColorBrush(ink.Face), 1) : null, shape);
                // The bar the reader is on takes the accent, in proportion to how near the focus is.
                var hot = k == 1 || focus.Series == s ? focus.Hot(f.CatX(i), 0, Math.Max(1, f.Band * .5), false) : 0;
                if (hot > .01 && k == 1) context.DrawRectangle(Solid(Alpha(ink.Accent, hot)), null, shape);
                if (model.Editable)
                {
                    var cap = new Rect(x + width / 2 - 8, yEnd - 1.5, 16, 3);
                    context.DrawRectangle(Solid(Alpha(ink.Face, .85 + .15 * hot)), null, new RoundedRect(cap, 1.5));
                }
            }
        }
    }

    // ---- lines and areas -------------------------------------------------------------------------------------------------

    private void DrawLines(DrawingContext context, Frame f, Inks ink, double now, bool calm, Attention focus)
    {
        var n = model.Count;
        var k = model.Series.Count;
        var area = model.Kind == NthUiChartKind.Area;
        var progress = series.Count == 0 ? 1 : series.Min(s => s.Reveal.Value(now));
        var sweep = calm || progress >= 1 ? 1 : NthEase.Out.Ease(progress);
        var sweepRect = new Rect(f.Plot.Left - 12, f.Plot.Top - 12, (f.Plot.Width + 24) * sweep, f.Plot.Height + 24);
        var tops = new double[k][];
        var bottoms = new double[k][];
        var below = new double[n];
        var above = new double[n];
        for (var s = 0; s < k; s++)
        {
            tops[s] = new double[n];
            bottoms[s] = new double[n];
            for (var i = 0; i < n; i++)
            {
                var value = Present(s, i) ? Disp(s, i, now) : 0;
                if (model.Stacked)
                {
                    if (value >= 0)
                    {
                        bottoms[s][i] = above[i];
                        tops[s][i] = above[i] += value;
                    }
                    else
                    {
                        tops[s][i] = below[i];
                        bottoms[s][i] = below[i] += value;
                    }
                }
                else
                {
                    bottoms[s][i] = Math.Max(f.YLo, Math.Min(0, f.YHi));
                    tops[s][i] = value;
                }
            }
        }
        if (focus.Alpha > .01)
        {
            var x = Hairline(focus.X);
            using (context.PushOpacity(focus.Alpha)) context.DrawLine(Line(ink.Axis, Pixel, [3, 3], PenLineCap.Flat), new Point(x, f.Plot.Top), new Point(x, f.Plot.Bottom));
        }
        using (context.PushClip(sweepRect))
        {
            for (var order = 0; order < k; order++)
            {
                var s = model.Stacked ? order : k - 1 - order;
                var segments = Segments(s, n);
                var inkOf = NthUiChartInk.Series(s);
                if (area)
                {
                    foreach (var (from, to) in segments)
                    {
                        var geometry = new StreamGeometry();
                        using (var c = geometry.Open())
                        {
                            c.BeginFigure(new Point(f.CatX(from), f.Y(tops[s][from])), true);
                            for (var i = from + 1; i <= to; i++) c.LineTo(new Point(f.CatX(i), f.Y(tops[s][i])));
                            for (var i = to; i >= from; i--) c.LineTo(new Point(f.CatX(i), f.Y(bottoms[s][i])));
                            c.EndFigure(true);
                        }
                        IBrush fill = model.Stacked
                            ? Solid(Alpha(ink.Strong, NthUiChartInk.FillTone(s, k) * .55))
                            : new ImmutableLinearGradientBrush(
                                [new ImmutableGradientStop(0, Alpha(ink.Strong, inkOf.Tone * .30)), new ImmutableGradientStop(1, Alpha(ink.Strong, 0))],
                                startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
                        context.DrawGeometry(fill, null, geometry);
                    }
                }
                var pen = Line(Alpha(ink.Strong, inkOf.Tone), 2, inkOf.Dash);
                foreach (var (from, to) in segments)
                {
                    if (from == to) continue;
                    var geometry = new StreamGeometry();
                    using (var c = geometry.Open())
                    {
                        c.BeginFigure(new Point(f.CatX(from), f.Y(tops[s][from])), false);
                        for (var i = from + 1; i <= to; i++) c.LineTo(new Point(f.CatX(i), f.Y(tops[s][i])));
                        c.EndFigure(false);
                    }
                    context.DrawGeometry(null, pen, geometry);
                }
            }
            var showDots = !area && n <= 24 || model.Editable;
            var reach = Math.Max(1, Math.Min(f.Band * .5, 36));
            for (var s = 0; s < k; s++)
            {
                for (var i = 0; i < n; i++)
                {
                    if (!Present(s, i)) continue;
                    var center = new Point(f.CatX(i), f.Y(tops[s][i]));
                    var near = focus.Hot(center.X, 0, reach, false);
                    if (!showDots && near < .02) continue;
                    var chosen = k == 1 || focus.Series == s ? near : 0;
                    DrawDot(context, center, NthUiChartInk.Series(s), ink, chosen, near, model.Editable ? 4.5 : 3.5, calm ? 1 : Grown(s, i, now));
                }
            }
        }
    }

    /// <summary>The runs of consecutive points of series <paramref name="s"/> with a value (a gap breaks the line).</summary>
    private List<(int From, int To)> Segments(int s, int n)
    {
        var runs = new List<(int, int)>();
        var start = -1;
        for (var i = 0; i <= n; i++)
        {
            var has = i < n && Present(s, i);
            if (has && start < 0) start = i;
            else if (!has && start >= 0)
            {
                runs.Add((start, i - 1));
                start = -1;
            }
        }
        return runs;
    }

    /// <summary>A point of a line: a ring in the series' tone that takes the accent, a halo and a little size as the focus
    /// comes to it (<paramref name="chosen"/>, 0 to 1); <paramref name="near"/> grows the other series' rings at the same column.</summary>
    private static void DrawDot(DrawingContext context, Point center, NthUiSeriesInk style, Inks ink, double chosen, double near, double radius, double grown)
    {
        radius *= .35 + .65 * grown;
        var tone = Mix(Alpha(ink.Strong, style.Tone), ink.Accent, chosen);
        if (chosen > .01) context.DrawEllipse(Solid(Alpha(ink.Accent, .22 * chosen)), null, center, radius + 5 * chosen, radius + 5 * chosen);
        var r = radius + 1 * chosen + .5 * near * (1 - chosen);
        context.DrawEllipse(Solid(ink.Face), Line(tone, 1.75 + .5 * chosen), center, r, r);
        if (chosen > .01) context.DrawEllipse(Solid(Alpha(ink.Accent, chosen)), null, center, Math.Max(0, r - 2.25), Math.Max(0, r - 2.25));
    }

    // ---- scatter ---------------------------------------------------------------------------------------------------------

    private void DrawScatter(DrawingContext context, Frame f, Inks ink, double now, bool calm, Attention focus)
    {
        var small = model.Series.Sum(s => s.Values.Count) > 80;
        var baseRadius = small ? 3.25 : 4.5;
        for (var s = 0; s < model.Series.Count; s++)
        {
            var style = NthUiChartInk.Series(s);
            for (var i = 0; i < model.Series[s].Values.Count; i++)
            {
                if (!Present(s, i)) continue;
                var center = new Point(f.X(DispX(s, i, now)), f.Y(Disp(s, i, now)));
                if (!f.Plot.Inflate(8).Contains(center)) continue;
                var grown = calm ? 1 : Grown(s, i, now);
                var hot = focus.Series == s ? focus.Hot(center.X, center.Y, 18, true) : 0;
                var radius = baseRadius * (.3 + .7 * grown) * (1 + .35 * hot);
                if (hot > .01) context.DrawEllipse(Solid(Alpha(ink.Accent, .22 * hot)), null, center, radius + 5 * hot, radius + 5 * hot);
                var fill = Mix(Alpha(ink.Strong, style.Tone * (small ? .75 : .9)), ink.Accent, hot);
                Marker(context, style.Marker, center, radius, Solid(fill), Line(ink.Face, 1));
            }
        }
    }

    private static void Marker(DrawingContext context, NthUiMarker shape, Point c, double r, IBrush fill, IPen rim)
    {
        switch (shape)
        {
            case NthUiMarker.Square:
                context.DrawRectangle(fill, rim, new RoundedRect(new Rect(c.X - r * .9, c.Y - r * .9, r * 1.8, r * 1.8), 1.5));
                return;
            case NthUiMarker.Diamond:
            case NthUiMarker.Triangle:
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    if (shape == NthUiMarker.Diamond)
                    {
                        g.BeginFigure(new Point(c.X, c.Y - r * 1.25), true);
                        g.LineTo(new Point(c.X + r * 1.25, c.Y));
                        g.LineTo(new Point(c.X, c.Y + r * 1.25));
                        g.LineTo(new Point(c.X - r * 1.25, c.Y));
                    }
                    else
                    {
                        g.BeginFigure(new Point(c.X, c.Y - r * 1.2), true);
                        g.LineTo(new Point(c.X + r * 1.15, c.Y + r * .85));
                        g.LineTo(new Point(c.X - r * 1.15, c.Y + r * .85));
                    }
                    g.EndFigure(true);
                }
                context.DrawGeometry(fill, rim, geometry);
                return;
            default:
                context.DrawEllipse(fill, rim, c, r, r);
                return;
        }
    }

    // ---- readout ---------------------------------------------------------------------------------------------------------

    /// <summary>The place the readout points at: the topmost mark of the active column, or the active scatter point.</summary>
    private Point? Anchor(Frame f, double now)
    {
        if (Active is not { } a) return null;
        if (model.Kind == NthUiChartKind.Scatter)
        {
            if (!Present(a.Series, a.Index)) return null;
            return new Point(f.X(DispX(a.Series, a.Index, now)), f.Y(Disp(a.Series, a.Index, now)));
        }
        var top = double.PositiveInfinity;
        for (var s = 0; s < model.Series.Count; s++)
        {
            if (!Present(s, a.Index)) continue;
            double value = Disp(s, a.Index, now);
            if (model.Stacked)
            {
                value = 0;
                for (var t = 0; t <= s; t++) value += Present(t, a.Index) ? Math.Max(0, Disp(t, a.Index, now)) : 0;
            }
            top = Math.Min(top, f.Y(value));
        }
        return double.IsFinite(top) ? new Point(f.CatX(a.Index), top) : null;
    }

    private (Size Size, double Heading, double Line) Measure(NthUiReadout content)
    {
        var heading = text.Measure(content.Heading, NthUiChartInk.ReadoutFont);
        var swatch = content.Rows.Count > 1 ? 14.0 : 0;
        var names = content.Rows.Select(r => text.Measure(r.Name, NthUiChartInk.ReadoutFont).Width).DefaultIfEmpty(0).Max();
        var values = content.Rows.Select(r => text.Measure(r.Value, NthUiChartInk.ReadoutStrong).Width).DefaultIfEmpty(0).Max();
        var line = Math.Max(heading.Height, 14);
        var columns = swatch + names + (names > 0 ? 12 : 0) + values;
        var width = Math.Max(heading.Width, columns) + 20;
        var height = 16 + heading.Height + (content.Rows.Count > 0 ? 3 + content.Rows.Count * (line + 2) : 0);
        return (new Size(Math.Ceiling(width), Math.Ceiling(height)), heading.Height, line);
    }

    /// <summary>Moves the readout and the focus to the active point (called when the point, the data or the frame moves).</summary>
    private void Track(double now)
    {
        var active = Active;
        if (active is null || Bounds.Width < 24)
        {
            Unfocus(now);
            return;
        }
        var a = active.Value;
        var content = model.Kind == NthUiChartKind.Scatter ? NthUiChartSummary.ScatterReadout(model, a.Series, a.Index, culture) : NthUiChartSummary.Readout(model, a.Index, culture);
        var frame = MakeFrame(now);
        if (Anchor(frame, now) is not { } anchor)
        {
            Unfocus(now);
            return;
        }
        var (size, _, _) = Measure(content);
        // The readout sits above the point; with no room above (a point near the top of the plot) it stands beside it, on the
        // right when there is room, so it never covers the point it is about, and never goes below it onto the axis labels.
        var x = Math.Clamp(anchor.X - size.Width / 2, 0, Math.Max(0, Bounds.Width - size.Width));
        var y = anchor.Y - size.Height - 12;
        if (y < 0)
        {
            var right = anchor.X + 16;
            x = right + size.Width <= Bounds.Width ? right : Math.Max(0, anchor.X - 16 - size.Width);
            y = Math.Clamp(anchor.Y - size.Height / 2, 0, Math.Max(0, Bounds.Height - size.Height));
        }
        readout = content;
        readoutSize = size;
        focusSeries = a.Series;
        var calm = Calm;
        var fade = calm ? NthUiTokens.CalmFadeSeconds : .14;
        var glide = Animate && !IsDragging;
        if (!readoutShown)
        {
            readoutShown = true;
            readoutX = NthUiGlide.At(x);
            readoutY = NthUiGlide.At(y);
            focusX = NthUiGlide.At(anchor.X);
            focusY = NthUiGlide.At(anchor.Y);
            if (Animate)
            {
                readoutAlpha = readoutAlpha.Retarget(1, now, fade, NthEase.Out);
                focusAlpha = focusAlpha.Retarget(1, now, fade, NthEase.Out);
                context.Motion.Start(this);
            }
            else
            {
                readoutAlpha = NthUiTween.At(1);
                focusAlpha = NthUiTween.At(1);
            }
            return;
        }
        var moved = Math.Abs(readoutX.Target - x) > .25 || Math.Abs(readoutY.Target - y) > .25 || Math.Abs(focusX.Target - anchor.X) > .25 || Math.Abs(focusY.Target - anchor.Y) > .25;
        readoutX = glide ? readoutX.Retarget(x, now, calm) : NthUiGlide.At(x);
        readoutY = glide ? readoutY.Retarget(y, now, calm) : NthUiGlide.At(y);
        focusX = glide ? focusX.Retarget(anchor.X, now, calm) : NthUiGlide.At(anchor.X);
        focusY = glide ? focusY.Retarget(anchor.Y, now, calm) : NthUiGlide.At(anchor.Y);
        // A tween retargeted to where it is already heading would start its whole fade again every frame.
        if (readoutAlpha.Target < 1) readoutAlpha = readoutAlpha.Retarget(1, now, fade, NthEase.Out);
        if (focusAlpha.Target < 1) focusAlpha = focusAlpha.Retarget(1, now, fade, NthEase.Out);
        if (moved && Animate) context.Motion.Start(this);
    }

    /// <summary>The reader's attention left: the readout, the cursor and the highlights fade out where they are.</summary>
    private void Unfocus(double now)
    {
        if (!readoutShown) return;
        readoutShown = false;
        var fade = Calm ? NthUiTokens.CalmFadeSeconds : .12;
        readoutAlpha = readoutAlpha.Retarget(0, now, fade, NthEase.Out);
        focusAlpha = focusAlpha.Retarget(0, now, fade, NthEase.Out);
        if (Animate) context.Motion.Start(this);
        else
        {
            readoutAlpha = NthUiTween.At(0);
            focusAlpha = NthUiTween.At(0);
        }
    }

    private void DrawReadout(DrawingContext context, Inks ink, double now)
    {
        if (readout is not { } content) return;
        var alpha = Math.Clamp(readoutAlpha.Value(now), 0, 1);
        if (alpha <= .01) return;
        var (size, headingHeight, line) = Measure(content);
        var x = Math.Round(readoutX.Rest(now));
        var y = Math.Round(readoutY.Rest(now));
        var rect = new Rect(x, y, size.Width, size.Height);
        using (alpha < .999 ? context.PushOpacity(alpha) : default)
        {
            context.DrawRectangle(Solid(ink.Menu), Line(ink.MenuRim, 1), new RoundedRect(rect, 10), new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 18, Spread = -6, Color = ink.Shadow }));
            text.Get(content.Heading, NthUiChartInk.ReadoutFont, ink.Text2).Draw(context, new Point(x + 10, y + 8));
            var rowY = y + 8 + headingHeight + 3;
            var swatch = content.Rows.Count > 1 ? 14.0 : 0;
            foreach (var row in content.Rows)
            {
                if (swatch > 0)
                {
                    var style = NthUiChartInk.Series(row.Series);
                    Marker(context, model.Kind == NthUiChartKind.Scatter ? style.Marker : NthUiMarker.Circle, new Point(x + 10 + 4, rowY + line / 2 + 1), 3.5, Solid(Alpha(ink.Strong, style.Tone)), Line(ink.Menu, 1));
                }
                if (row.Name.Length > 0) text.Get(row.Name, NthUiChartInk.ReadoutFont, ink.Text2).Draw(context, new Point(x + 10 + swatch, rowY));
                var value = text.Get(row.Value, NthUiChartInk.ReadoutStrong, ink.Text1);
                value.Draw(context, new Point(rect.Right - 10 - value.WidthIncludingTrailingWhitespace, rowY));
                rowY += line + 2;
            }
        }
    }
}
