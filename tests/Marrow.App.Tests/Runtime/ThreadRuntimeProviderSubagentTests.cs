using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// Provider-run (native) heads driven by the lead session's agent events: ThreadRuntime.swift:2240-2251, 2151-2153,
// 2438-2443 and 2578-2677. The events go through the runtime's real handle path (Rehearse).
public sealed class ThreadRuntimeProviderSubagentTests
{
    private sealed record Fixture(AppModelHarness Harness, AppModel Model, ChatThread Lead, ThreadRuntime Runtime) : IDisposable
    {
        public void Dispose() => Harness.Dispose();
        public ChatThread[] Heads() => Harness.Main.OnMain(() => Model.HydraHeads(Lead.Id).ToArray());
        public void Send(ProviderEvent value) => Harness.Main.OnMain(() => Runtime.Rehearse(value));
    }

    private static Fixture Create(bool hydraOn = true)
    {
        var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude, HydraEnabled = hydraOn };
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = false);
        h.PrepareBootstrap(model);
        var runtime = h.Main.OnMain(() => model.Runtime(lead.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return new Fixture(h, model, lead, runtime);
    }

    private static AgentSpawn Spawn(string id, string description, string? toolUseID = null, bool background = false) => new()
    {
        Id = id,
        ToolUseID = toolUseID,
        Description = description,
        Prompt = "brief",
        IsBackground = background,
    };

    [Fact]
    [Trait("Req", "PR-RUNTIME-104")] // :2578-2603 known spawn ids update, "Subagent" is unnamed, new ids spawn once
    public void An_announced_agent_spawns_one_head_and_a_repeat_announcement_only_renames_a_named_one()
    {
        using var f = Create();
        f.Send(new ProviderEvent.AgentStarted(Spawn("agent-1", "Read the packet")));
        var head = Assert.Single(f.Heads());
        Assert.Equal("Read the packet", head.Hydra!.Task);

        f.Send(new ProviderEvent.AgentStarted(Spawn("agent-1", "Subagent")));
        Assert.Equal("Read the packet", Assert.Single(f.Heads()).Hydra!.Task);

        f.Send(new ProviderEvent.AgentStarted(Spawn("agent-1", "Write the report")));
        var renamed = Assert.Single(f.Heads());
        Assert.Equal(head.Id, renamed.Id);
        Assert.Equal("Write the report", renamed.Hydra!.Task);
        Assert.EndsWith("Write the report", renamed.Title, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-104")] // :2580 guard app.hydraIsOn(thread)
    public void With_Hydra_off_an_announced_agent_creates_nothing()
    {
        using var f = Create(hydraOn: false);
        f.Send(new ProviderEvent.AgentStarted(Spawn("agent-1", "Read the packet")));
        Assert.Empty(f.Heads());
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-107")] // :2646-2652 finishHydraHead with the provider's status and summary
    public void A_finished_agent_finishes_its_head_with_the_provider_status_and_summary()
    {
        using var f = Create();
        f.Send(new ProviderEvent.AgentStarted(Spawn("agent-1", "Read the packet")));
        f.Send(new ProviderEvent.AgentFinished("agent-1", TurnStatus.Completed, "All gaps listed."));
        var head = Assert.Single(f.Heads());
        Assert.Equal(HydraHeadInfo.Status.Completed, head.Hydra!.HydraHeadInfoStatus);
        Assert.Equal("All gaps listed.", head.Hydra.Summary);
    }

    [Theory]
    [Trait("Req", "PR-RUNTIME-108")] // :2151-2153, :2657-2667 completed -> completed, declined -> interrupted, else failed
    [InlineData(ToolCall.Status.Completed, HydraHeadInfo.Status.Completed)]
    [InlineData(ToolCall.Status.Declined, HydraHeadInfo.Status.Stopped)]
    [InlineData(ToolCall.Status.Failed, HydraHeadInfo.Status.Failed)]
    public void A_foreground_heads_returning_tool_call_finishes_it_with_the_output_as_summary(ToolCall.Status tool, HydraHeadInfo.Status expected)
    {
        using var f = Create();
        f.Send(new ProviderEvent.AgentStarted(Spawn("agent-1", "Read the packet", toolUseID: "tool-1")));
        f.Send(new ProviderEvent.ToolStarted("tool-1", new ToolCall { ToolCallKind = ToolCall.Kind.Agent, Title = "Read the packet" }));
        f.Send(new ProviderEvent.ToolUpdated("tool-1", new ToolUpdate { Status = tool, Output = "report text" }));
        var head = Assert.Single(f.Heads());
        Assert.Equal(expected, head.Hydra!.HydraHeadInfoStatus);
        Assert.Equal("report text", head.Hydra.Summary);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-108")] // :2660 !info.isBackground: a background head's call returns at once and says nothing
    public void A_background_heads_returning_tool_call_leaves_it_running()
    {
        using var f = Create();
        f.Send(new ProviderEvent.AgentStarted(Spawn("agent-1", "Watch the build", toolUseID: "tool-1", background: true)));
        f.Send(new ProviderEvent.ToolStarted("tool-1", new ToolCall { ToolCallKind = ToolCall.Kind.Agent, Title = "Watch the build" }));
        f.Send(new ProviderEvent.ToolUpdated("tool-1", new ToolUpdate { Status = ToolCall.Status.Completed, Output = "started" }));
        Assert.Equal(HydraHeadInfo.Status.Running, Assert.Single(f.Heads()).Hydra!.HydraHeadInfoStatus);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-122")] // :2438-2443, :2671-2677 foreground heads end with the lead's turn, background ones do not
    public void The_leads_finished_turn_settles_foreground_heads_and_leaves_background_heads_running()
    {
        using var f = Create();
        f.Harness.Main.OnMain(() => f.Runtime.RehearseTurn("lead turn"));
        f.Send(new ProviderEvent.TurnStarted("provider-turn"));
        f.Send(new ProviderEvent.AgentStarted(Spawn("fore", "Foreground task")));
        f.Send(new ProviderEvent.AgentStarted(Spawn("back", "Background task", background: true)));
        f.Send(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        f.Harness.Main.Loop.RunUntil(() => f.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        var heads = f.Heads();
        Assert.Equal(HydraHeadInfo.Status.Completed, heads.Single(h => h.Hydra!.Task == "Foreground task").Hydra!.HydraHeadInfoStatus);
        Assert.Equal(HydraHeadInfo.Status.Running, heads.Single(h => h.Hydra!.Task == "Background task").Hydra!.HydraHeadInfoStatus);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-105")] // :2609-2612 a head heard from before its announcement is recovered from its agent tool row
    public void An_agent_heard_from_before_its_announcement_is_adopted_from_its_tool_row()
    {
        using var f = Create();
        f.Send(new ProviderEvent.ToolStarted("agent-x", new ToolCall { ToolCallKind = ToolCall.Kind.Agent, Title = "Survey the callers" }));
        Assert.Empty(f.Heads());
        f.Send(new ProviderEvent.AgentEvent("agent-x", new ProviderEvent.MessageDelta("m1", "working")));
        var head = Assert.Single(f.Heads());
        Assert.Equal("Survey the callers", head.Hydra!.Task);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-105")] // :2639-2640 a nested title is one of the six dropped cases
    public void A_nested_title_event_does_not_retitle_the_head()
    {
        using var f = Create();
        f.Send(new ProviderEvent.AgentStarted(Spawn("agent-1", "Read the packet")));
        var title = Assert.Single(f.Heads()).Title;
        f.Send(new ProviderEvent.AgentEvent("agent-1", new ProviderEvent.Title("Something else")));
        Assert.Equal(title, Assert.Single(f.Heads()).Title);
    }
}
