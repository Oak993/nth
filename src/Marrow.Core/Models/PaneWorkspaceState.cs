using Marrow.Core.Port;

// PORT-ONLY: web surfaces W2, a thread's pane workspace as data (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.2,
// 3.3 to 3.6, 3.11; ED-NEW-web-w2-1). No Swift counterpart. Every member is a pure value operation: the window reads
// the state, PaneLayout.Solve turns it into rectangles, and nothing here knows a pixel. Bad data never throws: every
// operation starts from Normalized(), and the solver clamps whatever ratios it is given.

namespace Marrow.Core.Models;

/// <summary>One place in the workspace order: a single pane, or an explicit tab group the user made (3.6).</summary>
public sealed record PaneSlot(ValueList<PaneKind> Panes)
{
    public static PaneSlot Of(params PaneKind[] panes) => new(new ValueList<PaneKind>(panes));

    /// <summary>An explicit tab group: two or more panes the user joined.</summary>
    public bool IsGroup => Panes.Count > 1;

    public bool Contains(PaneKind kind) => Panes.Contains(kind);

    /// <summary>The insertion rank of a slot: the lowest rank among its panes (3.2 rule 9).</summary>
    public int Rank => Panes.Count == 0 ? int.MaxValue : Panes.Min(PaneKinds.Rank);
}

/// <summary>The ratio and collapse keys of the workspace gutters (DESIGN 3.2 rule 10).</summary>
public static class PaneGutterKeys
{
    /// <summary>Chat column and everything right of it; collapsing it folds every pane into one rail.</summary>
    public const string Chat = "chat";

    /// <summary>The grid and the Side chat column; collapsing it folds Side chat into a rail.</summary>
    public const string Side = "side";

    /// <summary>Two grid cells side by side.</summary>
    public const string Cols = "cols";

    /// <summary>The two cells of the two-column grid's top row.</summary>
    public const string Top = "top";

    /// <summary>The two cells of the two-column grid's bottom row (four or more cells).</summary>
    public const string Bottom = "bottom";

    /// <summary>The first horizontal boundary: top row and bottom row of the two-column grid, or rows 1 and 2.</summary>
    public const string Row1 = "row1";

    /// <summary>Rows 2 and 3 of the one-column grid.</summary>
    public const string Row2 = "row2";

    public static IReadOnlyList<string> All { get; } = [Chat, Side, Cols, Top, Bottom, Row1, Row2];
}

/// <summary>
/// A thread's pane workspace (DESIGN 3.11): open panes and their order, tab groups, ratios, collapsed sides and their
/// sizes before the collapse, the expanded pane, the most recently chosen tabs and the narrow-mode selection.
/// </summary>
public sealed record PaneWorkspaceState
{
    public static PaneWorkspaceState Default { get; } = new();

    /// <summary>The grid's ordered slots (Chat and Side chat are never in it; Terminal is, only when it is in the grid).</summary>
    public ValueList<PaneSlot> Order { get; init; } = ValueList<PaneSlot>.Empty;

    /// <summary>Side chat is open (its own column when there is room, else a grid cell).</summary>
    public bool IsSideChatOpen { get; init; }

    /// <summary>The terminal is open as the chat column's drawer (its default place, 3.1).</summary>
    public bool IsTerminalInDrawer { get; init; }

    /// <summary>The terminal was last dragged into the grid, so opening it puts it there.</summary>
    public bool TerminalPrefersGrid { get; init; }

    /// <summary>Stored gutter ratios by key (<see cref="PaneGutterKeys"/>); a missing key uses its default.</summary>
    public ValueMap<string, double> Ratios { get; init; } = ValueMap<string, double>.Empty;

    /// <summary>Collapsed gutters: <see cref="PaneGutterKeys.Chat"/> (all panes) and <see cref="PaneGutterKeys.Side"/>.</summary>
    public ValueSet<string> Collapsed { get; init; } = ValueSet<string>.Empty;

    /// <summary>The ratio each collapsed gutter had before the collapse; missing means restore to the default (3.2 rule 11).</summary>
    public ValueMap<string, double> PreCollapse { get; init; } = ValueMap<string, double>.Empty;

