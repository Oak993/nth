// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 2.1 "Sidebar": "The Asturs are nested under it, one row each. A row shows the Astur's mark (its node shape and tint, 2.5), the name, the current work line and a badge
// when that Astur needs the owner. The disclosure is remembered."; the canvas's .srow.sub, work/design/assistant/index.html lines 164-172 and 2254; ED-NEW-dextra-as17-7). No Swift twin.
// The Asturs under Dextra's row in the sidebar. A disclosure line (a chevron, "Asturs" and how many) folds the group, and the choice is remembered; under it one row for each Astur: its own mark at 20 px (the
// node shape alone, in its tint, a corner badge for its state), its name, the line of what it does now, and a count of the cards that wait on the owner. The rows hang on one tree line (a trunk with a rounded
// elbow into the last row, a tick into each, like the canvas's) and keep their place and height in every state, so a state change moves nothing. A press opens the Astur; a right-click gives Open, Pause or
// Resume its tasks, and Show in Team. A new Astur's row grows in and a retired one folds away (380 ms and 300 ms, the sidebar group's own motion), none with Reduce motion. The marks never move, so the group
// costs the motion budget nothing.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

/// <summary>What the sidebar's Astur rows reach outside the tree; a missing one disables its item.</summary>
public sealed record AsturTreeActions
{
    /// <summary>A row's press: the Astur's own thread (or, with none yet, the home).</summary>
    public Action<string>? Open { get; init; }

    /// <summary>Shows the Astur in the home's Team tab (its detail has Talk, Pause and Settings).</summary>
    public Action<string>? ShowInTeam { get; init; }

    /// <summary>The group is folded, and its setter (remembered in Dextra's settings).</summary>
    public Func<bool>? Collapsed { get; init; }

    public Action<bool>? SetCollapsed { get; init; }

    /// <summary>What a control's answer says (the window's live region, if any).</summary>
    public Action<string>? Said { get; init; }

    /// <summary>Opens a task's lead chat (a line of an Astur's Missions group); the Assistant row's own is used when this is not given.</summary>
    public Action<Guid>? OpenChat { get; init; }
}

/// <summary>The tree line of a row: the trunk comes down from above, bends in an elbow into the last row, and goes on past the others with a tick into each.</summary>
internal sealed class TreeElbow : Control
{
    public const double Gutter = 30;
    public const double TrunkX = 18;
    private const double Bend = 6;

    private readonly IPen pen;
    private bool last;

    /// <summary>Where the tick leaves the trunk into the row's face: the row may have its Missions group under it, and the trunk goes on past that.</summary>
    public const double TickY = AsturRow.FaceHeight / 2 + 1;

    public TreeElbow()
    {
        IsHitTestVisible = false;
        pen = new Pen(TaskLook.Line2.Brush(this), 1);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public bool Last
    {
        get => last;
        set
        {
            if (last == value) return;
            last = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(Gutter, 0);

    public override void Render(DrawingContext context)
    {
        var mid = TickY;
        var tick = Gutter - 3;
        if (last)
        {
            // the trunk comes down, bends into the row and stops
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(TrunkX, 0), false);
                ctx.LineTo(new Point(TrunkX, mid - Bend));
                ctx.ArcTo(new Point(TrunkX + Bend, mid), new Size(Bend, Bend), 0, false, SweepDirection.CounterClockwise);
                ctx.LineTo(new Point(tick, mid));
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, pen, g);
            return;
        }
        // the trunk goes on past the row; a tick leaves it into the row
        context.DrawLine(pen, new Point(TrunkX, 0), new Point(TrunkX, Bounds.Height));
        context.DrawLine(pen, new Point(TrunkX, mid), new Point(tick, mid));
    }
}

/// <summary>One Astur's row.</summary>
public sealed class AsturRow : Decorator
{
    public const double FaceHeight = 40;
    public const double MarkSize = 20;

