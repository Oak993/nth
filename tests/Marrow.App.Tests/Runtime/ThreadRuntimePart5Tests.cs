using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimePart5Tests
{
    [Fact]
    [Trait("Req", "PR-RUNTIME-086")] // App/Runtime/ThreadRuntime.swift:2142-2143, :3065-3072
    public void Tool_output_is_kept_unflattened_for_a_known_row_and_discarded_for_an_unknown_id()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Part 5", h.NewFolder("part5"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        const string toolID = "known-tool";

        h.Main.OnMain(() =>
        {
            runtime.Rehearse(new ProviderEvent.ToolStarted(toolID, new ToolCall
            {
                ToolCallKind = ToolCall.Kind.Command,
                Title = "Run command",
            }));
            runtime.Rehearse(new ProviderEvent.ToolOutput("unknown-tool", "discarded — output"));
            runtime.Rehearse(new ProviderEvent.ToolOutput(toolID, "process — output"));
        });
        h.Main.AdvanceMilliseconds(70);

        h.Main.OnMain(() =>
        {
            var entry = Assert.Single(runtime.Entries);
            Assert.Equal(toolID, entry.Id);
            var call = Assert.IsType<TimelineItem.Content.Tool>(entry.Item.TimelineItemContent).Value;
            Assert.Equal("process — output", call.Output);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-087")] // App/Runtime/ThreadRuntime.swift:2154-2155, :3063-3064
    public void An_empty_first_plan_delta_creates_an_empty_drafting_plan()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Part 5", h.NewFolder("part5"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));

        h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.PlanDelta("plan-1", "")));
        h.Main.AdvanceMilliseconds(70);

        h.Main.OnMain(() =>
        {
            var entry = Assert.Single(runtime.Entries);
            Assert.Equal("plan-1", entry.Id);
            var plan = Assert.IsType<TimelineItem.Content.Plan>(entry.Item.TimelineItemContent).Value;
            Assert.Equal("", plan.Markdown);
            Assert.Equal(ProposedPlan.State.Drafting, plan.ProposedPlanState);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-088")] // App/Runtime/ThreadRuntime.swift:2156-2158, :3244-3255
    public void Empty_plan_completion_preserves_streamed_markdown_and_proposes_the_plan()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Part 5", h.NewFolder("part5"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));

        h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.PlanDelta("plan-2", "## Steps\n1. Read the packet")));
        h.Main.AdvanceMilliseconds(70);
        h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.PlanCompleted("plan-2", "")));

        h.Main.OnMain(() =>
        {
            var entry = Assert.Single(runtime.Entries);
            var plan = Assert.IsType<TimelineItem.Content.Plan>(entry.Item.TimelineItemContent).Value;
            Assert.Equal("## Steps\n1. Read the packet", plan.Markdown);
            Assert.Equal(ProposedPlan.State.Proposed, plan.ProposedPlanState);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-089")] // App/Runtime/ThreadRuntime.swift:2159-2160, :3523-3547
    public void Repeated_todo_events_overwrite_the_single_todo_row()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Part 5", h.NewFolder("part5"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));

        h.Main.OnMain(() =>
        {
            runtime.Rehearse(new ProviderEvent.Todos(new ValueList<TodoStep>([
                new TodoStep { Text = "Old step", TodoStepStatus = TodoStep.Status.Pending },
            ])));
            runtime.Rehearse(new ProviderEvent.Todos(new ValueList<TodoStep>([
                new TodoStep { Text = "Read the packet", TodoStepStatus = TodoStep.Status.Done },
                new TodoStep { Text = "Write tests", TodoStepStatus = TodoStep.Status.Active },
            ])));
        });

        h.Main.OnMain(() =>
        {
            var entry = Assert.Single(runtime.Entries);
            var todos = Assert.IsType<TimelineItem.Content.Todos>(entry.Item.TimelineItemContent).Value;
            Assert.Collection(todos,
                step =>
                {
                    Assert.Equal("Read the packet", step.Text);
                    Assert.Equal(TodoStep.Status.Done, step.TodoStepStatus);
                },
                step =>
                {
                    Assert.Equal("Write tests", step.Text);
                    Assert.Equal(TodoStep.Status.Active, step.TodoStepStatus);
                });
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-090")] // App/Runtime/ThreadRuntime.swift:2161-2164
    [Trait("Req", "PR-RUNTIME-091")] // App/Runtime/ThreadRuntime.swift:2165-2168
    [Trait("Req", "PR-RUNTIME-092")] // App/Runtime/ThreadRuntime.swift:2169-2171
    public void Repeated_requests_move_to_the_end_and_resolution_removes_the_id_from_both_lists()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Part 5", h.NewFolder("part5"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));

        h.Main.OnMain(() =>
        {
            runtime.Rehearse(new ProviderEvent.Approval(new ApprovalRequest(
                "approval-a", ApprovalRequest.Kind.Command, "First approval", ValueList<ApprovalRequest.Option>.Empty)));
            runtime.Rehearse(new ProviderEvent.Approval(new ApprovalRequest(
                "approval-b", ApprovalRequest.Kind.Tool, "Second approval", ValueList<ApprovalRequest.Option>.Empty)));
            runtime.Rehearse(new ProviderEvent.Approval(new ApprovalRequest(
                "approval-a", ApprovalRequest.Kind.Command, "Updated approval", ValueList<ApprovalRequest.Option>.Empty)));

            runtime.Rehearse(new ProviderEvent.Question(new QuestionRequest("question-a", ValueList<QuestionRequest.Question>.Empty)));
            runtime.Rehearse(new ProviderEvent.Question(new QuestionRequest("question-b", ValueList<QuestionRequest.Question>.Empty)));
            runtime.Rehearse(new ProviderEvent.Question(new QuestionRequest("question-a", new ValueList<QuestionRequest.Question>([
                new QuestionRequest.Question("choice", "Header", "Choose one", ValueList<QuestionRequest.Choice>.Empty,
                    AllowsMultiple: false, AllowsOther: true, IsSecret: false),
            ]))));

            runtime.Rehearse(new ProviderEvent.Approval(new ApprovalRequest(
                "shared", ApprovalRequest.Kind.Plan, "Shared approval", ValueList<ApprovalRequest.Option>.Empty)));
            runtime.Rehearse(new ProviderEvent.Question(new QuestionRequest("shared", ValueList<QuestionRequest.Question>.Empty)));

            Assert.Equal(["approval-b", "approval-a", "shared"], runtime.Approvals.Select(request => request.Id));
            Assert.Equal("Updated approval", runtime.Approvals[1].Title);
            Assert.Equal(["question-b", "question-a", "shared"], runtime.Questions.Select(request => request.Id));
            Assert.Single(runtime.Questions[1].Questions);

            runtime.Rehearse(new ProviderEvent.RequestResolved("shared"));

            Assert.Equal(["approval-b", "approval-a"], runtime.Approvals.Select(request => request.Id));
            Assert.Equal(["question-b", "question-a"], runtime.Questions.Select(request => request.Id));
            Assert.Empty(runtime.Entries);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-093")] // App/Runtime/ThreadRuntime.swift:2177-2184, :3655
    public void Usage_events_replace_the_context_usage_value()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Part 5", h.NewFolder("part5"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));

        h.Main.OnMain(() =>
        {
            runtime.Rehearse(new ProviderEvent.Usage(new ContextUsage { UsedTokens = 321, WindowTokens = 1_024 }));
            runtime.Rehearse(new ProviderEvent.Usage(new ContextUsage { UsedTokens = 654, WindowTokens = 2_048 }));

            Assert.Equal(654, runtime.Usage!.UsedTokens);
            Assert.Equal(2_048, runtime.Usage.WindowTokens);
            Assert.Empty(runtime.Entries);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-095")] // App/Runtime/ThreadRuntime.swift:2187-2188, :3549-3552; Core/Models/Timeline.swift:232-244
    public void Notice_event_keeps_the_level_and_message_but_discards_limit_metadata()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Part 5", h.NewFolder("part5"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        var limit = new LimitNotice { ThreadID = thread.Id };

        h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.Notice(new Notice
        {
            NoticeLevel = Notice.Level.Warning,
            Message = "Provider limit reached",
            Limit = limit,
        })));

        h.Main.OnMain(() =>
        {
            var entry = Assert.Single(runtime.Entries);
            var notice = Assert.IsType<TimelineItem.Content.Notice>(entry.Item.TimelineItemContent).Value;
            Assert.Equal(Notice.Level.Warning, notice.NoticeLevel);
            Assert.Equal("Provider limit reached", notice.Message);
            Assert.Null(notice.Limit);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-096")] // App/Runtime/ThreadRuntime.swift:2189-2190
    public void Usage_limit_does_not_change_the_timeline_or_runtime_phase()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Part 5", h.NewFolder("part5"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));

        h.Main.OnMain(() =>
        {
            runtime.Rehearse(new ProviderEvent.UsageLimit(null));

            Assert.Empty(runtime.Entries);
            Assert.Empty(runtime.Turns);
            Assert.Equal(RuntimePhase.Idle, runtime.Phase);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-097")] // App/Runtime/ThreadRuntime.swift:2191-2192
    public void Mode_change_updates_the_thread_without_adding_a_timeline_row()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Part 5", h.NewFolder("part5"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));

        h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.ModeChanged(InteractionMode.Plan)));

        h.Main.OnMain(() =>
        {
            Assert.Equal(InteractionMode.Plan, model.Thread(thread.Id)!.InteractionMode);
            Assert.Empty(runtime.Entries);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-100")] // App/Runtime/ThreadRuntime.swift:2199-2203
    public void Provider_title_does_not_replace_a_custom_thread_title()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Part 5", h.NewFolder("part5"));
        var thread = AppModelSamples.Thread(project) with { Title = "My title", HasCustomTitle = true };
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));

        h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.Title("Provider title")));

        h.Main.OnMain(() => Assert.Equal("My title", model.Thread(thread.Id)!.Title));
    }
}