    /// <summary>The pane sprung to the full workspace (3.4), if any.</summary>
    public PaneKind? Expanded { get; init; }

    /// <summary>Panes most recently opened or chosen, newest first: a tab group shows its most recent member.</summary>
    public ValueList<PaneKind> RecentTabs { get; init; } = ValueList<PaneKind>.Empty;

    /// <summary>The surface narrow mode shows (3.8); Chat unless a pane was opened or chosen.</summary>
    public PaneKind NarrowSelection { get; init; } = PaneKind.Chat;

    /// <summary>The grid panes in order, groups flattened.</summary>
    public IEnumerable<PaneKind> GridPanes => Order.SelectMany(slot => slot.Panes);

    /// <summary>
    /// Migration (3.11): an old thread with no workspace state maps the old <c>ThreadRuntime</c> flags in once, the
    /// terminal drawer and Changes.
    /// </summary>
    public static PaneWorkspaceState FromLegacyFlags(bool isTerminalVisible, bool isDiffVisible)
    {
        var state = Default with { IsTerminalInDrawer = isTerminalVisible };
        return isDiffVisible ? state.Open(PaneKind.Changes) : state;
    }

    public bool IsOpen(PaneKind kind) => kind switch
    {
        PaneKind.Chat => true,
        PaneKind.SideChat => IsSideChatOpen,
        PaneKind.Terminal => IsTerminalInDrawer || InOrder(kind),
        PaneKind.BackgroundTasks or PaneKind.Changes or PaneKind.Artifacts or PaneKind.Browser or PaneKind.App
            or PaneKind.Transcript => InOrder(kind),
    };

    /// <summary>The index of the slot holding <paramref name="kind"/>, or -1.</summary>
    public int SlotIndexOf(PaneKind kind)
    {
        for (var i = 0; i < Order.Count; i++)
            if (Order[i].Contains(kind)) return i;
        return -1;
    }

    /// <summary>
    /// The same workspace with bad data removed: unknown or repeated panes, Chat or Side chat in the order, empty
    /// slots, non-finite ratios, a terminal both in the drawer and the grid (the grid wins), an expanded or selected
    /// pane that is not open.
    /// </summary>
    public PaneWorkspaceState Normalized()
    {
        var seen = new HashSet<PaneKind>();
        var slots = new List<PaneSlot>();
        foreach (var slot in Order)
        {
            if (slot?.Panes is null) continue;
            var panes = slot.Panes.Where(kind => PaneKinds.IsGridKind(kind) && seen.Add(kind)).ToArray();
            if (panes.Length > 0) slots.Add(panes.Length == slot.Panes.Count ? slot : new PaneSlot(new ValueList<PaneKind>(panes)));
        }
        var order = slots.Count == Order.Count && slots.SequenceEqual(Order) ? Order : new ValueList<PaneSlot>(slots);
        var ratios = Ratios.All(pair => double.IsFinite(pair.Value)) ? Ratios
            : new ValueMap<string, double>(Ratios.Where(pair => double.IsFinite(pair.Value)));
        var pre = PreCollapse.All(pair => double.IsFinite(pair.Value)) ? PreCollapse
            : new ValueMap<string, double>(PreCollapse.Where(pair => double.IsFinite(pair.Value)));
        var recent = RecentTabs.All(k => Enum.IsDefined(k)) && RecentTabs.Distinct().Count() == RecentTabs.Count ? RecentTabs
            : new ValueList<PaneKind>(RecentTabs.Where(k => Enum.IsDefined(k)).Distinct());
        var state = this with
        {
            Order = order,
            Ratios = ratios,
            PreCollapse = pre,
            RecentTabs = recent,
            IsTerminalInDrawer = IsTerminalInDrawer && !seen.Contains(PaneKind.Terminal),
        };
        var expanded = state.Expanded is { } e && e != PaneKind.Chat && Enum.IsDefined(e) && state.IsOpen(e)
            && !(e == PaneKind.Terminal && state.IsTerminalInDrawer) ? e : (PaneKind?)null;
        var narrow = Enum.IsDefined(state.NarrowSelection) && state.IsOpen(state.NarrowSelection)
            && !(state.NarrowSelection == PaneKind.Terminal && state.IsTerminalInDrawer) ? state.NarrowSelection : PaneKind.Chat;
        return state.Expanded == expanded && state.NarrowSelection == narrow ? state : state with { Expanded = expanded, NarrowSelection = narrow };
    }