    private readonly AsturTreeActions actions;
    private readonly ITeamSource source;
    private readonly Border face;
    private readonly IBrush hoverFill, selectedFill;
    private readonly TextBlock name = new() { MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock line = new() { MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock badgeText = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly MissionChatsGroup chats;
    private readonly RevealBox chatsReveal = new();
    private bool hovering;
    private bool selected;

    internal AsturRow(ITeamSource source, AsturTreeActions actions)
    {
        this.source = source;
        this.actions = actions;
        Mark = new AsturMark { MarkSize = MarkSize, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(Mark, AccessibilityView.Raw);
        SwiftFont.System(13, SwiftFont.Weight.Medium).Apply(name);
        name.Foreground = Chrome.PrimaryText.Brush(name);
        SwiftFont.System(11.5).Apply(line);
        line.Foreground = TaskLook.Ink2.Brush(line);
        SwiftFont.System(11, SwiftFont.Weight.Semibold).MonospacedDigit().Apply(badgeText);
        badgeText.Foreground = TaskLook.Inverse.Brush(badgeText);
        Badge = new Border { MinWidth = 18, Height = 18, Padding = new Thickness(5, 0), Child = badgeText, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        Badge.Capsule(RoundedCornerStyle.Continuous);
        Badge.Background = Chrome.PrimaryText.Brush(Badge);
        AutomationProperties.SetAccessibilityView(Badge, AccessibilityView.Raw);

        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { name, line } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(Badge, 2);
        grid.Children.Add(Mark);
        grid.Children.Add(texts);
        grid.Children.Add(Badge);

        face = new Border { Height = FaceHeight, Margin = new Thickness(0, 1), Padding = new Thickness(6, 0, Chrome.RowHorizontalPadding, 0), Background = Brushes.Transparent, Child = grid };
        face.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous);
        hoverFill = Chrome.Overlay(.06).Brush(face);
        selectedFill = Chrome.Overlay(.12).Brush(face);

        Button = PlainControls.Button(face, () => this.actions.Open?.Invoke(Id));
        Button.KeyboardFocus(Chrome.RowCornerRadius);
        Button.HorizontalAlignment = HorizontalAlignment.Stretch;
        Button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Button.PointerEntered += (_, _) => { hovering = true; Paint(); };
        Button.PointerExited += (_, _) => { hovering = false; Paint(); };
        Menu = new ContextMenu();
        Menu.Opening += (_, _) => FillMenu();
        Button.ContextMenu = Menu;

        // the Astur's own Missions group hangs under its face (2.1: "under the assistant that owns them"), collapsed until opened; the trunk goes on beside it
        chats = new MissionChatsGroup(this.actions.OpenChat, indent: 12, lineIndent: 6);
        chatsReveal.Child = chats.Root;
        Elbow = new TreeElbow();
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        Grid.SetColumn(Button, 1);
        Grid.SetRowSpan(Elbow, 2);
        Grid.SetColumn(chatsReveal, 1);
        Grid.SetRow(chatsReveal, 1);
        row.Children.Add(Elbow);
        row.Children.Add(Button);
        row.Children.Add(chatsReveal);
        Reveal = new RevealBox { Child = row };
        Child = Reveal;
    }

    public string Id { get; private set; } = "";

    public TeamMember? Member { get; private set; }

    public AsturMark Mark { get; }

    public Button Button { get; }

    /// <summary>The Astur's own Missions group, under its face while it has a lead chat to list.</summary>
    public MissionChatsGroup Chats => chats;

    /// <summary>The group's reveal: open while there is a chat to list (tests read it).</summary>
    internal RevealBox ChatsReveal => chatsReveal;

    public Border Badge { get; }

    public string BadgeCount => badgeText.Text ?? "";

    public string NameText => name.Text ?? "";

    public string LineText => line.Text ?? "";

    public ContextMenu Menu { get; }

    internal TreeElbow Elbow { get; }

    internal RevealBox Reveal { get; }

    public bool IsLast
    {
        get => Elbow.Last;
        internal set => Elbow.Last = value;
    }

    public bool IsSelected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            Paint();
            name.FontWeight = value ? FontWeight.SemiBold : FontWeight.Medium;
        }
    }

    public void Show(TeamMember next, bool still = true)
    {
        if (Member == next) return;
        Member = next;
        Id = next.Id;
        name.Text = next.Name;
        chats.OwnerName = next.Name;
        chats.Show(next.Chats);
        chatsReveal.Open(next.Chats.Count > 0, still);
        line.Text = next.Work;
        Mark.Shape = AsturMarkStates.ShapeOf(next.Shape);
        Mark.State = next.MarkState;
        Mark.Activity = $"{next.Name}: {next.Activity ?? next.Work}";
        Badge.IsVisible = next.Waiting > 0;
        badgeText.Text = next.Waiting.ToString(System.Globalization.CultureInfo.CurrentCulture);
        AutomationProperties.SetName(Button, next.Reading);
        AutomationProperties.SetHelpText(Button, next.Waiting == 0 ? "No decisions open" : ControlWords.Plural(next.Waiting, "decision") + " open");
    }

    /// <summary>The motion budget stilled the sidebar's list: the marks show their still poses.</summary>
    public bool Calm
    {
        set
        {
            Mark.Calm = value;
            chats.Calm = value;
        }
    }

    /// <summary>How many of this row's marks move now (its own never does; its Missions group's task marks may).</summary>
    public int MovingMarks => chats.MovingMarks;

    /// <summary>The controls the Up and Down keys walk in this row: its face, then its Missions group.</summary>
    internal IEnumerable<Control> Stops() => [Button, .. chats.Stops()];

    /// <summary>The menu's items now: Open, Pause or Resume its tasks, Show in Team.</summary>
    public IReadOnlyList<MenuItem> MenuItems()
    {
        var m = Member;
        var items = new List<MenuItem> { Item("Open", actions.Open is null || m is null ? null : () => actions.Open(m.Id)) };
        if (m is not null)
        {
            var resume = !m.CanPause && m.CanResume;
            items.Add(Item(resume ? "Resume its tasks" : "Pause its tasks", m.CanPause || m.CanResume
                ? () => actions.Said?.Invoke(resume ? source.ResumeTasks(m.Id) : source.PauseTasks(m.Id))
                : null));
        }
        items.Add(Item("Show in Team", actions.ShowInTeam is null || m is null ? null : () => actions.ShowInTeam(m.Id)));
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
}

public sealed class AsturTree : Decorator
{
    private readonly AsturTreeActions actions;
    private readonly StackPanel rows = new() { Orientation = Orientation.Vertical };
    private readonly RevealBox group = new();
    private readonly List<AsturRow> order = [];
    private readonly Dictionary<string, AsturRow> byId = [];
    private readonly Avalonia.Controls.Shapes.Path chevron = TaskLook.Chevron();
    private readonly TextBlock label = new() { Text = "Asturs", VerticalAlignment = VerticalAlignment.Center };
    private ITeamSource? source;
    private bool owned;
    private string? selectedId;
    private bool calm;
    private bool attached;
    private int count;
    private int queued;

    public AsturTree(AsturTreeActions? actions = null)
    {
        this.actions = actions ?? new AsturTreeActions();
        SwiftFont.System(12, SwiftFont.Weight.Medium).MonospacedDigit().Apply(label);
        label.Foreground = TaskLook.Ink2.Brush(label);
        var face = new Border
        {
            Height = 24,
            Padding = new Thickness(Chrome.RowHorizontalPadding + 4, 0, Chrome.RowHorizontalPadding, 0),
            Background = Brushes.Transparent,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { chevron, label } },
        };
        Toggle = new DisclosureButton { Content = face, Padding = default, Background = Brushes.Transparent, BorderThickness = default, HorizontalAlignment = HorizontalAlignment.Stretch };
        Toggle.KeyboardFocus(Chrome.RowCornerRadius);
        Toggle.Click += (_, _) =>
        {
            var open = !Toggle.IsExpanded;
            this.actions.SetCollapsed?.Invoke(!open);
            ShowOpen(open, TaskLook.Still(this));
        };
        group.Child = rows;
        Child = new StackPanel { Orientation = Orientation.Vertical, Children = { Toggle, group } };
        ShowOpen(!(this.actions.Collapsed?.Invoke() ?? false), still: true);
        AutomationProperties.SetControlTypeOverride(rows, AutomationControlType.List);
        AutomationProperties.SetName(rows, "Asturs");
        IsVisible = false;
    }

    /// <summary>A tree already reading <paramref name="source"/> (tests; the sidebar binds the app's source when the controller starts).</summary>
    public AsturTree(ITeamSource source, AsturTreeActions? actions = null) : this(actions) => Bind(source, owned: false);

    public DisclosureButton Toggle { get; }

    public string LabelText => label.Text ?? "";

    public bool IsOpen => Toggle.IsExpanded;

    /// <summary>The Astur rows, in the order they are listed.</summary>
    public IReadOnlyList<AsturRow> Rows => [.. order];

    public AsturRow? RowOf(string id) => byId.GetValueOrDefault(id);

    /// <summary>Reads another source (the controller started or stopped); a source it owned is let go. Null: no Asturs.</summary>
    public void Bind(ITeamSource? next, bool owned = false)
    {
        if (ReferenceEquals(next, source)) return;
        if (source is not null && attached) source.Changed -= OnChanged;
        if (this.owned && source is IDisposable done) done.Dispose();
        source = next;
        this.owned = owned;
        if (source is not null && attached) source.Changed += OnChanged;
        Refresh();
    }

    /// <summary>The Astur whose own thread is in the main area (its row takes the selected look), or none.</summary>
    public string? SelectedId
    {
        get => selectedId;
        set
        {
            selectedId = value;
            foreach (var (id, row) in byId) row.IsSelected = id == value;
        }
    }

    /// <summary>The motion budget stilled the sidebar's list: the marks show their still poses (they never move, but a working one is told by its badge).</summary>
    public bool Calm
    {
        get => calm;
        set
        {
            calm = value;
            foreach (var row in order) row.Calm = value;
        }
    }

    /// <summary>How many marks of the tree move now (the Asturs' own never do; the task marks of their Missions groups may); zero while it is folded or hidden.</summary>
    public int MovingMarks => IsVisible && Toggle.IsExpanded ? order.Sum(r => r.MovingMarks) : 0;

    /// <summary>Reads the team and shows the Asturs; the group is hidden while there are none.</summary>
    public void Refresh()
    {
        var snap = source?.Now() ?? TeamSnapshot.Empty;
        var asturs = snap.Asturs.ToList();
        var still = TaskLook.Still(this) || !attached;
        IsVisible = asturs.Count > 0;
        count = asturs.Count;
        label.Text = count == 0 ? "Asturs" : "Asturs  " + CountText();
        NameToggle();
        var keep = asturs.Select(a => a.Id).ToHashSet();
        foreach (var gone in order.Where(r => !keep.Contains(r.Id)).ToList())
        {
            order.Remove(gone);
            byId.Remove(gone.Id);
            Fold(gone, still);
        }
        var index = 0;
        foreach (var astur in asturs)
        {
            if (!byId.TryGetValue(astur.Id, out var row))
            {
                row = new AsturRow(source!, actions);
                row.Calm = calm;
                row.Reveal.Open(false, still: true);
                byId[astur.Id] = row;
                row.Show(astur);
                rows.Children.Insert(Math.Min(index, rows.Children.Count), row);
                order.Insert(Math.Min(index, order.Count), row);
                row.Reveal.Open(true, still);
            }
            else
            {
                row.Show(astur, still);
                var at = order.IndexOf(row);
                if (at != index)
                {
                    order.RemoveAt(at);
                    order.Insert(Math.Min(index, order.Count), row);
                    rows.Children.Remove(row);
                    rows.Children.Insert(Math.Min(index, rows.Children.Count), row);
                }
            }
            row.IsSelected = astur.Id == selectedId;
            index++;
        }
        for (var i = 0; i < order.Count; i++) order[i].IsLast = i == order.Count - 1;
    }

    // a retired Astur's row folds away and is taken out when it has
    private void Fold(AsturRow row, bool still)
    {
        row.Reveal.Open(false, still);
        if (still)
        {
            rows.Children.Remove(row);
            return;
        }
        SwiftClock.Provider.Current.CreateTimer(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => rows.Children.Remove(row)), null, RevealBox.Out + TimeSpan.FromMilliseconds(20), Timeout.InfiniteTimeSpan);
    }

