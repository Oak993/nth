using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-5 (DESIGN.md rev 5, 13.2; ED-NEW-dextra-as5-2): depth, concurrency and the free-memory gate. A mission is two
// levels (lead and heads); helpers are a child mission one level down; the default depth is 3 and the setting goes up
// to 4. Local CLI agents are capped at 16 and none starts under 15% free memory; API agents are not counted.
public sealed class MissionGateTests
{
    private static MemoryReading Free(double fraction) => new(1000, (ulong)(1000 * fraction));

    [Fact]
    public void A_top_mission_can_have_helpers_once_by_default_and_twice_at_the_ceiling()
    {
        var child = MissionGate.ChildLevel(MissionLimits.TopLevel);
        Assert.Equal(3, child);
        Assert.Null(MissionGate.OverDepth(child, MissionLimits.Default));
        Assert.Equal("Helpers here would run 4 levels deep; your limit is 3.", MissionGate.OverDepth(MissionGate.ChildLevel(child), MissionLimits.Default));
        var deep = new MissionLimits { Depth = 4 };
        Assert.Null(MissionGate.OverDepth(4, deep));
        Assert.NotNull(MissionGate.OverDepth(5, deep));
        Assert.False(MissionGate.BeyondCeiling(4));
        Assert.True(MissionGate.BeyondCeiling(5));
    }

    [Theory]
    [InlineData(-3, 2)]
    [InlineData(1, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(99, 4)]
    public void The_depth_setting_is_clamped_to_two_to_four(int setting, int effective) =>
        Assert.Equal(effective, new MissionLimits { Depth = setting }.EffectiveDepth);

    [Fact]
    public void The_seventeenth_local_agent_waits_and_an_api_agent_never_does()
    {
        var limits = MissionLimits.Default;
        Assert.True(MissionGate.Admit(AgentPlace.Local, 15, Free(0.5), limits).Admitted);
        var waits = MissionGate.Admit(AgentPlace.Local, 16, Free(0.5), limits);
        Assert.False(waits.Admitted);
        Assert.Equal("16 local agents are running; this one starts when one ends.", waits.Waiting);
        Assert.True(MissionGate.Admit(AgentPlace.Api, 400, null, limits).Admitted);
    }

    [Fact]
    public void No_new_local_agent_starts_under_fifteen_percent_free_memory()
    {
        var limits = MissionLimits.Default;
        Assert.True(MissionGate.Admit(AgentPlace.Local, 0, Free(0.15), limits).Admitted);
        var low = MissionGate.Admit(AgentPlace.Local, 0, Free(0.12), limits);
        Assert.False(low.Admitted);
        Assert.Equal("Free memory is under 15% (12% free); this agent starts when more is free.", low.Waiting);
        Assert.False(MissionGate.Admit(AgentPlace.Local, 0, null, limits).Admitted);
        Assert.False(MissionGate.Admit(AgentPlace.Local, 0, new MemoryReading(0, 0), limits).Admitted);
    }

    [Fact]
    public void The_owner_can_tighten_the_limits_but_not_loosen_them_past_the_defaults()
    {
        var loose = new MissionLimits { LocalAgents = 64, FreeRamFloor = 0.01 };
        Assert.Equal(16, loose.EffectiveLocalAgents);
        Assert.Equal(0.15, loose.EffectiveFreeRamFloor);
        Assert.False(MissionGate.Admit(AgentPlace.Local, 16, Free(0.9), loose).Admitted);
        Assert.False(MissionGate.Admit(AgentPlace.Local, 0, Free(0.10), loose).Admitted);
        var tight = new MissionLimits { LocalAgents = 2, FreeRamFloor = 0.4 };
        Assert.False(MissionGate.Admit(AgentPlace.Local, 2, Free(0.9), tight).Admitted);
        Assert.False(MissionGate.Admit(AgentPlace.Local, 0, Free(0.3), tight).Admitted);
        Assert.Equal(0.15, new MissionLimits { FreeRamFloor = double.NaN }.EffectiveFreeRamFloor);
    }
}