    /// <summary>
    /// Opens a pane, or chooses it when it is open (3.2 rule 9, 3.3). A grid pane is inserted before the first slot
    /// of a higher rank; Side chat opens its column; the terminal opens in its drawer unless it was last in the grid.
    /// Opening restores a collapsed workspace (and a collapsed Side chat when Side chat is opened).
    /// </summary>
    public PaneWorkspaceState Open(PaneKind kind)
    {
        var state = Normalized();
        if (!Enum.IsDefined(kind)) return state;
        switch (kind)
        {
            case PaneKind.Chat:
                return state with { NarrowSelection = PaneKind.Chat };
            case PaneKind.SideChat:
                state = (state with { IsSideChatOpen = true }).Restore(PaneGutterKeys.Side);
                break;
            case PaneKind.Terminal when !state.TerminalPrefersGrid && !state.InOrder(kind):
                return state with { IsTerminalInDrawer = true };
            case PaneKind.Terminal:
            case PaneKind.BackgroundTasks:
            case PaneKind.Changes:
            case PaneKind.Artifacts:
            case PaneKind.Browser:
            case PaneKind.App:
            case PaneKind.Transcript:
                if (!state.InOrder(kind)) state = state with { Order = InsertByRank(state.Order, kind), IsTerminalInDrawer = kind != PaneKind.Terminal && state.IsTerminalInDrawer };
                break;
        }
        return state.Restore(PaneGutterKeys.Chat).Chosen(kind);
    }

    /// <summary>
    /// Closes a pane (3.5): it leaves the layout and its tab group; an expanded or narrow-selected pane gives way to the
    /// chat. Collapse memories stay so a reopened Side chat comes back as it was.
    /// </summary>
    public PaneWorkspaceState Close(PaneKind kind)
    {
        var state = Normalized();
        if (kind == PaneKind.Chat || !Enum.IsDefined(kind)) return state;
        state = kind switch
        {
            PaneKind.SideChat => state with { IsSideChatOpen = false },
            PaneKind.Terminal => state.Without(kind) with { IsTerminalInDrawer = false },
            PaneKind.BackgroundTasks or PaneKind.Changes or PaneKind.Artifacts or PaneKind.Browser or PaneKind.App
                or PaneKind.Transcript => state.Without(kind),
            PaneKind.Chat => state,
        };
        return state with
        {
            Expanded = state.Expanded == kind ? null : state.Expanded,
            NarrowSelection = state.NarrowSelection == kind ? PaneKind.Chat : state.NarrowSelection,
            RecentTabs = state.RecentTabs.Contains(kind) ? new ValueList<PaneKind>(state.RecentTabs.Where(k => k != kind)) : state.RecentTabs,
        };
    }

    public PaneWorkspaceState Toggle(PaneKind kind) => IsOpen(kind) && kind != PaneKind.Chat ? Close(kind) : Open(kind);

    /// <summary>Chooses an open pane: its tab shows in any group and narrow mode shows it.</summary>
    public PaneWorkspaceState Select(PaneKind kind)
    {
        var state = Normalized();
        return state.IsOpen(kind) ? state.Chosen(kind) : state;
    }

    /// <summary>Swaps the slots of two panes in the order (3.6 drop on another cell's body; Ctrl+Alt+Arrow).</summary>
    public PaneWorkspaceState Swap(PaneKind pane, PaneKind target)
    {
        var state = Normalized();
        int a = state.SlotIndexOf(pane), b = state.SlotIndexOf(target);
        if (a < 0 || b < 0 || a == b) return state;
        var slots = state.Order.ToList();
        (slots[a], slots[b]) = (slots[b], slots[a]);
        return state with { Order = new ValueList<PaneSlot>(slots) };
    }

