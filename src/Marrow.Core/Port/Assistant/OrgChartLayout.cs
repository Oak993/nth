// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.4 "Org chart (Team tab)"; the canvas's .org, .orow and drawLines, work/design/assistant/index.html lines
// 421-446 and 2854-2886; unit AS-17; ED-NEW-dextra-as17-1). No Swift twin.
// Where the org chart puts its nodes and its connectors. Wide (640 px and up): Dextra's node centred at the top, the Asturs beneath it in
// rows that wrap and centre, joined by thin connectors that run in the free lanes between the nodes (the stem under Dextra, a bus above each
// row, and, when the Asturs wrap, one spine in the margin), never across a node. Narrow (under 640 px): an indented list with a trunk down the
// left and an elbow to each Astur, as the sidebar tree draws it. Pure geometry in logical px: no Avalonia, no clock. The view asks for the
// column widths, measures its nodes at them, and hands the heights back to Plan; rows stretch their nodes to the tallest one.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

public readonly record struct OrgPoint(double X, double Y);

public readonly record struct OrgRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;
}

/// <summary>The chart (nodes in rows under Dextra) or the indented list (narrow).</summary>
public enum OrgLayoutMode { Chart, List }

/// <summary>One connector figure: a polyline whose inner bends are drawn rounded (<see cref="OrgChartLayout.CornerRadius"/>, or less where a leg is short).</summary>
public sealed record OrgConnector(IReadOnlyList<OrgPoint> Points);

/// <summary>Where everything goes, in the chart's own coordinates (x from its left edge, y from its top).</summary>
public sealed record OrgPlan(OrgLayoutMode Mode, double Width, double Height, OrgRect Root, IReadOnlyList<OrgRect> Kids, IReadOnlyList<OrgConnector> Connectors);

public static class OrgChartLayout
{
    /// <summary>Below this width the chart is an indented list (9.4: "Narrow widths (under 640 px)").</summary>
    public const double NarrowBelow = 640;

    public const double RootWidth = 220;
    public const double KidWidth = 176;

    /// <summary>Between nodes of a row, and between rows.</summary>
    public const double Gap = 12;

    /// <summary>From the bottom of Dextra's node to the top of the first row: the lane the stem and the bus run in.</summary>
    public const double Drop = 34;

    public const double PadTop = 8;
    public const double PadBottom = 4;

    /// <summary>The list's trunk, the left edge of the Asturs, the gap between them, and the lane under Dextra's node.</summary>
    public const double ListTrunkX = 18;
    public const double ListIndent = 30;
    public const double ListGap = 8;
    public const double ListTop = 10;

    /// <summary>How round a connector's bends are drawn.</summary>
    public const double CornerRadius = 8;

    private const double Same = .01;

    public static OrgLayoutMode ModeFor(double width) => width < NarrowBelow ? OrgLayoutMode.List : OrgLayoutMode.Chart;

    /// <summary>The widths the nodes are measured at: Dextra's and each Astur's.</summary>
    public static (double Root, double Kid) Widths(double width)
    {
        width = Math.Max(0, width);
        return ModeFor(width) == OrgLayoutMode.Chart
            ? (Math.Min(RootWidth, width), Math.Min(KidWidth, width))
            : (width, Math.Max(0, width - ListIndent));
    }

    /// <summary>How many Asturs a row holds at this width, and the margin kept free either side for the spine (one gap).</summary>
    public static int PerRow(double width) => Math.Max(1, (int)Math.Floor((width - 2 * Gap + Gap) / (KidWidth + Gap)));

    /// <summary>Lays everything out for the available <paramref name="width"/>, given the heights the nodes measured at <see cref="Widths"/>.</summary>
    public static OrgPlan Plan(double width, double rootHeight, IReadOnlyList<double> kidHeights)
    {
        ArgumentNullException.ThrowIfNull(kidHeights);
        width = Math.Max(0, width);
        return ModeFor(width) == OrgLayoutMode.Chart ? Chart(width, rootHeight, kidHeights) : List(width, rootHeight, kidHeights);
    }

    // ---- the chart ----

    private static OrgPlan Chart(double width, double rootHeight, IReadOnlyList<double> kidHeights)
    {
        var (rootW, kidW) = Widths(width);
        var root = new OrgRect((width - rootW) / 2, PadTop, rootW, rootHeight);
        var n = kidHeights.Count;
        if (n == 0) return new OrgPlan(OrgLayoutMode.Chart, width, root.Bottom + PadBottom, root, [], []);

        // rows of equal share (3 and 3, not 4 and 2), each centred
        var rowCount = (int)Math.Ceiling(n / (double)PerRow(width));
        var per = (int)Math.Ceiling(n / (double)rowCount);
        var rows = new List<(int First, int Count, double Top, double Height, double Left)>();
        var kids = new OrgRect[n];
        var top = root.Bottom + Drop;
        for (var first = 0; first < n; first += per)
        {
            var count = Math.Min(per, n - first);
            var height = 0.0;
            for (var i = first; i < first + count; i++) height = Math.Max(height, kidHeights[i]);
            var left = (width - (count * kidW + (count - 1) * Gap)) / 2;
            for (var j = 0; j < count; j++) kids[first + j] = new OrgRect(left + j * (kidW + Gap), top, kidW, height);
            rows.Add((first, count, top, height, left));
            top += height + Gap;
        }
        var bottom = rows[^1].Top + rows[^1].Height;
        return new OrgPlan(OrgLayoutMode.Chart, width, bottom + PadBottom, root, kids, ChartConnectors(root, kids, rows.Select(r => (r.First, r.Count, r.Top, r.Left)).ToList()));
    }

