// PORT-ONLY: first-release feature unit SK-1. The lookup of ONDEMAND-PLAN.md 5.3, built from the catalogue's provides lists.
namespace Marrow.Core.Port.Skills.Catalogue;

/// <summary>
/// "The token goes through the catalogue's provides index to one item (for a module, the smallest set that provides it). No hit, no offer."
/// (ONDEMAND-PLAN.md 5.3). A program or message must have exactly one provider; a module may be provided by several sets and goes to the one with the
/// smallest plan size (an unknown size counts as the largest); two equally small candidates are a problem, never a guess (ED-NEW-f-sk-1). Program names
/// compare without regard to case (Windows), module names and messages exactly (Python and Node are case sensitive).
/// </summary>
public sealed class ProvidesIndex
{
    private readonly Dictionary<(ProvideKind Kind, string Name), CatalogueItem?> resolved;

    private ProvidesIndex(Dictionary<(ProvideKind, string), CatalogueItem?> resolved, IReadOnlyList<string> problems)
    {
        this.resolved = resolved;
        Problems = problems;
    }

    /// <summary>Names that cannot resolve to exactly one item, each with the candidates.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>The number of distinct names the catalogue provides (resolved or not).</summary>
    public int Count => resolved.Count;

    public CatalogueItem? Resolve(ProvideKind kind, string name) =>
        resolved.TryGetValue((kind, Normalize(kind, name)), out var item) ? item : null;

    private static string Normalize(ProvideKind kind, string name) => kind == ProvideKind.Exe ? name.ToLowerInvariant() : name;

    public static ProvidesIndex Build(SkillsCatalogue catalogue)
    {
        var candidates = new Dictionary<(ProvideKind, string), List<CatalogueItem>>();
        foreach (var item in catalogue.Items)
            foreach (var provide in item.Provides)
            {
                if (string.IsNullOrWhiteSpace(provide.Name)) continue;
                var key = (provide.Kind, Normalize(provide.Kind, provide.Name));
                if (!candidates.TryGetValue(key, out var list)) candidates[key] = list = [];
                if (!list.Contains(item)) list.Add(item);
            }

        var resolved = new Dictionary<(ProvideKind, string), CatalogueItem?>();
        var problems = new List<string>();
        foreach (var (key, list) in candidates)
        {
            if (list.Count == 1)
            {
                resolved[key] = list[0];
                continue;
            }
            resolved[key] = null;
            var ids = string.Join(", ", list.Select(i => i.Id));
            if (key.Item1 is ProvideKind.Exe or ProvideKind.Message)
            {
                problems.Add($"{key.Item1} '{key.Item2}' is provided by more than one item: {ids}");
                continue;
            }
            var smallest = list.Min(Size);
            var best = list.Where(i => Size(i) == smallest).ToList();
            if (best.Count == 1) resolved[key] = best[0];
            else problems.Add($"{key.Item1} '{key.Item2}' is provided by equally small sets: {string.Join(", ", best.Select(i => i.Id))}");
        }
        return new ProvidesIndex(resolved, problems);
    }

    private static double Size(CatalogueItem item) => item.ApproxSizeMb ?? double.PositiveInfinity;
}