    /// <summary>
    /// A header dropped on another cell's body (3.6): a single pane swaps slots with the target; a pane that is a tab in a
    /// group leaves the group and takes the target slot's place, the rest shifting by one.
    /// </summary>
    public PaneWorkspaceState DropOnBody(PaneKind pane, PaneKind target)
    {
        var state = Normalized();
        int from = state.SlotIndexOf(pane), to = state.SlotIndexOf(target);
        if (from < 0 || to < 0 || pane == target) return state;
        if (!state.Order[from].IsGroup) return state.Swap(pane, target);
        var slots = state.Order.ToList();
        slots[from] = new PaneSlot(new ValueList<PaneKind>(slots[from].Panes.Where(k => k != pane)));
        slots.Insert(to, PaneSlot.Of(pane));
        return (state with { Order = new ValueList<PaneSlot>(slots) }).Chosen(pane);
    }

    /// <summary>A header dropped on another cell's header (3.6): the pane joins that slot as its last tab, and shows.</summary>
    public PaneWorkspaceState JoinAsTab(PaneKind pane, PaneKind target)
    {
        var state = Normalized();
        int from = state.SlotIndexOf(pane), to = state.SlotIndexOf(target);
        if (from < 0 || to < 0 || from == to) return state;
        var slots = state.Order.ToList();
        slots[to] = new PaneSlot(slots[to].Panes.Appending(pane));
        slots[from] = new PaneSlot(new ValueList<PaneKind>(slots[from].Panes.Where(k => k != pane)));
        if (slots[from].Panes.Count == 0) slots.RemoveAt(from);
        return (state with { Order = new ValueList<PaneSlot>(slots) }).Chosen(pane);
    }

    /// <summary>Ctrl+Alt+Left or Up: the pane's slot swaps with the slot before it.</summary>
    public PaneWorkspaceState MoveEarlier(PaneKind pane) => MoveBy(pane, -1);

    /// <summary>Ctrl+Alt+Right or Down: the pane's slot swaps with the slot after it.</summary>
    public PaneWorkspaceState MoveLater(PaneKind pane) => MoveBy(pane, +1);

    /// <summary>
    /// Ctrl+Alt+T (3.6): a pane in a group splits out into its own slot right after the group; a single pane joins the
    /// previous slot as a tab (the next one when it is first).
    /// </summary>
    public PaneWorkspaceState ToggleTabGroup(PaneKind pane)
    {
        var state = Normalized();
        var index = state.SlotIndexOf(pane);
        if (index < 0) return state;
        if (state.Order[index].IsGroup)
        {
            var slots = state.Order.ToList();
            slots[index] = new PaneSlot(new ValueList<PaneKind>(slots[index].Panes.Where(k => k != pane)));
            slots.Insert(index + 1, PaneSlot.Of(pane));
            return (state with { Order = new ValueList<PaneSlot>(slots) }).Chosen(pane);
        }
        if (state.Order.Count < 2) return state;
        var target = state.Order[index > 0 ? index - 1 : index + 1].Panes[0];
        return state.JoinAsTab(pane, target);
    }

    /// <summary>The terminal leaves its drawer for the grid (W4's drag), at a slot index or by rank.</summary>
    public PaneWorkspaceState MoveTerminalToGrid(int? slotIndex = null)
    {
        var state = Normalized().Without(PaneKind.Terminal);
        var order = slotIndex is { } at
            ? state.Order.Mutating(slots => slots.Insert(Math.Max(0, Math.Min(slots.Count, at)), PaneSlot.Of(PaneKind.Terminal)))
            : InsertByRank(state.Order, PaneKind.Terminal);
        return (state with { Order = order, IsTerminalInDrawer = false, TerminalPrefersGrid = true })
            .Restore(PaneGutterKeys.Chat).Chosen(PaneKind.Terminal);
    }

    /// <summary>The terminal goes back to the chat's drawer.</summary>
    public PaneWorkspaceState MoveTerminalToDrawer()
    {
        var state = Normalized().Without(PaneKind.Terminal);
        return state with
        {
            IsTerminalInDrawer = true,
            TerminalPrefersGrid = false,
            Expanded = state.Expanded == PaneKind.Terminal ? null : state.Expanded,
            NarrowSelection = state.NarrowSelection == PaneKind.Terminal ? PaneKind.Chat : state.NarrowSelection,
        };
    }

