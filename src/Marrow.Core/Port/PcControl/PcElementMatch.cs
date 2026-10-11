// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-7; DESIGN.md 2.12). Precise targeting. An element is known
// by a stable identity: its UIA RuntimeId together with a content fingerprint (control type, name, and for elements that
// are not edit fields their value), its AutomationId, and the path of its ancestors in the window. Just before Tactus acts,
// the identity is found again in the live tree: the same RuntimeId counts only while its content is the same (virtualised
// lists recycle containers); else one unique match on AutomationId, type and path; else one unique match on name, type and
// path; else one unique match after a fuzzy pass that ignores counts in brackets ("Inbox (12)" is "Inbox (13)"). None is
// gone, two or more is ambiguous. A locator from the model (name, type, id, within, nth) is matched the same careful way.
// Tactus never guesses.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.PcControl;

/// <summary>Who an element is, independent of its number in one observation.</summary>
public sealed record PcElementIdentity(string RuntimeId, string Role, string Name, string? AutomationId, IReadOnlyList<string> Path)
{
    /// <summary>The value the element showed, for elements whose value is part of what they are (not edit fields).</summary>
    public string? Content { get; init; }

    /// <summary>The element as Tactus last saw it.</summary>
    public PcUiNode? Seen { get; init; }

    /// <summary>"button "Save"" for messages.</summary>
    public string Describe() => (Role.Length > 0 ? Role : "element") + (Name.Length > 0 ? " " + PcWindowLine.Quote(TreeFormatter.Clean(Name, 60)) : "");
}

public enum PcMatchKind
{
    Found,
    Gone,
    Ambiguous,
}

/// <summary>The result of finding an element: its number in the tree, or why there is none.</summary>
public sealed record PcMatch(PcMatchKind Kind, int Index, IReadOnlyList<int> Candidates, string? Detail)
{
    public bool Found => Kind == PcMatchKind.Found;
    public static PcMatch At(int index) => new(PcMatchKind.Found, index, [index], null);
    public static PcMatch None(string detail) => new(PcMatchKind.Gone, 0, [], detail);
    public static PcMatch Many(IReadOnlyList<int> candidates, string detail) => new(PcMatchKind.Ambiguous, 0, candidates, detail);
}

public static partial class PcElementMatch
{
    /// <summary>The identity of element <paramref name="index"/> of <paramref name="tree"/>.</summary>
    public static PcElementIdentity Identity(PcTree tree, int index)
    {
        var node = tree.Elements[index];
        return new PcElementIdentity(node.RuntimeId, Norm(node.Role), Norm(node.Name), Blank(node.AutomationId), PathOf(tree, index))
        {
            Content = Content(node),
            Seen = node,
        };
    }

