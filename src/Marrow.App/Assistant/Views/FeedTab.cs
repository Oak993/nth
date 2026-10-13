// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 5.3 "Feed tab": newest first, chips All · Needs you · Finished · Errors · Usage, each
// row its time, a kind icon, the summary and the source, a click opens the source, quiet events lighter and "Hide quiet" on by
// default; the canvas's feed and applyFilter, work/design/assistant/index.html lines 2236-2251; ED-NEW-dextra-as8-6). No Swift twin.
// The live feed reads the bus off the UI thread and hands each batch to the UI thread; it keeps the newest 500 lines in view (the
// 30-day files are the store's). Quiet lines use the 70 % ink like every small line here, so nothing drops under 4.5:1; they are
// told apart by their word "quiet" in the row's name and a hollow kind dot.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.App.Views.Settings;

namespace Marrow.App.Assistant.Views;

public enum FeedFilter { All, NeedsYou, Finished, Errors, Usage }

public sealed class FeedTab : Decorator
{
    public const int Kept = 500;

    private readonly Func<Action<IReadOnlyList<FeedEvent>>, IDisposable>? subscribe;
    private readonly Func<FeedEvent, string> sourceOf;
    private readonly Action<FeedEvent>? open;
    private readonly StackPanel rows = new() { Orientation = Orientation.Vertical, Spacing = 2 };
    private readonly TextBlock empty = TaskLook.Text("Nothing yet. Events from your chats, missions and duties show here.", 12, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);
    private readonly List<FeedEvent> events = [];
    private IDisposable? subscription;
    private FeedFilter filter = FeedFilter.All;
    private bool hideQuiet = true;

