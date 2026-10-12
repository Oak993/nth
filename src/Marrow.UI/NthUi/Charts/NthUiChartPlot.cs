// PORT-ONLY: web surfaces W16b (DESIGN.md 7.2 "chart (line, bar, area, pie, scatter)" and "Editable charts: ... lets the user drag
// a line or bar point; the drag sets the bound state list, every let recomputes ... Keyboard: focus a point, Up and Down change
// it by the axis step"; 7.4 "Charts animate their marks in once"; 7.5 "monochrome line art with the accent only for the current
// value and focus"; 10 Reduce Motion; ED-NEW-web-w16b-1). No Swift counterpart. The drawing of a line, area, scatter or
// vertical bar chart: one control that paints gridlines, labels and marks, and keeps every moving number as a spring so the
// marks, the axis range and the readout all glide and can be interrupted. Only paint changes while it moves, never layout.
//
// ENGINEERING DECISIONS (ED-NEW-web-w16b-1): the plot is one control painted in one pass (a chart is hundreds of marks, and a
// control per mark would cost layout); an editable chart adds one invisible range control per point (NthUiChartHandle) for the
// keyboard and for assistive technology, and the plot owns the pointer. A drag picks the column under the pointer once and
// moves only that point, with the axis range held still while the finger is down, so the point stays under the pointer; the
// state it writes snaps to the axis step (the runtime does that), while the dot follows the pointer exactly and settles onto
// the step by spring when released.
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

/// <summary>The plot of a line, area, scatter or vertical bar chart.</summary>
public sealed partial class NthUiChartPlot : Control, INthUiAnimated
{
    /// <summary>The size of an editable point's keyboard control.</summary>
    public const double HandleSize = 28;

    /// <summary>How long the marks take to draw in, the stagger included.</summary>
    public const double RevealSeconds = .85;

    private static readonly NthCurve Linear = new(0, 0, 1, 1);

    private readonly NthUiContext context;
    private readonly NthUiChartText text = new();
    private readonly List<SeriesState> series = [];
    private readonly NthUiTickBank yTicks = new();
    private readonly NthUiTickBank xTicks = new();
    private readonly List<NthUiChartHandle> handles = [];
    private CultureInfo culture;
    private NthUiChartModel model;
    private NthSpring yLo;
    private NthSpring yHi;
    private NthSpring xLo;
    private NthSpring xHi;
    private NthSpring margin;
    private bool settledOnce;

    private sealed class SeriesState
    {
        public NthSpring[] Y = [];
        public NthSpring[] X = [];
        public bool[] Present = [];
        public NthUiTween Reveal = NthUiTween.At(1);
        public bool Started;
    }

    public NthUiChartPlot(NthUiContext context, NthUiChartModel model, CultureInfo culture)
    {
        this.context = context;
        this.culture = culture;
        this.model = model;
        ClipToBounds = false;
        ActualThemeVariantChanged += (_, _) =>
        {
            text.Clear();
            brushes.Clear();
            InvalidateVisual();
        };
        Show(model);
    }

    /// <summary>The model on show.</summary>
    public NthUiChartModel Model => model;

    /// <summary>The reader dragged or keyed point <c>index</c> of the bound series to <c>value</c> (the runtime then snaps it).</summary>
    public event Action<int, double>? PointRequested;

    /// <summary>The chart's per-point keyboard controls (an editable chart only).</summary>
    public IReadOnlyList<NthUiChartHandle> Handles => handles;

    /// <summary>The point being dragged (-1 when none).</summary>
    public int DragIndex { get; private set; } = -1;

    /// <summary>The point the readout is on: being dragged, else under the pointer, else under the keyboard cursor.</summary>
    public (int Series, int Index)? Active => DragIndex >= 0 ? (0, DragIndex) : hover ?? cursor;

    /// <summary>What the readout says now (null while it is hidden).</summary>
    public NthUiReadout? Readout => readoutShown ? readout : null;

    /// <summary>Whether the plot is held in place by a drag.</summary>
    public bool IsDragging => DragIndex >= 0;

    // ---- what is drawn now (for tests and for hosts that place things over the chart) --------------------------------------

