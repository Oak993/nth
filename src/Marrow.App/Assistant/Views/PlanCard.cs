// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.4, 11.5; the approved canvas "A big task, by voice"; unit AS-13; ED-NEW-dextra-as13-10). No Swift twin.
// The card the owner sees before a big task starts, and again when it ends. Before: one line ("About 12% of Codex A, 8% of Claude B
// and up to 6% of Claude 20x. Your 30% reserve on Claude 20x stays."), how his rule was read, a bar per account (used, planned,
// reserve, floor) and Start and Adjust; Start is there when the plan needs him (11.4), with the reason beside it. After: what each
// account used against the plan, the state of every reserve, and the use outside nth on its own (11.5).
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public sealed class PlanCard : Border
{
    public const double CardPadding = 14;
    public const string StartTitle = "Start";
    public const string AdjustTitle = "Adjust";

    private readonly Action? start;
    private readonly Action? adjust;
    private readonly LiveStatusText line = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock reading = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel rows = new() { Orientation = Orientation.Vertical, Spacing = 8 };
    private readonly Border actions = new();
    private readonly StackPanel content = new() { Orientation = Orientation.Vertical, Spacing = 8 };

    /// <param name="start">What Start does (the plan needed the owner's tap); null: the card shows no Start.</param>
    /// <param name="adjust">What Adjust does; null: no Adjust.</param>
    public PlanCard(Action? start = null, Action? adjust = null)
    {
        this.start = start;
        this.adjust = adjust;
        SwiftFont.System(13, SwiftFont.Weight.Medium).Apply(line);
        line.Foreground = Chrome.PrimaryText.Brush(line);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(reading);
        reading.Foreground = Chrome.SecondaryText.Brush(reading);
        AutomationProperties.SetLiveSetting(line, AutomationLiveSetting.Polite);
        content.Children.Add(line);
        content.Children.Add(reading);
        content.Children.Add(rows);
        content.Children.Add(actions);
        Child = content;
        Padding = new Thickness(CardPadding);
        this.RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous);
        Background = Chrome.Overlay(0.04).Brush(this);
        AutomationProperties.SetName(this, "Usage plan");
        Focusable = false;
    }

    /// <summary>The plan line or the report's headline, as the card says it now.</summary>
    public string Line => line.Text ?? "";

    /// <summary>The reading under it: how the rule was read, the team, the time and whether Start needs the owner.</summary>
    public string Reading => reading.Text ?? "";

    public IReadOnlyList<ReserveBarRow> Rows => [.. rows.Children.OfType<ReserveBarRow>()];

    /// <summary>The Start button, when the plan needs the owner's tap and the card was given something for it to do.</summary>
    public Button? StartButton { get; private set; }

    public Button? AdjustButton { get; private set; }

    /// <summary>The reason Start waits for the owner ("Start needs you: over 5% of a window."), or empty.</summary>
    public string Note { get; private set; } = "";

    /// <summary>The plan, before the task starts.</summary>
    public void ShowPlan(ReservePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Clear();
        AutomationProperties.SetName(this, "Usage plan");
        line.Text = plan.Line;
        reading.Text = plan.Details;
        var tightest = plan.Accounts.Where(a => a.Room is not null).OrderBy(a => a.Room).FirstOrDefault();
        foreach (var account in plan.Accounts)
        {
            var row = new ReserveBarRow();
            // The number beside the bar is the number the line says: up to the p90 for the tightest account, the p50 for the rest.
            var planned = ReferenceEquals(account, tightest) ? account.PlannedP90 : account.PlannedP50;
            row.Set(account.Name, ReserveBarRow.Line(account.Used, planned, account.Floor, account.WindowLabel, account.Rough), account.Used, account.PlannedP90, account.Floor);
            rows.Children.Add(row);
        }
        var needs = plan.StartNeeds;
        Note = needs.Count == 0 ? "" : "Start needs you: " + string.Join("; ", needs) + ".";
        // The buttons wrap and the reason sits under them, so a narrow card or large text never runs off its edge.
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        if (needs.Count > 0 && start is not null)
        {
            var (button, shown) = Capsule(StartTitle, start, "Start the task as planned");
            StartButton = button;
            buttons.Children.Add(shown);
        }
        if (adjust is not null)
        {
            var (button, shown) = Capsule(AdjustTitle, adjust, "Change what the task may use");
            AdjustButton = button;
            buttons.Children.Add(shown);
        }
        var below = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        if (buttons.Children.Count > 0) below.Children.Add(buttons);
        if (Note.Length > 0) below.Children.Add(Secondary(Note));
        actions.Child = below.Children.Count == 0 ? null : below;
        actions.IsVisible = below.Children.Count > 0;
    }

    /// <summary>The task runs: the buttons give way to one status line, read out when it changes.</summary>
    public void ShowRunning(string text)
    {
        Clear();
        StartButton = null;
        AdjustButton = null;
        Note = "";
        var status = new LiveStatusText { Text = text, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(status);
        status.Foreground = Chrome.SecondaryText.Brush(status);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        actions.Child = status;
        actions.IsVisible = true;
    }

    /// <summary>The report, when the task ends (11.5 "After the mission").</summary>
    public void ShowReport(ReserveAfterReport report, string headline)
    {
        ArgumentNullException.ThrowIfNull(report);
        Clear();
        StartButton = null;
        AdjustButton = null;
        Note = "";
        AutomationProperties.SetName(this, "Usage report");
        line.Text = headline;
        reading.Text = report.ReserveLine;
        rows.Children.Add(Table(report));
        foreach (var outside in report.OutsideLines) rows.Children.Add(Secondary(outside));
        actions.Child = null;
        actions.IsVisible = false;
    }

    private void Clear()
    {
        rows.Children.Clear();
        actions.Child = null;
    }

    // Account | Planned | Used, as the canvas's small table.
    private static Grid Table(ReserveAfterReport report)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 16, RowSpacing = 3 };
        void Cell(string text, int row, int column, bool head = false, bool right = false)
        {
            var cell = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: head ? SwiftFont.Weight.Semibold : SwiftFont.Weight.Regular).Apply(cell);
            cell.Foreground = (head ? Chrome.SecondaryText : Chrome.PrimaryText).Brush(cell);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Cell("Account", 0, 0, head: true);
        Cell("Planned", 0, 1, head: true, right: true);
        Cell("Used", 0, 2, head: true, right: true);
        for (var i = 0; i < report.Rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var row = report.Rows[i];
            Cell(row.AccountName, i + 1, 0);
            Cell(row.PlannedPercent.ToString("0", CultureInfo.InvariantCulture) + "%", i + 1, 1, right: true);
            Cell(row.UsedPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%", i + 1, 2, right: true);
        }
        AutomationProperties.SetName(grid, "What each account used, against the plan");
        return grid;
    }

    private static TextBlock Secondary(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(block);
        block.Foreground = Chrome.SecondaryText.Brush(block);
        return block;
    }

    // The button, and what to put on the card: the button inside its glass capsule.
    internal static (Button Button, Control Shown) Capsule(string title, Action action, string accessibleName)
    {
        var label = new TextBlock { Text = title, MaxLines = 1, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(12.5, SwiftFont.Weight.Medium).Apply(label);
        label.Foreground = Chrome.PrimaryText.Brush(label);
        var face = new Border { Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0), Height = Chrome.CapsuleContentHeight, Child = label, Background = Brushes.Transparent };
        face.Capsule(RoundedCornerStyle.Continuous);
        var button = PlainControls.Button(face, action);
        AutomationProperties.SetName(button, accessibleName);
        return (button, button.ChromeGlassCapsule());
    }
}
