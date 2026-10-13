// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.2 "Right: the Board pane, with four tabs", 9.4 "Org chart (Team tab)"; the canvas's
// .btabs and .bviews, work/design/assistant/index.html lines 770-800; ED-NEW-dextra-as8-6). No Swift twin.
// The Board pane: the tab strip and one view per tab (Missions, Decisions, Feed, Team), only the picked one shown, so a hidden tab's
// marks and shimmers cost the motion budget nothing. The Decisions tab carries the open count. The Team tab shows Dextra with its
// Asturs as an indented list (the narrow form of 9.4's chart) until the Asturs unit hands its org chart in through the seam.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

/// <summary>The Team tab: Dextra and its Asturs, or what to do when there are none.</summary>
public sealed class TeamTab : Decorator
{
    private readonly Func<IReadOnlyList<AssistantProfile>> profiles;
    private readonly bool charted;
    private readonly StackPanel list = new() { Orientation = Orientation.Vertical, Spacing = 6 };
    private readonly TextBlock empty = TaskLook.Text("No Asturs yet. Ask Dextra to make one for an area of your life, such as a shop or a channel.", 12, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);

    public TeamTab(Func<IReadOnlyList<AssistantProfile>>? profiles, Control? chart = null)
    {
        this.profiles = profiles ?? (() => []);
        charted = chart is not null;
        Child = chart ?? new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, Children = { list, empty } };
        AutomationProperties.SetName(this, "Team");
    }

    public IReadOnlyList<Control> Rows => [.. list.Children];

    public TextBlock Empty => empty;

    private MotionScope.Entry? budget;
    private bool calm;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Refresh();
        // the tab is a list of the Board's view: it costs the budget only while it is the tab shown
        if (TopLevel.GetTopLevel(this) is { } top)
            budget = MotionScope.For(top).Join(ArmillaListRank.Board, () => IsEffectivelyVisible ? Marks.Count(m => m.Moves) : 0, () => IsEffectivelyVisible, c =>
            {
                calm = c;
                foreach (var m in Marks) m.Calm = c;
            });
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        budget?.Leave();
        budget = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) budget?.Changed();
    }

    private IEnumerable<ArmillaMark> Marks => list.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<ArmillaMark>());

    public void Refresh()
    {
        if (charted) return; // the org chart (AS-17) shows the team; the interim list is not built behind it
        list.Children.Clear();
        var all = profiles();
        var asturs = all.Where(p => p.ParentId is not null).ToList();
        list.Children.Add(Node(AssistantNames.Assistant, asturs.Count == 0 ? "Your assistant" : $"Your assistant · {ControlWords.Plural(asturs.Count, "Astur")}", 0,
            new ArmillaMark { MarkSize = 40, Kind = ArmillaKind.Live, Nodes = [.. asturs.Select(a => new ArmillaNode(Shape(a.Shape)))] }));
        foreach (var astur in asturs)
            list.Children.Add(Node(astur.Name, astur.Area, 1, new ArmillaMark { MarkSize = 24, Kind = ArmillaKind.Still, Nodes = [new ArmillaNode(Shape(astur.Shape))] }));
        empty.IsVisible = asturs.Count == 0;
        foreach (var m in Marks) m.Calm = calm;
        budget?.Changed();
    }

    private static ArmillaShape Shape(AsturShape? shape) => shape switch
    {
        AsturShape.Hexagon => ArmillaShape.Hexagon,
        AsturShape.Diamond => ArmillaShape.Diamond,
        AsturShape.RoundedTriangle => ArmillaShape.Triangle,
        AsturShape.Clover => ArmillaShape.Clover,
        AsturShape.Star => ArmillaShape.Star,
        _ => ArmillaShape.Squircle,
    };

    private static Control Node(string name, string line, int depth, ArmillaMark mark)
    {
        AutomationProperties.SetAccessibilityView(mark, AccessibilityView.Raw);
        mark.VerticalAlignment = VerticalAlignment.Center;
        var title = TaskLook.Text(name, 13, SwiftFont.Weight.Semibold, TaskLook.Ink);
        var sub = TaskLook.Text(line, 11, SwiftFont.Weight.Regular, TaskLook.Ink2);
        var texts = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Children = { title, sub } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Margin = new Thickness(depth * 28, 0, 0, 0) };
        Grid.SetColumn(texts, 1);
        row.Children.Add(mark);
        row.Children.Add(texts);
        AutomationProperties.SetName(row, line.Length > 0 ? name + ", " + line : name);
        return row;
    }
}

public sealed class BoardPane : Decorator
{
    private readonly Dictionary<BoardTab, Control> views = [];

    public BoardPane(MissionBoard missions, DecisionList decisions, FeedTab feed, TeamTab team)
    {
        Missions = missions ?? throw new ArgumentNullException(nameof(missions));
        Decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        Feed = feed ?? throw new ArgumentNullException(nameof(feed));
        Team = team ?? throw new ArgumentNullException(nameof(team));
        views[BoardTab.Missions] = missions;
        views[BoardTab.Decisions] = decisions;
        views[BoardTab.Feed] = feed;
        views[BoardTab.Team] = team;
        var host = new Panel();
        foreach (var view in views.Values) host.Children.Add(view);
        Tabs.Changed += Show;
        decisions.CountChanged += Tabs.SetCount;
        Child = new DockPanel { LastChildFill = true, Children = { Tabs, host } };
        DockPanel.SetDock(Tabs, Dock.Top);
        Tabs.Margin = new Thickness(0, 0, 0, 12);
        Show(Tabs.Selected);
    }

    public BoardTabs Tabs { get; } = new();

    public MissionBoard Missions { get; }

    public DecisionList Decisions { get; }

    public FeedTab Feed { get; }

    public TeamTab Team { get; }

    public void Pick(BoardTab tab) => Tabs.Select(tab, true);

    private void Show(BoardTab tab)
    {
        foreach (var (t, view) in views) view.IsVisible = t == tab;
    }
}
