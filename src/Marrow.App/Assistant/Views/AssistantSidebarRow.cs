// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.1 "Sidebar": the pinned Assistant row above "Needs attention" with Armilla at
// 24 px in its small form, the name, one live status line and a count badge for open decisions; the collapsed "Missions" group
// of the lead chats the Assistant starts, remembered; the right-click menu Open, Talk, Stop all, Pause all, Pause duties for
// 1 hour, Settings; ED-NEW-dextra-as8-8). No Swift twin.
// The row reads the host's one set of sources (DextraLive), so the sidebar and the home never disagree. Its fill follows the
// sidebar's own rows (hover 6 %, selected 12 %, radius 7, continuous corners); its mark joins the window's motion budget as an
// "other" list, after the Board, the Result list and the header.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Safety;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

/// <summary>What the row's click, its menu and its Missions group call. Talk and Pause duties show only when given.</summary>
public sealed record AssistantRowActions
{
    public Action? Open { get; init; }

    public Action? Talk { get; init; }

    public Action? PauseDuties { get; init; }

    public Action? Settings { get; init; }

    /// <summary>Opens a mission's lead chat ("watching its computer").</summary>
    public Action<Guid>? OpenChat { get; init; }

    /// <summary>A note that replaces the status line (Dextra off, first run not done); null shows the live status.</summary>
    public Func<string?>? Note { get; init; }

    /// <summary>The Missions group is collapsed; and its setter (remembered in DextraSettings).</summary>
    public Func<bool>? Collapsed { get; init; }

    public Action<bool>? SetCollapsed { get; init; }

    /// <summary>What a control's answer says (the live region of the window, if any).</summary>
    public Action<string>? Said { get; init; }
}

public sealed class AssistantSidebarRow : Decorator
{
    public const double MarkSize = 24;
    public const double RowHeight = 44;

    private readonly AssistantRowActions actions;
    private readonly Border face;
    private readonly Grid grid;
    private readonly StackPanel stack;
    private readonly IBrush hoverFill;
    private readonly IBrush selectedFill;
    private readonly StackPanel missionRows = new() { Spacing = 1 };
    private readonly RevealBox missionsReveal = new();
    private readonly Dictionary<Guid, (Button Button, ArmillaMark Mark, TextBlock Title)> missionLines = [];
    private DextraLive live = DextraLive.Idle;
    private MotionScope.Entry? budget;
    private bool selected;
    private bool hovering;
    private int queued;

