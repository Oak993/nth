using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: the Activity view's lineage (DESIGN.md 12.5: "Lineage runs mission → agent → action → grant"). Mission order is
// the mission that acted last first, with the actions of no mission last; inside a mission the agent that acted first comes
// first; every action keeps the grant or decision it names. ActivityDataset's six actions are the data.
[Trait("Decision", "ED-NEW-dextra-as21-4")]
[Trait("Feature", "AS-21")]
public sealed class ActivityLineageTests : IDisposable
{
    private readonly ActivityLogRig data = ActivityDataset.Make();

    public void Dispose() => data.Dispose();

    private IReadOnlyList<ActivityRow> Rows => ActivityLogReader.Load(data.Source).Rows;

    [Fact]
    public void Rows_group_by_mission_then_agent_in_the_order_things_happened()
    {
        var lineage = ActivityLineage.Build(Rows);

        Assert.Equal(["m-3", "m-2", "m-1", ""], lineage.Select(m => m.Mission));
        Assert.Equal(["dextra", "astur-shop", "dextra", "dextra"], lineage.Select(m => m.Assistant));
        Assert.Equal([["t-1"], ["t-3"], ["t-1", "t-2"], [""]], lineage.Select(m => m.Agents.Select(a => a.Agent).ToArray()));
        Assert.Equal([[6L], [3L, 4], [1L], [2L], [5L]], lineage.SelectMany(m => m.Agents).Select(a => a.Actions.Select(r => r.Sequence).ToArray()));
    }

    [Fact]
    public void Every_level_counts_its_actions_cost_tokens_time_and_the_grants_behind_them()
    {
        var lineage = ActivityLineage.Build(Rows);
        var second = lineage[1];
        var first = lineage[2];

        Assert.Equal(2, second.Count);
        Assert.Equal(0.07m, second.CostUsd);
        Assert.Equal(700L, second.Tokens);
        Assert.Equal(["decision:d-7"], second.Grants);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 8, 0, 30, TimeSpan.Zero), second.First);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 8, 1, 30, TimeSpan.Zero), second.Last);
        Assert.Equal(2, first.Count);
        Assert.Equal(0.03m, first.CostUsd);
        Assert.Equal(["grant:g-1"], first.Grants); // the second action named no grant
        Assert.Equal(["grant:g-1"], first.Agents[0].Grants);
        Assert.Empty(first.Agents[1].Grants);
        Assert.Equal(1, first.Agents[1].Count);
        Assert.Equal(0.02m, first.Agents[1].CostUsd);
        Assert.Equal(200L, first.Agents[1].Tokens);
    }

    [Fact]
    public void Lines_the_log_wrote_about_itself_and_lines_that_cannot_be_read_sit_with_the_actions_of_no_mission()
    {
        data.Close();
        File.AppendAllText(data.DayFile("2026-10-12"), "garbage\n");

        var lineage = ActivityLineage.Build(Rows);

        var none = lineage[^1];
        Assert.Equal("", none.Mission);
        Assert.Equal([5L, 0], none.Agents[0].Actions.Select(r => r.Sequence));
        Assert.Equal(ActivityRowKind.Unreadable, none.Agents[0].Actions[1].Kind);
    }

    [Fact]
    public void A_filter_before_the_lineage_leaves_only_what_it_passes()
    {
        var lineage = ActivityLineage.Build(Rows.Where(ActivityFilter.ForMission("m-1").Matches));

        var mission = Assert.Single(lineage);
        Assert.Equal("m-1", mission.Mission);
        Assert.Equal(["t-1", "t-2"], mission.Agents.Select(a => a.Agent));
        Assert.Empty(ActivityLineage.Build([]));
    }

    [Fact]
    public void Grants_are_listed_once_in_the_order_they_were_first_used()
    {
        var grants = ActivityLineage.GrantsOf(Rows.Where(r => r.Sequence is 6 or 3 or 1 or 4));

        Assert.Equal(["grant:g-1", "decision:d-7"], grants);
    }
}
