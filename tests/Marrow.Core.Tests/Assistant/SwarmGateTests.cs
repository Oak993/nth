using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2 "Swarm gate", 11.4; ED-NEW-dextra-as15-1): before more than 8 agents, or any cloud or API fan-out, the
// plan card shows the agents, the estimated time, the estimated tokens or dollars and which accounts pay, and the owner must tap Start.
[Trait("Feature", "AS-15")]
[Trait("Decision", "ED-NEW-dextra-as15-1")]
public sealed class SwarmGateTests
{
    private static ReservePlan Plan(int agents, params SwarmLane[] lanes) => new([])
    {
        Agents = agents,
        Lanes = lanes,
    };

    private static SwarmLane Local(int agents, string name = "Claude 20x") => new("claude:20x", name, SwarmPlace.Local, agents);

    private static SwarmLane Api(int agents, string name = "OpenRouter") => new("openrouter:key", name, SwarmPlace.Api, agents);

    private static SwarmLane Cloud(int agents, string name = "Devin") => new("devin:cloud", name, SwarmPlace.Cloud, agents);

    [Fact]
    public void Eight_agents_start_on_their_own_and_a_ninth_needs_the_owners_tap()
    {
        var eight = Plan(8, Local(8));
        Assert.Empty(eight.StartNeeds);
        Assert.False(eight.NeedsStartTap);
        Assert.Null(eight.Swarm);

        var nine = Plan(9, Local(9));
        Assert.Equal(["9 agents planned"], nine.StartNeeds);
        Assert.True(nine.NeedsStartTap);
        Assert.NotNull(nine.Swarm);
    }

    [Fact]
    public void Any_cloud_or_api_fan_out_needs_the_tap_however_few_agents_there_are()
    {
        Assert.Empty(Plan(1, Api(1)).StartNeeds);                                  // one paid agent is not a fan-out
        Assert.Empty(Plan(1, Cloud(1)).StartNeeds);
        Assert.Equal(["2 cloud or API agents at once"], Plan(2, Api(2)).StartNeeds);
        Assert.Equal(["2 cloud or API agents at once"], Plan(2, Api(1), Cloud(1)).StartNeeds);
        Assert.Equal(["3 cloud or API agents at once"], Plan(5, Local(2), Api(3)).StartNeeds);
        Assert.Empty(Plan(8, Local(8)).StartNeeds);                                // local agents cost no money: no fan-out
        Assert.True(Plan(2, Api(2)).NeedsStartTap);
        Assert.NotNull(Plan(2, Api(2)).Swarm);
    }

    [Fact]
    public void A_plan_over_eight_agents_that_also_fans_out_names_both_reasons_in_the_designs_order()
    {
        var plan = Plan(12, Local(8), Api(4));
        Assert.Equal(["12 agents planned", "4 cloud or API agents at once"], plan.StartNeeds);
        Assert.EndsWith("start needs you (12 agents planned; 4 cloud or API agents at once)", plan.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_that_does_not_say_where_its_agents_run_claims_no_fan_out()
    {
        Assert.Empty(Plan(4).StartNeeds);
        Assert.Equal(["9 agents planned"], Plan(9).StartNeeds);
    }

    [Fact]
    public void The_card_shows_the_agents_the_time_the_tokens_and_the_dollars_and_who_pays()
    {
        var plan = new ReservePlan([
            new PlanAccountLine("claude:20x", "Claude 20x", ReserveWindowKind.FiveHour, "", 40, 4, 6, 70, false),
            new PlanAccountLine("codex:a", "Codex A", ReserveWindowKind.Weekly, "", 20, 3, 4, null, false),
        ])
        {
            Agents = 12,
            Lanes = [Local(8), Api(4)],
            Duration = TimeSpan.FromMinutes(25),
            TokensP50 = 1_200_000,
            TokensP90 = 2_400_000,
            ApiDollars = 3.5m,
        };
        var facts = Assert.IsType<SwarmFacts>(plan.Swarm);
        Assert.Equal("12 agents: 8 on this PC, 4 on cloud or API accounts", facts.Agents);
        Assert.Equal("about 25 min", facts.Time);
        Assert.Equal("about 1.2M tokens (up to 2.4M) · about $3.5 of API spend", facts.Cost);
        Assert.Equal("Claude 20x, Codex A and OpenRouter", facts.PaidBy);
        Assert.Equal([("Agents", facts.Agents), ("Time", facts.Time), ("Estimated cost", facts.Cost), ("Paid by", facts.PaidBy)], facts.Rows);
    }

    [Fact]
    public void A_plan_with_no_estimate_says_so_and_never_invents_a_number()
    {
        var facts = Assert.IsType<SwarmFacts>(Plan(9).Swarm);
        Assert.Equal("9 agents", facts.Agents);
        Assert.Equal("not estimated", facts.Time);
        Assert.Equal("not estimated", facts.Cost);
        Assert.Equal("no account named yet", facts.PaidBy);
        var dollars = Assert.IsType<SwarmFacts>(new ReservePlan([]) { Agents = 9, ApiDollars = 0.25m }.Swarm);
        Assert.Equal("about $0.25 of API spend", dollars.Cost);
    }

    [Fact]
    public void Tokens_read_as_k_and_M()
    {
        Assert.Equal("0", SwarmGate.Tokens(-5));
        Assert.Equal("999", SwarmGate.Tokens(999));
        Assert.Equal("1k", SwarmGate.Tokens(1_000));
        Assert.Equal("48k", SwarmGate.Tokens(48_000));
        Assert.Equal("1M", SwarmGate.Tokens(999_600));
        Assert.Equal("1.2M", SwarmGate.Tokens(1_200_000));
        Assert.Equal("2M", SwarmGate.Tokens(2_000_000));
    }

    [Fact]
    public void Providers_are_placed_on_this_pc_on_an_api_key_or_in_the_providers_cloud()
    {
        foreach (var kind in new[] { ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.Zai, ProviderKind.OpenRouter, ProviderKind.Opencode })
            Assert.Equal(SwarmPlace.Api, SwarmPlaces.Of(kind));
        Assert.Equal(SwarmPlace.Cloud, SwarmPlaces.Of(ProviderKind.Devin));
        foreach (var kind in new[] { ProviderKind.Claude, ProviderKind.Codex, ProviderKind.Cursor, ProviderKind.Grok, ProviderKind.Pi, ProviderKind.Local })
            Assert.Equal(SwarmPlace.Local, SwarmPlaces.Of(kind));
    }

    [Fact]
    public void The_gate_counts_only_agents_that_are_not_on_this_pc()
    {
        Assert.Equal(0, SwarmGate.PaidAgents([Local(16)]));
        Assert.Equal(5, SwarmGate.PaidAgents([Local(3), Api(2), Cloud(3)]));
        Assert.Equal(0, SwarmGate.PaidAgents([Api(-4)]));          // a negative count is nobody
        Assert.Equal(8, SwarmGate.AgentsWithoutTap);
        Assert.Equal(2, SwarmGate.FanOutAgents);
    }
}