    /// <summary>The value drawn for a point now: a spring in flight, the pointer's while the point is dragged; null for a gap.</summary>
    public double? ShownValue(int series, int index) => Present(series, index) ? Disp(series, index, Now) : null;

    /// <summary>The axis range drawn now (it springs to a new range when the data's range changes).</summary>
    public (double Lo, double Hi) ShownRange => (yLo.Rest(Now), yHi.Rest(Now));

    /// <summary>How much of a mark is drawn now, 0 to 1 (marks grow in once, left to right).</summary>
    public double Grow(int series, int index) => Present(series, index) ? Grown(series, index, Now) : 0;

    /// <summary>The Y gridline values showing now and how strongly (a line that is leaving or arriving is part way).</summary>
    public IReadOnlyList<(double Value, double Alpha)> ShownTicks => yTicks.Visible(Now).ToArray();

    /// <summary>Where the readout bubble is drawn now, in the plot's space; null while it is hidden.</summary>
    public Rect? ReadoutBounds => readout is not null && readoutAlpha.Value(Now) > .01 ? new Rect(readoutX.Rest(Now), readoutY.Rest(Now), readoutSize.Width, readoutSize.Height) : null;

    /// <summary>The plot's inner rectangle as drawn now (without the axis labels), in the plot's own space.</summary>
    public Rect PlotArea => MakeFrame(Now).Plot;

    /// <summary>Where a mark is drawn now, in the plot's space (the top of its band in a stack); null for a gap or an empty chart.</summary>
    public Point? PointOf(int series, int index)
    {
        if (!Present(series, index) || Bounds.Width < 24) return null;
        var now = Now;
        var f = MakeFrame(now);
        if (model.Kind == NthUiChartKind.Scatter) return new Point(f.X(DispX(series, index, now)), f.Y(Disp(series, index, now)));
        var value = Disp(series, index, now);
        if (model.Stacked)
        {
            value = 0;
            for (var t = 0; t <= series; t++) value += Present(t, index) ? Math.Max(0, Disp(t, index, now)) : 0;
        }
        return new Point(f.CatX(index), f.Y(value));
    }

    // ---- showing data ----------------------------------------------------------------------------------------------------

    private double Now => NthUiMotion.Now;

    private bool Animate => context.Motion.CanAnimate;

    private bool Calm => context.IsCalm(this);

    /// <summary>Takes new data: marks spring to their new values, the range springs to the new range, new series draw in.</summary>
    public void Show(NthUiChartModel next)
    {
        var first = !settledOnce;
        settledOnce = true;
        model = next;
        var now = Now;
        var animate = Animate && !first;
        var calm = animate && Calm;
        while (series.Count < model.Series.Count) series.Add(new SeriesState());
        if (series.Count > model.Series.Count) series.RemoveRange(model.Series.Count, series.Count - model.Series.Count);
        var scatter = model.Kind == NthUiChartKind.Scatter;
        for (var s = 0; s < model.Series.Count; s++)
        {
            var state = series[s];
            var count = model.Series[s].Values.Count;
            Fit(ref state.Y, count);
            Fit(ref state.X, count);
            Fit(ref state.Present, count);
            var fresh = !state.Started;
            for (var i = 0; i < count; i++)
            {
                if (model.Value(s, i) is not double value)
                {
                    state.Present[i] = false;
                    continue;
                }
                var x = scatter ? model.X(s, i) : 0;
                if (!state.Present[i] || fresh || !animate)
                {
                    state.Y[i] = NthSpring.At(value);
                    state.X[i] = NthSpring.At(x);
                }
                else
                {
                    if (!(DragIndex == i && s == 0)) state.Y[i] = state.Y[i].Retarget(value, now, calm);
                    state.X[i] = state.X[i].Retarget(x, now, calm);
                }
                state.Present[i] = true;
            }
            if (fresh)
            {
                state.Started = true;
                state.Reveal = Animate ? NthUiTween.At(0).Retarget(1, now, Calm ? NthUiTokens.CalmFadeSeconds : RevealSeconds, Linear) : NthUiTween.At(1);
            }
        }
        if (first || !animate)
        {
            yLo = NthSpring.At(model.YLo);
            yHi = NthSpring.At(model.YHi);
            xLo = NthSpring.At(model.XLo);
            xHi = NthSpring.At(model.XHi);
            margin = NthSpring.At(LeftMargin());
        }
        else
        {
            if (!IsDragging)
            {
                yLo = yLo.Retarget(model.YLo, now, calm);
                yHi = yHi.Retarget(model.YHi, now, calm);
            }
            xLo = xLo.Retarget(model.XLo, now, calm);
            xHi = xHi.Retarget(model.XHi, now, calm);
            margin = margin.Retarget(LeftMargin(), now, calm);
        }
        yTicks.Set(model.YTicks, now, animate, calm);
        xTicks.Set(model.XTicks, now, animate, calm);
        Focusable = !model.Editable;
        Cursor = model.Editable ? new Cursor(StandardCursorType.SizeNorthSouth) : null;
        AutomationProperties.SetControlTypeOverride(this, model.Editable ? AutomationControlType.Group : AutomationControlType.Image);
        AutomationProperties.SetName(this, model.Data.Title is { Length: > 0 } title ? title : NthUiStrings.Chart);
        AutomationProperties.SetHelpText(this, NthUiChartSummary.Describe(model, culture));
        FitHandles();
        if (cursor is { } c && (c.Series >= model.Series.Count || c.Index >= model.Series[c.Series].Values.Count || model.Value(c.Series, c.Index) is null)) cursor = null;
        if (hover is { } h && (h.Series >= model.Series.Count || h.Index >= model.Series[h.Series].Values.Count || model.Value(h.Series, h.Index) is null)) hover = null;
        Track(now);
        InvalidateMeasure();
        InvalidateVisual();
        Wake(now);
    }

