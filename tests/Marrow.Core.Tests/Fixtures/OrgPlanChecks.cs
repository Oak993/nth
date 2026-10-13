using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>Dextra AS-17: the rules every org chart plan keeps, as checks a test can run over many widths and team sizes.</summary>
public static class OrgPlanChecks
{
    public static IReadOnlyList<double> Heights(int count, double each = 150) => [.. Enumerable.Repeat(each, count)];

    /// <summary>Every node inside the plan's width, none over another; returns the first thing wrong, or null.</summary>
    public static string? NodesProblem(OrgPlan plan)
    {
        var all = new List<(string Name, OrgRect Rect)> { ("root", plan.Root) };
        for (var i = 0; i < plan.Kids.Count; i++) all.Add(($"kid {i}", plan.Kids[i]));
        foreach (var (name, r) in all)
        {
            if (r.X < -.001 || r.Right > plan.Width + .001) return $"{name} runs outside the width ({r} in {plan.Width})";
            if (r.Y < -.001 || r.Bottom > plan.Height + .001) return $"{name} runs outside the height ({r} in {plan.Height})";
        }
        for (var a = 0; a < all.Count; a++)
        for (var b = a + 1; b < all.Count; b++)
            if (Overlap(all[a].Rect, all[b].Rect)) return $"{all[a].Name} overlaps {all[b].Name}";
        return null;
    }

    /// <summary>No connector leg passes through the inside of a node, and every leg runs along an axis; returns the first thing wrong, or null.</summary>
    public static string? ConnectorsProblem(OrgPlan plan)
    {
        var nodes = new List<OrgRect> { plan.Root };
        nodes.AddRange(plan.Kids);
        foreach (var line in plan.Connectors)
        {
            for (var i = 1; i < line.Points.Count; i++)
            {
                OrgPoint a = line.Points[i - 1], b = line.Points[i];
                if (Math.Abs(a.X - b.X) > .001 && Math.Abs(a.Y - b.Y) > .001) return $"a leg from {a} to {b} is not along an axis";
                foreach (var node in nodes)
                    if (Crosses(a, b, node)) return $"a leg from {a} to {b} runs through {node}";
                if (Math.Min(a.X, b.X) < -.001 || Math.Max(a.X, b.X) > plan.Width + .001 || Math.Min(a.Y, b.Y) < -.001 || Math.Max(a.Y, b.Y) > plan.Height + .001)
                    return $"a leg from {a} to {b} leaves the chart";
            }
        }
        return null;
    }

    /// <summary>Whether a connector ends on the node's top edge (chart) or at its left edge (list), centred, so the node is joined to Dextra.</summary>
    public static bool Reaches(OrgPlan plan, OrgRect node)
    {
        foreach (var line in plan.Connectors)
        {
            var end = line.Points[^1];
            if (plan.Mode == OrgLayoutMode.Chart && Math.Abs(end.X - node.CenterX) < .01 && Math.Abs(end.Y - node.Y) < .01) return true;
            if (plan.Mode == OrgLayoutMode.List && Math.Abs(end.X - node.X) < .01 && Math.Abs(end.Y - node.CenterY) < .01) return true;
        }
        return false;
    }

    /// <summary>The whole chart is one tree: every connector touches the stem (or the trunk), or one that does, at a corner or along a leg.</summary>
    public static bool Connected(OrgPlan plan)
    {
        var start = plan.Mode == OrgLayoutMode.Chart ? new OrgPoint(plan.Root.CenterX, plan.Root.Bottom) : new OrgPoint(OrgChartLayout.ListTrunkX, plan.Root.Bottom);
        var reached = new List<OrgConnector> { new([start, start]) };
        var waiting = plan.Connectors.ToList();
        var changed = true;
        while (changed && waiting.Count > 0)
        {
            changed = false;
            foreach (var line in waiting.ToList())
            {
                if (!reached.Any(r => Touch(r, line))) continue;
                reached.Add(line);
                waiting.Remove(line);
                changed = true;
            }
        }
        return waiting.Count == 0;
    }

    private static bool Touch(OrgConnector a, OrgConnector b) => a.Points.Any(p => OnPolyline(b, p)) || b.Points.Any(p => OnPolyline(a, p));

    private static bool OnPolyline(OrgConnector line, OrgPoint p)
    {
        for (var i = 1; i < line.Points.Count; i++)
        {
            OrgPoint a = line.Points[i - 1], b = line.Points[i];
            if (p.X >= Math.Min(a.X, b.X) - .01 && p.X <= Math.Max(a.X, b.X) + .01 && p.Y >= Math.Min(a.Y, b.Y) - .01 && p.Y <= Math.Max(a.Y, b.Y) + .01) return true;
        }
        return false;
    }

    private static bool Overlap(OrgRect a, OrgRect b) => a.X < b.Right - .001 && b.X < a.Right - .001 && a.Y < b.Bottom - .001 && b.Y < a.Bottom - .001;

    // A leg is an axis-aligned segment; it runs through a node when it passes strictly inside the node's box.
    private static bool Crosses(OrgPoint a, OrgPoint b, OrgRect node)
    {
        double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
        return x0 < node.Right - .001 && x1 > node.X + .001 && y0 < node.Bottom - .001 && y1 > node.Y + .001
            && !(x0 == x1 && (Math.Abs(x0 - node.X) < .001 || Math.Abs(x0 - node.Right) < .001))
            && !(y0 == y1 && (Math.Abs(y0 - node.Y) < .001 || Math.Abs(y0 - node.Bottom) < .001));
    }
}
