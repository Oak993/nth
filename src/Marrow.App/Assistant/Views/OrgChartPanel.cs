// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 9.4 "Layout" and "Narrow widths (under 640 px) turn the chart into an indented list"; the canvas's .org, .orow and .onode, work/design/assistant/
// index.html lines 421-446; ED-NEW-dextra-as17-1). No Swift twin.
// The panel that places the org chart: Dextra's node, the Asturs' nodes and the lines between them. It asks the layout (OrgChartLayout) for the widths the nodes are measured
// at, measures them there, and arranges them where the layout's plan puts them; the nodes of a row are as tall as the tallest. The connectors sit behind the nodes and draw the
// plan. Nothing here animates: the chart moves nodes after a change of team by a transform of its own (FLIP), and a width change is a direct resize.
using Avalonia;
using Avalonia.Controls;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant.Views;

public sealed class OrgChartPanel : Panel
{
    /// <summary>The width the chart takes when it is given none (a host that measures it unbounded).</summary>
    public const double UnboundedWidth = 900;

    private readonly List<OrgNode> kids = [];
    private OrgNode? root;

    public OrgChartPanel()
    {
        Connectors = new OrgConnectors();
        Children.Add(Connectors);
    }

    public OrgConnectors Connectors { get; }

    /// <summary>Dextra's node.</summary>
    public OrgNode? Root => root;

    /// <summary>The Asturs' nodes, in the order they are drawn (the order they were made).</summary>
    public IReadOnlyList<OrgNode> Kids => kids;

    /// <summary>The plan the last arrangement used; null before the first.</summary>
    public OrgPlan? Plan { get; private set; }

    /// <summary>Whether the chart is the list (narrow) rather than the chart.</summary>
    public bool IsList => Plan?.Mode == OrgLayoutMode.List;

    public void SetRoot(OrgNode node)
    {
        if (ReferenceEquals(root, node)) return;
        if (root is not null) Children.Remove(root);
        root = node;
        Children.Add(node);
        InvalidateMeasure();
    }

    /// <summary>Puts the Asturs' nodes in this order (adding those new to the panel, taking those no longer listed out).</summary>
    public void SetKids(IReadOnlyList<OrgNode> nodes)
    {
        foreach (var gone in kids.Where(k => !nodes.Contains(k)).ToList())
        {
            Children.Remove(gone);
            kids.Remove(gone);
        }
        foreach (var node in nodes.Where(n => !kids.Contains(n))) Children.Add(node);
        kids.Clear();
        kids.AddRange(nodes);
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? UnboundedWidth : availableSize.Width;
        var list = OrgChartLayout.ModeFor(width) == OrgLayoutMode.List;
        var (rootWidth, kidWidth) = OrgChartLayout.Widths(width);
        var rootHeight = 0.0;
        if (root is not null)
        {
            root.Compact = list;
            root.Measure(new Size(rootWidth, double.PositiveInfinity));
            rootHeight = root.DesiredSize.Height;
        }
        var heights = new List<double>(kids.Count);
        foreach (var kid in kids)
        {
            kid.Compact = list;
            kid.Measure(new Size(kidWidth, double.PositiveInfinity));
            heights.Add(kid.DesiredSize.Height);
        }
        var plan = OrgChartLayout.Plan(width, rootHeight, heights);
        Connectors.Measure(new Size(plan.Width, plan.Height));
        return new Size(plan.Width, plan.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // the plan is laid out left to right; a right-to-left window mirrors the whole tree itself (nodes, lines and all), so the first Astur is on the right there
        var plan = OrgChartLayout.Plan(finalSize.Width, root?.DesiredSize.Height ?? 0, [.. kids.Select(k => k.DesiredSize.Height)]);
        Plan = plan;
        root?.Arrange(ToRect(plan.Root));
        for (var i = 0; i < kids.Count; i++) kids[i].Arrange(ToRect(plan.Kids[i]));
        Connectors.Arrange(new Rect(0, 0, plan.Width, plan.Height));
        Connectors.Show(plan);
        return new Size(plan.Width, plan.Height);
    }

    private static Rect ToRect(OrgRect r) => new(r.X, r.Y, r.Width, r.Height);
}