    private static void Fit<T>(ref T[] items, int count)
    {
        if (items.Length != count) Array.Resize(ref items, count);
    }

    private void Wake(double now)
    {
        if (Moving(now)) context.Motion.Start(this);
    }

    private bool Moving(double now)
    {
        if (!yLo.Settled(now) || !yHi.Settled(now) || !xLo.Settled(now) || !xHi.Settled(now) || !margin.Settled(now)) return true;
        if (yTicks.Moving(now) || xTicks.Moving(now)) return true;
        if (!readoutAlpha.Settled(now) || !readoutX.Settled(now) || !readoutY.Settled(now)) return true;
        if (!focusAlpha.Settled(now) || !focusX.Settled(now) || !focusY.Settled(now)) return true;
        foreach (var state in series)
        {
            if (!state.Reveal.Settled(now)) return true;
            for (var i = 0; i < state.Y.Length; i++)
            {
                if (!state.Y[i].Settled(now) || !state.X[i].Settled(now)) return true;
            }
        }
        return false;
    }

    bool INthUiAnimated.Step(double now)
    {
        Track(now);
        InvalidateVisual();
        if (Moving(now)) return true;
        InvalidateArrange();
        return false;
    }

    // ---- layout ----------------------------------------------------------------------------------------------------------

    private bool HasUnit => !string.IsNullOrEmpty(model.Data.Unit);

    private double LabelHeight => text.Measure("0", NthUiChartInk.AxisFont).Height;

    /// <summary>The room on the left for the widest value label, and the gap before the plot.</summary>
    private double LeftMargin()
    {
        var widest = 0.0;
        foreach (var tick in model.YTicks) widest = Math.Max(widest, text.Measure(YLabel(tick), NthUiChartInk.AxisFont).Width);
        return Math.Ceiling(widest) + 10;
    }

    private string YLabel(double value) => NthUiChartMath.TickLabel(value, model.YStep, culture);

    private string XLabel(double value) => NthUiChartMath.TickLabel(value, model.XStep, culture);

    private Frame MakeFrame(double now)
    {
        var top = 8 + (HasUnit ? LabelHeight + 4 : 0);
        var left = Math.Max(0, margin.Rest(now));
        var bottom = LabelHeight + 10;
        var plot = new Rect(left, top, Math.Max(8, Bounds.Width - left - 10), Math.Max(8, Bounds.Height - top - bottom));
        return new Frame(plot, yLo.Rest(now), yHi.Rest(now), xLo.Rest(now), xHi.Rest(now), model.Count);
    }