    /// <summary>
    /// Finds the element again in a fresh tree (DESIGN.md 2.12): the same RuntimeId while its content is the same; else one
    /// unique match by AutomationId, type and path; else by name, type and path; else by the fuzzy name pass. A RuntimeId
    /// whose content changed is never used: it may be a recycled container now showing another item.
    /// </summary>
    public static PcMatch Resolve(PcTree fresh, PcElementIdentity identity)
    {
        string? recycled = null;
        if (identity.RuntimeId.Length > 0 && fresh.IndexOf(identity.RuntimeId) is { } byRuntime)
        {
            var node = fresh.Elements[byRuntime];
            if (Same(node, identity)) return PcMatch.At(byRuntime);
            recycled = identity.Describe() + " is now " + Describe(node);
        }
        var sameType = fresh.Lines.Where(l => l.Index > 1 && Norm(fresh.Elements[l.Index].Role) == identity.Role).ToList();
        if (identity.AutomationId is { } automationId)
        {
            // The AutomationId names the control, not the item it shows, so the name must still fit (exactly or fuzzily).
            var byId = sameType.Where(l => Blank(fresh.Elements[l.Index].AutomationId) == automationId && PathOf(fresh, l.Index).SequenceEqual(identity.Path)
                && NameFits(fresh.Elements[l.Index], identity)).Select(l => l.Index).ToList();
            if (byId.Count == 1) return PcMatch.At(byId[0]);
            if (byId.Count > 1) return Ambiguous(byId, identity);
        }
        var byName = sameType.Where(l => Norm(fresh.Elements[l.Index].Name) == identity.Name && ContentFits(fresh.Elements[l.Index], identity)
            && PathOf(fresh, l.Index).SequenceEqual(identity.Path)).Select(l => l.Index).ToList();
        if (byName.Count == 1) return PcMatch.At(byName[0]);
        if (byName.Count > 1) return Ambiguous(byName, identity);
        var key = Fuzzy(identity.Name);
        if (key.Length > 0)
        {
            var fuzzy = sameType.Where(l => Fuzzy(fresh.Elements[l.Index].Name) == key && PathOf(fresh, l.Index).SequenceEqual(identity.Path))
                .Select(l => l.Index).ToList();
            if (fuzzy.Count == 1) return PcMatch.At(fuzzy[0]);
            if (fuzzy.Count > 1) return Ambiguous(fuzzy, identity);
        }
        return PcMatch.None(recycled ?? identity.Describe() + " is no longer in the window");
    }

    /// <summary>
    /// The element a locator names (DESIGN.md 2.12): exact name (case-insensitive, NFC), type and AutomationId where given,
    /// inside a container named <see cref="PcLocator.Within"/>, and the nth of equal matches when nth is given. When the
    /// exact name finds nothing, the fuzzy pass is tried and taken only when exactly one element fits. A label and a field
    /// sharing a name resolve to the one that can be acted on, when exactly one can.
    /// </summary>
    public static PcMatch Find(PcTree tree, PcLocator locator)
    {
        var what = Describe(locator);
        var pool = tree.Lines.Where(l => Fits(tree, l.Index, locator)).Select(l => l.Index).ToList();
        var name = locator.Name is { Length: > 0 } wanted ? Norm(wanted) : null;
        var found = name is null ? pool : pool.Where(i => string.Equals(Norm(tree.Elements[i].Name), name, StringComparison.OrdinalIgnoreCase)).ToList();
        var fuzzy = false;
        if (found.Count == 0 && name is not null && Fuzzy(name) is { Length: > 0 } key)
        {
            found = pool.Where(i => Fuzzy(tree.Elements[i].Name) == key).ToList();
            fuzzy = true;
        }
        if (found.Count > 1 && locator.Nth is null)
        {
            var actionable = found.Where(i => Actionable(tree.Elements[i]) && !tree.Elements[i].Has(PcStates.Disabled)).ToList();
            if (actionable.Count == 1) found = actionable;
        }
        if (locator.Nth is { } nth)
        {
            if (fuzzy && found.Count > 1) return PcMatch.Many(found, Count(found.Count) + " elements nearly match " + what + "; name one exactly");
            return nth >= 1 && nth <= found.Count
                ? PcMatch.At(found[nth - 1])
                : PcMatch.None("there are " + Count(found.Count) + " " + what + ", not " + Count(nth));
        }
        return found.Count switch
        {
            0 => PcMatch.None("no " + what + " in the window"),
            1 => PcMatch.At(found[0]),
            _ => PcMatch.Many(found, Count(found.Count) + " elements " + (fuzzy ? "nearly " : "") + "match " + what),
        };
    }

    /// <summary>
    /// The element under a screen point: the smallest element whose bounds hold it, preferring one that has an action; 0
    /// when only the window itself holds the point.
    /// </summary>
    public static int AtPoint(PcTree tree, PcPoint point)
    {
        var holding = tree.Lines.Where(l => l.Index > 1 && tree.Elements[l.Index].Bounds.Contains(point)).ToList();
        if (holding.Count == 0) return 0;
        static long Area(PcRect r) => (long)r.Width * r.Height;
        var actionable = holding.Where(l => Actionable(tree.Elements[l.Index])).ToList();
        var pool = actionable.Count > 0 ? actionable : holding;
        return pool.OrderBy(l => Area(tree.Elements[l.Index].Bounds)).ThenByDescending(l => l.Depth).First().Index;
    }

