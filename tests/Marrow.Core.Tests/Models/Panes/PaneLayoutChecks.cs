using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Models.Panes;

// DESIGN 13.1 (web surfaces): the invariants every solved layout keeps, and the random workspaces the solver fuzz
// draws (ED-NEW-web-w2-1). A helper for PaneLayoutTests and PaneLayoutFuzzTests; it holds no test.
internal static class PaneLayoutChecks
{
    private const double Tolerance = 1e-6;

    /// <summary>Every broken invariant of <paramref name="layout"/>, solved from <paramref name="state"/>; empty when it holds.</summary>
    public static List<string> Problems(PaneWorkspaceState state, PaneLayoutResult layout)
    {
        var problems = new List<string>();
        var s = state.Normalized();
        double w = layout.Width, h = layout.Height;
        void Check(bool ok, string what)
        {
            if (!ok) problems.Add(what);
        }

        var anythingOpen = s.Order.Count > 0 || s.IsSideChatOpen;
        var narrow = anythingOpen && w < PaneLayout.NarrowThreshold(s);
        Check(layout.Mode == (narrow ? PaneLayoutMode.Narrow : PaneLayoutMode.Tiles), $"mode {layout.Mode}, narrow threshold {PaneLayout.NarrowThreshold(s)}");
        Check(layout.IsShown(PaneKind.Chat) || layout.Mode == PaneLayoutMode.Narrow, "chat not shown");

        if (layout.Mode == PaneLayoutMode.Narrow)
        {
            var tabs = new List<PaneKind> { PaneKind.Chat };
            tabs.AddRange(s.GridPanes);
            if (s.IsSideChatOpen) tabs.Add(PaneKind.SideChat);
            Check(layout.NarrowTabs.SequenceEqual(tabs), "narrow tabs are not Chat then the open panes in order");
            Check(layout.Shown.Count == 1 && layout.IsShown(layout.NarrowSelection), "narrow shows one surface");
            var stripHeight = Math.Min(h, PaneKinds.NarrowTabStrip); // ED-NEW-web-review-7: clipped only at tiny transient heights
            Check(layout.NarrowStrip == new PaneRect(0, 0, w, stripHeight), "narrow strip outside the workspace");
            foreach (var tab in tabs)
                Check(layout.BoundsOf(tab) == new PaneRect(0, stripHeight, w, h - stripHeight), $"{tab} not on the narrow surface");
            Check(layout.Gutters.Count == 0 && layout.Rails.Count == 0 && layout.Cells.Count == 0, "narrow has gutters, rails or cells");
            return problems;
        }

        // The chat: full height, at least its minimum when the workspace has room for it.
        Check(Near(layout.Chat.Height, h) && Near(layout.Chat.Y, 0), "chat is not full height");
        Check(layout.Chat.Width >= Math.Min(PaneKinds.ChatMinWidth, w) - Tolerance, $"chat {layout.Chat.Width} under its minimum");

        // Nothing outside the workspace, nothing overlapping.
        var rects = new List<(string Name, PaneRect Rect)> { ("chat", layout.Chat) };
        rects.AddRange(layout.Cells.Select(c => (c.Shown.ToString(), c.Bounds)));
        rects.AddRange(layout.Rails.Select(r => ("rail " + r.Key, r.Bounds)));
        foreach (var (name, r) in rects)
        {
            Check(r.X >= -Tolerance && r.Y >= -Tolerance && r.Right <= w + Tolerance && r.Bottom <= h + Tolerance, $"{name} {r} outside {w} x {h}");
            Check(r.Width >= -Tolerance && r.Height >= -Tolerance, $"{name} {r} negative");
        }
        for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
                Check(!rects[i].Rect.Overlaps(rects[j].Rect), $"{rects[i].Name} {rects[i].Rect} overlaps {rects[j].Name} {rects[j].Rect}");

        // Each pane kind's minimum and maximum (a tab shares its cell, so the cell must suit every tab in it).
        foreach (var cell in layout.Cells)
        {
            foreach (var tab in cell.Tabs)
            {
                var size = PaneKinds.Size(tab);
                Check(cell.Bounds.Width >= size.MinWidth - Tolerance, $"{tab} width {cell.Bounds.Width} under {size.MinWidth}");
                Check(cell.Bounds.Width <= size.MaxWidth + Tolerance, $"{tab} width {cell.Bounds.Width} over {size.MaxWidth}");
                Check(cell.Bounds.Height >= Math.Min(size.MinHeight, h) - Tolerance, $"{tab} height {cell.Bounds.Height} under {size.MinHeight}");
                Check(layout.BoundsOf(tab) == cell.Bounds, $"{tab} bounds differ from its cell");
            }
            Check(cell.Tabs.Contains(cell.Shown) && layout.IsShown(cell.Shown), $"cell shown {cell.Shown} not in its tabs or not shown");
            Check(cell.Tabs.Where(t => t != cell.Shown).All(t => !layout.IsShown(t)), "a hidden tab is shown");
        }

        // Every open pane is in exactly one cell or one rail; the expanded pane is open.
        var open = s.GridPanes.ToList();
        if (s.IsSideChatOpen) open.Add(PaneKind.SideChat);
        foreach (var pane in open)
        {
            var places = layout.Cells.Count(c => c.Tabs.Contains(pane)) + layout.Rails.Count(r => r.Panes.Contains(pane));
            Check(places == 1, $"{pane} is in {places} places");
        }
        Check(layout.Cells.SelectMany(c => c.Tabs).All(open.Contains), "a cell holds a closed pane");
        Check(layout.Expanded is null || (open.Contains(layout.Expanded.Value) && layout.ExpandedBounds == new PaneRect(0, 0, w, h)), "expanded pane is not open or not full");

        // Gutters: unique keys, value inside its range (collapsed positions included), 1:1 with the pointer.
        Check(layout.Gutters.Select(g => g.Key).Distinct().Count() == layout.Gutters.Count, "repeated gutter key");
        foreach (var g in layout.Gutters)
        {
            Check(g.Minimum <= g.Value + Tolerance && g.Value <= g.Maximum + Tolerance, $"{g.Key} value {g.Value} outside [{g.Minimum}, {g.Maximum}]");
            Check(g.NormalMinimum <= g.NormalMaximum + Tolerance, $"{g.Key} normal range [{g.NormalMinimum}, {g.NormalMaximum}] crossed");
            Check(g.Minimum <= g.NormalMinimum + Tolerance && g.NormalMaximum <= g.Maximum + Tolerance, $"{g.Key} normal range outside its range");
            if (g.IsCollapsed) Check(Near(g.Value, g.CollapsedValue), $"{g.Key} collapsed at {g.Value}, not {g.CollapsedValue}");
            else Check(g.NormalMinimum <= g.Value + Tolerance && g.Value <= g.NormalMaximum + Tolerance, $"{g.Key} value {g.Value} outside [{g.NormalMinimum}, {g.NormalMaximum}]");
            if (g.CanCollapse) Check(g.Minimum <= g.CollapsedValue + Tolerance && g.CollapsedValue <= g.Maximum + Tolerance, $"{g.Key} collapsed value outside its range");
            Check(Near(g.ValueAt(g.Position), g.Value, 1e-6), $"{g.Key} ValueAt(Position) {g.ValueAt(g.Position)} is not {g.Value}");
            Check(g.Base > 0, $"{g.Key} base {g.Base}");
            Check(g.Leading.Count > 0 && g.Trailing.Count > 0 && g.Label.Length > 0 && g.ValueText.Length > 0, $"{g.Key} unnamed");
        }

        // No empty space: every edge of every shape is the workspace's edge or faces another shape one gap away.
        foreach (var (name, r) in rects)
        {
            if (r.Width <= Tolerance || r.Height <= Tolerance) continue;
            Check(Near(r.X, 0) || rects.Any(o => Near(o.Rect.Right + PaneKinds.Gap, r.X) && Overlap(o.Rect.Y, o.Rect.Bottom, r.Y, r.Bottom)), $"{name} {r}: nothing before its leading edge");
            Check(Near(r.Right, w) || rects.Any(o => Near(r.Right + PaneKinds.Gap, o.Rect.X) && Overlap(o.Rect.Y, o.Rect.Bottom, r.Y, r.Bottom)), $"{name} {r}: nothing after its trailing edge");
            Check(Near(r.Y, 0) || rects.Any(o => Near(o.Rect.Bottom + PaneKinds.Gap, r.Y) && Overlap(o.Rect.X, o.Rect.Right, r.X, r.Right)), $"{name} {r}: nothing above it");
            Check(Near(r.Bottom, h) || rects.Any(o => Near(r.Bottom + PaneKinds.Gap, o.Rect.Y) && Overlap(o.Rect.X, o.Rect.Right, r.X, r.Right)), $"{name} {r}: nothing below it");
        }
        return problems;
    }