    public AssistantSidebarRow(string name, AssistantRowActions? actions = null)
    {
        this.actions = actions ?? new AssistantRowActions();

        Mark = new ArmillaMark { MarkSize = MarkSize, Kind = ArmillaKind.Row, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(Mark, AccessibilityView.Raw);
        NameText = new TextBlock { Text = name, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        SwiftFont.System(13, SwiftFont.Weight.Medium).Apply(NameText);
        NameText.Foreground = Chrome.PrimaryText.Brush(NameText);
        StatusText = new TextBlock { MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        SwiftFont.System(11.5).MonospacedDigit().Apply(StatusText);
        StatusText.Foreground = Chrome.SecondaryText.Brush(StatusText);

        BadgeText = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(11, SwiftFont.Weight.Semibold).MonospacedDigit().Apply(BadgeText);
        BadgeText.Foreground = TaskLook.Inverse.Brush(BadgeText);
        Badge = new Border { MinWidth = 18, Height = 18, Padding = new Thickness(5, 0), Child = BadgeText, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        Badge.Capsule(RoundedCornerStyle.Continuous);
        Badge.Background = Chrome.PrimaryText.Brush(Badge);
        AutomationProperties.SetAccessibilityView(Badge, AccessibilityView.Raw);

        var lines = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { NameText, StatusText } };
        grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(Mark);
        grid.Children.Add(lines);
        Grid.SetColumn(lines, 1);
        Grid.SetColumn(Badge, 2);

        face = new Border { Height = RowHeight, Padding = new Thickness(Chrome.RowHorizontalPadding - 2, 0, Chrome.RowHorizontalPadding, 0), Background = Brushes.Transparent, Child = grid };
        face.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous);
        hoverFill = Chrome.Overlay(.06).Brush(face);
        selectedFill = Chrome.Overlay(.12).Brush(face);

        Button = PlainControls.Button(face, () => this.actions.Open?.Invoke());
        Button.KeyboardFocus(Chrome.RowCornerRadius);
        Button.HorizontalAlignment = HorizontalAlignment.Stretch;
        Button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Button.PointerEntered += (_, _) => { hovering = true; Paint(); };
        Button.PointerExited += (_, _) => { hovering = false; Paint(); };
        Menu = new ContextMenu();
        Menu.Opening += (_, _) => FillMenu();
        Button.ContextMenu = Menu;

        // The Missions group: a disclosure line with its count, then one line per mission's lead chat.
        var chevron = TaskLook.Chevron();
        MissionsLabel = new TextBlock { Text = "Missions", VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(12, SwiftFont.Weight.Medium).MonospacedDigit().Apply(MissionsLabel);
        MissionsLabel.Foreground = Chrome.SecondaryText.Brush(MissionsLabel);
        var toggleFace = new Border
        {
            Height = 24,
            Padding = new Thickness(Chrome.RowHorizontalPadding + 4, 0, Chrome.RowHorizontalPadding, 0),
            Background = Brushes.Transparent,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { chevron, MissionsLabel } },
        };
        MissionsToggle = new DisclosureButton { Content = toggleFace, Padding = default, Background = Brushes.Transparent, BorderThickness = default, HorizontalAlignment = HorizontalAlignment.Stretch };
        MissionsToggle.KeyboardFocus(Chrome.RowCornerRadius);
        MissionsToggle.Click += (_, _) =>
        {
            var open = !MissionsToggle.IsExpanded;
            this.actions.SetCollapsed?.Invoke(!open);
            ShowMissionsOpen(open, TaskLook.Still(this));
        };
        missionsReveal.Child = new StackPanel { Margin = new Thickness(8, 1, 0, 2), Children = { missionRows } };
        MissionsGroup = new StackPanel { Spacing = 0, Children = { MissionsToggle, missionsReveal } };
        Chevron = chevron;

        // The badge and the Missions group join the row the first time they have something to show: a part that was never shown is not
        // kept, hidden and never measured, in the tree.
        stack = new StackPanel { Spacing = 1, Children = { Button } };
        Child = stack;
        ShowMissionsOpen(!(this.actions.Collapsed?.Invoke() ?? true), still: true);
        Refresh();
    }

    public ArmillaMark Mark { get; }

    public TextBlock NameText { get; }

    public TextBlock StatusText { get; }

    public Border Badge { get; }

    public TextBlock BadgeText { get; }

    /// <summary>The row itself: opens the home; right-click opens <see cref="Menu"/>.</summary>
    public Button Button { get; }

    public ContextMenu Menu { get; }

    public StackPanel MissionsGroup { get; }

    public DisclosureButton MissionsToggle { get; }

    public TextBlock MissionsLabel { get; }

    internal Avalonia.Controls.Shapes.Path Chevron { get; }

    /// <summary>The lead-chat lines of the Missions group, newest first (live ones before finished ones).</summary>
    public IReadOnlyList<Button> MissionButtons => [.. missionRows.Children.OfType<Button>()];

    public bool MissionsOpen => MissionsToggle.IsExpanded;

    /// <summary>The home is in the main area.</summary>
    public bool IsSelected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            Paint();
            NameText.FontWeight = value ? FontWeight.SemiBold : FontWeight.Medium;
        }
    }

    /// <summary>Reads another set of sources (the controller started or stopped).</summary>
    public void Bind(DextraLive sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (ReferenceEquals(live, sources)) return;
        Watch(false);
        live = sources;
        if (IsAttachedToVisualTree()) Watch(true);
        Refresh();
    }

    private bool IsAttachedToVisualTree() => Avalonia.VisualTree.VisualExtensions.IsAttachedToVisualTree(this);

    /// <summary>The words of the status line, the badge and the mark, from the sources now.</summary>
    public void Refresh()
    {
        var jobs = live.Jobs.Snapshot(SwiftClock.Provider.Current.GetUtcNow());
        var stopped = live.Controls.Stopped is not null;
        StatusText.Text = actions.Note?.Invoke() ?? HomeWords.Status(jobs, stopped);
        Mark.State = HomeWords.Mark(jobs, stopped);
        var open = live.Decisions.Waiting.Count;
        if (open > 0 && Badge.Parent is null) grid.Children.Add(Badge);
        Badge.IsVisible = open > 0;
        BadgeText.Text = open.ToString(System.Globalization.CultureInfo.CurrentCulture);
        AutomationProperties.SetName(Button, NameText.Text + ", " + StatusText.Text);
        AutomationProperties.SetHelpText(Button, open == 0 ? "No decisions open" : ControlWords.Plural(open, "decision") + " open");
        ShowMissions(jobs);
        budget?.Changed();
    }

