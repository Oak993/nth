// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 9.4 "Org chart (Team tab)", 2.5 "Motion budget", 16 AS-17; the canvas's renderOrg and flip, work/design/assistant/index.html lines 1823-1840 and 2858-2892;
// ED-NEW-dextra-as17-1, -4). No Swift twin.
// The org chart: Dextra's node at the top (Armilla at 64 px with a node for each Astur), the Asturs beneath it (each one's node shape at 30 px, in its tint), joined by thin connectors, and under
// 640 px an indented list. One node is selected (Dextra's first); a press, Enter or Space selects another, the arrow keys move between nodes, and the detail panel under the chart shows the
// selected one. When an Astur is made it comes in (a rise and a fade); when one is retired it fades and the others move into the place it leaves by a transform alone (FLIP, no fade between moves),
// and the connectors come back once the nodes have landed. With Reduce motion the chart is simply the new chart. Dextra's mark joins the window's motion budget as part of the Board; the
// Asturs' marks never move and cost nothing.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

public sealed class OrgChart : Decorator
{
    /// <summary>How long a retired Astur's node takes to fade before the others move into its place.</summary>
    public static readonly TimeSpan Leave = TimeSpan.FromMilliseconds(240);

    /// <summary>How long the nodes take to move, and so how long the connectors wait before they come back.</summary>
    public static readonly TimeSpan Move = TimeSpan.FromMilliseconds(420);

    private readonly OrgChartPanel panel = new();
    private readonly Dictionary<string, OrgNode> nodes = [];
    private readonly Action<Guid>? openChat;
    private OrgNode? root;
    private TeamSnapshot snapshot = TeamSnapshot.Empty;
    private string selected = "";
    private MotionScope.Entry? budget;
    private bool calm;
    private ITimer? leaving;
    private TeamSnapshot? pending;

