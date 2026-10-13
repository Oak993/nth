// PORT-ONLY: web surfaces W16b (DESIGN.md 7.2 `chart bar x="Compute,Egress" y=[...]`, 7.4 "Charts animate their marks in once", 7.5;
// the approved canvas index.html section 5: .bars, .bar { grid 70px 1fr 56px; gap 8; 12 px text; i { 8 px tall, radius 4, a
// 90-degree gradient of the text colour .55 to .85, the accent one in blue; scaleX over .45 s } }; ED-NEW-web-w16b-1). No
// Swift counterpart. The bar chart of the canvas: one row a category, its name, a bar as long as its share of the largest and
// its value in the monospaced face. The largest bar takes the accent, as the canvas's first bar does. A bar springs to its new
// length when a slider moves it (the canvas animates scaleX the same way), and grows from nothing the first time.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi.Charts;

/// <summary>One bar: a rounded gradient as long as its share of the track.</summary>
public sealed class NthUiBarTrack : Control, INthUiAnimated
{
    private readonly NthUiContext context;
    private NthSpring ratio = NthSpring.At(0);
    private bool accent;

    public NthUiBarTrack(NthUiContext context)
    {
        this.context = context;
        Height = 8;
        VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The length the bar is heading for (0 to 1 of the track).</summary>
    public double Target => ratio.Target;

    /// <summary>The length the bar is drawn at now.</summary>
    public double Shown => Math.Clamp(ratio.Rest(NthUiMotion.Now), 0, 1);

    public bool IsAccent => accent;

    /// <summary>Sends the bar to <paramref name="value"/> (0 to 1). The first call from <paramref name="from"/> grows it in.</summary>
    public void Go(double value, bool isAccent, double? from = null)
    {
        value = Math.Clamp(value, 0, 1);
        accent = isAccent;
        var now = NthUiMotion.Now;
        if (!context.Motion.CanAnimate) ratio = NthSpring.At(value);
        else
        {
            if (from is { } start) ratio = NthSpring.At(start);
            ratio = ratio.Retarget(value, now, context.IsCalm(this));
            context.Motion.Start(this);
        }
        InvalidateVisual();
    }

    bool INthUiAnimated.Step(double now)
    {
        InvalidateVisual();
        return !ratio.Settled(now);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Math.Max(24, double.IsFinite(availableSize.Width) ? availableSize.Width : 120), 8);

    public override void Render(DrawingContext drawing)
    {
        var width = Bounds.Width * Shown;
        if (width < .5) return;
        var theme = ActualThemeVariant;
        Color from, to;
        if (accent)
        {
            var color = NthUiTokens.Accent.Resolve(theme);
            from = Color.FromArgb(180, color.R, color.G, color.B);
            to = color;
        }
        else
        {
            var strong = NthUiTokens.Strong.Resolve(theme);
            from = Color.FromArgb(theme == Avalonia.Styling.ThemeVariant.Dark ? (byte)140 : (byte)128, strong.R, strong.G, strong.B);
            to = Color.FromArgb(theme == Avalonia.Styling.ThemeVariant.Dark ? (byte)217 : (byte)209, strong.R, strong.G, strong.B);
        }
        var brush = new ImmutableLinearGradientBrush(
            [new ImmutableGradientStop(0, from), new ImmutableGradientStop(1, to)],
            startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(1, 0, RelativeUnit.Relative));
        drawing.DrawRectangle(brush, null, new RoundedRect(new Rect(0, 0, width, Bounds.Height), Math.Min(4, width / 2)));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        context.Motion.Remove(this);
    }
}

/// <summary>The canvas's bar chart: name, bar and value on each row.</summary>
public sealed class NthUiBarList : Grid
{
    private readonly NthUiContext context;
    private readonly List<(TextBlock Name, NthUiBarTrack Bar, TextBlock Value)> rows = [];
    private CultureInfo culture;
    private NthUiChartModel model;

    public NthUiBarList(NthUiContext context, NthUiChartModel model, CultureInfo culture)
    {
        this.context = context;
        this.culture = culture;
        this.model = model;
        ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto");
        ColumnDefinitions[0].MaxWidth = 150;
        ColumnDefinitions[2].MaxWidth = 110;
        ColumnSpacing = 8;
        RowSpacing = 6;
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Group);
        Show(model);
    }

    public NthUiChartModel Model => model;

    /// <summary>The bars, top to bottom.</summary>
    public IReadOnlyList<NthUiBarTrack> Bars => rows.Select(r => r.Bar).ToArray();

    /// <summary>The row names and values as shown.</summary>
    public IReadOnlyList<(string Name, string Value)> Shown => rows.Select(r => (r.Name.Text ?? "", r.Value.Text ?? "")).ToArray();

    /// <summary>The share of the largest value each row's bar is heading for.</summary>
    public IReadOnlyList<double> Ratios(NthUiChartModel of)
    {
        var values = Enumerable.Range(0, of.Count).Select(i => of.Value(0, i) ?? 0).ToArray();
        var scale = of.Data.Max is double fixedMax && fixedMax > 0 ? fixedMax : values.Length == 0 ? 0 : values.Max();
        return values.Select(v => scale > 0 && v > 0 ? Math.Min(1, v / scale) : 0).ToArray();
    }

    /// <summary>Takes new data: the bars spring to their new lengths.</summary>
    public void Show(NthUiChartModel next)
    {
        model = next;
        var count = model.Count;
        var fresh = rows.Count != count;
        if (fresh) Rebuild(count);
        var ratios = Ratios(model);
        var values = Enumerable.Range(0, count).Select(i => model.Value(0, i) ?? double.MinValue).ToArray();
        var largest = count == 0 ? -1 : Array.IndexOf(values, values.Max());
        for (var i = 0; i < count; i++)
        {
            var (name, bar, value) = rows[i];
            name.Text = model.Label(i);
            value.Text = NthUiNumbers.Format(model.Value(0, i), culture, model.Data.Unit);
            bar.Go(ratios[i], i == largest && values[i] > 0, fresh ? 0 : null);
        }
        AutomationProperties.SetName(this, model.Data.Title is { Length: > 0 } title ? title : NthUiStrings.KindName(NthUiChartKind.Bar));
        AutomationProperties.SetHelpText(this, NthUiChartSummary.Describe(model, culture));
    }

    private void Rebuild(int count)
    {
        Children.Clear();
        RowDefinitions.Clear();
        rows.Clear();
        for (var i = 0; i < count; i++)
        {
            RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = NthUiText.Block("", NthUiChartInk.LabelFont, NthUiTokens.Text2, wrap: false);
            name.VerticalAlignment = VerticalAlignment.Center;
            var bar = new NthUiBarTrack(context);
            var value = NthUiText.Block("", NthUiTokens.Mono.FontWeight(Marrow.UI.Port.SwiftFont.Weight.Medium), NthUiTokens.Text1, wrap: false);
            value.VerticalAlignment = VerticalAlignment.Center;
            value.TextAlignment = TextAlignment.Right;
            SetRow(name, i);
            SetRow(bar, i);
            SetRow(value, i);
            SetColumn(bar, 1);
            SetColumn(value, 2);
            Children.Add(name);
            Children.Add(bar);
            Children.Add(value);
            rows.Add((name, bar, value));
        }
    }
}
