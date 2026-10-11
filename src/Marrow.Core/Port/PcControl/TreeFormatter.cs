// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1; DESIGN.md 2.1, 2.4). Turns a window's accessibility tree into nth's own
// compact text: one numbered line per useful element, off-screen and zero-size elements skipped, single-child wrapper panes
// folded, unnamed decorative images and separators dropped, names and values capped, long lists summarised, and a hard line
// cap with a tail that points at query. Numbers are given once per observation, in reading order.
using System.Globalization;
using System.Text;

namespace Marrow.Core.Port.PcControl;

/// <summary>One line of a formatted tree. <see cref="Body"/> is the line without its number and indent.</summary>
public sealed record PcTreeLine(int Index, int Depth, int ParentIndex, string RuntimeId, string Body)
{
    public string Render() => "[" + Index.ToString(CultureInfo.InvariantCulture) + "] " + new string(' ', 2 * Depth) + Body;
}

/// <summary>A formatted tree: its lines in order and the element each number stands for.</summary>
public sealed record PcTree(IReadOnlyList<PcTreeLine> Lines, IReadOnlyDictionary<int, PcUiNode> Elements)
{
    public static PcTree Empty { get; } = new([], new Dictionary<int, PcUiNode>());

    /// <summary>The number of the focused element, if any line holds it.</summary>
    public int? Focused => Lines.FirstOrDefault(l => Elements[l.Index].Has(PcStates.Focused))?.Index;

    /// <summary>Numbered lines of selected elements.</summary>
    public IEnumerable<PcTreeLine> Selected => Lines.Where(l => Elements[l.Index].Has(PcStates.Selected));

    /// <summary>The number of the element with this RuntimeId, if it is in the tree.</summary>
    public int? IndexOf(string runtimeId) => Lines.FirstOrDefault(l => l.RuntimeId == runtimeId && runtimeId.Length > 0)?.Index;

    /// <summary>The text: the kept lines (all, or those matching <paramref name="query"/> with their parents), at most <paramref name="maxLines"/>.</summary>
    public string Render(string? query = null, int maxLines = TreeFormatter.DefaultMaxLines)
    {
        IReadOnlyList<PcTreeLine> kept = Lines;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var byIndex = Lines.ToDictionary(l => l.Index);
            var keep = new HashSet<int>();
            foreach (var line in Lines.Where(l => l.Body.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                for (var at = line.Index; at > 0 && keep.Add(at); at = byIndex[at].ParentIndex) { }
            }
            if (keep.Count == 0) return "no element matches " + PcWindowLine.Quote(query.Trim());
            kept = Lines.Where(l => keep.Contains(l.Index)).ToList();
        }
        var text = new StringBuilder();
        var shown = Math.Min(kept.Count, Math.Max(1, maxLines));
        for (var i = 0; i < shown; i++) text.Append(kept[i].Render()).Append('\n');
        if (kept.Count > shown)
            text.Append("... ").Append((kept.Count - shown).ToString(CultureInfo.InvariantCulture)).Append(" more lines not shown. Use query to narrow.\n");
        return text.ToString().TrimEnd('\n');
    }
}

public static class TreeFormatter
{
    public const int DefaultMaxLines = 300;
    public const int NameCap = 80;
    public const int ValueCap = 120;
    public const int ListCap = 50;

    private static readonly HashSet<string> wrapperRoles = new(StringComparer.OrdinalIgnoreCase) { "pane", "group", "custom", "" };
    private static readonly HashSet<string> decorativeRoles = new(StringComparer.OrdinalIgnoreCase) { "image", "graphic", "picture" };
    private static readonly HashSet<string> separatorRoles = new(StringComparer.OrdinalIgnoreCase) { "separator", "thumb" };
    private static readonly HashSet<string> listRoles = new(StringComparer.OrdinalIgnoreCase) { "list", "tree", "table", "data grid", "datagrid", "grid", "menu", "tab" };

    private sealed record Item(PcUiNode Node, List<Item> Children, int TotalChildren);

