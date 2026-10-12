// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5 "Activity view": lineage runs mission → agent →
// action → grant; unit AS-21). No Swift counterpart.
// ED-NEW-dextra-as21-6: the detail of the selected line.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.Services.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

/// <summary>
/// Everything the log holds about the selected line: when, who (assistant, mission, agent), what (tool, class, target, inputs,
/// result), what it cost, what allowed it and where it sits in the chain (its number, its HMAC and the one before it). A
/// mission, an agent or a grant here is a button that filters the log to it: that is how the lineage is walked. A value is
/// selectable, so the owner can copy a path, a line number or an HMAC.
/// </summary>
internal sealed class ActivityDetailPanel : Decorator
{
    private readonly ActivityLogModel model;
    private readonly TimeZoneInfo zone;
    private readonly Decorator host = new();
    private readonly List<Button> actions = [];

    public ActivityDetailPanel(ActivityLogModel model, TimeZoneInfo? zone = null)
    {
        this.model = model;
        this.zone = zone ?? TimeZoneInfo.Local;
        var scroll = new ScrollViewer
        {
            Content = host,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        SoftScroll.SetIsEnabled(scroll, true);
        Child = scroll;
        AutomationProperties.SetName(this, "Line detail");
        Refresh();
    }

    /// <summary>The buttons that filter the log from the selected line, by their names.</summary>
    public IReadOnlyList<Button> Actions => actions;

    /// <summary>Every text the panel shows now, in order (a test reads the panel through this).</summary>
    public IReadOnlyList<string> Texts => host.Child is null ? [] : Descendants(host.Child).OfType<TextBlock>().Select(t => t.Text ?? "").Where(t => t.Length > 0).ToList();

    public void Refresh()
    {
        actions.Clear();
        host.Child = model.Selected is { } row ? Build(row) : Empty();
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.GetLogicalChildren().OfType<Control>())
            foreach (var inner in Descendants(child))
                yield return inner;
    }