    /// <summary>Springs an open pane to the full workspace (3.4). The chat cannot be expanded.</summary>
    public PaneWorkspaceState Expand(PaneKind kind)
    {
        var state = Normalized();
        return kind != PaneKind.Chat && state.IsOpen(kind) && !(kind == PaneKind.Terminal && state.IsTerminalInDrawer)
            ? (state with { Expanded = kind }).Chosen(kind) : state;
    }

    public PaneWorkspaceState RestoreExpanded() => Normalized() with { Expanded = null };

    /// <summary>Ctrl+Shift+\ and the header's Expand or Restore.</summary>
    public PaneWorkspaceState ToggleExpanded(PaneKind kind) => Normalized().Expanded == kind ? RestoreExpanded() : Expand(kind);

    /// <summary>Stores a gutter's ratio (a share of that gutter's base length).</summary>
    public PaneWorkspaceState WithRatio(string key, double ratio) =>
        double.IsFinite(ratio) ? this with { Ratios = Ratios.Mutating(map => map[key] = ratio) } : this;

    /// <summary>Forgets a gutter's ratio so it uses its default (double-click, Enter).</summary>
    public PaneWorkspaceState WithoutRatio(string key) =>
        Ratios.ContainsKey(key) ? this with { Ratios = Ratios.Mutating(map => map.Remove(key)) } : this;

    /// <summary>
    /// Collapses a gutter's collapsible side and remembers the ratio before it (<paramref name="ratioBefore"/>; null when
    /// that side was at its minimum, so restore uses the default). PaneLayout.Collapse works the memory out.
    /// </summary>
    public PaneWorkspaceState Collapse(string key, double? ratioBefore)
    {
        if (Collapsed.Contains(key)) return this;
        return this with
        {
            Collapsed = Collapsed.Mutating(set => set.Add(key)),
            PreCollapse = ratioBefore is { } r && double.IsFinite(r)
                ? PreCollapse.Mutating(map => map[key] = r)
                : PreCollapse.Mutating(map => map.Remove(key)),
        };
    }

    /// <summary>Restores a collapsed side to its size before the collapse, or to the default (3.2 rule 11).</summary>
    public PaneWorkspaceState Restore(string key)
    {
        if (!Collapsed.Contains(key)) return this;
        var ratios = PreCollapse.TryGetValue(key, out var before)
            ? Ratios.Mutating(map => map[key] = before)
            : Ratios.Mutating(map => map.Remove(key));
        return this with
        {
            Collapsed = Collapsed.Mutating(set => set.Remove(key)),
            Ratios = ratios,
            PreCollapse = PreCollapse.Mutating(map => map.Remove(key)),
        };
    }

    private bool InOrder(PaneKind kind) => SlotIndexOf(kind) >= 0;

    private PaneWorkspaceState Chosen(PaneKind kind) => this with
    {
        RecentTabs = new ValueList<PaneKind>(RecentTabs.Where(k => k != kind).Prepend(kind)),
        NarrowSelection = kind == PaneKind.Terminal && IsTerminalInDrawer ? NarrowSelection : kind,
    };

    private PaneWorkspaceState Without(PaneKind kind)
    {
        var index = SlotIndexOf(kind);
        if (index < 0) return this;
        var rest = Order[index].Panes.Where(k => k != kind).ToArray();
        return this with
        {
            Order = Order.Mutating(slots =>
            {
                if (rest.Length == 0) slots.RemoveAt(index);
                else slots[index] = new PaneSlot(new ValueList<PaneKind>(rest));
            }),
        };
    }

    private PaneWorkspaceState MoveBy(PaneKind pane, int step)
    {
        var state = Normalized();
        var index = state.SlotIndexOf(pane);
        var other = index + step;
        if (index < 0 || other < 0 || other >= state.Order.Count) return state;
        return state.Swap(pane, state.Order[other].Panes[0]);
    }

    private static ValueList<PaneSlot> InsertByRank(ValueList<PaneSlot> order, PaneKind kind)
    {
        var rank = PaneKinds.Rank(kind);
        var at = order.Count;
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i].Rank <= rank) continue;
            at = i;
            break;
        }
        return order.Mutating(slots => slots.Insert(at, PaneSlot.Of(kind)));
    }
}
