// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1; DESIGN.md 2.4). The short diff an action returns: elements added (+),
// removed (-) and changed (~) since the previous observation of the same window, matched by UIA RuntimeId. An element whose
// line is the same but whose place moved is reported as changed with "(moved)". Over the line cap there is no diff (null):
// the caller sends the full pruned tree instead.
using System.Globalization;
using System.Text;

namespace Marrow.Core.Port.PcControl;

public static class TreeDiff
{
    public const int DefaultMaxLines = 60;

    /// <summary>The diff text, "no visible change" when nothing changed, or null when it is longer than <paramref name="maxLines"/>.</summary>
    public static string? Compute(PcTree previous, PcTree current, string previousObs, int maxLines = DefaultMaxLines)
    {
        var before = previous.Lines.ToDictionary(l => l.RuntimeId, StringComparer.Ordinal);
        var after = current.Lines.ToDictionary(l => l.RuntimeId, StringComparer.Ordinal);
        var removed = previous.Lines.Where(l => !after.ContainsKey(l.RuntimeId)).ToList();
        var added = current.Lines.Where(l => !before.ContainsKey(l.RuntimeId)).Select(l => l.Index).ToHashSet();
        var changed = current.Lines.Where(l => before.TryGetValue(l.RuntimeId, out var old) && old.Body != l.Body).Select(l => l.Index).ToHashSet();
        var moved = Moved(previous, current, before);
        moved.ExceptWith(changed);
        var total = removed.Count + added.Count + changed.Count + moved.Count;
        if (total == 0) return "no visible change since " + previousObs;
        if (total > maxLines) return null;
        var text = new StringBuilder();
        text.Append("changes since ").Append(previousObs).Append(": ")
            .Append(added.Count.ToString(CultureInfo.InvariantCulture)).Append(" added, ")
            .Append(removed.Count.ToString(CultureInfo.InvariantCulture)).Append(" removed, ")
            .Append((changed.Count + moved.Count).ToString(CultureInfo.InvariantCulture)).Append(" changed");
        foreach (var line in removed) text.Append("\n- ").Append(new string(' ', 2 * line.Depth)).Append(line.Body);
        foreach (var line in current.Lines)
        {
            if (added.Contains(line.Index)) text.Append("\n+ ").Append(line.Render());
            else if (changed.Contains(line.Index)) text.Append("\n~ ").Append(line.Render());
            else if (moved.Contains(line.Index)) text.Append("\n~ ").Append(line.Render()).Append(" (moved)");
        }
        return text.ToString();
    }

    // Elements in both trees whose order changed: those outside the longest run kept in the same relative order (a longest
    // increasing subsequence of their old positions), plus those whose depth or parent changed.
    private static HashSet<int> Moved(PcTree previous, PcTree current, Dictionary<string, PcTreeLine> before)
    {
        var position = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < previous.Lines.Count; i++) position[previous.Lines[i].RuntimeId] = i;
        var common = current.Lines.Where(l => before.ContainsKey(l.RuntimeId)).ToList();
        var sequence = common.Select(l => position[l.RuntimeId]).ToArray();
        var keep = LongestIncreasing(sequence);
        var moved = new HashSet<int>();
        var previousParent = previous.Lines.ToDictionary(l => l.Index, l => l.RuntimeId);
        var currentParent = current.Lines.ToDictionary(l => l.Index, l => l.RuntimeId);
        for (var i = 0; i < common.Count; i++)
        {
            var now = common[i];
            var old = before[now.RuntimeId];
            var oldParent = old.ParentIndex == 0 ? "" : previousParent[old.ParentIndex];
            var newParent = now.ParentIndex == 0 ? "" : currentParent[now.ParentIndex];
            if (!keep.Contains(i) || old.Depth != now.Depth || oldParent != newParent) moved.Add(now.Index);
        }
        return moved;
    }

    private static HashSet<int> LongestIncreasing(int[] values)
    {
        var tails = new List<int>();
        var tailIndex = new List<int>();
        var parent = new int[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var lo = 0;
            var hi = tails.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (tails[mid] < values[i]) lo = mid + 1;
                else hi = mid;
            }
            parent[i] = lo > 0 ? tailIndex[lo - 1] : -1;
            if (lo == tails.Count)
            {
                tails.Add(values[i]);
                tailIndex.Add(i);
            }
            else
            {
                tails[lo] = values[i];
                tailIndex[lo] = i;
            }
        }
        var kept = new HashSet<int>();
        for (var at = tailIndex.Count > 0 ? tailIndex[^1] : -1; at >= 0; at = parent[at]) kept.Add(at);
        return kept;
    }
}
