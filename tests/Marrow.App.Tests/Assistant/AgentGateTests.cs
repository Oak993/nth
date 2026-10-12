using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-5 (DESIGN.md rev 5, 13.2 "Concurrency"; ED-NEW-dextra-as5-2, -10): every local agent passes the gate before its
// chat starts. At most the owner's number run at once (16 by default), none starts under the free-memory floor or when
// memory cannot be read, and the ones that wait go first come first served, with the line the board shows. API agents
// pass at once. Near the floor it lets one agent in per look.
[Trait("Decision", "ED-NEW-dextra-as5-10")]
[Trait("Feature", "AS-5")]
public sealed class AgentGateTests
{
    private readonly ManualTimeProvider clock = new();

    private static MemoryReading Free(double fraction) => new(1000UL << 20, (ulong)((1000UL << 20) * fraction));

    [Fact]
    public void Sixteen_local_agents_run_at_once_and_the_seventeenth_waits_its_turn()
    {
        var memory = new FakeMemory(FakeMemory.Plenty);
        var gate = new AgentGate(() => MissionLimits.Default, memory, clock);
        var agents = Enumerable.Range(0, 18).Select(_ => Guid.NewGuid()).ToList();
        var entered = agents.Select(a => gate.Enter(a, AgentPlace.Local)).ToList();
        Assert.All(entered.Take(16), t => Assert.True(t.IsCompletedSuccessfully));
        Assert.False(entered[16].IsCompleted);
        Assert.False(entered[17].IsCompleted);
        Assert.Equal(16, gate.Running);
        Assert.Equal("16 local agents are running; this one starts when one ends.", gate.Waiting(agents[16]));

        gate.Leave(agents[3]);
        Assert.True(MissionRig.Within(entered[16], 5));
        Assert.False(entered[17].IsCompleted);
        Assert.True(gate.Holds(agents[16]));
        Assert.False(gate.Holds(agents[3]));
        Assert.Null(gate.Waiting(agents[16]));
    }

    [Fact]
    public void API_agents_are_not_counted()
    {
        var gate = new AgentGate(() => new MissionLimits { LocalAgents = 1 }, new FakeMemory(null), clock);
        Assert.True(gate.Enter(Guid.NewGuid(), AgentPlace.Api).IsCompletedSuccessfully);
        Assert.Equal(0, gate.Running);
    }

    [Fact]
    public void No_local_agent_starts_under_the_floor_or_when_memory_cannot_be_read()
    {
        var memory = new FakeMemory(Free(0.10));
        var gate = new AgentGate(() => MissionLimits.Default, memory, clock);
        var agent = Guid.NewGuid();
        var entering = gate.Enter(agent, AgentPlace.Local);
        Assert.False(entering.IsCompleted);
        Assert.Equal("Free memory is under 15% (10% free); this agent starts when more is free.", gate.Waiting(agent));
        Assert.Equal(1, clock.ActiveTimers);

        memory.Reading = null;
        clock.Advance(AgentGate.Recheck);
        Assert.False(entering.IsCompleted);
        Assert.Equal("Free memory could not be read; this agent starts once it can be.", gate.Waiting(agent));

        memory.Reading = Free(0.50);
        clock.Advance(AgentGate.Recheck);
        Assert.True(MissionRig.Within(entering, 5));
        Assert.Equal(0, clock.ActiveTimers);
    }

    [Fact]
    public void Close_to_the_floor_it_lets_one_agent_in_per_look()
    {
        var memory = new FakeMemory(Free(0.10));
        var gate = new AgentGate(() => MissionLimits.Default, memory, clock);
        var entered = Enumerable.Range(0, 3).Select(_ => gate.Enter(Guid.NewGuid(), AgentPlace.Local)).ToList();
        Assert.All(entered, t => Assert.False(t.IsCompleted));
        memory.Reading = Free(0.25); // above 15%, under twice the floor
        clock.Advance(AgentGate.Recheck);
        Assert.True(MissionRig.Within(entered[0], 5));
        Assert.False(entered[1].IsCompleted);
        clock.Advance(AgentGate.Recheck);
        Assert.True(MissionRig.Within(entered[1], 5));
        Assert.False(entered[2].IsCompleted);

        memory.Reading = Free(0.60);
        var more = Enumerable.Range(0, 3).Select(_ => gate.Enter(Guid.NewGuid(), AgentPlace.Local)).ToList();
        Assert.True(MissionRig.Within(entered[2], 5));
        Assert.All(more, t => Assert.True(MissionRig.Within(t, 5)));
    }

    [Fact]
    public void The_owners_lower_cap_holds_and_a_cancelled_wait_leaves_the_line()
    {
        var gate = new AgentGate(() => new MissionLimits { LocalAgents = 2 }, new FakeMemory(FakeMemory.Plenty), clock);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        Assert.True(gate.Enter(a, AgentPlace.Local).IsCompletedSuccessfully);
        Assert.True(gate.Enter(b, AgentPlace.Local).IsCompletedSuccessfully);
        Assert.True(gate.Enter(a, AgentPlace.Local).IsCompletedSuccessfully); // already in
        using var cancel = new CancellationTokenSource();
        var waiting = gate.Enter(c, AgentPlace.Local, cancel.Token);
        Assert.NotNull(gate.Waiting(c));
        cancel.Cancel();
        Assert.True(waiting.IsCanceled);
        Assert.Null(gate.Waiting(c));
        gate.Leave(a);
        Assert.Equal(1, gate.Running);
        Assert.False(gate.Holds(c));
    }

    [Fact]
    public void Leaving_while_waiting_cancels_the_wait()
    {
        var gate = new AgentGate(() => new MissionLimits { LocalAgents = 1 }, new FakeMemory(FakeMemory.Plenty), clock);
        Assert.True(gate.Enter(Guid.NewGuid(), AgentPlace.Local).IsCompletedSuccessfully);
        var queued = Guid.NewGuid();
        var waiting = gate.Enter(queued, AgentPlace.Local);
        gate.Leave(queued);
        Assert.True(SpinWait.SpinUntil(() => waiting.IsCanceled, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, gate.Running);
    }
}
