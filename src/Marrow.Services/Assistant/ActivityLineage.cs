// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5 "Activity view"; unit AS-21). No Swift counterpart.
// ED-NEW-dextra-as21-4: lineage.
namespace Marrow.Services.Assistant;

/// <summary>One agent's actions inside a mission, oldest first. <see cref="Agent"/> is empty for actions no agent took.</summary>
public sealed record ActivityLineageAgent(string Agent, IReadOnlyList<ActivityRow> Actions)
{
    public int Count => Actions.Count;

    public decimal CostUsd => Actions.Sum(a => a.CostUsd ?? 0m);

    public long Tokens => Actions.Sum(a => a.Tokens ?? 0L);

    /// <summary>The grants and decisions these actions name, in the order they were first used.</summary>
    public IReadOnlyList<string> Grants => ActivityLineage.GrantsOf(Actions);
}

/// <summary>One mission's lineage: its agents, and under each agent its actions and the grant behind each. <see cref="Mission"/> is empty for the actions that belong to no mission.</summary>
public sealed record ActivityLineageMission(string Mission, string Assistant, IReadOnlyList<ActivityLineageAgent> Agents)
{
    public int Count => Agents.Sum(a => a.Count);

    public decimal CostUsd => Agents.Sum(a => a.CostUsd);

    public long Tokens => Agents.Sum(a => a.Tokens);

    public IReadOnlyList<string> Grants => ActivityLineage.GrantsOf(Agents.SelectMany(a => a.Actions));

    public DateTimeOffset First => Agents.SelectMany(a => a.Actions).Where(r => r.Kind != ActivityRowKind.Unreadable).Select(r => r.Time).DefaultIfEmpty().Min();

    public DateTimeOffset Last => Agents.SelectMany(a => a.Actions).Where(r => r.Kind != ActivityRowKind.Unreadable).Select(r => r.Time).DefaultIfEmpty().Max();
}

/// <summary>
/// The lineage of a set of rows (12.5: "Lineage runs mission → agent → action → grant"): rows grouped by mission, then by
/// agent, with every action in chain order and the grant or decision id each one names. The mission that acted last comes
/// first; the actions that belong to no mission come last. Within a mission the agent that acted first comes first (the
/// lead). Lines the log wrote about itself and lines that cannot be read have no mission, so they sit with the actions of
/// no mission, and the first failing line is in the lineage too.
/// </summary>
public static class ActivityLineage
{
    public static IReadOnlyList<ActivityLineageMission> Build(IEnumerable<ActivityRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var missions = new Dictionary<string, MissionBucket>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = row.Mission ?? "";
            if (!missions.TryGetValue(key, out var mission)) missions[key] = mission = new MissionBucket(row.Assistant);
            mission.Add(row);
        }
        return missions
            .OrderBy(m => m.Key.Length == 0 ? 1 : 0)
            .ThenByDescending(m => m.Value.Last)
            .ThenBy(m => m.Key, StringComparer.Ordinal)
            .Select(m => new ActivityLineageMission(m.Key, m.Value.Assistant,
                m.Value.Agents.Select(a => new ActivityLineageAgent(a.Key, a.Value)).ToList()))
            .ToList();
    }

    /// <summary>The distinct grant and decision ids the rows name, in the order they were first used.</summary>
    public static IReadOnlyList<string> GrantsOf(IEnumerable<ActivityRow> rows)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var grants = new List<string>();
        foreach (var row in rows)
            if (row.AllowedBy is { Length: > 0 } grant && seen.Add(grant)) grants.Add(grant);
        return grants;
    }

    private sealed class MissionBucket(string assistant)
    {
        // Dictionary keeps insertion order while nothing is removed: the agent that acted first comes first.
        public Dictionary<string, List<ActivityRow>> Agents { get; } = new(StringComparer.Ordinal);

        public string Assistant { get; } = assistant;

        public long Last { get; private set; }

        public void Add(ActivityRow row)
        {
            var key = row.Agent ?? "";
            if (!Agents.TryGetValue(key, out var list)) Agents[key] = list = [];
            list.Add(row);
            Last = Math.Max(Last, row.Sequence);
        }
    }
}