    /// <summary>The nearest element at or above <paramref name="index"/> that has <paramref name="action"/>; 0 when none.</summary>
    public static int Ancestor(PcTree tree, int index, PcActions action)
    {
        var byIndex = tree.Lines.ToDictionary(l => l.Index);
        for (var at = index; at > 0 && byIndex.ContainsKey(at); at = byIndex[at].ParentIndex)
        {
            if (tree.Elements[at].Can(action)) return at;
        }
        return 0;
    }

    /// <summary>Whether an element has an action beyond focus and scroll-into-view.</summary>
    public static bool Actionable(PcUiNode node) => (node.Actions & ~(PcActions.Focus | PcActions.ScrollIntoView)) != PcActions.None;

    /// <summary>An edit field: its value is what the user or Tactus types, not part of what the element is.</summary>
    public static bool Editable(PcUiNode node) => (node.Can(PcActions.Value) || node.Can(PcActions.Text) || node.Can(PcActions.RangeValue)) && !node.Has(PcStates.ReadOnly);

    /// <summary>One line of text, trimmed, for comparing names.</summary>
    public static string Norm(string? text) => TreeFormatter.Clean(text, 200).Normalize(NormalizationForm.FormC);

    /// <summary>
    /// The fuzzy name key (DESIGN.md 2.12, critique C28): counts in brackets removed, spaces collapsed, NFC, case-folded.
    /// "Inbox (12)" and "Inbox (13)" share a key; it is only ever used when exactly one element has it. Digits outside
    /// brackets stay: "Invoice 7" and "Invoice 9" are different items, never near matches.
    /// </summary>
    public static string Fuzzy(string? text)
    {
        var cleaned = Brackets().Replace(Norm(text), " ");
        return string.Join(' ', cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }

    [GeneratedRegex(@"[\(\[\{][^\)\]\}]*\d[^\)\]\}]*[\)\]\}]", RegexOptions.CultureInvariant)]
    private static partial Regex Brackets();

    /// <summary>
    /// Whether a live element is still the one an identity names: the same type, name and content (critique C25). Used on
    /// the element read by its RuntimeId just before acting; when it differs, the element is found again in a fresh tree.
    /// </summary>
    public static bool Matches(PcUiNode node, PcElementIdentity identity) => Same(node, identity);

    private static bool Same(PcUiNode node, PcElementIdentity identity) =>
        Norm(node.Role) == identity.Role && Norm(node.Name) == identity.Name && Content(node) == identity.Content;

    private static bool NameFits(PcUiNode node, PcElementIdentity identity) =>
        (Norm(node.Name) == identity.Name || (Fuzzy(node.Name) == Fuzzy(identity.Name) && Fuzzy(identity.Name).Length > 0)) && ContentFits(node, identity);

    private static bool ContentFits(PcUiNode node, PcElementIdentity identity) => identity.Content is null || Content(node) == identity.Content;

    private static string? Content(PcUiNode node) => Editable(node) ? null : TreeFormatter.Clean(node.Value, TreeFormatter.ValueCap) is { Length: > 0 } value ? value : null;

    private static PcMatch Ambiguous(IReadOnlyList<int> candidates, PcElementIdentity identity) =>
        PcMatch.Many(candidates, Count(candidates.Count) + " elements match " + identity.Describe());

    // The window itself (line 1) is left out of the path: its title changes as documents change. Each step is the type and
    // the fuzzy name (or the AutomationId when there is no name), so a count in a pane title does not break the path.
    private static List<string> PathOf(PcTree tree, int index)
    {
        var path = new List<string>();
        var byIndex = tree.Lines.ToDictionary(l => l.Index);
        if (!byIndex.TryGetValue(index, out var line)) return path;
        for (var at = line.ParentIndex; at > 1 && byIndex.ContainsKey(at); at = byIndex[at].ParentIndex)
        {
            var node = tree.Elements[at];
            path.Insert(0, Norm(node.Role) + ":" + (Fuzzy(node.Name) is { Length: > 0 } key ? key : Blank(node.AutomationId) ?? ""));
        }
        return path;
    }

    private static bool Fits(PcTree tree, int index, PcLocator locator)
    {
        var node = tree.Elements[index];
        if (locator.Type is { Length: > 0 } type && !string.Equals(Norm(node.Role), Norm(type), StringComparison.OrdinalIgnoreCase)) return false;
        if (locator.Id is { Length: > 0 } id && !string.Equals(Blank(node.AutomationId), id.Trim(), StringComparison.Ordinal)) return false;
        if (locator.Within is { Length: > 0 } within && !Inside(tree, index, within)) return false;
        return true;
    }

    // Inside a container whose name or AutomationId is the given text (exactly, case-insensitive) or holds it.
    private static bool Inside(PcTree tree, int index, string within)
    {
        var wanted = Norm(within);
        var byIndex = tree.Lines.ToDictionary(l => l.Index);
        for (var at = byIndex[index].ParentIndex; at > 0 && byIndex.ContainsKey(at); at = byIndex[at].ParentIndex)
        {
            var node = tree.Elements[at];
            if (Norm(node.Name).Contains(wanted, StringComparison.OrdinalIgnoreCase)) return true;
            if (Blank(node.AutomationId) is { } id && string.Equals(id, wanted, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string Describe(PcUiNode node) => (Norm(node.Role) is { Length: > 0 } role ? role : "element")
        + (Norm(node.Name) is { Length: > 0 } name ? " " + PcWindowLine.Quote(TreeFormatter.Clean(name, 60)) : "")
        + (Content(node) is { } content ? " showing " + PcWindowLine.Quote(TreeFormatter.Clean(content, 60)) : "");

    private static string Describe(PcLocator locator)
    {
        var text = new StringBuilder(locator.Type is { Length: > 0 } type ? Norm(type) : "element");
        if (locator.Name is { Length: > 0 } name) text.Append(' ').Append(PcWindowLine.Quote(TreeFormatter.Clean(name, 60)));
        if (locator.Id is { Length: > 0 } id) text.Append(" #").Append(id.Trim());
        if (locator.Within is { Length: > 0 } within) text.Append(" in ").Append(PcWindowLine.Quote(TreeFormatter.Clean(within, 60)));
        return text.ToString();
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Stable element numbers for one window: an element keeps its number across looks for as long as it exists, so a number
/// the model holds never silently points at another element after the tree changes. New elements get new numbers; the
/// window itself is always 1. An element without a RuntimeId of its own gets a new number at every look.
/// </summary>
public sealed class PcNumbering
{
    private readonly Dictionary<string, int> numbers = new(StringComparer.Ordinal);
    private int next = 2;

    public PcTree Apply(PcTree tree)
    {
        var map = new Dictionary<int, int>();
        foreach (var line in tree.Lines)
        {
            int number;
            if (line.ParentIndex == 0) number = 1;
            else if (line.RuntimeId.Contains('#', StringComparison.Ordinal)) number = next++;
            else if (!numbers.TryGetValue(line.RuntimeId, out number))
            {
                number = next++;
                numbers[line.RuntimeId] = number;
            }
            map[line.Index] = number;
        }
        var lines = tree.Lines.Select(l => l with { Index = map[l.Index], ParentIndex = l.ParentIndex == 0 ? 0 : map[l.ParentIndex] }).ToList();
        var elements = tree.Elements.ToDictionary(e => map[e.Key], e => e.Value);
        return new PcTree(lines, elements);
    }
}
