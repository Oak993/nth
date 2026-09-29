using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Runtime;

// The native-head half of ThreadRuntime (App/Runtime/ThreadRuntime.swift:2578-2690): hydraAgentStarted,
// hydraAgentEvent, hydraAgentFinished and hydraToolFinished. Before Job STUBS the port's four methods had empty
// bodies ("Blocked until the owned AppModel port exposes the native-Hydra operations") and the ToolUpdated arm held
// an empty block, so a Claude, Codex or Copilot lead's sub-agents never became Hydra heads. AppModel.SpawnNativeHead,
// FinishHydraHead and the runtime's Rehearse* seams all exist now. Each test drives the LEAD's ThreadRuntime through
// Rehearse(ProviderEvent), the entry a live session's events also reach (PR-RUNTIME-078), and reads the heads back
// from the AppModel.
public sealed class ThreadRuntimeNativeHeadTests
{
    private static ThreadRuntimeNativeHeadRig MakeRig(bool hydraOn = true, Func<Project, ChatThread, IEnumerable<ChatThread>>? extraThreads = null)
    {
        var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Project", harness.NewFolder("project"));
        var lead = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude, HydraEnabled = true };
        harness.WriteLibrary([project], [lead, .. extraThreads?.Invoke(project, lead) ?? []]);
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = hydraOn;
            model.Settings.HydraIsolateHeads = false;
        });
        harness.PrepareBootstrap(model);
        var runtime = harness.Main.OnMain(() => model.Runtime(lead.Id));
        harness.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return new ThreadRuntimeNativeHeadRig(harness, model, runtime, lead);
    }

    // U+00B7 MIDDLE DOT, the separator in a native head's title (:2586); built from its code point, R-STR-9.
    private static readonly string MiddleDot = ((char)0xB7).ToString();

    private static AgentSpawn Spawn(string id, string description = "Inspect the parser", string? toolUseID = "tool-use-1",
        string? prompt = null, bool background = false) =>
        new() { Id = id, ToolUseID = toolUseID, Description = description, Prompt = prompt, IsBackground = background };

    // ---- PR-RUNTIME-104: .agentStarted ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-104")] // ThreadRuntime.swift:2579 guard app.hydraIsOn(thread)
    public void With_Hydra_off_an_announced_head_is_ignored()
    {
        using var rig = MakeRig(hydraOn: false);
        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1")));
        Assert.Empty(rig.Heads);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-104")] // ThreadRuntime.swift:2596-2603 spawnNativeHead, register, map the tool row
    public void An_announced_head_is_spawned_registered_and_mapped_to_its_tool_row()
    {
        using var rig = MakeRig();
        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1", toolUseID: "tool-use-1")));
        var head = Assert.Single(rig.Heads);
        Assert.Equal(HydraHeadInfo.Kind.Native, head.Hydra!.HydraHeadInfoKind);
        Assert.Equal("agent-1", head.Hydra.NativeID);
        Assert.True(rig.Registered("agent-1")); // :2598 hydraNativeHeads[spawn.id] = head.id
        Assert.Equal(head.Id, rig.Harness.Main.OnMain(() => rig.Lead.HydraHead("tool-use-1"))); // :2602 hydraToolHeads[toolUseID]
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-104")] // ThreadRuntime.swift:2580-2593: a known spawn id updates its head, and "Subagent" is unnamed
    public void A_known_spawn_id_renames_its_head_only_for_a_real_description_and_adopts_a_late_tool_use_id()
    {
        using var rig = MakeRig();
        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1", description: "Inspect the parser", toolUseID: null)));
        var first = rig.Head("agent-1");
        Assert.Null(first.Hydra!.ToolUseID);

        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1", description: "Subagent", toolUseID: "tool-use-9")));
        var afterUnnamed = rig.Fresh(first);
        Assert.Equal("Inspect the parser", afterUnnamed.Hydra!.Task); // :2581 the literal "Subagent" is unnamed: no rename
        Assert.Equal(first.Title, afterUnnamed.Title);
        Assert.Equal("tool-use-9", afterUnnamed.Hydra.ToolUseID); // :2587 adopted while nil
        Assert.Equal(first.Id, rig.Harness.Main.OnMain(() => rig.Lead.HydraHead("tool-use-9"))); // :2591

        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1", description: "Refactor the lexer", toolUseID: "other")));
        var renamed = rig.Fresh(first);
        Assert.Single(rig.Heads); // idempotent by spawn id
        Assert.Equal("Refactor the lexer", renamed.Hydra!.Task); // :2584
        Assert.Equal($"{renamed.Hydra.Persona.Name} {MiddleDot} Refactor the lexer", renamed.Title); // :2586 the middle dot (U+00B7)
        Assert.Equal("tool-use-9", renamed.Hydra.ToolUseID); // :2587 only while nil

        var longText = string.Join(" ", Enumerable.Repeat("investigate", 15)); // 164 characters, one line
        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1", description: longText, toolUseID: null)));
        var capped = rig.Fresh(first);
        Assert.Equal(longText, capped.Hydra!.Task); // the task keeps the whole description...
        Assert.Equal($"{capped.Hydra.Persona.Name} {MiddleDot} {TextCleanup.SingleLine(longText, 60)}", capped.Title); // ...the title is capped at 60 characters (:2586)
        Assert.True(TextCleanup.SingleLine(longText, 60).Length < longText.Length);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-104")] // ThreadRuntime.swift:2592-2594: a late brief goes onto a head that has said nothing yet
    public void A_late_brief_is_rehearsed_onto_a_head_with_no_user_message_and_only_once()
    {
        using var rig = MakeRig();
        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1", prompt: null)));
        var head = rig.Head("agent-1");
        Assert.DoesNotContain("Read the packet.", UserTexts(rig.HeadRuntime(head)));

        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1", prompt: "Read the packet.")));
        Assert.Equal(1, UserTexts(rig.HeadRuntime(head)).Count(text => text == "Read the packet."));

        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1", prompt: "Read the packet.")));
        Assert.Equal(1, UserTexts(rig.HeadRuntime(head)).Count(text => text == "Read the packet.")); // the head has its brief now
    }

    // ---- PR-RUNTIME-105: .agentEvent ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-105")] // ThreadRuntime.swift:2610-2612: a head heard from before it was announced is recovered from its .agent tool row
    public void A_head_heard_before_it_was_announced_is_recovered_from_its_agent_tool_row()
    {
        using var rig = MakeRig();
        rig.Feed(new ProviderEvent.ToolStarted("toolA", new ToolCall { ToolCallKind = ToolCall.Kind.Agent, Title = "Audit the build" }));
        rig.Feed(new ProviderEvent.AgentEvent("toolA", new ProviderEvent.Title("ignored")));
        var head = Assert.Single(rig.Heads);
        Assert.Equal("toolA", head.Hydra!.NativeID);
        Assert.Equal("Audit the build", head.Hydra.Task); // the row's title names the task
        Assert.False(head.Hydra.IsBackground); // :2611 isBackground: false
        Assert.Equal("toolA", head.Hydra.ToolUseID); // :2611 toolUseID: agentID
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-105")] // ThreadRuntime.swift:2613-2617, :2629-2634, :2637-2638, :2639-2641
    public void A_heads_tool_events_land_on_its_own_timeline_and_the_dropped_cases_change_nothing()
    {
        using var rig = MakeRig();
        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1")));
        var head = rig.Head("agent-1");
        var headRuntime = rig.HeadRuntime(head);
        var toolRows = () => rig.Harness.Main.OnMain(() => headRuntime.Entries
            .Count(entry => entry.Item.TimelineItemContent is TimelineItem.Content.Tool));
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Read, Title = "Read Parser.cs" };

        rig.Feed(new ProviderEvent.AgentEvent("agent-1", new ProviderEvent.ToolStarted("t1", call)));
        Assert.Equal(1, headRuntime.HydraToolCalls); // :2631 hydraToolCalls += 1
        Assert.Equal(1, toolRows()); // :2635 headRuntime.rehearse(event)
        Assert.Equal(Marrow.App.Views.Chat.ToolPresentation.Label(call), headRuntime.HydraActivity); // :2632
        Assert.NotNull(headRuntime.HydraLastEventAt); // :2633

        var titleBefore = rig.Fresh(head).Title;
        rig.Feed(new ProviderEvent.AgentEvent("agent-1", new ProviderEvent.Title("A new title")));
        rig.Feed(new ProviderEvent.AgentEvent("agent-1", new ProviderEvent.AssistantMessageID("m1")));
        rig.Feed(new ProviderEvent.AgentEvent("agent-1", new ProviderEvent.Models(new ValueList<ModelOption>([new ModelOption("heads-own-model", "Head Model")]), null)));
        Assert.Equal(titleBefore, rig.Fresh(head).Title); // :2640-2641 title, assistantMessageID, models and the other three are dropped...
        Assert.Null(rig.Harness.Main.OnMain(() => rig.Model.Providers.Model("heads-own-model", head.Provider))); // ...so a native head's catalog (which a rehearsed models event would load) never lands
        Assert.Equal(1, toolRows());
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-105")] // ThreadRuntime.swift:2618-2627: a finished head briefed again works another turn
    public void A_turn_started_on_a_finished_head_puts_it_back_to_work()
    {
        using var rig = MakeRig();
        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1")));
        var head = rig.Head("agent-1");
        rig.Feed(new ProviderEvent.AgentFinished("agent-1", TurnStatus.Completed, "done"));
        var finished = rig.Fresh(head);
        Assert.Equal(HydraHeadInfo.Status.Completed, finished.Hydra!.HydraHeadInfoStatus);
        rig.WaitUntilIdle(head);

        rig.Feed(new ProviderEvent.AgentEvent("agent-1", new ProviderEvent.TurnStarted(null)));

        var resumed = rig.Fresh(head);
        Assert.Equal(HydraHeadInfo.Status.Running, resumed.Hydra!.HydraHeadInfoStatus); // :2620-2622
        Assert.Null(resumed.Hydra.FinishedAt);
        Assert.True(rig.HeadRuntime(head).IsRunning); // :2619 rehearseTurn
    }

    // ---- PR-RUNTIME-107: .agentFinished ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-107")] // ThreadRuntime.swift:2646-2652
    public void A_finished_head_takes_the_providers_status_and_summary_and_its_turn_closes_and_the_id_is_kept()
    {
        using var rig = MakeRig();
        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1")));
        var head = rig.Head("agent-1");
        Assert.True(rig.HeadRuntime(head).IsRunning); // the spawn rehearsed a turn for it

        rig.Feed(new ProviderEvent.AgentFinished("agent-1", TurnStatus.Completed, "Found two callers."));

        var after = rig.Fresh(head);
        Assert.Equal(HydraHeadInfo.Status.Completed, after.Hydra!.HydraHeadInfoStatus); // :2648
        Assert.Equal("Found two callers.", after.Hydra.Summary);
        rig.WaitUntilIdle(head); // :2649-2651 the rehearsed turn is closed
        Assert.True(rig.Registered("agent-1")); // not pruned: only stopNativeHeads clears the map
        rig.Feed(new ProviderEvent.AgentFinished("agent-1", TurnStatus.Completed, "again")); // a head can be finished twice
        Assert.True(rig.Registered("agent-1"));
    }

    // ---- PR-RUNTIME-108: a foreground head's tool result ----

    [Theory]
    [Trait("Req", "PR-RUNTIME-108")] // ThreadRuntime.swift:2657-2666: completed -> completed, declined -> interrupted, anything else -> failed
    [InlineData(ToolCall.Status.Completed, HydraHeadInfo.Status.Completed)]
    [InlineData(ToolCall.Status.Declined, HydraHeadInfo.Status.Stopped)]
    [InlineData(ToolCall.Status.Failed, HydraHeadInfo.Status.Failed)]
    public void A_foreground_heads_tool_result_finishes_it_through_the_three_way_status_map(ToolCall.Status result, HydraHeadInfo.Status expected)
    {
        using var rig = MakeRig();
        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1", toolUseID: "tu-1", background: false)));
        rig.Feed(new ProviderEvent.ToolStarted("tu-1", new ToolCall { ToolCallKind = ToolCall.Kind.Agent, Title = "Inspect the parser" }));
        var head = rig.Head("agent-1");

        rig.Feed(new ProviderEvent.ToolUpdated("tu-1", new ToolUpdate { Status = result, Output = "the report" }));

        var after = rig.Fresh(head);
        Assert.Equal(expected, after.Hydra!.HydraHeadInfoStatus);
        Assert.Equal("the report", after.Hydra.Summary); // :2665 the tool's output is the head's summary
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-108")] // ThreadRuntime.swift:2658-2659 guard !info.isBackground
    public void A_background_heads_tool_result_says_nothing_about_the_head()
    {
        using var rig = MakeRig();
        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1", toolUseID: "tu-1", background: true)));
        rig.Feed(new ProviderEvent.ToolStarted("tu-1", new ToolCall { ToolCallKind = ToolCall.Kind.Agent, Title = "Inspect the parser" }));
        var head = rig.Head("agent-1");

        rig.Feed(new ProviderEvent.ToolUpdated("tu-1", new ToolUpdate { Status = ToolCall.Status.Completed, Output = "launched" }));

        Assert.Equal(HydraHeadInfo.Status.Running, rig.Fresh(head).Hydra!.HydraHeadInfoStatus);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-108")] // ThreadRuntime.swift:2658 guard info.kind == .native
    public void A_tool_result_never_finishes_a_head_that_is_not_native()
    {
        var droppy = Guid.Empty;
        using var rig = MakeRig(extraThreads: (project, lead) =>
        {
            var head = new ChatThread(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised)
            {
                ParentThreadID = lead.Id,
                IsInPanel = true,
                Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running) with
                {
                    HydraHeadInfoKind = HydraHeadInfo.Kind.Droppy, IsBackground = false, NativeID = "droppy-native-id",
                },
            };
            droppy = head.Id;
            return [head];
        });
        // Registered in the lead's map and mapped to a tool row, foreground, unfinished and carrying a native id:
        // only its kind keeps a tool result from finishing it (a Droppy-run head ends on its own runtime's word).
        rig.Harness.Main.OnMain(() =>
        {
            var map = (System.Collections.IDictionary)typeof(ThreadRuntime).GetField("hydraNativeHeads", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig.Lead)!;
            map["droppy-native-id"] = droppy;
            rig.Lead.RehearseHydraHead("tu-1", droppy);
            // App start-up recovery interrupts a persisted running head; put this one back to work.
            rig.Model.UpdateThread(droppy, thread => thread with
            {
                Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running, FinishedAt = null },
            });
            Assert.False(rig.Model.Thread(droppy)!.Hydra!.IsFinished);
        });
        rig.Feed(new ProviderEvent.ToolStarted("tu-1", new ToolCall { ToolCallKind = ToolCall.Kind.Agent, Title = "Inspect" }));

        rig.Feed(new ProviderEvent.ToolUpdated("tu-1", new ToolUpdate { Status = ToolCall.Status.Completed, Output = "report" }));

        Assert.Equal(HydraHeadInfo.Status.Running, rig.Harness.Main.OnMain(() => rig.Model.Thread(droppy)!.Hydra!.HydraHeadInfoStatus));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-108")] // ThreadRuntime.swift:2658-2659 guard !info.isFinished
    public void A_second_tool_result_does_not_finish_an_already_finished_head_again()
    {
        using var rig = MakeRig();
        rig.Feed(new ProviderEvent.AgentStarted(Spawn("agent-1", toolUseID: "tu-1", background: false)));
        rig.Feed(new ProviderEvent.ToolStarted("tu-1", new ToolCall { ToolCallKind = ToolCall.Kind.Agent, Title = "Inspect the parser" }));
        var head = rig.Head("agent-1");
        rig.Feed(new ProviderEvent.ToolUpdated("tu-1", new ToolUpdate { Status = ToolCall.Status.Completed, Output = "first report" }));

        rig.Feed(new ProviderEvent.ToolUpdated("tu-1", new ToolUpdate { Status = ToolCall.Status.Failed, Output = "second report" }));

        var after = rig.Fresh(head);
        Assert.Equal(HydraHeadInfo.Status.Completed, after.Hydra!.HydraHeadInfoStatus);
        Assert.Equal("first report", after.Hydra.Summary);
    }

    private static IEnumerable<string> UserTexts(ThreadRuntime runtime) =>
        runtime.Entries.Select(entry => entry.Item.TimelineItemContent).OfType<TimelineItem.Content.User>().Select(user => user.Value.Text);
}