    /// <param name="subscribe">The bus's Subscribe (null: an empty feed).</param>
    /// <param name="sourceOf">The source's words for a line (a chat, a head, a duty).</param>
    /// <param name="open">Opens a line's source.</param>
    public FeedTab(Func<Action<IReadOnlyList<FeedEvent>>, IDisposable>? subscribe, Func<FeedEvent, string>? sourceOf = null, Action<FeedEvent>? open = null)
    {
        this.subscribe = subscribe;
        this.sourceOf = sourceOf ?? DefaultSource;
        this.open = open;
        var chips = new ChromeSegmentedPicker<FeedFilter>(
            new (FeedFilter, string)[] { (FeedFilter.All, "All"), (FeedFilter.NeedsYou, "Needs you"), (FeedFilter.Finished, "Finished"), (FeedFilter.Errors, "Errors"), (FeedFilter.Usage, "Usage") },
            new Binding<FeedFilter>(() => filter, v => { filter = v; Paint(); }));
        AutomationProperties.SetName(chips, "Show");
        var quiet = new SettingsSwitch(new Binding<bool>(() => hideQuiet, v => { hideQuiet = v; Paint(); }));
        AutomationProperties.SetName(quiet, "Hide quiet");
        var quietLabel = TaskLook.Text("Hide quiet", 12, SwiftFont.Weight.Regular, TaskLook.Ink2);
        quietLabel.VerticalAlignment = VerticalAlignment.Center;
        var top = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 6, Children = { chips, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { quiet, quietLabel } } } };
        Child = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, Children = { top, rows, empty } };
        AutomationProperties.SetName(this, "Feed");
    }

    public FeedFilter Filter { get => filter; set { filter = value; Paint(); } }

    public bool HideQuiet { get => hideQuiet; set { hideQuiet = value; Paint(); } }

    public IReadOnlyList<Control> Rows => [.. rows.Children.Where(r => r.IsVisible)];

    public TextBlock Empty => empty;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        subscription ??= subscribe?.Invoke(batch => Dispatcher.UIThread.Post(() => Add(batch)));
        Paint();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        subscription?.Dispose();
        subscription = null;
    }

    /// <summary>Adds a batch from the bus (on the UI thread).</summary>
    public void Add(IReadOnlyList<FeedEvent> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        foreach (var line in batch)
        {
            events.Insert(0, line);
            rows.Children.Insert(0, Row(line));
        }
        while (events.Count > Kept)
        {
            events.RemoveAt(events.Count - 1);
            rows.Children.RemoveAt(rows.Children.Count - 1);
        }
        Paint();
    }

    public static bool Shows(FeedEvent line, FeedFilter filter, bool hideQuiet)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (hideQuiet && line.Severity == FeedSeverity.Quiet) return false;
        return filter switch
        {
            FeedFilter.NeedsYou => line.Severity == FeedSeverity.NeedsYou,
            FeedFilter.Finished => line.Kind is FeedEventKind.TurnFinished or FeedEventKind.HeadFinished or FeedEventKind.AgentFinished,
            FeedFilter.Errors => line.Severity == FeedSeverity.Error,
            FeedFilter.Usage => line.Kind is FeedEventKind.TokenSpend or FeedEventKind.UsageLimit or FeedEventKind.AccountWindow
                or FeedEventKind.AccountLimitReached or FeedEventKind.AccountWindowReset,
            _ => true,
        };
    }

    private void Paint()
    {
        for (var i = 0; i < events.Count; i++) rows.Children[i].IsVisible = Shows(events[i], filter, hideQuiet);
        empty.IsVisible = !rows.Children.Any(r => r.IsVisible);
    }

    private static string DefaultSource(FeedEvent line) => line.Kind switch
    {
        FeedEventKind.TokenSpend or FeedEventKind.UsageLimit or FeedEventKind.AccountWindow or FeedEventKind.AccountLimitReached or FeedEventKind.AccountWindowReset => "Usage",
        FeedEventKind.DutyFired or FeedEventKind.DutySkipped or FeedEventKind.WatcherChanged => "Duty",
        FeedEventKind.PcAction or FeedEventKind.PcApproval or FeedEventKind.PcStop => "PC control",
        FeedEventKind.MissionState or FeedEventKind.DecisionRaised or FeedEventKind.DecisionAnswered or FeedEventKind.GrantUsed => AssistantNames.Assistant,
        _ => "A chat",
    };

    private Control Row(FeedEvent line)
    {
        var time = TaskLook.Text(TaskWords.Local(line.Time).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), 11, SwiftFont.Weight.Regular, TaskLook.Ink2);
        SwiftFont.System(11).MonospacedDigit().Apply(time);
        time.Width = 36;
        var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        var ink = line.Severity is FeedSeverity.NeedsYou or FeedSeverity.Error ? Chrome.PrimaryText : Chrome.SecondaryText;
        if (line.Severity == FeedSeverity.Quiet) { dot.Stroke = ink.Brush(dot); dot.StrokeThickness = 1.25; }
        else dot.Fill = ink.Brush(dot);
        var summary = TaskLook.Text(line.Summary, 12, line.Severity is FeedSeverity.NeedsYou or FeedSeverity.Error ? SwiftFont.Weight.Medium : SwiftFont.Weight.Regular,
            line.Severity == FeedSeverity.Quiet ? TaskLook.Ink2 : TaskLook.Ink, wrap: true);
        var source = TaskLook.Text(sourceOf(line), 11, SwiftFont.Weight.Regular, TaskLook.Ink2);
        var texts = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1, Children = { summary, source } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 8 };
        Grid.SetColumn(dot, 1);
        Grid.SetColumn(texts, 2);
        grid.Children.Add(time);
        grid.Children.Add(dot);
        grid.Children.Add(texts);
        var face = new Border { Padding = new Thickness(6, 5), Child = grid, Background = Brushes.Transparent };
        face.RoundedRectangle(9, RoundedCornerStyle.Continuous);
        var name = $"{time.Text}, {(line.Severity == FeedSeverity.Quiet ? "quiet, " : line.Severity == FeedSeverity.Error ? "error, " : line.Severity == FeedSeverity.NeedsYou ? "needs you, " : "")}{line.Summary}, {source.Text}";
        if (open is null || line.ThreadId is null && line.MissionId is null)
        {
            AutomationProperties.SetName(face, name);
            return face;
        }
        var hot = Chrome.Overlay(.05).Brush(face);
        var button = PlainControls.Button(face, () => open(line));
        button.KeyboardFocus(9);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.PointerEntered += (_, _) => face.Background = hot;
        button.PointerExited += (_, _) => face.Background = Brushes.Transparent;
        AutomationProperties.SetName(button, name);
        return button;
    }
}
