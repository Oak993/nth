using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: the lines the Activity view lists (DESIGN.md 12.5; ED-NEW-dextra-as21-4). The log lists newest first under a
// header for each day; the lineage lists mission, agent, action, oldest first inside a mission, and a folded header hides
// what is under it. ActivityDataset's six actions are the data.
[Trait("Decision", "ED-NEW-dextra-as21-4")]
[Trait("Feature", "AS-21")]
public sealed class ActivityListBuilderTests : IDisposable
{
    private readonly ActivityLogRig data = ActivityDataset.Make();

    public void Dispose() => data.Dispose();

    private IReadOnlyList<ActivityRow> Rows => ActivityLogReader.Load(data.Source).Rows;

    private static string Shape(IEnumerable<ActivityListItem> items) => string.Join(" | ", items.Select(i => i.Kind switch
    {
        ActivityListItemKind.DayHeader => $"day {i.Day:yyyy-MM-dd} x{i.Count}",
        ActivityListItemKind.MissionHeader => $"mission '{i.Id}' x{i.Count}{(i.Expanded ? "" : " folded")}",
        ActivityListItemKind.AgentHeader => $"agent '{i.Id}' x{i.Count}{(i.Expanded ? "" : " folded")}",
        _ => $"#{i.Row!.Sequence}@{i.Depth}",
    }));

    [Fact]
    public void The_log_lists_newest_first_under_a_header_for_each_day()
    {
        var items = ActivityListBuilder.Build(Rows, ActivityViewMode.Log);

        Assert.Equal("day 2026-10-12 x4 | #6@0 | #5@0 | #4@0 | #3@0 | day 2026-10-11 x2 | #2@0 | #1@0", Shape(items));
    }

    [Fact]
    public void The_log_counts_a_days_rows_that_pass_the_filter()
    {
        var passed = Rows.Where(new ActivityFilter { Assistant = "dextra" }.Matches).ToList();

        var items = ActivityListBuilder.Build(passed, ActivityViewMode.Log);

        Assert.Equal("day 2026-10-12 x2 | #6@0 | #5@0 | day 2026-10-11 x2 | #2@0 | #1@0", Shape(items));
        Assert.Empty(ActivityListBuilder.Build([], ActivityViewMode.Log));
        Assert.Empty(ActivityListBuilder.Build([], ActivityViewMode.Lineage));
    }

    [Fact]
    public void The_lineage_lists_mission_then_agent_then_action_with_depths()
    {
        var items = ActivityListBuilder.Build(Rows, ActivityViewMode.Lineage);

        Assert.Equal("mission 'm-3' x1 | agent 't-1' x1 | #6@2 | mission 'm-2' x2 | agent 't-3' x2 | #3@2 | #4@2"
            + " | mission 'm-1' x2 | agent 't-1' x1 | #1@2 | agent 't-2' x1 | #2@2 | mission '' x1 | agent '' x1 | #5@2", Shape(items));
        Assert.Equal([0, 1, 2, 0, 1, 2, 2, 0, 1, 2, 1, 2, 0, 1, 2], items.Select(i => i.Depth));
        var header = items[3];
        Assert.Equal("astur-shop", header.Assistant);
        Assert.Equal(0.07m, header.CostUsd);
        Assert.Equal(700L, header.Tokens);
        Assert.Equal(["decision:d-7"], header.Grants);
        Assert.Equal("m-2", items[4].Mission);
        Assert.Equal("m-2", items[5].Mission);
    }

    [Fact]
    public void A_folded_mission_hides_its_agents_and_a_folded_agent_hides_its_actions()
    {
        var folded = new HashSet<string> { ActivityListBuilder.MissionKey("m-2"), ActivityListBuilder.AgentKey("m-1", "t-1") };

        var items = ActivityListBuilder.Build(Rows, ActivityViewMode.Lineage, folded);

        Assert.Equal("mission 'm-3' x1 | agent 't-1' x1 | #6@2 | mission 'm-2' x2 folded"
            + " | mission 'm-1' x2 | agent 't-1' x1 folded | agent 't-2' x1 | #2@2 | mission '' x1 | agent '' x1 | #5@2", Shape(items));
    }

    [Fact]
    public void A_header_key_names_the_header_and_nothing_else()
    {
        Assert.Equal("m:m-1", ActivityListBuilder.MissionKey("m-1"));
        Assert.NotEqual(ActivityListBuilder.AgentKey("m-1", "t-1"), ActivityListBuilder.AgentKey("m-1", "t-2"));
        Assert.NotEqual(ActivityListBuilder.AgentKey("m-1", "t-1"), ActivityListBuilder.AgentKey("m-2", "t-1"));
        Assert.NotEqual(ActivityListBuilder.AgentKey("a", "bc"), ActivityListBuilder.AgentKey("ab", "c"));
        Assert.Equal("d:2026-10-12", ActivityListBuilder.Build(Rows, ActivityViewMode.Log)[0].Key);
    }
}