    private void ShowMissions(IReadOnlyList<MissionJob> jobs)
    {
        var today = TaskWords.Local(SwiftClock.Provider.Current.GetUtcNow()).Date;
        var shown = jobs.Where(j => j.LeadThread is not null && (j.Group != BoardGroup.DoneToday || j.Ended is { } ended && TaskWords.Local(ended).Date == today))
            .OrderBy(j => j.Group == BoardGroup.DoneToday ? 1 : 0).ThenByDescending(j => j.Created).ToList();
        if (shown.Count > 0 && MissionsGroup.Parent is null) stack.Children.Add(MissionsGroup);
        MissionsGroup.IsVisible = shown.Count > 0;
        MissionsLabel.Text = shown.Count == 0 ? "Missions" : "Missions  " + shown.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        AutomationProperties.SetName(MissionsToggle, (MissionsToggle.IsExpanded ? "Hide" : "Show") + " missions, " + shown.Count.ToString(System.Globalization.CultureInfo.CurrentCulture));
        var keep = shown.Select(j => j.Id).ToHashSet();
        foreach (var gone in missionLines.Keys.Where(id => !keep.Contains(id)).ToList()) missionLines.Remove(gone);
        missionRows.Children.Clear();
        foreach (var job in shown)
        {
            if (!missionLines.TryGetValue(job.Id, out var line)) missionLines[job.Id] = line = MissionLine(job);
            TaskMark.Show(line.Mark, job);
            line.Title.Text = job.Title;
            AutomationProperties.SetName(line.Button, job.Title + ", " + MissionStates.Label(job.State));
            line.Mark.Calm = calm;
            missionRows.Children.Add(line.Button);
        }
    }

