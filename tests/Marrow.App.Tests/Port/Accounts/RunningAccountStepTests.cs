using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class RunningAccountStepTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_real_API_step_waits_for_the_edit_then_continues_the_original_bubble_or_keeps_the_source_on_revocation(bool revoke)
    {
        Guid sourceId = default;
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(new()
        {
            PlanName = "Pro", Windows = new([new() { Id = "five", Title = "5-hour limit", Percent = key.IsDefault ? 95 : 10, DurationMinutes = 300 }]),
        }));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false,
            configure: thread => thread with { RuntimeMode = RuntimeMode.FullAccess },
            settings: app => app.Settings.DeepseekAPIKeyInput = "offline-step-source");
        Guid target = default;
        rig.H.Main.OnMain(() =>
        {
            sourceId = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id;
            var next = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            target = next.Id;
            Assert.True(APIKeychain.SetKey("offline-step-destination", next));
            rig.App.Settings.AccountHandshakeEnabled = true;
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.Bring, DecisionValues.MarrowSummary);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.MidReply, DecisionValues.Finish);
        });
        AccountStepSession source = null!;
        ThreadRuntimeScriptedSession? destination = null;
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
        {
            var prepared = registry.PrepareSession(configuration, id, useAccountDefaults: false);
            if (!fresh) { Assert.Equal(sourceId, id); source = new(prepared); return new(prepared, source); }
            var privateSession = new ThreadRuntimeScriptedSession();
            if (id == target) destination = privateSession;
            privateSession.SendScript = input =>
            {
                if (revoke && id == target && privateSession.Inputs.Count > 1
                    && input.Text.StartsWith("[Nth Handshake] Read this recorded context", StringComparison.Ordinal)) rig.App.Settings.AccountHandshakeEnabled = false;
                privateSession.Emit(new ProviderEvent.MessageCompleted("ack", input.IsFinalReport ? "Ready: finished-step.txt; continue the original task." : "Destination tail."));
                privateSession.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                return Task.CompletedTask;
            };
            return new(prepared, privateSession);
        });
        var send = rig.StartTurn("Complete this edit, read it, then continue.");
        rig.H.Main.Loop.RunUntil(() => source is not null && source.Editing, AppModelHarness.Bound);
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(HandshakePhase.InSync, rig.H.Main.OnMain(() => rig.App.AccountRouting.State(rig.InitialThread.Id)!.To));
        Assert.NotNull(destination);
        Assert.True(source.IsRunning);
        Assert.False(File.Exists(Path.Combine(rig.Directory, "finished-step.txt")));
        Assert.NotEqual(target, rig.Thread.ProviderAccountID);
        Assert.False(rig.H.Main.OnMain(() => rig.Runtime.IsAccountSwitchSafe));
        var turnId = rig.H.Main.OnMain(() => Assert.Single(rig.Runtime.Turns).Id);
        rig.H.Main.OnMain(() => source.EditRelease.SetResult());
        rig.Finish(send);
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal("Completed once.", File.ReadAllText(Path.Combine(rig.Directory, "finished-step.txt")));
        Assert.Equal(new[] { "edit", "read" }, source.Executed);
        Assert.Equal(revoke ? 2 : 1, source.Rounds);
        if (revoke) Assert.NotEqual(target, rig.Thread.ProviderAccountID);
        else
        {
            Assert.Equal(target, rig.Thread.ProviderAccountID);
            Assert.False(source.IsRunning);
            Assert.Contains(destination.Inputs, input => !input.IsFinalReport && input.Text.Contains("Continue the interrupted reply", StringComparison.Ordinal));
        }
        var history = rig.H.Main.OnMain(() => rig.Runtime.SnapshotForPersistence());
        Assert.Equal(turnId, Assert.Single(history.Turns).Id);
        Assert.Equal(TurnStatus.Completed, history.Turns[0].Status);
        Assert.Single(history.Items, item => item.TimelineItemContent is TimelineItem.Content.User);
        var answer = Assert.Single(history.Items, item => item.TimelineItemContent is TimelineItem.Content.Assistant);
        Assert.Equal("source-prefix", answer.Id);
        Assert.Equal(revoke ? "Original prefix. Original continuation." : "Original prefix. Destination tail.", Assert.IsType<TimelineItem.Content.Assistant>(answer.TimelineItemContent).Value.Text);
        Assert.Equal(2, history.Items.Count(item => item.TimelineItemContent is TimelineItem.Content.Tool));
        Assert.Single(history.Items, item => item.TimelineItemContent is TimelineItem.Content.TurnEnd);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
    [Fact]
    public void Canceling_the_final_step_acknowledgement_releases_the_pause_without_losing_the_original_turn()
    {
        Guid sourceId = default;
        var finalAcknowledgement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitingForFinal = false;
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(new()
        {
            PlanName = "Pro", Windows = new([new() { Id = "five", Title = "5-hour limit", Percent = key.IsDefault ? 95 : 10, DurationMinutes = 300 }]),
        }));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false,
            configure: thread => thread with { RuntimeMode = RuntimeMode.FullAccess },
            settings: app => app.Settings.DeepseekAPIKeyInput = "offline-step-source");
        Guid target = default;
        rig.H.Main.OnMain(() =>
        {
            sourceId = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id;
            var next = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            target = next.Id;
            Assert.True(APIKeychain.SetKey("offline-step-destination", next));
            rig.App.Settings.AccountHandshakeEnabled = true;
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.Bring, DecisionValues.MarrowSummary);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.MidReply, DecisionValues.Finish);
        });
        AccountStepSession source = null!;
        ThreadRuntimeScriptedSession? destination = null;
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
        {
            var prepared = registry.PrepareSession(configuration, id, useAccountDefaults: false);
            if (!fresh) { Assert.Equal(sourceId, id); source = new(prepared); return new(prepared, source); }
            var privateSession = new ThreadRuntimeScriptedSession();
            if (id == target) destination = privateSession;
            privateSession.SendScript = async input =>
            {
                if (id == target && privateSession.Inputs.Count > 1
                    && input.Text.StartsWith("[Nth Handshake] Read this recorded context", StringComparison.Ordinal))
                {
                    waitingForFinal = true;
                    await finalAcknowledgement.Task.WaitAsync(Marrow.Core.Port.SwiftTask.CancellationToken);
                }
                privateSession.Emit(new ProviderEvent.MessageCompleted("ack", input.IsFinalReport ? "Ready: finished-step.txt; continue the original task." : "Destination tail."));
                privateSession.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
            };
            return new(prepared, privateSession);
        });
        var send = rig.StartTurn("Complete this edit, read it, then continue.");
        rig.H.Main.Loop.RunUntil(() => source is not null && source.Editing, AppModelHarness.Bound);
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(HandshakePhase.InSync, rig.H.Main.OnMain(() => rig.App.AccountRouting.State(rig.InitialThread.Id)!.To));
        Assert.NotNull(destination);
        Assert.True(source.IsRunning);
        Assert.False(File.Exists(Path.Combine(rig.Directory, "finished-step.txt")));
        Assert.NotEqual(target, rig.Thread.ProviderAccountID);
        Assert.False(rig.H.Main.OnMain(() => rig.Runtime.IsAccountSwitchSafe));
        var turnId = rig.H.Main.OnMain(() => Assert.Single(rig.Runtime.Turns).Id);
        rig.H.Main.OnMain(() => source.EditRelease.SetResult());
        rig.H.Main.Loop.RunUntil(() => waitingForFinal, AppModelHarness.Bound);
        Assert.True(rig.H.Main.OnMain(() => rig.Runtime.IsAccountSwitchSafe));
        rig.H.Main.OnMain(rig.Runtime.Interrupt);
        rig.Finish(send);
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal("Completed once.", File.ReadAllText(Path.Combine(rig.Directory, "finished-step.txt")));
        Assert.Equal(new[] { "edit", "read" }, source.Executed);
        Assert.Equal(1, source.Rounds);
        Assert.NotEqual(target, rig.Thread.ProviderAccountID);
        Assert.Equal(1, destination.StopCalls);
        Assert.Equal(HandshakePhase.Idle, rig.H.Main.OnMain(() => rig.App.AccountRouting.State(rig.InitialThread.Id)!.To));
        var history = rig.H.Main.OnMain(() => rig.Runtime.SnapshotForPersistence());
        Assert.Equal(turnId, Assert.Single(history.Turns).Id);
        Assert.Equal(TurnStatus.Interrupted, history.Turns[0].Status);
        Assert.Single(history.Items, item => item.TimelineItemContent is TimelineItem.Content.User);
        var answer = Assert.Single(history.Items, item => item.TimelineItemContent is TimelineItem.Content.Assistant);
        Assert.Equal("source-prefix", answer.Id);
        Assert.Equal("Original prefix. ", Assert.IsType<TimelineItem.Content.Assistant>(answer.TimelineItemContent).Value.Text);
        Assert.Equal(2, history.Items.Count(item => item.TimelineItemContent is TimelineItem.Content.Tool));
        Assert.Single(history.Items, item => item.TimelineItemContent is TimelineItem.Content.TurnEnd);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
}