    private string CountText() => count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    // the toggle's name says what pressing it does, so it follows both the fold and the count
    private void NameToggle() => AutomationProperties.SetName(Toggle, (Toggle.IsExpanded ? "Hide" : "Show") + " Asturs, " + CountText());

    private void ShowOpen(bool open, bool still)
    {
        Toggle.IsExpanded = open;
        NameToggle();
        group.Open(open, still);
        TaskLook.Turn(chevron, open, still);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        if (source is not null) source.Changed += OnChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        attached = false;
        if (source is not null) source.Changed -= OnChanged;
    }

    // the source's changes can come from any thread of the controller; the tree redraws once, on the UI thread
    private void OnChanged()
    {
        if (Interlocked.Exchange(ref queued, 1) == 1) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref queued, 0);
            Refresh();
        });
    }

    // Up and Down walk the group: the disclosure line, then each row
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key is not (Key.Up or Key.Down)) return;
        var stops = new List<Control> { Toggle };
        if (Toggle.IsExpanded) stops.AddRange(order.SelectMany(r => r.Stops()));
        var at = stops.FindIndex(c => c.IsFocused);
        if (at < 0) return;
        var to = Math.Clamp(at + (e.Key == Key.Down ? 1 : -1), 0, stops.Count - 1);
        e.Handled = true;
        if (to != at) stops[to].Focus(NavigationMethod.Directional);
    }
}