    private static Control Empty()
    {
        var text = new TextBlock { Text = ActivityWords.NothingSelected, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) };
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(text);
        text.Foreground = Chrome.SecondaryText.Brush(text);
        return text;
    }

    private Control Build(ActivityRow row)
    {
        var labels = model.Labels;
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12, Margin = new Thickness(16) };
        stack.Children.Add(Heading(row));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("84,*"), RowSpacing = 8 };
        var next = 0;
        void Add(string label, string? value, bool mono = false, string? filterName = null, Action? run = null)
        {
            if (string.IsNullOrEmpty(value)) return;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = Muted(label);
            Grid.SetRow(name, next);
            var cell = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
            cell.Children.Add(Value(value, mono));
            if (filterName is not null && run is not null) cell.Children.Add(FilterButton(filterName, run));
            Grid.SetRow(cell, next);
            Grid.SetColumn(cell, 1);
            grid.Children.Add(name);
            grid.Children.Add(cell);
            next++;
        }
        if (row.Kind != ActivityRowKind.Unreadable)
        {
            Add("When", ActivityWords.Stamp(row.Time, zone));
            Add("Assistant", labels.ForAssistant(row.Assistant), filterName: "Filter to this assistant", run: () => _ = model.UpdateFilterAsync(f => f with { Assistant = row.Assistant }));
            if (row.Mission is { Length: > 0 } mission)
                Add("Mission", labels.ForMission(mission), filterName: "Show this mission's lineage", run: () => _ = model.ShowMissionAsync(mission));
            if (row.Agent is { Length: > 0 } agent)
                Add("Agent", labels.ForAgent(agent), filterName: "Filter to this agent", run: () => _ = model.UpdateFilterAsync(f => f with { Agent = agent }));
            Add("Tool", row.Tool);
            Add("Class", row.Class, filterName: "Filter to this class", run: () => _ = model.UpdateFilterAsync(f => f with { Class = row.Class }));
            Add("Target", row.Target);
            Add("Result", row.Result);
            if (row.AllowedBy is { Length: > 0 } by)
                Add("Allowed by", by, mono: true, filterName: "Filter to this grant", run: () => _ = model.UpdateFilterAsync(f => f with { Grant = by }));
            else if (row.Kind == ActivityRowKind.Action) Add("Allowed by", "Nothing: no grant or decision covered this action");
            if (row.CostUsd is { } cost) Add("Cost", ActivityWords.Money(cost));
            if (row.Tokens is { } tokens) Add("Tokens", ActivityWords.Tokens(tokens));
        }
        if (row.Note is { Length: > 0 } note) Add(row.Kind == ActivityRowKind.Unreadable ? "Why" : "Meaning", note);
        stack.Children.Add(grid);

        if (row.Inputs.Count > 0 || row.InputsNotLogged > 0)
        {
            stack.Children.Add(Section("Inputs"));
            var inputs = new Grid { ColumnDefinitions = new ColumnDefinitions("84,*"), RowSpacing = 6 };
            var at = 0;
            foreach (var input in row.Inputs)
            {
                inputs.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var name = Muted(input.Name);
                Grid.SetRow(name, at);
                var value = Value(input.Display, input.Kind == AuditInputKind.Text);
                Grid.SetRow(value, at);
                Grid.SetColumn(value, 1);
                inputs.Children.Add(name);
                inputs.Children.Add(value);
                at++;
            }
            if (row.InputsNotLogged > 0)
            {
                inputs.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var more = Muted(ActivityWords.Count(row.InputsNotLogged, "more input was", "more inputs were") + " not logged");
                Grid.SetRow(more, at);
                Grid.SetColumnSpan(more, 2);
                inputs.Children.Add(more);
            }
            stack.Children.Add(inputs);
        }

        stack.Children.Add(Section("In the chain"));
        var chain = new Grid { ColumnDefinitions = new ColumnDefinitions("84,*"), RowSpacing = 6 };
        var link = 0;
        void Link(string label, string? value, bool mono)
        {
            if (string.IsNullOrEmpty(value)) return;
            chain.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = Muted(label);
            Grid.SetRow(name, link);
            var text = Value(value, mono);
            Grid.SetRow(text, link);
            Grid.SetColumn(text, 1);
            chain.Children.Add(name);
            chain.Children.Add(text);
            link++;
        }
        Link("Check", ActivityWords.TrustWords(row.Trust), false);
        Link("Line", $"{row.At.Line.ToString(CultureInfo.InvariantCulture)} of {row.At.File}" + (row.At.Sequence is { } n ? $" · number {n.ToString(CultureInfo.InvariantCulture)}" : ""), false);
        Link("HMAC", row.Hmac, true);
        Link("Previous", row.Prev, true);
        stack.Children.Add(chain);
        return stack;
    }

    private Control Heading(ActivityRow row)
    {
        var title = new TextBlock
        {
            Text = row.Kind switch
            {
                ActivityRowKind.Unreadable => "Unreadable line",
                ActivityRowKind.Record => "Log record: " + (row.Event ?? ""),
                _ => row.Tool,
            },
            TextWrapping = TextWrapping.Wrap,
        };
        SwiftFont.System(SwiftFont.TextStyle.Headline).Apply(title);
        title.Foreground = Chrome.PrimaryText.Brush(title);
        AutomationProperties.SetName(title, title.Text);
        return title;
    }

    private static TextBlock Section(string text)
    {
        var block = new TextBlock { Text = text };
        SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold).Apply(block);
        block.Foreground = Chrome.SecondaryText.Brush(block);
        block.Margin = new Thickness(0, 4, 0, 0);
        return block;
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
        SwiftFont.System(12).Apply(block);
        block.Foreground = Chrome.SecondaryText.Brush(block);
        return block;
    }

    private static SelectableTextBlock Value(string text, bool mono)
    {
        var block = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
        (mono ? SwiftFont.MonospacedSystemFont(11.5, SwiftFont.Weight.Regular) : SwiftFont.System(12.5)).Apply(block);
        block.Foreground = Chrome.PrimaryText.Brush(block);
        return block;
    }

    private Button FilterButton(string title, Action run)
    {
        var label = new TextBlock { Text = title, MaxLines = 1, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(11.5, SwiftFont.Weight.Medium).Apply(label);
        label.Foreground = Chrome.Accent.Brush(label);
        var face = new Border { Padding = new Thickness(0, 2), Child = label, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left };
        var button = PlainControls.Button(face, run);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(button, title);
        actions.Add(button);
        return button;
    }
}