    private static List<OrgConnector> ChartConnectors(OrgRect root, OrgRect[] kids, List<(int First, int Count, double Top, double Left)> rows)
    {
        var lines = new List<OrgConnector>();
        var x0 = root.CenterX;
        var y0 = root.Bottom;
        var bus0 = rows[0].Top - Drop / 2;
        // the margin lane beside the widest row, where a second row's bus branches off
        var spine = rows.Min(r => r.Left) - Gap / 2;
        var first = Enumerable.Range(rows[0].First, rows[0].Count).ToList();

        // the stem under Dextra; a node straight below it takes the stem on to its own top
        var below = first.FirstOrDefault(i => Math.Abs(kids[i].CenterX - x0) < Same, -1);
        lines.Add(new OrgConnector([new OrgPoint(x0, y0), new OrgPoint(x0, below >= 0 ? kids[below].Y : bus0)]));

        var left = first.Where(i => kids[i].CenterX < x0 - Same).OrderByDescending(i => kids[i].CenterX).ToList();
        var right = first.Where(i => kids[i].CenterX > x0 + Same).OrderBy(i => kids[i].CenterX).ToList();
        if (rows.Count == 1) Arm(lines, x0, bus0, left, kids);
        else
        {
            // with more than one row the left arm carries on to the margin lane and down it; the first row's left nodes just hang from it
            foreach (var i in left) Hang(lines, kids[i].CenterX, bus0, kids[i].Y);
            var trail = new List<OrgPoint> { new(x0, bus0), new(spine, bus0) };
            for (var r = 1; r < rows.Count; r++)
            {
                var bus = rows[r].Top - Gap / 2;
                var members = Enumerable.Range(rows[r].First, rows[r].Count).ToList();
                var farthest = members.Max(i => kids[i].CenterX);
                var last = r == rows.Count - 1;
                if (last)
                {
                    trail.Add(new OrgPoint(spine, bus));
                    trail.Add(new OrgPoint(farthest, bus));
                    trail.Add(new OrgPoint(farthest, rows[r].Top));
                }
                else
                {
                    // a branch off the lane: along to the farthest node and down; the others hang from it
                    lines.Add(new OrgConnector([new OrgPoint(spine, bus), new OrgPoint(farthest, bus), new OrgPoint(farthest, rows[r].Top)]));
                }
                foreach (var i in members.Where(i => kids[i].CenterX < farthest - Same)) Hang(lines, kids[i].CenterX, bus, kids[i].Y);
            }
            lines.Add(new OrgConnector(trail));
        }
        Arm(lines, x0, bus0, right, kids);
        return lines;
    }

    // One arm of a bus: along to the farthest node and down, the nearer ones hanging from it with a straight drop.
    private static void Arm(List<OrgConnector> lines, double x0, double bus, List<int> nodesNearToFar, OrgRect[] kids)
    {
        if (nodesNearToFar.Count == 0) return;
        var far = kids[nodesNearToFar[^1]];
        lines.Add(new OrgConnector([new OrgPoint(x0, bus), new OrgPoint(far.CenterX, bus), new OrgPoint(far.CenterX, far.Y)]));
        foreach (var i in nodesNearToFar.Take(nodesNearToFar.Count - 1)) Hang(lines, kids[i].CenterX, bus, kids[i].Y);
    }

    private static void Hang(List<OrgConnector> lines, double x, double fromY, double toY) =>
        lines.Add(new OrgConnector([new OrgPoint(x, fromY), new OrgPoint(x, toY)]));

    // ---- the narrow list ----

    private static OrgPlan List(double width, double rootHeight, IReadOnlyList<double> kidHeights)
    {
        var (_, kidW) = Widths(width);
        var root = new OrgRect(0, PadTop, width, rootHeight);
        var n = kidHeights.Count;
        if (n == 0) return new OrgPlan(OrgLayoutMode.List, width, root.Bottom + PadBottom, root, [], []);
        var kids = new OrgRect[n];
        var y = root.Bottom + ListTop;
        for (var i = 0; i < n; i++)
        {
            kids[i] = new OrgRect(ListIndent, y, kidW, kidHeights[i]);
            y += kidHeights[i] + ListGap;
        }
        var lines = new List<OrgConnector>();
        for (var i = 0; i < n - 1; i++)
            lines.Add(new OrgConnector([new OrgPoint(ListTrunkX, kids[i].CenterY), new OrgPoint(ListIndent, kids[i].CenterY)]));
        // the trunk runs from Dextra's node down to the last elbow
        lines.Add(new OrgConnector([new OrgPoint(ListTrunkX, root.Bottom), new OrgPoint(ListTrunkX, kids[^1].CenterY), new OrgPoint(ListIndent, kids[^1].CenterY)]));
        return new OrgPlan(OrgLayoutMode.List, width, kids[^1].Bottom + PadBottom, root, kids, lines);
    }

    // ---- reading a plan (tests and the drawing) ----

    /// <summary>The corner radius a bend of a polyline gets: the standard radius, or less where either leg is short (half of it at most).</summary>
    public static double BendRadius(OrgPoint before, OrgPoint bend, OrgPoint after)
    {
        var a = Math.Abs(bend.X - before.X) + Math.Abs(bend.Y - before.Y);
        var b = Math.Abs(after.X - bend.X) + Math.Abs(after.Y - bend.Y);
        return Math.Min(CornerRadius, Math.Min(a, b));
    }

    /// <summary>A short line for logs and test failures.</summary>
    public static string Describe(OrgPlan plan) =>
        string.Create(CultureInfo.InvariantCulture, $"{plan.Mode} {plan.Width:0.#}x{plan.Height:0.#}, root {plan.Root}, {plan.Kids.Count} kids, {plan.Connectors.Count} connectors");
}
