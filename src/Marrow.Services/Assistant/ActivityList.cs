// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5 "Activity view"; unit AS-21). No Swift counterpart.
// ED-NEW-dextra-as21-4: the two ways the view lists rows.
namespace Marrow.Services.Assistant;

/// <summary>How the Activity view lists the rows that pass the filter.</summary>
public enum ActivityViewMode
{
    /// <summary>One line per action, newest first, under a header for each day.</summary>
    Log,

    /// <summary>Mission → agent → action, each action with the grant behind it; oldest first inside a mission.</summary>
    Lineage,
}

public enum ActivityListItemKind
{
    DayHeader,
    MissionHeader,
    AgentHeader,
    Row,
}

/// <summary>One line of the list: a day, a mission, an agent or an action. Every line is the same height on screen.</summary>
public sealed record ActivityListItem
{
    public ActivityListItemKind Kind { get; init; }

    /// <summary>The action, for a <see cref="ActivityListItemKind.Row"/>.</summary>
    public ActivityRow? Row { get; init; }

    /// <summary>The day of a day header.</summary>
    public DateOnly Day { get; init; }

    /// <summary>A header's identity: what a collapse remembers it by.</summary>
    public string Key { get; init; } = "";

    /// <summary>The mission or agent id a header stands for (empty: none).</summary>
    public string Id { get; init; } = "";

    /// <summary>The assistant of a mission header.</summary>
    public string Assistant { get; init; } = "";

    /// <summary>The mission of an agent header or of a row inside a lineage.</summary>
    public string Mission { get; init; } = "";

    public int Count { get; init; }

    public decimal CostUsd { get; init; }

    public long Tokens { get; init; }

    public DateTimeOffset First { get; init; }

    public DateTimeOffset Last { get; init; }

    public IReadOnlyList<string> Grants { get; init; } = [];

    /// <summary>How far in the line sits: 0 for a mission, 1 for an agent, 2 for an action in a lineage.</summary>
    public int Depth { get; init; }

    public bool Expanded { get; init; } = true;
}

/// <summary>Turns the rows that pass the filter into the lines the list shows.</summary>
public static class ActivityListBuilder
{
    public static string MissionKey(string mission) => "m:" + mission;

    public static string AgentKey(string mission, string agent) => "a:" + mission + "\u001f" + agent;

    /// <param name="rows">The rows that pass the filter, in chain order.</param>
    /// <param name="mode">How to list them.</param>
    /// <param name="collapsed">The keys of the headers the owner folded (lineage only).</param>
    public static IReadOnlyList<ActivityListItem> Build(IReadOnlyList<ActivityRow> rows, ActivityViewMode mode, IReadOnlySet<string>? collapsed = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return mode == ActivityViewMode.Lineage ? Lineage(rows, collapsed ?? new HashSet<string>()) : Log(rows);
    }

    private static List<ActivityListItem> Log(IReadOnlyList<ActivityRow> rows)
    {
        var items = new List<ActivityListItem>(rows.Count + 8);
        // Newest first. A day header stands over the first row of its day; its count is the day's rows that pass.
        var lastDay = (DateOnly?)null;
        var headerAt = -1;
        var count = 0;
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            var row = rows[i];
            if (lastDay != row.Day)
            {
                Seal(items, headerAt, count);
                lastDay = row.Day;
                headerAt = items.Count;
                count = 0;
                items.Add(new ActivityListItem { Kind = ActivityListItemKind.DayHeader, Day = row.Day, Key = "d:" + row.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) });
            }
            count++;
            items.Add(new ActivityListItem { Kind = ActivityListItemKind.Row, Row = row });
        }
        Seal(items, headerAt, count);
        return items;
    }

    private static void Seal(List<ActivityListItem> items, int headerAt, int count)
    {
        if (headerAt >= 0) items[headerAt] = items[headerAt] with { Count = count };
    }

    private static List<ActivityListItem> Lineage(IReadOnlyList<ActivityRow> rows, IReadOnlySet<string> collapsed)
    {
        var items = new List<ActivityListItem>(rows.Count + 16);
        foreach (var mission in ActivityLineage.Build(rows))
        {
            var missionKey = MissionKey(mission.Mission);
            var missionOpen = !collapsed.Contains(missionKey);
            items.Add(new ActivityListItem
            {
                Kind = ActivityListItemKind.MissionHeader,
                Key = missionKey,
                Id = mission.Mission,
                Assistant = mission.Assistant,
                Count = mission.Count,
                CostUsd = mission.CostUsd,
                Tokens = mission.Tokens,
                First = mission.First,
                Last = mission.Last,
                Grants = mission.Grants,
                Depth = 0,
                Expanded = missionOpen,
            });
            if (!missionOpen) continue;
            foreach (var agent in mission.Agents)
            {
                var agentKey = AgentKey(mission.Mission, agent.Agent);
                var agentOpen = !collapsed.Contains(agentKey);
                items.Add(new ActivityListItem
                {
                    Kind = ActivityListItemKind.AgentHeader,
                    Key = agentKey,
                    Id = agent.Agent,
                    Mission = mission.Mission,
                    Assistant = mission.Assistant,
                    Count = agent.Count,
                    CostUsd = agent.CostUsd,
                    Tokens = agent.Tokens,
                    Grants = agent.Grants,
                    Depth = 1,
                    Expanded = agentOpen,
                });
                if (!agentOpen) continue;
                foreach (var row in agent.Actions)
                    items.Add(new ActivityListItem { Kind = ActivityListItemKind.Row, Row = row, Mission = mission.Mission, Depth = 2 });
            }
        }
        return items;
    }
}
