// PORT-ONLY: first-release feature unit SK-1. The needs graph, for the confirmation that names and sizes what a download also needs (ONDEMAND-PLAN.md 2.4).
namespace Marrow.Core.Port.Skills.Catalogue;

public static class SkillsCatalogueGraph
{
    /// <summary>Every item the given item needs, directly or through others, without the item itself; safe on a graph that has a cycle.</summary>
    public static IReadOnlyList<string> Closure(SkillsCatalogue catalogue, string id)
    {
        var byId = catalogue.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        if (!byId.ContainsKey(id)) throw new ArgumentException("No catalogue item has the id '" + id + "'.", nameof(id));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(id);
        while (stack.Count > 0)
            foreach (var need in byId[stack.Pop()].Needs)
                if (byId.ContainsKey(need) && need != id && seen.Add(need)) stack.Push(need);
        return seen.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }
}