    /// <summary>The same layout read right to left: every rectangle mirrored, every gutter's values unchanged.</summary>
    public static List<string> MirrorProblems(PaneLayoutResult ltr, PaneLayoutResult rtl)
    {
        var problems = new List<string>();
        var w = ltr.Width;
        if (rtl.Chat != ltr.Chat.Mirrored(w)) problems.Add("chat not mirrored");
        if (rtl.Cells.Count != ltr.Cells.Count || rtl.Cells.Zip(ltr.Cells).Any(p => p.First.Bounds != p.Second.Bounds.Mirrored(w) || !p.First.Tabs.SequenceEqual(p.Second.Tabs)))
            problems.Add("cells not mirrored");
        if (rtl.Rails.Zip(ltr.Rails).Any(p => p.First.Bounds != p.Second.Bounds.Mirrored(w))) problems.Add("rails not mirrored");
        foreach (var (r, l) in rtl.Gutters.Zip(ltr.Gutters))
        {
            if (r.Key != l.Key || r.Value != l.Value || r.Minimum != l.Minimum || r.Maximum != l.Maximum) problems.Add($"{l.Key} values changed");
            var position = l.Axis == PaneGutterAxis.X ? w - l.Position : l.Position;
            if (!Near(r.Position, position)) problems.Add($"{l.Key} position {r.Position}, not {position}");
            if (!Near(r.ValueAt(r.Position), r.Value)) problems.Add($"{l.Key} ValueAt broken when mirrored");
            if (l.Axis == PaneGutterAxis.X && !Near(r.ValueAt(r.Position + 10), l.ValueAt(l.Position - 10))) problems.Add($"{l.Key} pointer runs the wrong way");
        }
        return problems;
    }