    /// <summary>The frame the data will settle in (where the keyboard controls sit).</summary>
    private Frame TargetFrame()
    {
        var top = 8 + (HasUnit ? LabelHeight + 4 : 0);
        var left = LeftMargin();
        var bottom = LabelHeight + 10;
        var plot = new Rect(left, top, Math.Max(8, Bounds.Width - left - 10), Math.Max(8, Bounds.Height - top - bottom));
        return new Frame(plot, model.YLo, model.YHi, model.XLo, model.XHi, model.Count);
    }

    private readonly record struct Frame(Rect Plot, double YLo, double YHi, double XLo, double XHi, int Count)
    {
        public double Band => Count == 0 ? Plot.Width : Plot.Width / Count;

        public double Y(double value) => Plot.Bottom - NthUiChartMath.Fraction(value, YLo, YHi) * Plot.Height;

        public double CatX(int index) => Plot.Left + (index + .5) * Band;

        public double X(double value) => Plot.Left + NthUiChartMath.Fraction(value, XLo, XHi) * Plot.Width;

        public double ValueAt(double y) => YLo + Math.Clamp((Plot.Bottom - y) / Math.Max(1, Plot.Height), 0, 1) * (YHi - YLo);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 360;
        foreach (var handle in handles) handle.Measure(new Size(HandleSize, HandleSize));
        return new Size(width, NthUiChartInk.PlotHeight(width));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var frame = TargetFrame();
        foreach (var handle in handles)
        {
            var x = frame.CatX(handle.Index);
            var y = model.Value(0, handle.Index) is double v ? frame.Y(v) : frame.Plot.Bottom;
            handle.Arrange(new Rect(x - HandleSize / 2, y - HandleSize / 2, HandleSize, HandleSize));
        }
        return finalSize;
    }

    // ---- editable points: keyboard and assistive technology -------------------------------------------------------------

    private int tabStop;
    private bool clearPending;

    private void FitHandles()
    {
        var want = model.Editable ? model.Series[0].Values.Count : 0;
        while (handles.Count > want)
        {
            var gone = handles[^1];
            handles.RemoveAt(handles.Count - 1);
            LogicalChildren.Remove(gone);
            VisualChildren.Remove(gone);
        }
        while (handles.Count < want)
        {
            var handle = new NthUiChartHandle(handles.Count);
            handle.Requested += value => PointRequested?.Invoke(handle.Index, value);
            handle.Stepped += (index, step) => FocusHandle(index + step);
            handle.Jumped += (_, end) => FocusHandle(end ? handles.Count - 1 : 0);
            handle.FocusChanged += (index, gained) =>
            {
                if (gained)
                {
                    tabStop = index;
                    clearPending = false;
                    if (handle.ShowsFocus) SetCursor(0, index);
                }
                else
                {
                    // Focus moving to the next point lets go of this one first: wait a beat before the readout goes.
                    clearPending = true;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (clearPending) ClearCursor();
                    });
                }
                for (var i = 0; i < handles.Count; i++) handles[i].IsTabStop = i == tabStop;
            };
            handles.Add(handle);
            LogicalChildren.Add(handle);
            VisualChildren.Add(handle);
        }
        tabStop = Math.Clamp(tabStop, 0, Math.Max(0, handles.Count - 1));
        var step = model.Data.Step;
        var lo = model.Data.Min ?? model.YLo;
        var hi = model.Data.Max ?? model.YHi;
        for (var i = 0; i < handles.Count; i++)
        {
            var handle = handles[i];
            handle.IsTabStop = i == tabStop;
            var value = model.Value(0, i) ?? 0;
            handle.Show(Math.Min(lo, value), Math.Max(hi, value), value, step);
            var shown = NthUiNumbers.Format(value, culture, model.Data.Unit);
            handle.Describe(model.Label(i), shown);
            AutomationProperties.SetHelpText(handle, NthUiStrings.PointHint);
        }
    }

    private void FocusHandle(int index)
    {
        if (handles.Count == 0) return;
        index = Math.Clamp(index, 0, handles.Count - 1);
        handles[index].Focus(NavigationMethod.Directional);
    }
}