    public OrgChart(Action<Guid>? openChat = null)
    {
        this.openChat = openChat;
        Child = panel;
        AutomationProperties.SetName(this, "Team");
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.List);
    }

    public OrgChartPanel Panel => panel;

    public TeamSnapshot Snapshot => snapshot;

    /// <summary>Dextra's node (null before the first snapshot).</summary>
    public OrgNode? Root => root;

    /// <summary>The Asturs' nodes in the order they are drawn.</summary>
    public IReadOnlyList<OrgNode> Kids => panel.Kids;

    /// <summary>Every node, Dextra's first: the order the arrow keys walk.</summary>
    public IReadOnlyList<OrgNode> All => [.. root is null ? [] : new[] { root }, .. panel.Kids];

    /// <summary>The selected assistant's id (Dextra's until another is chosen).</summary>
    public string SelectedId => selected;

    /// <summary>The selection changed (raised on the UI thread, also when the selected node went away and Dextra took it back).</summary>
    public event Action<TeamMember>? SelectionChanged;

    public TeamMember? SelectedMember => snapshot.Find(selected);

    public OrgNode? NodeOf(string id) => nodes.GetValueOrDefault(id);

    /// <summary>Selects an assistant's node (the detail follows). An id nobody has is ignored.</summary>
    public void Select(string id)
    {
        if (!nodes.ContainsKey(id)) return;
        var changed = selected != id;
        selected = id;
        foreach (var (key, node) in nodes) node.IsSelected = key == id;
        if (changed && snapshot.Find(id) is { } member) SelectionChanged?.Invoke(member);
    }

    /// <summary>Shows the team. Nodes that stay are updated in place; one that comes or goes moves the rest by a transform.</summary>
    public void Show(TeamSnapshot next)
    {
        var still = TaskLook.Still(this);
        if (leaving is not null && pending is { } waiting && Ids(waiting).SequenceEqual(Ids(next)))
        {
            // the same people are on their way out: what stays shows its new facts, and the move waits for the fade to end, not for a tick
            pending = next;
            snapshot = next;
            if (next.Dextra is { } head && root is not null)
            {
                root.Show(head, next, still);
                foreach (var w in next.Asturs.Where(w => nodes.ContainsKey(w.Id))) nodes[w.Id].Show(w, next, still);
            }
            return;
        }
        FinishLeaving();
        var previous = snapshot;
        snapshot = next;
        if (next.Dextra is not { } dextra) return;
        if (root is null)
        {
            root = Make(dextra);
            panel.SetRoot(root);
            nodes[dextra.Id] = root;
            selected = dextra.Id;
            root.IsSelected = true;
        }
        var wanted = next.Asturs.ToList();
        var gone = nodes.Where(p => p.Key != root.Id && wanted.All(w => w.Id != p.Key)).Select(p => p.Value).ToList();
        var fresh = wanted.Where(w => !nodes.ContainsKey(w.Id)).ToList();
        var order = panel.Kids.Select(k => k.Id).ToList();
        var reorder = !order.Where(id => wanted.Any(w => w.Id == id)).SequenceEqual(wanted.Where(w => order.Contains(w.Id)).Select(w => w.Id));
        var moves = gone.Count > 0 || fresh.Count > 0 || reorder;

        if (!moves)
        {
            Apply(next, still, animate: false, changed: false);
            return;
        }
        if (still || VisualRoot is null || previous.Dextra is null)
        {
            Apply(next, still, animate: false, changed: true);
            return;
        }
        if (gone.Count > 0)
        {
            // a retired Astur's node fades first; then the rest move into the place it leaves
            foreach (var node in gone)
            {
                node.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = Leave, Easing = new Avalonia.Animation.Easings.SplineEasing(.32, .72, 0, 1) }];
                node.Opacity = 0;
                node.IsHitTestVisible = false;
            }
            pending = next;
            leaving = SwiftClock.Provider.Current.CreateTimer(_ => Dispatcher.UIThread.Post(FinishLeaving), null, Leave, Timeout.InfiniteTimeSpan);
            // what stays shows its new facts at once; only the set waits
            foreach (var w in wanted.Where(w => nodes.ContainsKey(w.Id))) nodes[w.Id].Show(w, next, still);
            root.Show(dextra, next, still);
            return;
        }
        Apply(next, still, animate: true, changed: true);
    }

    private static List<string> Ids(TeamSnapshot snap) => [.. snap.Asturs.Select(a => a.Id)];

    private void FinishLeaving()
    {
        leaving?.Dispose();
        leaving = null;
        var run = pending;
        pending = null;
        if (run is null) return;
        Apply(run, TaskLook.Still(this), animate: true, changed: true);
    }

    // the set of nodes becomes the snapshot's: new ones are made, old ones go, the others are shown again, and (animated) the ones that stay glide to their new places
    private void Apply(TeamSnapshot next, bool still, bool animate, bool changed)
    {
        var dextra = next.Dextra!;
        var before = animate ? Positions() : null;
        var wanted = next.Asturs.ToList();
        foreach (var gone in nodes.Where(p => p.Key != dextra.Id && wanted.All(w => w.Id != p.Key)).ToList())
        {
            nodes.Remove(gone.Key);
            gone.Value.Opacity = 1;
            gone.Value.Transitions = null;
            gone.Value.IsHitTestVisible = true;
        }
        var fresh = new List<OrgNode>();
        var order = new List<OrgNode>();
        foreach (var w in wanted)
        {
            if (!nodes.TryGetValue(w.Id, out var node))
            {
                node = Make(w);
                nodes[w.Id] = node;
                fresh.Add(node);
            }
            order.Add(node);
        }
        panel.SetKids(order);
        root!.Show(dextra, next, still);
        foreach (var (w, node) in wanted.Zip(order)) node.Show(w, next, still);
        if (!nodes.ContainsKey(selected))
        {
            selected = dextra.Id;
            foreach (var (key, node) in nodes) node.IsSelected = key == selected;
            SelectionChanged?.Invoke(dextra);
        }
        else foreach (var (key, node) in nodes) node.IsSelected = key == selected;
        foreach (var node in nodes.Values) node.Calm = calm;
        budget?.Changed();
        if (!changed) return;
        if (!animate || before is null)
        {
            panel.Connectors.Settle(still: true, TimeSpan.Zero);
            return;
        }
        UpdateLayout();
        Glide(before);
        foreach (var node in fresh) Enter(node);
        panel.Connectors.Settle(still: false, Move);
    }

    private OrgNode Make(TeamMember member)
    {
        var node = new OrgNode(member.IsDextra, Choose) { OpenChat = openChat };
        node.Calm = calm;
        if (node.DextraMark is { } mark) mark.MovesChanged += _ => budget?.Changed();
        return node;
    }

    private void Choose(OrgNode node)
    {
        Select(node.Id);
    }

    // ---- the move ----

    // where each Astur's node is arranged now (the panel's own coordinates, before any transform)
    private Dictionary<OrgNode, Point>? Positions() => VisualRoot is null ? null : panel.Kids.Where(n => n.Bounds.Width > 0).ToDictionary(n => n, n => n.Bounds.Position);

    // the Asturs that stay glide from where they were to where they are, by a transform alone
    private static void Glide(Dictionary<OrgNode, Point> before)
    {
        foreach (var (node, old) in before)
        {
            if (node.Parent is null) continue;
            var at = node.Bounds.Position;
            var dx = old.X - at.X;
            var dy = old.Y - at.Y;
            if (Math.Abs(dx) < .5 && Math.Abs(dy) < .5) continue;
            var shift = node.RenderTransform as TranslateTransform ?? new TranslateTransform();
            shift.Transitions = null;
            shift.X = dx;
            shift.Y = dy;
            node.RenderTransform = shift;
            var ease = new Avalonia.Animation.Easings.SplineEasing(.32, .72, 0, 1);
            shift.Transitions =
            [
                new DoubleTransition { Property = TranslateTransform.XProperty, Duration = Move, Easing = ease },
                new DoubleTransition { Property = TranslateTransform.YProperty, Duration = Move, Easing = ease },
            ];
            shift.X = 0;
            shift.Y = 0;
        }
    }

    // a new Astur's node rises into its place (8 px) as it comes in
    private static void Enter(OrgNode node)
    {
        var shift = node.RenderTransform as TranslateTransform ?? new TranslateTransform();
        shift.Transitions = null;
        shift.Y = 8;
        node.RenderTransform = shift;
        node.Transitions = null;
        node.Opacity = 0;
        var ease = new Avalonia.Animation.Easings.SplineEasing(.32, .72, 0, 1);
        shift.Transitions = [new DoubleTransition { Property = TranslateTransform.YProperty, Duration = Move, Easing = ease }];
        node.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = Move, Easing = ease }];
        shift.Y = 0;
        node.Opacity = 1;
    }

    // ---- keys ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        var all = All;
        var at = all.ToList().FindIndex(n => n.Face.IsFocused);
        if (at < 0) return;
        var to = e.Key switch
        {
            Key.Left or Key.Up => Math.Max(0, at - 1),
            Key.Right or Key.Down => Math.Min(all.Count - 1, at + 1),
            Key.Home => 0,
            Key.End => all.Count - 1,
            _ => -1,
        };
        if (to < 0) return;
        e.Handled = true;
        if (to != at) all[to].Focus();
    }

    // ---- the window's motion budget ----

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top)
            budget = MotionScope.For(top).Join(ArmillaListRank.Board, () => IsEffectivelyVisible && root?.DextraMark is { Moves: true } ? 1 : 0, () => IsEffectivelyVisible, c =>
            {
                calm = c;
                foreach (var node in nodes.Values) node.Calm = c;
            });
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        leaving?.Dispose();
        leaving = null;
        // a chart taken off its window while a node fades is made the new team at once, so it is never left half way
        if (pending is { } waiting)
        {
            pending = null;
            Apply(waiting, still: true, animate: false, changed: true);
        }
        budget?.Leave();
        budget = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) budget?.Changed();
    }

    /// <summary>Whether Dextra's mark moves now (the budget counts it).</summary>
    public bool Moves => root?.DextraMark is { Moves: true };

    /// <summary>The window's budget stilled this chart: Dextra's mark shows its still pose.</summary>
    public bool Calm => calm;
}
