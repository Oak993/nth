using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Port.MultiAccount;
using Marrow.Core.Port.MultiAccount;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeRuntimeAdoptionTests
{
    [Fact]
    public void Atomic_cross_service_adoption_keeps_the_explicit_account_step_and_launch_settings_and_ignores_old_callbacks()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.OnMain(() =>
        {
            var oldCallback = rig.Session.OnEvent!;
            var account = rig.App.Settings.Accounts.ForService(ProviderKind.Codex)[0];
            var relay = new PillThreadState(Guid.NewGuid(), 1, account.Id.ToString("D"));
            rig.Runtime.AdoptHandshakeSession(incoming, "incoming-session", Config(rig, account.Id, ProviderKind.Codex) with
                { Model = "destination-model", Effort = "high", FastMode = true }, relay);
            var thread = rig.App.Thread(rig.InitialThread.Id)!;
            Assert.Equal(ProviderKind.Codex, thread.Provider);
            Assert.Equal(account.Id, thread.ProviderAccountID);
            Assert.Equal("incoming-session", thread.ProviderSessionID);
            Assert.Equal("destination-model", thread.Model);
            Assert.Equal("high", thread.Effort);
            Assert.True(thread.FastMode);
            Assert.Equal(relay, thread.PillRelay);
            Assert.Equal(1, rig.Session.StopCalls);
            oldCallback(new ProviderEvent.MessageCompleted("late", "must not appear"));
            Assert.DoesNotContain(rig.Runtime.Entries, entry => entry.Id == "late");
        });
        rig.Send("Use the adopted session.");
        Assert.Single(incoming.Inputs);
        Assert.Equal(0, incoming.StartCalls);
    }

    [Fact]
    public void Disabled_or_wrong_service_destination_is_rejected_without_stopping_or_changing_the_source()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            var account = rig.App.Settings.Accounts.Add(ProviderKind.Claude);
            var original = rig.App.Thread(rig.InitialThread.Id)!;
            var incoming = new ThreadRuntimeScriptedSession();
            rig.App.Settings.Accounts.SetEnabled(account.Id, false);
            Assert.Throws<InvalidOperationException>(() => rig.Runtime.AdoptHandshakeSession(incoming, "incoming", Config(rig, account.Id, ProviderKind.Claude)));
            rig.App.Settings.Accounts.SetEnabled(account.Id, true);
            Assert.Throws<InvalidOperationException>(() => rig.Runtime.AdoptHandshakeSession(incoming, "incoming", Config(rig, account.Id, ProviderKind.Codex)));
            Assert.Equal(original, rig.App.Thread(rig.InitialThread.Id)!);
            Assert.Equal(0, rig.Session.StopCalls);
        });
    }

    [Fact]
    public void Generic_provider_menu_changes_still_clear_the_old_binding()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            var account = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0];
            rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with { ProviderAccountID = account.Id });
            rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with { Provider = ProviderKind.Codex });
            Assert.Null(rig.App.Thread(rig.InitialThread.Id)!.ProviderAccountID);
        });
    }

    [Fact]
    public void Handshake_ownership_clears_the_legacy_limit_note_so_an_old_reset_cannot_enqueue_a_new_turn()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("Keep going only after I choose.");
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.UsageLimit(DateTimeOffset.UtcNow.AddHours(1)));
            rig.App.AutoContinue.TakeLimitOwnership(rig.InitialThread.Id);
            Assert.False(rig.App.AutoContinue.TurnFinished(rig.InitialThread.Id, TurnStatus.Failed, false));
            Assert.Empty(rig.Runtime.FollowUps);
        });
    }

    [Fact]
    public void A_limited_reply_continues_in_the_original_bubble_and_turn_and_preserves_all_segments_after_persistence()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("Finish 17 and Café.");
        Guid originalTurn = default;
        string? originalUser = null;
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.MessageCompleted("partial", "Original prefix. "));
            originalTurn = rig.Runtime.Turns[0].Id;
            originalUser = rig.Runtime.Turns[0].UserItemID;
            rig.Session.Emit(new ProviderEvent.UsageLimit(null));
            rig.App.AutoContinue.TakeLimitOwnership(rig.InitialThread.Id);
        });
        rig.Complete(TurnStatus.Failed);
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.OnMain(() =>
        {
            var account = rig.App.Settings.Accounts.Add(ProviderKind.Claude);
            rig.Runtime.AdoptHandshakeSession(incoming, "incoming", Config(rig, account.Id, ProviderKind.Claude), resumeReply: true);
        });
        rig.H.Main.Loop.RunUntil(() => incoming.Inputs.Count == 1, AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            incoming.Emit(new ProviderEvent.MessageDelta("new-one", "First tail. "));
            incoming.Emit(new ProviderEvent.MessageCompleted("new-one", ""));
            incoming.Emit(new ProviderEvent.MessageCompleted("new-two", "Final tail."));
            incoming.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        });
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.IsAccountSwitchSafe, AppModelHarness.Bound);
        var history = rig.H.Main.OnMain(() => rig.Runtime.SnapshotForPersistence());
        var roundtrip = System.Text.Json.JsonSerializer.Deserialize<ThreadDocument>(System.Text.Json.JsonSerializer.Serialize(history))!;
        Assert.Single(roundtrip.Turns);
        Assert.Equal(originalTurn, roundtrip.Turns[0].Id);
        Assert.Equal(originalUser, roundtrip.Turns[0].UserItemID);
        Assert.Single(roundtrip.Items, item => item.TimelineItemContent is TimelineItem.Content.User);
        var answer = Assert.Single(roundtrip.Items, item => item.TimelineItemContent is TimelineItem.Content.Assistant);
        Assert.Equal("partial", answer.Id);
        Assert.Equal("Original prefix. First tail. Final tail.", Assert.IsType<TimelineItem.Content.Assistant>(answer.TimelineItemContent).Value.Text);
        Assert.Single(roundtrip.Items, item => item.TimelineItemContent is TimelineItem.Content.TurnEnd);
    }

    [Fact]
    public void Reusing_the_current_provider_as_a_standby_is_rejected_without_stopping_it()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            var account = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0];
            Assert.Throws<InvalidOperationException>(() => rig.Runtime.AdoptHandshakeSession(rig.Session, "same", Config(rig, account.Id, ProviderKind.Claude)));
            Assert.Equal(0, rig.Session.StopCalls);
        });
    }

    [Fact]
    public void Journal_replay_keeps_source_and_destination_tools_separate_when_their_provider_ids_match()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var incoming = Interrupted(rig, source =>
        {
            source.Emit(new ProviderEvent.ToolStarted("same", new() { ToolCallKind = ToolCall.Kind.Other, Title = "source" }));
            source.Emit(new ProviderEvent.ToolOutput("same", "SOURCE " + new string('s', 70_000) + "SOURCE-TAIL"));
            source.Emit(new ProviderEvent.ToolUpdated("same", new() { Status = ToolCall.Status.Completed }));
        });
        rig.H.Main.OnMain(() =>
        {
            incoming.Emit(new ProviderEvent.ToolStarted("same", new() { ToolCallKind = ToolCall.Kind.Other, Title = "destination" }));
            incoming.Emit(new ProviderEvent.ToolOutput("same", "DESTINATION"));
            incoming.Emit(new ProviderEvent.ToolUpdated("same", new() { Status = ToolCall.Status.Completed }));
            incoming.Emit(new ProviderEvent.MessageCompleted("reply", "Done."));
            var restored = ReadCheckpoint(rig);
            var source = Assert.IsType<TimelineItem.Content.Tool>(Assert.Single(restored.History.Items, item => item.Id == "same").TimelineItemContent);
            Assert.StartsWith("SOURCE ", source.Value.Output, StringComparison.Ordinal);
            Assert.EndsWith("SOURCE-TAIL", source.Value.Output, StringComparison.Ordinal);
            Assert.Contains(restored.History.Items, item => item.Id != "same" && item.TimelineItemContent is TimelineItem.Content.Tool tool && tool.Value.Output == "DESTINATION");
        });
    }

    [Fact]
    public void A_streaming_checkpoint_counts_the_continuation_once_and_preserves_the_raw_original_prefix()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var incoming = Interrupted(rig, source => source.Emit(new ProviderEvent.MessageCompleted("partial", "Café—17. ")));
        rig.H.Main.OnMain(() =>
        {
            incoming.Emit(new ProviderEvent.MessageDelta("partial", "TAIL"));
            var restored = ReadCheckpoint(rig);
            var answer = Assert.IsType<TimelineItem.Content.Assistant>(Assert.Single(restored.History.Items, item => item.Id == "partial").TimelineItemContent);
            Assert.Equal("Café—17. TAIL", answer.Value.Text);
            incoming.Emit(new ProviderEvent.MessageCompleted("partial", "TAIL"));
            answer = Assert.IsType<TimelineItem.Content.Assistant>(Assert.Single(ReadCheckpoint(rig).History.Items, item => item.Id == "partial").TimelineItemContent);
            Assert.Equal("Café—17. TAIL", answer.Value.Text);
        });
    }

    [Fact]
    public void Continued_plan_approval_resolves_its_aliased_plan_without_changing_the_provider_request_id()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var incoming = Interrupted(rig);
        rig.H.Main.OnMain(() =>
        {
            incoming.Emit(new ProviderEvent.PlanCompleted("p", "Plan 17"));
            var option = new ApprovalRequest.Option("approve", "Approve", ApprovalRequest.Option.Role.Approve);
            incoming.Emit(new ProviderEvent.Approval(new("request", ApprovalRequest.Kind.Plan, "Plan", new([option]), ToolItemID: "p")));
            var request = Assert.Single(rig.Runtime.Approvals);
            Assert.Equal("request", request.Id);
            rig.Runtime.Resolve(request, option);
            var plan = Assert.IsType<TimelineItem.Content.Plan>(Assert.Single(rig.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.Plan).Item.TimelineItemContent);
            Assert.Equal(ProposedPlan.State.Accepted, plan.Value.ProposedPlanState);
        });
    }

    [Fact]
    public void A_completed_continuation_clears_the_interrupted_reply_state()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var incoming = Interrupted(rig);
        rig.H.Main.OnMain(() => incoming.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null)));
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.IsAccountSwitchSafe, AppModelHarness.Bound);
        Assert.False(rig.H.Main.OnMain(() => rig.Runtime.HandshakeReplyInterrupted));
    }

    [Theory]
    [InlineData(true, "priority")]
    [InlineData(false, "default")]
    public void A_continued_Codex_reply_uses_the_selected_models_fast_service_tier(bool fast, string expected)
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("Continue my exact reply.");
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.UsageLimit(null));
            rig.App.AutoContinue.TakeLimitOwnership(rig.InitialThread.Id);
        });
        rig.Complete(TurnStatus.Failed);
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.IsAccountSwitchSafe, AppModelHarness.Bound);
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Providers.UpdateCatalog(new([new ModelOption("fast-model", "Fast model", fastTier: "priority")]), ProviderKind.Codex);
            var account = rig.App.Settings.Accounts.ForService(ProviderKind.Codex)[0];
            rig.Runtime.AdoptHandshakeSession(incoming, "incoming", Config(rig, account.Id, ProviderKind.Codex) with { Model = "fast-model", FastMode = fast }, resumeReply: true);
        });
        rig.H.Main.Loop.RunUntil(() => incoming.Inputs.Count == 1, AppModelHarness.Bound);
        Assert.Equal(expected, incoming.Inputs[0].ServiceTier);
    }

    [Fact]
    public void A_queued_continuation_does_not_send_after_the_runtime_releases_the_destination_session()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("Continue my exact reply.");
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.UsageLimit(null));
            rig.App.AutoContinue.TakeLimitOwnership(rig.InitialThread.Id);
        });
        rig.Complete(TurnStatus.Failed);
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.IsAccountSwitchSafe, AppModelHarness.Bound);
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.OnMain(() =>
        {
            var account = rig.App.Settings.Accounts.Add(ProviderKind.Claude);
            rig.Runtime.AdoptHandshakeSession(incoming, "incoming", Config(rig, account.Id, ProviderKind.Claude), resumeReply: true);
            rig.Runtime.StopSession();
        });
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.IsAccountSwitchSafe, AppModelHarness.Bound);
        Assert.Empty(incoming.Inputs);
    }

    private static ThreadRuntimeScriptedSession Interrupted(ThreadRuntimeTurnRig rig, Action<ThreadRuntimeScriptedSession>? prepare = null)
    {
        rig.Send("Continue my exact reply.");
        rig.H.Main.OnMain(() =>
        {
            prepare?.Invoke(rig.Session);
            rig.Session.Emit(new ProviderEvent.UsageLimit(null));
            rig.App.AutoContinue.TakeLimitOwnership(rig.InitialThread.Id);
        });
        rig.Complete(TurnStatus.Failed);
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.IsAccountSwitchSafe, AppModelHarness.Bound);
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.OnMain(() =>
        {
            var account = rig.App.Settings.Accounts.Add(ProviderKind.Claude);
            rig.Runtime.AdoptHandshakeSession(incoming, "incoming", Config(rig, account.Id, ProviderKind.Claude), resumeReply: true);
        });
        rig.H.Main.Loop.RunUntil(() => incoming.Inputs.Count == 1, AppModelHarness.Bound);
        return incoming;
    }

    private static HandoffContext ReadCheckpoint(ThreadRuntimeTurnRig rig) =>
        System.Text.Json.JsonSerializer.Deserialize<HandoffContext>(File.ReadAllText(rig.Runtime.SaveHandshakeCheckpoint().HandoffFile))!;

    private static SessionConfiguration Config(ThreadRuntimeTurnRig rig, Guid accountId, ProviderKind provider) => new()
    {
        Provider = provider, AccountId = accountId, WorkingDirectory = SwiftURL.Create("marrow-native-path:" + rig.Directory.Replace('\\', '/'))!,
        Environment = ValueMap<string, string>.Empty, Model = "offline-model", RuntimeMode = rig.App.Thread(rig.InitialThread.Id)!.RuntimeMode, InteractionMode = rig.App.Thread(rig.InitialThread.Id)!.InteractionMode,
    };
}