    /// <summary>Prunes, folds and numbers the tree under <paramref name="root"/> (the window itself is line 1).</summary>
    public static PcTree Format(PcUiNode root)
    {
        var items = Visit(root, isRoot: true);
        var lines = new List<PcTreeLine>();
        var elements = new Dictionary<int, PcUiNode>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items) Emit(item, 0, 0, lines, elements, seen);
        return new PcTree(lines, elements);
    }

    private static List<Item> Visit(PcUiNode node, bool isRoot)
    {
        if (!isRoot && node.Has(PcStates.Offscreen)) return [];
        var children = new List<Item>();
        foreach (var child in node.Children) children.AddRange(Visit(child, isRoot: false));
        if (isRoot) return [new Item(node, children, node.Children.Count)];
        // A zero-size element, a decorative image or a separator is not shown; its children (if any) take its place.
        if (node.Bounds.IsEmpty || IsDecorative(node)) return children;
        // A wrapper with one child is folded into that child.
        if (children.Count == 1 && IsWrapper(node)) return children;
        return [new Item(node, children, node.Children.Count)];
    }

    private static bool IsDecorative(PcUiNode node) =>
        separatorRoles.Contains(node.Role)
        || (decorativeRoles.Contains(node.Role) && string.IsNullOrWhiteSpace(node.Name) && node.Actions == PcActions.None);

    private static bool IsWrapper(PcUiNode node) =>
        wrapperRoles.Contains(node.Role) && string.IsNullOrWhiteSpace(node.Name)
        && (node.Actions & ~(PcActions.Focus | PcActions.ScrollIntoView)) == PcActions.None
        && (node.States & ~PcStates.ReadOnly) == PcStates.None;

    private static void Emit(Item item, int depth, int parent, List<PcTreeLine> lines, Dictionary<int, PcUiNode> elements, Dictionary<string, int> seen)
    {
        var index = lines.Count + 1;
        var children = item.Children;
        var shown = children.Count;
        if (listRoles.Contains(item.Node.Role) && shown > ListCap) shown = ListCap;
        var body = Body(item.Node, listRoles.Contains(item.Node.Role) ? (item.TotalChildren, shown) : null);
        lines.Add(new PcTreeLine(index, depth, parent, Key(item.Node.RuntimeId, seen), body));
        elements[index] = item.Node;
        for (var i = 0; i < shown; i++) Emit(children[i], depth + 1, index, lines, elements, seen);
    }

    // RuntimeIds are unique per element; an element that has none (or a repeat) still gets a stable key for the diff.
    private static string Key(string runtimeId, Dictionary<string, int> seen)
    {
        var count = seen.TryGetValue(runtimeId, out var n) ? n + 1 : 1;
        seen[runtimeId] = count;
        return count == 1 && runtimeId.Length > 0 ? runtimeId : runtimeId + "#" + count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The line text of one element, without number or indent.</summary>
    public static string Body(PcUiNode node, (int Total, int Shown)? list = null)
    {
        var text = new StringBuilder(string.IsNullOrWhiteSpace(node.Role) ? "element" : node.Role.Trim().ToLowerInvariant());
        var name = Clean(node.Name, NameCap);
        if (name.Length > 0) text.Append(' ').Append(PcWindowLine.Quote(name));
        if (ShortAutomationId(node.AutomationId) is { } id) text.Append(" #").Append(id);
        var states = States(node);
        if (states.Count > 0) text.Append(" {").Append(string.Join(", ", states)).Append('}');
        if (!node.Has(PcStates.Password) && Clean(node.Value, ValueCap) is { Length: > 0 } value && value != name)
            text.Append(" value=").Append(PcWindowLine.Quote(value));
        if (Clean(node.Shortcut, 40) is { Length: > 0 } shortcut) text.Append(" keys=").Append(PcWindowLine.Quote(shortcut));
        var actions = ActionNames(node);
        if (actions.Count > 0) text.Append(" actions: ").Append(string.Join(", ", actions));
        if (list is { } counts && counts.Total != counts.Shown)
            text.Append(" (").Append(counts.Total.ToString(CultureInfo.InvariantCulture)).Append(" items, ")
                .Append(counts.Shown.ToString(CultureInfo.InvariantCulture)).Append(" shown)");
        return text.ToString();
    }

    private static List<string> States(PcUiNode node)
    {
        var states = new List<string>();
        if (node.Has(PcStates.Focused)) states.Add("focused");
        if (node.Has(PcStates.Disabled)) states.Add("disabled");
        if (node.Has(PcStates.Selected)) states.Add("selected");
        if (node.Has(PcStates.Checked)) states.Add("checked");
        if (node.Has(PcStates.Mixed)) states.Add("mixed");
        if (node.Has(PcStates.Expanded)) states.Add("expanded");
        if (node.Has(PcStates.Collapsed)) states.Add("collapsed");
        if (node.Has(PcStates.Password)) states.Add("password");
        if (node.Has(PcStates.ReadOnly) && node.Can(PcActions.Value)) states.Add("readonly");
        if (node.Has(PcStates.Scrollable)) states.Add("scrollable");
        return states;
    }

    /// <summary>The secondary actions pc_action accepts for an element (a plain click is always there).</summary>
    public static List<string> ActionNames(PcUiNode node)
    {
        var actions = new List<string>();
        if (node.Can(PcActions.Toggle)) actions.Add("toggle");
        if (node.Can(PcActions.Select) && !node.Has(PcStates.Selected)) actions.Add("select");
        if (node.Can(PcActions.ExpandCollapse))
        {
            if (!node.Has(PcStates.Expanded)) actions.Add("expand");
            if (!node.Has(PcStates.Collapsed)) actions.Add("collapse");
        }
        if ((node.Can(PcActions.Value) || node.Can(PcActions.RangeValue)) && !node.Has(PcStates.ReadOnly) && !node.Has(PcStates.Password)) actions.Add("set_value");
        if (node.Can(PcActions.Scroll)) actions.Add("scroll");
        if (node.Can(PcActions.Text)) actions.Add("read");
        if (node.Can(PcActions.Window)) actions.AddRange(["minimize", "maximize", "restore", "close"]);
        return actions;
    }

    private static string? ShortAutomationId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var trimmed = id.Trim();
        if (trimmed.Length > 40 || trimmed.Any(char.IsWhiteSpace) || Guid.TryParse(trimmed.Trim('{', '}'), out _)) return null;
        if (trimmed.All(char.IsAsciiDigit)) return null;
        return trimmed;
    }

    /// <summary>One line of text, at most <paramref name="cap"/> characters (the cut ends with "...").</summary>
    public static string Clean(string? text, int cap)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var builder = new StringBuilder(Math.Min(text.Length, cap + 3));
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = builder.Length > 0;
                continue;
            }
            if (space) builder.Append(' ');
            space = false;
            builder.Append(c);
            if (builder.Length > cap) break;
        }
        if (builder.Length <= cap) return builder.ToString();
        // Never split a surrogate pair at the cut.
        var cut = cap > 0 && char.IsHighSurrogate(builder[cap - 1]) ? cap - 1 : cap;
        return builder.ToString(0, cut) + "...";
    }
}