    /// <summary>A random workspace: any panes in any order and grouping, ratios (bad ones too), collapsed sides and memories.</summary>
    public static PaneWorkspaceState RandomState(Random random)
    {
        var kinds = PaneKinds.GridKinds.Where(_ => random.Next(2) == 0).OrderBy(_ => random.Next()).ToList();
        var slots = new List<PaneSlot>();
        foreach (var kind in kinds)
        {
            if (slots.Count > 0 && random.Next(5) == 0) slots[^1] = new PaneSlot(slots[^1].Panes.Appending(kind));
            else slots.Add(PaneSlot.Of(kind));
        }
        var ratios = new Dictionary<string, double>();
        foreach (var key in PaneGutterKeys.All)
            if (random.Next(2) == 0) ratios[key] = random.Next(12) == 0 ? (random.Next(2) == 0 ? -0.4 : 1.7) : random.NextDouble();
        var collapsed = PaneGutterKeys.All.Take(2).Where(_ => random.Next(4) == 0).ToList();
        var pre = collapsed.Where(_ => random.Next(2) == 0).ToDictionary(k => k, _ => random.NextDouble());
        var all = Enum.GetValues<PaneKind>();
        return new PaneWorkspaceState
        {
            Order = new ValueList<PaneSlot>(slots),
            IsSideChatOpen = random.Next(2) == 0,
            IsTerminalInDrawer = random.Next(4) == 0,
            Ratios = new ValueMap<string, double>(ratios),
            Collapsed = new ValueSet<string>(collapsed),
            PreCollapse = new ValueMap<string, double>(pre),
            RecentTabs = new ValueList<PaneKind>(all.Where(_ => random.Next(3) == 0).OrderBy(_ => random.Next())),
            Expanded = random.Next(4) == 0 ? all[random.Next(all.Length)] : null,
            NarrowSelection = all[random.Next(all.Length)],
        };
    }

    /// <summary>Fails with the first problems written out in full.</summary>
    public static void AssertNone(IEnumerable<string> problems)
    {
        var first = problems.Take(12).ToList();
        Assert.True(first.Count == 0, string.Join(Environment.NewLine, first));
    }

    public static bool Near(double a, double b, double tolerance = Tolerance) => Math.Abs(a - b) <= tolerance;

    private static bool Overlap(double a0, double a1, double b0, double b1) => Math.Min(a1, b1) - Math.Max(a0, b0) > Tolerance;
}
