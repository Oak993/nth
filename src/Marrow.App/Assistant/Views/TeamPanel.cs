// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 9.4 "Org chart (Team tab)", 16 AS-17; ED-NEW-dextra-as17-5, -6). No Swift twin.
// The Team tab: a heading with the size of the team and "Add an Astur", the org chart, a line when there are no Asturs yet, the detail of the selected node, and a line for any profile file nth
// could not use. It is handed to the Board's Team tab through the seam AS-8 left (TeamTab's chart). It reads the team from one source, shows a new snapshot whenever the source changes (once
// for a burst of changes, on the UI thread) and every half minute (today's use moves slowly), and says what Pause, Resume and Retire did in a polite live region of its own.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public sealed class TeamPanel : Decorator
{
    /// <summary>How often today's use is read again while the panel is on a window (the figures move slowly).</summary>
    public static readonly TimeSpan UsageEvery = TimeSpan.FromSeconds(30);

    private readonly ITeamSource source;
    private readonly TeamActions actions;
    private readonly Func<IReadOnlyList<string>>? problems;
    private readonly TextBlock title = TaskLook.Text("Team", 14, SwiftFont.Weight.Semibold, TaskLook.Ink);
    private readonly TextBlock count = TaskLook.Text("", 10.5, SwiftFont.Weight.Semibold, TaskLook.Ink2);
    private readonly Border countBox = new() { Padding = new Thickness(6, 0), Height = 18, MinWidth = 18, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock empty = TaskLook.Text(OrgChartWords.NoAsturs, 12, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);
    private readonly TextBlock trouble = TaskLook.Text("", 11.5, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);
    private int queued;
    private ITimer? usageTimer;
    private bool disposed;

    public TeamPanel(ITeamSource source, TeamActions? actions = null, Func<IReadOnlyList<string>>? problems = null)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.actions = actions ?? new TeamActions();
        this.problems = problems;

        AutomationProperties.SetHeadingLevel(title, 2);
        countBox.Capsule(RoundedCornerStyle.Continuous);
        countBox.Background = Marrow.UI.Chrome.Chrome.Overlay(.1).Brush(countBox);
        countBox.Child = count;
        count.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(countBox, AccessibilityView.Raw);
        Add = new DetailButton("Add an Astur");
        Add.Click += (_, _) => this.actions.Add?.Invoke();
        AutomationProperties.SetName(Add, "Add an Astur");

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { title, countBox } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(Add, 1);
        header.Children.Add(left);
        header.Children.Add(Add);

        Chart = new OrgChart(this.actions.OpenChat);
        Live = new LiveStatusText { Width = 1, Height = 1, ClipToBounds = true, Opacity = 0, IsHitTestVisible = false };
        Detail = new OrgDetail(source, this.actions, Say);
        Chart.SelectionChanged += _ => ShowDetail();
        trouble.IsVisible = false;

        Child = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, Children = { header, Chart, empty, Detail, trouble, Live } };
        AutomationProperties.SetName(this, "Team");
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Group);
        Refresh();
    }

    public OrgChart Chart { get; }

    public OrgDetail Detail { get; }

    public DetailButton Add { get; }

    public Border CountBox => countBox;

    /// <summary>The polite live region: what Pause, Resume and Retire did.</summary>
    public LiveStatusText Live { get; }

    public string CountText => count.Text ?? "";

    public bool EmptyHintShown => empty.IsVisible;

    public string TroubleText => trouble.Text ?? "";

    /// <summary>The team a moment ago, for a test to compare.</summary>
    public TeamSnapshot Snapshot => Chart.Snapshot;

    /// <summary>Says <paramref name="line"/> in the polite live region (the same words twice still announce).</summary>
    public void Say(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (Live.Text == line) Live.Text = "";
        Live.Text = line;
    }

    /// <summary>Reads the team again and shows it.</summary>
    public void Refresh()
    {
        var snap = source.Now();
        Chart.Show(snap);
        var asturs = snap.Asturs.Count();
        count.Text = (asturs + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        AutomationProperties.SetName(countBox, ControlWords.Plural(asturs + 1, "assistant"));
        empty.IsVisible = asturs == 0 && snap.Dextra is not null;
        var full = asturs >= AsturShapes.MostAsturs;
        Add.IsEnabled = actions.Add is not null && !full;
        ToolTip.SetTip(Add, full ? OrgChartWords.AllSixMade : null);
        AutomationProperties.SetHelpText(Add, full ? OrgChartWords.AllSixMade : "");
        var lines = problems?.Invoke() ?? [];
        trouble.Text = string.Join(' ', lines);
        trouble.IsVisible = lines.Count > 0;
        ShowDetail();
    }

    private void ShowDetail()
    {
        if (Chart.SelectedMember is { } selected) Detail.Show(selected, TaskLook.Still(this));
    }

    // ---- the source's changes, once for a burst, on the UI thread ----

    private void Queue()
    {
        if (disposed || Interlocked.Exchange(ref queued, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref queued, 0);
            if (!disposed) Refresh();
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        source.Changed += Queue;
        Refresh();
        usageTimer?.Dispose();
        usageTimer = SwiftClock.Provider.Current.CreateTimer(_ => Dispatcher.UIThread.Post(() =>
        {
            if (!disposed) Refresh();
        }), null, UsageEvery, UsageEvery);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        source.Changed -= Queue;
        usageTimer?.Dispose();
        usageTimer = null;
    }

    /// <summary>The panel is let go for good (the home was rebuilt): nothing it queued runs.</summary>
    public void Close()
    {
        disposed = true;
        source.Changed -= Queue;
        usageTimer?.Dispose();
        usageTimer = null;
    }
}