    private (Button Button, ArmillaMark Mark, TextBlock Title) MissionLine(MissionJob job)
    {
        var mark = TaskMark.Make();
        AutomationProperties.SetAccessibilityView(mark, AccessibilityView.Raw);
        var title = new TextBlock { MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(12.5).Apply(title);
        title.Foreground = Chrome.PrimaryText.Opacity(.92).Brush(title);
        var face = new Border
        {
            Height = Chrome.RowHeight,
            Padding = new Thickness(Chrome.RowHorizontalPadding, 0),
            Background = Brushes.Transparent,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { mark, title } },
        };
        face.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous);
        var hot = Chrome.Overlay(.06).Brush(face);
        var lead = job.LeadThread!.Value;
        var button = PlainControls.Button(face, () => actions.OpenChat?.Invoke(lead));
        button.KeyboardFocus(Chrome.RowCornerRadius);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.PointerEntered += (_, _) => face.Background = hot;
        button.PointerExited += (_, _) => face.Background = Brushes.Transparent;
        return (button, mark, title);
    }

    private void ShowMissionsOpen(bool open, bool still)
    {
        MissionsToggle.IsExpanded = open;
        missionsReveal.Open(open, still);
        TaskLook.Turn(Chevron, open, still);
    }

    /// <summary>The menu's items now (2.1): Open, Talk, Stop all, Pause all (Resume all once every task is paused), Pause duties for 1 hour, Settings.</summary>
    public IReadOnlyList<MenuItem> MenuItems()
    {
        var items = new List<MenuItem> { Item("Open", actions.Open) };
        if (actions.Talk is { } talk) items.Add(Item("Talk", talk));
        items.Add(Item("Stop all", () =>
        {
            var report = live.Controls.StopAll(StopSource.Row);
            actions.Said?.Invoke(report.Strip);
        }));
        var toggle = live.Controls.Toggle;
        if (toggle.Kind == GlobalToggleKind.ResumeAll)
            items.Add(Item("Resume all", toggle.CanResumeAll ? () => actions.Said?.Invoke(live.Controls.ResumeAll()) : null));
        else
            items.Add(Item("Pause all", toggle.CanPauseAll ? () => actions.Said?.Invoke(live.Controls.PauseAll("Sidebar")) : null));
        if (actions.PauseDuties is { } duties) items.Add(Item("Pause duties for 1 hour", duties));
        items.Add(Item("Settings", actions.Settings));
        return items;
    }

    private void FillMenu()
    {
        Menu.Items.Clear();
        foreach (var item in MenuItems()) Menu.Items.Add(item);
    }

    private static MenuItem Item(string header, Action? act)
    {
        var item = new MenuItem { Header = header, IsEnabled = act is not null };
        if (act is not null) item.Click += (_, _) => act();
        return item;
    }

    private void Paint() => face.Background = selected ? selectedFill : hovering ? hoverFill : Brushes.Transparent;

    private bool calm;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(true);
        Refresh();
        if (TopLevel.GetTopLevel(this) is { } top)
            budget = MotionScope.For(top).Join(ArmillaListRank.Other,
                () => IsEffectivelyVisible ? (Mark.Moves ? 1 : 0) + (MissionsOpen ? missionLines.Values.Count(l => l.Mark.Moves) : 0) : 0,
                () => IsEffectivelyVisible, c =>
                {
                    calm = c;
                    Mark.Calm = c;
                    foreach (var line in missionLines.Values) line.Mark.Calm = c;
                });
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Watch(false);
        budget?.Leave();
        budget = null;
    }

    private bool watching;

    private void Watch(bool on)
    {
        if (on == watching) return;
        watching = on;
        if (on)
        {
            live.Jobs.Changed += Queue;
            live.Decisions.Changed += Queue;
            live.Controls.Changed += Queue;
        }
        else
        {
            live.Jobs.Changed -= Queue;
            live.Decisions.Changed -= Queue;
            live.Controls.Changed -= Queue;
        }
    }

    // The queue's changes can come from any thread; the row redraws once on the UI thread.
    private void Queue()
    {
        if (Interlocked.Exchange(ref queued, 1) == 1) return;
        Dispatcher.UIThread.Post(() => { Volatile.Write(ref queued, 0); Refresh(); });
    }
}

/// <summary>
/// The row as the sidebar holds it (2.1): built on attach over the app's Dextra host, selected while the home is in the main area,
/// and bound again whenever the controller starts or stops.
/// </summary>
public sealed class AssistantSidebarSlot : Decorator
{
    private AppModel? model;
    private DextraHost? host;

    public AssistantSidebarSlot() => Margin = new Thickness(Chrome.ListInset, 10, Chrome.ListInset, 0);

    /// <summary>The row, once the slot is attached to a window with an app model.</summary>
    public AssistantSidebarRow? Row { get; private set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Row is null)
        {
            try { model = AppEnvironment.GetAppModel(this); }
            catch (InvalidOperationException) { return; }
            host = model.AssistantHost;
            var app = model;
            var dextra = host;
            Row = new AssistantSidebarRow(DextraHost.Name, new AssistantRowActions
            {
                Open = app.ShowAssistantHome,
                Settings = () => AssistantHomeHost.OpenSettings(Marrow.App.Views.Settings.SettingsPage.Assistant),
                OpenChat = thread => { if (app.Thread(thread) is not null) app.SelectedThreadID = thread; },
                Note = () => !dextra.Settings.Value.On ? "Off" : !dextra.Settings.Value.FirstRunDone ? "Set up in a minute" : dextra.StartFault is { } fault ? "Could not start: " + fault : null,
                Collapsed = () => dextra.Settings.Value.MissionsCollapsed,
                SetCollapsed = collapsed => dextra.Settings.Update(p => p with { MissionsCollapsed = collapsed }),
            });
            Child = Row;
            Row.Track(() => app.AssistantHomeShown, shown => Row.IsSelected = shown);
        }
        if (host is not null)
        {
            host.Changed -= OnHost;
            host.Changed += OnHost;
            host.EnsureStarted(); // Dextra runs with the window once it is on and its first run is done (2.6)
            Row?.Bind(host.Live);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (host is not null) host.Changed -= OnHost;
    }

    private void OnHost() => Dispatcher.UIThread.Post(() =>
    {
        if (host is not null && Row is not null)
        {
            Row.Bind(host.Live);
            Row.Refresh();
        }
    });
}
