// PORT-ONLY: first-release feature unit SK-7. What uses an item, from SK-1's needs graph (ONDEMAND-PLAN.md 2.4 and 3.8).
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Port.Skills.State;

public static class ItemDependents
{
    private static void Require(SkillsCatalogue catalogue, string id)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        if (!catalogue.Items.Any(i => i.Id == id)) throw new ArgumentException($"No catalogue item has the id '{id}'.", nameof(id));
    }

    /// <summary>The items whose <c>needs</c> name this item, sorted by id.</summary>
    public static IReadOnlyList<string> Direct(SkillsCatalogue catalogue, string id)
    {
        Require(catalogue, id);
        return catalogue.Items.Where(i => i.Needs.Contains(id, StringComparer.Ordinal)).Select(i => i.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
    }

    /// <summary>The items that need this one directly or through others, each once, never the item itself; safe on a graph with a cycle.</summary>
    public static IReadOnlyList<string> Transitive(SkillsCatalogue catalogue, string id)
    {
        Require(catalogue, id);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(id);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var dependent in catalogue.Items.Where(i => i.Needs.Contains(current, StringComparer.Ordinal)).Select(i => i.Id))
                if (dependent != id && seen.Add(dependent)) queue.Enqueue(dependent);
        }
        return seen.OrderBy(i => i, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The transitive dependents that are installed: they have an installed version (an update, a repair or a failed update still counts) and are not being removed.
    /// This is the list 3.8's removal confirmation names (ENGINEERING DECISION, ED-295).
    /// </summary>
    public static IReadOnlyList<string> Installed(SkillsCatalogue catalogue, AddonsState state, string id)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Transitive(catalogue, id).Where(d => state.Get(d) is { InstalledPin: not null, State: not ItemState.Removing }).ToList();
    }
}
