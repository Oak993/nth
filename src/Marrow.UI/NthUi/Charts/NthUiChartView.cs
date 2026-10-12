// PORT-ONLY: web surfaces W16b (DESIGN.md 7.2 chart components and "Editable charts", 7.4 "Charts animate their marks in once",
// 10 "charts carry a text summary and an 'Show as table' alternative"; canvas index.html section 5; ED-NEW-web-w16b-1). No Swift
// counterpart. The chart node of a live view: it reads the runtime's chart data, picks the drawing the data wants (a bar list
// like the canvas's, a pie with its key, or a plot), keeps the key of several series under it, and offers "Show as table" in
// place of the drawing. A `series` line that arrives later, a state a slider changes, and a point the reader drags all reach it
// through Refresh, which hands the new data to the drawing so its marks spring rather than being drawn again.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Marrow.Core.NthUi;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi.Charts;

/// <summary>`chart`: the five chart types with a text summary and a table alternative.</summary>
public sealed class NthUiChartView : NthUiNodeView
{
    private readonly StackPanel root = new() { Spacing = 8 };
    private readonly Panel host = new();
    private readonly TextBlock? title;
    private readonly NthUiSeriesKey key = new();
    private readonly ToggleButton toggle;
    private readonly TextBlock toggleText;
    private readonly StackPanel toggleContent;
    private readonly NthUiIcon? tableIcon = NthUiText.Icon("table", 13);
    private readonly NthUiIcon? chartIcon = NthUiText.Icon("chart-line", 13);
    private readonly NthUiChartTable table = new();
    private readonly List<Fade> fades = [];
    private NthUiChartModel model;
    private Control? layer;
    private NthUiChartPlot? plot;
    private NthUiPieChart? pie;
    private NthUiPieKey? pieKey;
    private NthUiBarList? bars;
    private bool tableShown;
    private bool tableBuilt;

