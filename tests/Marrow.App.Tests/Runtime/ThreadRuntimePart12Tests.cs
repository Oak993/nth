using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Xunit;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimePart12Tests
{
    [Fact]
    [Trait("Req", "UI-RUNTIME-011")] // App/Runtime/ThreadRuntime.swift:3063-3064, 1141-1156, 3244-3255, 3042-3044
    public void Empty_plan_delta_creates_a_card_that_tracks_proposal_acceptance_and_dismissal()
    {
        using var h = new ThreadRuntimePart12Harness();

        var created = h.Main.OnMain(() =>
        {
            h.Runtime.Rehearse(new ProviderEvent.PlanDelta("empty-plan", ""));
            return h.Runtime.Entries.Single(entry => entry.Id == "empty-plan");
        });

        Assert.Equal(TimelineEntry.Kind.Plan, created.TimelineEntryKind);
        var initial = Assert.IsType<TimelineItem.Content.Plan>(created.Item.TimelineItemContent).Value;
        Assert.Equal("", initial.Markdown);
        Assert.Equal(ProposedPlan.State.Drafting, initial.ProposedPlanState);

        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.PlanCompleted("empty-plan", "The plan")));
        var proposed = h.Main.OnMain(() => Assert.IsType<TimelineItem.Content.Plan>(
            h.Runtime.Entries.Single(entry => entry.Id == "empty-plan").Item.TimelineItemContent).Value);
        Assert.Equal("The plan", proposed.Markdown);
        Assert.Equal(ProposedPlan.State.Proposed, proposed.ProposedPlanState);

        h.Main.OnMain(() => h.Runtime.ImplementPlan("empty-plan"));
        var accepted = h.Main.OnMain(() => Assert.IsType<TimelineItem.Content.Plan>(
            h.Runtime.Entries.Single(entry => entry.Id == "empty-plan").Item.TimelineItemContent).Value);
        Assert.Equal(ProposedPlan.State.Accepted, accepted.ProposedPlanState);

        h.Main.OnMain(() =>
        {
            h.Runtime.Rehearse(new ProviderEvent.PlanDelta("dismissed-plan", "Plan to dismiss"));
            h.Runtime.DismissPlan("dismissed-plan");
        });
        var dismissed = h.Main.OnMain(() => Assert.IsType<TimelineItem.Content.Plan>(
            h.Runtime.Entries.Single(entry => entry.Id == "dismissed-plan").Item.TimelineItemContent).Value);
        Assert.Equal(ProposedPlan.State.Dismissed, dismissed.ProposedPlanState);
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-012")] // App/Runtime/ThreadRuntime.swift:3300-3305
    public void An_unannounced_tool_update_creates_a_running_other_tool_and_later_finishes_it()
    {
        using var h = new ThreadRuntimePart12Harness();

        var created = h.Main.OnMain(() =>
        {
            h.Runtime.Rehearse(new ProviderEvent.ToolUpdated("late-tool", new ToolUpdate()));
            return h.Runtime.Entries.Single(entry => entry.Id == "late-tool");
        });

        Assert.Equal(TimelineEntry.Kind.Tool, created.TimelineEntryKind);
        var initial = Assert.IsType<TimelineItem.Content.Tool>(created.Item.TimelineItemContent).Value;
        Assert.Equal("Tool", initial.Title);
        Assert.Equal(ToolCall.Kind.Other, initial.ToolCallKind);
        Assert.Equal(ToolCall.Status.Running, initial.ToolCallStatus);

        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.ToolUpdated(
            "late-tool", new ToolUpdate { Status = ToolCall.Status.Completed })));
        var completed = h.Main.OnMain(() => Assert.IsType<TimelineItem.Content.Tool>(
            h.Runtime.Entries.Single(entry => entry.Id == "late-tool").Item.TimelineItemContent).Value);
        Assert.Equal(ToolCall.Status.Completed, completed.ToolCallStatus);
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-013")] // App/Runtime/ThreadRuntime.swift:3319-3333
    public void Todos_upsert_one_row_for_the_turn_and_an_empty_first_update_adds_none()
    {
        using var h = new ThreadRuntimePart12Harness();

        h.Main.OnMain(() =>
        {
            h.Runtime.Rehearse(new ProviderEvent.Todos(ValueList<TodoStep>.Empty));
            Assert.DoesNotContain(h.Runtime.Entries, entry => entry.TimelineEntryKind == TimelineEntry.Kind.Todos);
            h.Runtime.Rehearse(new ProviderEvent.TurnStarted("provider-turn"));
            h.Runtime.Rehearse(new ProviderEvent.Todos(new ValueList<TodoStep>([
                new TodoStep { Text = "First", TodoStepStatus = TodoStep.Status.Active },
                new TodoStep { Text = "Second", TodoStepStatus = TodoStep.Status.Pending },
            ])));
        });

        var firstRow = h.Main.OnMain(() => h.Runtime.Entries.Single(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Todos));
        Assert.Equal(2, Assert.IsType<TimelineItem.Content.Todos>(firstRow.Item.TimelineItemContent).Value.Count);

        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Todos(new ValueList<TodoStep>([
            new TodoStep { Text = "Updated", TodoStepStatus = TodoStep.Status.Done },
        ]))));
        var updatedRow = h.Main.OnMain(() => h.Runtime.Entries.Single(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Todos));
        Assert.Same(firstRow, updatedRow);
        var updatedSteps = Assert.IsType<TimelineItem.Content.Todos>(updatedRow.Item.TimelineItemContent).Value;
        Assert.Equal("Updated", Assert.Single(updatedSteps).Text);

        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Todos(ValueList<TodoStep>.Empty)));
        var emptyRow = h.Main.OnMain(() => h.Runtime.Entries.Single(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Todos));
        Assert.Same(firstRow, emptyRow);
        Assert.Empty(Assert.IsType<TimelineItem.Content.Todos>(emptyRow.Item.TimelineItemContent).Value);
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-014")] // App/Runtime/ThreadRuntime.swift:3014-3018
    public void Hydra_note_is_a_user_row_outside_a_turn_with_no_head_badges()
    {
        using var h = new ThreadRuntimePart12Harness();

        h.Main.OnMain(() => h.Runtime.AppendHydraNote("Heads finished", "hydra-note"));
        var entry = h.Main.OnMain(() => h.Runtime.Entries.Single(item => item.Id == "hydra-note"));

        Assert.Equal(TimelineEntry.Kind.User, entry.TimelineEntryKind);
        Assert.Null(entry.TurnID);
        var message = Assert.IsType<TimelineItem.Content.User>(entry.Item.TimelineItemContent).Value;
        Assert.Equal("Heads finished", message.Text);
        Assert.Empty(message.HydraHeads!);
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-017")] // App/Runtime/ThreadRuntime.swift:1158-1163, 102-135
    public void Plan_command_matches_trimmed_whole_text_and_only_toggles_the_thread_mode()
    {
        using var h = new ThreadRuntimePart12Harness();

        h.Main.OnMain(() =>
        {
            h.Runtime.Draft = new ComposerDraft { Text = "  /plan\r\n" };
            h.Runtime.Send();
        });

        Assert.Equal(InteractionMode.Plan, h.Main.OnMain(() => h.App.Thread(h.Thread.Id)!.InteractionMode));
        Assert.Empty(h.Main.OnMain(() => h.Runtime.Turns));
        Assert.Empty(h.Main.OnMain(() => h.Runtime.Entries));

        h.Main.OnMain(() =>
        {
            h.Runtime.Draft = new ComposerDraft { Text = "/plan" };
            h.Runtime.Send();
        });
        Assert.Equal(InteractionMode.Build, h.Main.OnMain(() => h.App.Thread(h.Thread.Id)!.InteractionMode));
        Assert.Empty(h.Main.OnMain(() => h.Runtime.Turns));
        Assert.Empty(h.Main.OnMain(() => h.Runtime.Entries));
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-018")] // App/Runtime/ThreadRuntime.swift:1164-1167, 1738-1757
    public void Compact_command_on_Claude_without_a_session_shows_the_local_info_notice()
    {
        using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);

        h.Main.OnMain(() =>
        {
            h.Runtime.Draft = new ComposerDraft { Text = " /compact " };
            h.Runtime.Send();
        });

        var notice = h.Main.OnMain(() => Assert.IsType<TimelineItem.Content.Notice>(
            h.Runtime.Entries.Single(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Notice).Item.TimelineItemContent).Value);
        Assert.Equal(Notice.Level.Info, notice.NoticeLevel);
        Assert.Equal("Nothing to compact yet.", notice.Message);
        Assert.Empty(h.Main.OnMain(() => h.Runtime.Turns));
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-035")] // App/Runtime/ThreadRuntime.swift:2206-2208
    public void Turn_completion_keeps_a_provider_error_unwrapped_and_omits_a_nil_error()
    {
        using var h = new ThreadRuntimePart12Harness();

        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Failed, null)));
        Assert.DoesNotContain(h.Main.OnMain(() => h.Runtime.Entries), entry => entry.TimelineEntryKind == TimelineEntry.Kind.Notice);

        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Failed, "provider supplied error")));
        var notice = h.Main.OnMain(() => Assert.IsType<TimelineItem.Content.Notice>(
            h.Runtime.Entries.Single(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Notice).Item.TimelineItemContent).Value);
        Assert.Equal(Notice.Level.Error, notice.NoticeLevel);
        Assert.Equal("provider supplied error", notice.Message);
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-036")] // App/Runtime/ThreadRuntime.swift:2221-2224
    public void Unexpected_exit_during_a_turn_uses_the_provider_display_name()
    {
        using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);

        h.Main.OnMain(() =>
        {
            h.Runtime.RehearseTurn("The user prompt.");
            h.Runtime.Rehearse(new ProviderEvent.TurnStarted("provider-turn"));
            h.Runtime.Rehearse(new ProviderEvent.Exited(null));
        });

        var notice = h.Main.OnMain(() => Assert.IsType<TimelineItem.Content.Notice>(
            h.Runtime.Entries.Single(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Notice).Item.TimelineItemContent).Value);
        Assert.Equal(Notice.Level.Error, notice.NoticeLevel);
        Assert.Equal(h.Thread.Provider.DisplayName + " stopped unexpectedly.", notice.Message);
    }
}
