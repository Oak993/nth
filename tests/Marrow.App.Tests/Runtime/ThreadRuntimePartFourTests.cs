using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Testing;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimePartFourTests
{
    [Fact]
    [Trait("Req", "PR-RUNTIME-078")] // App/Runtime/ThreadRuntime.swift:2119-2251, 3739-3741
    [Trait("Decision", "ED-075")]
    public void Rehearse_dispatches_one_sample_of_every_provider_event_case()
    {
        using var fixture = CreateRuntime();
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Edit, Title = "edit" };
        var update = new ToolUpdate();
        var approval = new ApprovalRequest("approval", ApprovalRequest.Kind.Command, "command",
            ValueList<ApprovalRequest.Option>.Empty);
        var question = new QuestionRequest("question", ValueList<QuestionRequest.Question>.Empty);
        var usage = new ContextUsage { UsedTokens = 0 };
        var spend = new TokenSpend(ProviderKind.Codex, 1);
        var notice = new Notice { NoticeLevel = Notice.Level.Info, Message = "notice" };
        var model = new ModelOption("model", "Model");
        var command = new SlashCommand { Name = "command", Detail = "Command" };
        var todo = new TodoStep { Text = "todo", TodoStepStatus = TodoStep.Status.Pending };
        var spawn = new AgentSpawn { Id = "agent", Description = "agent" };
        var samples = UnionCoverage.AssertCovers<ProviderEvent>(
            new ProviderEvent.SessionReady("session"),
            new ProviderEvent.TurnStarted(null),
            new ProviderEvent.MessageDelta("message", "text"),
            new ProviderEvent.MessageCompleted("message", "text"),
            new ProviderEvent.ReasoningDelta("reasoning", "text"),
            new ProviderEvent.ReasoningCompleted("reasoning", "text"),
            new ProviderEvent.ToolStarted("tool", call),
            new ProviderEvent.ToolOutput("tool", "output"),
            new ProviderEvent.ToolUpdated("tool", update),
            new ProviderEvent.PlanDelta("plan", "text"),
            new ProviderEvent.PlanCompleted("plan", "markdown"),
            new ProviderEvent.Todos(new ValueList<TodoStep>([todo])),
            new ProviderEvent.Approval(approval),
            new ProviderEvent.Question(question),
            new ProviderEvent.RequestResolved("request"),
            new ProviderEvent.Usage(usage),
            new ProviderEvent.TokenSpend(spend),
            new ProviderEvent.Diff("diff"),
            new ProviderEvent.Notice(notice),
            new ProviderEvent.UsageLimit(null),
            new ProviderEvent.ModeChanged(InteractionMode.Build),
            new ProviderEvent.Models(new ValueList<ModelOption>([model]), "model"),
            new ProviderEvent.Commands(new ValueList<SlashCommand>([command])),
            new ProviderEvent.Title("title"),
            new ProviderEvent.AssistantMessageID("assistant-message"),
            new ProviderEvent.TurnCompleted(TurnStatus.Completed, null),
            new ProviderEvent.Exited(null),
            new ProviderEvent.AgentStarted(spawn),
            new ProviderEvent.AgentEvent("agent", new ProviderEvent.Title("head title")),
            new ProviderEvent.AgentProgress("agent", "summary", "tool", 1, 1),
            new ProviderEvent.AgentFinished("agent", TurnStatus.Completed, "summary"),
            // DEVIATION OD-0134 / ED-NEW-ignis-contract: the port-only Ignis case (the runtime forwards it in unit U3).
            new ProviderEvent.CacheUsage(global::Marrow.Core.Port.Ignis.IgnisTurnUsage.NotReported(ProviderKind.Claude, null, null)));

        fixture.Harness.Main.OnMain(() =>
        {
            foreach (var sample in samples) fixture.Runtime.Rehearse(sample);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-078")] // App/Runtime/ThreadRuntime.swift:2172-2176, 2228-2237
    public void TokenSpend_uses_the_current_turn_branch_even_when_the_spend_names_a_head()
    {
        using var fixture = CreateRuntime();
        var spend = new TokenSpend(ProviderKind.Codex, 42, agentID: "head-1");

        fixture.Harness.Main.OnMain(() =>
        {
            fixture.Runtime.RehearseTurn("prompt");
            fixture.Runtime.Rehearse(new ProviderEvent.TokenSpend(spend));

            Assert.Equal(spend, Assert.Single(fixture.Runtime.Turns).TokenSpends!.Single());
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-078")] // App/Runtime/ThreadRuntime.swift:2073-2076, 2119-2122
    [Trait("Decision", "ED-075")]
    public void Observed_provider_events_use_the_same_session_ready_dispatch()
    {
        using var fixture = CreateRuntime("old-session");
        var provider = new TestProviderSession();

        fixture.Harness.Main.OnMain(() =>
        {
            fixture.Runtime.Observe(provider);
            provider.Emit(new ProviderEvent.SessionReady("observed-session"));

            Assert.Equal("observed-session", fixture.Runtime.Thread!.ProviderSessionID);
            Assert.Empty(fixture.Runtime.Entries);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-079")] // App/Runtime/ThreadRuntime.swift:2121-2122
    public void SessionReady_overwrites_the_thread_session_id_without_adding_a_timeline_row()
    {
        using var fixture = CreateRuntime("old-session");

        fixture.Harness.Main.OnMain(() =>
        {
            fixture.Runtime.Rehearse(new ProviderEvent.SessionReady("first-session"));
            fixture.Runtime.Rehearse(new ProviderEvent.SessionReady("last-session"));

            Assert.Equal("last-session", fixture.Runtime.Thread!.ProviderSessionID);
            Assert.Empty(fixture.Runtime.Entries);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-080")] // App/Runtime/ThreadRuntime.swift:2123-2127
    public void TurnStarted_sets_running_phase_even_without_an_active_turn_or_provider_id()
    {
        using var fixture = CreateRuntime();

        fixture.Harness.Main.OnMain(() =>
        {
            Assert.Equal(RuntimePhase.Idle, fixture.Runtime.Phase);
            fixture.Runtime.Rehearse(new ProviderEvent.TurnStarted(null));
            Assert.Equal(RuntimePhase.Running, fixture.Runtime.Phase);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-080")] // App/Runtime/ThreadRuntime.swift:2123-2127
    public void TurnStarted_records_the_provider_id_on_the_current_turn()
    {
        using var fixture = CreateRuntime();

        fixture.Harness.Main.OnMain(() =>
        {
            fixture.Runtime.RehearseTurn("prompt");
            fixture.Runtime.Rehearse(new ProviderEvent.TurnStarted("provider-turn"));

            Assert.Equal("provider-turn", Assert.Single(fixture.Runtime.Turns).ProviderTurnID);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-081")] // App/Runtime/ThreadRuntime.swift:2128-2129, 3053-3057
    public void MessageDelta_ignores_an_empty_opening_delta_and_creates_a_streaming_row_for_text()
    {
        using var fixture = CreateRuntime();

        fixture.Harness.Main.OnMain(() =>
        {
            fixture.Runtime.Rehearse(new ProviderEvent.MessageDelta("empty", ""));
            Assert.Empty(fixture.Runtime.Entries);

            fixture.Runtime.Rehearse(new ProviderEvent.MessageDelta("reply", "hello"));
            var row = Assert.Single(fixture.Runtime.Entries);
            Assert.Equal("reply", row.Id);
            Assert.Equal(TimelineEntry.Kind.Assistant, row.TimelineEntryKind);
            var assistant = Assert.IsType<TimelineItem.Content.Assistant>(row.Item.TimelineItemContent).Value;
            Assert.Equal("hello", assistant.Text);
            Assert.True(assistant.IsStreaming);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-082")] // App/Runtime/ThreadRuntime.swift:2130-2132, 3206-3224
    public void MessageCompleted_flushes_streamed_text_and_removes_a_whitespace_only_row()
    {
        using var fixture = CreateRuntime();

        fixture.Harness.Main.OnMain(() =>
        {
            fixture.Runtime.Rehearse(new ProviderEvent.MessageDelta("reply", "streamed text"));
            fixture.Runtime.Rehearse(new ProviderEvent.MessageCompleted("reply", ""));

            var reply = Assert.Single(fixture.Runtime.Entries);
            var assistant = Assert.IsType<TimelineItem.Content.Assistant>(reply.Item.TimelineItemContent).Value;
            Assert.Equal("streamed text", assistant.Text);
            Assert.False(assistant.IsStreaming);

            fixture.Runtime.Rehearse(new ProviderEvent.MessageDelta("whitespace", " "));
            fixture.Runtime.Rehearse(new ProviderEvent.MessageCompleted("whitespace", " \t"));
            Assert.Single(fixture.Runtime.Entries);
            Assert.DoesNotContain(fixture.Runtime.Entries, entry => entry.Id == "whitespace");
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-083")] // App/Runtime/ThreadRuntime.swift:2133-2134, 3058-3062
    public void ReasoningDelta_ignores_an_empty_opening_delta_and_creates_a_streaming_row_for_text()
    {
        using var fixture = CreateRuntime();

        fixture.Harness.Main.OnMain(() =>
        {
            fixture.Runtime.Rehearse(new ProviderEvent.ReasoningDelta("empty", ""));
            Assert.Empty(fixture.Runtime.Entries);

            fixture.Runtime.Rehearse(new ProviderEvent.ReasoningDelta("reasoning", "thinking"));
            var row = Assert.Single(fixture.Runtime.Entries);
            Assert.Equal("reasoning", row.Id);
            Assert.Equal(TimelineEntry.Kind.Reasoning, row.TimelineEntryKind);
            var reasoning = Assert.IsType<TimelineItem.Content.Reasoning>(row.Item.TimelineItemContent).Value;
            Assert.Equal("thinking", reasoning.Text);
            Assert.True(reasoning.IsStreaming);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-084")] // App/Runtime/ThreadRuntime.swift:2135-2137, 3226-3242
    public void ReasoningCompleted_flushes_streamed_text_and_removes_a_whitespace_only_row()
    {
        using var fixture = CreateRuntime();

        fixture.Harness.Main.OnMain(() =>
        {
            fixture.Runtime.Rehearse(new ProviderEvent.ReasoningDelta("reasoning", "streamed thought"));
            fixture.Runtime.Rehearse(new ProviderEvent.ReasoningCompleted("reasoning", ""));

            var reasoning = Assert.IsType<TimelineItem.Content.Reasoning>(
                Assert.Single(fixture.Runtime.Entries).Item.TimelineItemContent).Value;
            Assert.Equal("streamed thought", reasoning.Text);
            Assert.False(reasoning.IsStreaming);

            fixture.Runtime.Rehearse(new ProviderEvent.ReasoningDelta("whitespace", " "));
            fixture.Runtime.Rehearse(new ProviderEvent.ReasoningCompleted("whitespace", " \t"));
            Assert.Single(fixture.Runtime.Entries);
            Assert.DoesNotContain(fixture.Runtime.Entries, entry => entry.Id == "whitespace");
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-085")] // App/Runtime/ThreadRuntime.swift:2138-2141
    public void ToolStarted_upserts_a_tool_row_without_scheduling_a_document_save()
    {
        using var fixture = CreateRuntime();
        var sentinelDocument = new ThreadDocument(fixture.Runtime.ThreadID)
        {
            FollowUps = new ValueList<FollowUpPrompt>([new FollowUpPrompt { Text = "disk sentinel" }]),
        };
        DiskWriter.Shared.WriteSynchronously(sentinelDocument, Storage.ThreadURL(fixture.Runtime.ThreadID));
        var before = File.ReadAllBytes(Storage.ThreadURL(fixture.Runtime.ThreadID));

        fixture.Harness.Main.OnMain(() =>
            fixture.Runtime.Rehearse(new ProviderEvent.ToolStarted("edit-tool", new ToolCall
            {
                ToolCallKind = ToolCall.Kind.Edit,
                Title = "Edit file",
            })));
        fixture.Harness.Main.AdvanceMilliseconds(500);
        DiskWriter.Shared.WaitForQueuedWrites();

        fixture.Harness.Main.OnMain(() =>
        {
            var row = Assert.Single(fixture.Runtime.Entries);
            Assert.Equal("edit-tool", row.Id);
            Assert.Equal(TimelineEntry.Kind.Tool, row.TimelineEntryKind);
            var tool = Assert.IsType<TimelineItem.Content.Tool>(row.Item.TimelineItemContent).Value;
            Assert.Equal(ToolCall.Kind.Edit, tool.ToolCallKind);
            Assert.Equal("Edit file", tool.Title);
        });
        Assert.Equal(before, File.ReadAllBytes(Storage.ThreadURL(fixture.Runtime.ThreadID)));
    }

    private static RuntimeFixture CreateRuntime(string? providerSessionID = null)
    {
        var harness = new AppModelHarness();
        var project = AppModelSamples.Project("P", harness.NewFolder("project"));
        var thread = AppModelSamples.Thread(project) with { ProviderSessionID = providerSessionID };
        harness.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var app = harness.MakeModel();
        var runtime = harness.Main.OnMain(() => app.Runtime(thread.Id));
        harness.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return new RuntimeFixture(harness, runtime);
    }

}