    public NthUiChartView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        model = NthUiChartModel.From(Runtime.Chart(node));
        if (model.Data.Title is { Length: > 0 } caption)
        {
            title = NthUiText.Block(caption, NthUiTokens.BodyStrong, NthUiTokens.Text1);
            root.Children.Add(title);
        }
        root.Children.Add(host);
        toggle = NthUiPress.Plain(new ToggleButton());
        var face = new NthUiFace(toggle, NthUiFaceStyle.Segment, 13) { Height = 26, Padding = new Thickness(9, 0) };
        toggleContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        toggleText = NthUiText.Block(NthUiStrings.ShowAsTable, NthUiTokens.Small, null, wrap: false);
        toggleText.VerticalAlignment = VerticalAlignment.Center;
        if (tableIcon is not null)
        {
            tableIcon.VerticalAlignment = VerticalAlignment.Center;
            toggleContent.Children.Add(tableIcon);
        }
        toggleContent.Children.Add(toggleText);
        face.Child = toggleContent;
        toggle.Content = face;
        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        toggle.Click += (_, _) => ShowTable(toggle.IsChecked == true);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 26 };
        footer.Children.Add(key);
        Grid.SetColumn(toggle, 1);
        footer.Children.Add(toggle);
        root.Children.Add(footer);
        AutomationProperties.SetControlTypeOverride(root, AutomationControlType.Group);
        Describe();
        Build();
    }

    public override Control Control => root;

    /// <summary>The data on show.</summary>
    public NthUiChartModel Model => model;

    /// <summary>The plot (line, area, scatter, vertical bars), when the chart is drawn as one.</summary>
    public NthUiChartPlot? Plot => plot;

    /// <summary>The pie, when the chart is one.</summary>
    public NthUiPieChart? Pie => pie;

    /// <summary>The pie's key.</summary>
    public NthUiPieKey? PieKey => pieKey;

    /// <summary>The bar list, when the chart is drawn as the canvas's bars.</summary>
    public NthUiBarList? Bars => bars;

    /// <summary>The key of several series.</summary>
    public NthUiSeriesKey Key => key;

    /// <summary>The "Show as table" button.</summary>
    public ToggleButton TableButton => toggle;

    /// <summary>The table, once it has been shown.</summary>
    public NthUiChartTable Table => table;

    /// <summary>Whether the table is on show in place of the drawing.</summary>
    public bool IsTableShown => tableShown;

    /// <summary>The words that describe the chart, as a screen reader gets them.</summary>
    public string Summary => NthUiChartSummary.Describe(model, Runtime.Culture);

    public override NthUiNodeView? Add(NthUiNode child)
    {
        if (child.Kind != "series" || child.Degraded) return null;
        Context.Register(child, this);
        Refresh();
        return null;
    }

    public override void Refresh()
    {
        model = NthUiChartModel.From(Runtime.Chart(Node));
        title?.SetCurrentValue(TextBlock.TextProperty, model.Data.Title ?? "");
        Describe();
        if (layer is null || Wanted(model) != Current())
        {
            Build();
            return;
        }
        switch (model.Layout)
        {
            case NthUiChartLayout.BarList:
                bars?.Show(model);
                break;
            case NthUiChartLayout.Pie:
                pie?.Show(model);
                pieKey?.Show(model);
                break;
            default:
                plot?.Show(model);
                break;
        }
        key.Show(model);
        key.IsVisible = model.Layout == NthUiChartLayout.Plot && model.Series.Count > 1;
        if (tableBuilt) table.Show(NthUiChartRows.Build(model, Runtime.Culture));
    }

    private void Describe()
    {
        AutomationProperties.SetName(root, model.Data.Title is { Length: > 0 } caption ? caption : NthUiStrings.Chart);
        AutomationProperties.SetHelpText(root, Summary);
        AutomationProperties.SetName(toggle, tableShown ? NthUiStrings.ShowAsChart : NthUiStrings.ShowAsTable);
    }

    private NthUiChartLayout Current() =>
        plot is not null ? NthUiChartLayout.Plot : pie is not null ? NthUiChartLayout.Pie : NthUiChartLayout.BarList;

    private static NthUiChartLayout Wanted(NthUiChartModel of) => of.Layout;

    /// <summary>Makes the drawing the data wants and puts it in place of the one on show.</summary>
    private void Build()
    {
        var culture = Runtime.Culture;
        plot = null;
        pie = null;
        pieKey = null;
        bars = null;
        Control next;
        switch (model.Layout)
        {
            case NthUiChartLayout.BarList:
                bars = new NthUiBarList(Context, model, culture);
                next = bars;
                break;
            case NthUiChartLayout.Pie:
                pie = new NthUiPieChart(Context, model, culture);
                pieKey = new NthUiPieKey(pie, culture);
                next = new NthUiPieLayout { Children = { pie, pieKey } };
                break;
            default:
                plot = new NthUiChartPlot(Context, model, culture);
                plot.PointRequested += (index, value) => Context.Changed(Runtime.SetPoint(Node, index, value));
                next = plot;
                break;
        }
        key.Show(model);
        key.IsVisible = model.Layout == NthUiChartLayout.Plot && model.Series.Count > 1;
        if (layer is not null) host.Children.Remove(layer);
        layer = next;
        layer.IsVisible = !tableShown;
        host.Children.Insert(0, layer);
        if (tableBuilt) table.Show(NthUiChartRows.Build(model, culture));
    }

    /// <summary>Shows the table in place of the drawing (or the drawing again): the height follows by spring and the incoming
    /// side fades in where it sits.</summary>
    public void ShowTable(bool on)
    {
        if (tableShown == on) return;
        tableShown = on;
        toggle.IsChecked = on;
        toggleText.Text = on ? NthUiStrings.ShowAsChart : NthUiStrings.ShowAsTable;
        var icon = on ? chartIcon : tableIcon;
        if (icon is not null && toggleContent.Children.Count > 0 && !ReferenceEquals(toggleContent.Children[0], icon))
        {
            icon.VerticalAlignment = VerticalAlignment.Center;
            toggleContent.Children[0] = icon;
        }
        if (!tableBuilt)
        {
            tableBuilt = true;
            table.Show(NthUiChartRows.Build(model, Runtime.Culture));
            host.Children.Add(table.Control);
        }
        Slot?.AnimateNextChange();
        var incoming = on ? table.Control : layer!;
        var outgoing = on ? layer! : table.Control;
        outgoing.IsVisible = false;
        incoming.IsVisible = true;
        AutomationProperties.SetName(toggle, on ? NthUiStrings.ShowAsChart : NthUiStrings.ShowAsTable);
        if (!Context.Motion.CanAnimate) return;
        var now = NthUiMotion.Now;
        incoming.Opacity = 0;
        var fade = new Fade(incoming, now, Context.IsCalm(incoming) ? NthUiTokens.CalmFadeSeconds : NthUiTokens.FadeSeconds);
        fades.Clear();
        fades.Add(fade);
        Context.Motion.Start(fade);
    }

    private sealed class Fade(Control target, double start, double seconds) : INthUiAnimated
    {
        public bool Step(double now)
        {
            var p = seconds <= 0 ? 1 : Math.Clamp((now - start) / seconds, 0, 1);
            target.Opacity = NthEase.Out.Ease(p);
            if (p < 1) return true;
            target.Opacity = 1;
            return false;
        }
    }
}
