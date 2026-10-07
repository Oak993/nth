using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class ThreadAccountHostTests
{
    [Fact]
    public void A_legacy_provider_conversation_keeps_its_original_account_after_the_priority_order_changes()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek,
            configure: thread => thread with { ProviderSessionID = "legacy-native", ProviderAccountID = null },
            settings: app => app.Settings.DeepseekAPIKeyInput = "offline-outgoing-briefing");
        var briefing = new ThreadRuntimeScriptedSession();
        rig.H.Main.Loop.Run(async () =>
        {
            var legacy = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var newer = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            rig.App.Settings.Accounts.Reorder(ProviderKind.Deepseek, [newer.Id, legacy.Id]);
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries, (configuration, id) =>
            {
                Assert.Equal(legacy.Id, id);
                return new(configuration, briefing);
            });
            Assert.Equal(legacy.Id.ToString("D"), host.ActiveAccountId);
            briefing.SendScript = input =>
            {
                briefing.Emit(new ProviderEvent.MessageCompleted("brief", "Legacy briefing."));
                briefing.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                return Task.CompletedTask;
            };
            Assert.Equal("Legacy briefing.", await host.RequestBriefingAsync(await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), CancellationToken.None));
        }, AppModelHarness.Bound);
        Assert.Equal(1, briefing.StopCalls);
        Assert.Empty(rig.Session.Inputs);
    }

    [Fact]
    public void A_change_to_inherited_destination_defaults_during_readback_invalidates_the_prepared_session()
    {
        using var rig = new ThreadRuntimeTurnRig(settings: app => app.Settings.DeepseekAPIKeyInput = "offline-host-destination");
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.Loop.Run(async () =>
        {
            var destination = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            rig.App.Settings.Accounts.SetDefaults(destination.Id, new() { Model = "before", Effort = "low", FastMode = false });
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries, (configuration, id) => new(configuration, incoming));
            var target = new SwitchTarget(new(destination.Id.ToString("D"), destination.Service.Id, "Destination"));
            incoming.SendScript = input =>
            {
                if (incoming.Inputs.Count == 2)
                    rig.App.Settings.Accounts.SetDefaults(destination.Id, new() { Model = "after", Effort = "high", FastMode = true });
                incoming.Emit(new ProviderEvent.MessageCompleted("readback", "Ready."));
                incoming.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                return Task.CompletedTask;
            };
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), true, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.SwitchAsync(standby, target, false, CancellationToken.None));
            Assert.Equal(ProviderKind.Claude, rig.App.Thread(rig.InitialThread.Id)!.Provider);
            Assert.Equal(0, rig.Session.StopCalls);
        }, AppModelHarness.Bound);
        Assert.Equal(1, incoming.StopCalls);
    }

    [Fact]
    public void A_new_preparation_during_final_readback_invalidates_the_previous_owned_candidate()
    {
        using var rig = new ThreadRuntimeTurnRig(settings: app => app.Settings.DeepseekAPIKeyInput = "offline-host-destination");
        var first = new ThreadRuntimeScriptedSession();
        var second = new ThreadRuntimeScriptedSession();
        IStandbySession? replacement = null;
        rig.H.Main.Loop.Run(async () =>
        {
            var destination = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var other = rig.App.Settings.Accounts.ForService(ProviderKind.Meta)[0];
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries,
                (configuration, id) => new(configuration, id == destination.Id ? first : second));
            var target = new SwitchTarget(new(destination.Id.ToString("D"), destination.Service.Id, "First"));
            var next = new SwitchTarget(new(other.Id.ToString("D"), other.Service.Id, "Second"));
            first.SendScript = async input =>
            {
                if (first.Inputs.Count == 2) replacement = await host.StartStandbyAsync(next, CancellationToken.None);
                first.Emit(new ProviderEvent.MessageCompleted("readback", "Ready."));
                first.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
            };
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), true, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.SwitchAsync(standby, target, false, CancellationToken.None));
            Assert.Equal(ProviderKind.Claude, rig.App.Thread(rig.InitialThread.Id)!.Provider);
            Assert.Equal(0, rig.Session.StopCalls);
            if (replacement is not null) await replacement.DisposeAsync();
        }, AppModelHarness.Bound);
        Assert.Equal(1, first.StopCalls);
        Assert.Equal(1, second.StopCalls);
    }

    [Fact]
    public void The_host_projects_the_persisted_pill_and_step_and_adopts_the_steps_exact_settings()
    {
        using var rig = new ThreadRuntimeTurnRig(settings: app => app.Settings.DeepseekAPIKeyInput = "offline-host-destination");
        var pills = new PillStore();
        var incoming = new ThreadRuntimeScriptedSession();
        ThreadAccountHost host = null!;
        SwitchTarget target = null!;
        rig.H.Main.OnMain(() =>
        {
            var source = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0];
            var destination = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var pill = pills.Create("My pill", [new(source.Id.ToString("D")), new(destination.Id.ToString("D"), "chosen-model", "high", true)]);
            rig.App.UpdateThread(rig.InitialThread.Id, thread => thread.WithPillRelay(new(pill.Id, 0, source.Id.ToString("D"))));
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            incoming.SendScript = input =>
            {
                incoming.Emit(new ProviderEvent.MessageCompleted("readback", "Ready."));
                incoming.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                return Task.CompletedTask;
            };
            host = new(rig.App, rig.Runtime, pills, summaries, (configuration, id) => new(configuration, incoming));
            Assert.Equal(source.Id.ToString("D"), host.ActiveAccountId);
            Assert.Equal(pill.Id, host.Pill!.Id);
            Assert.Equal(0, host.StepIndex);
            target = new(new(destination.Id.ToString("D"), ProviderKind.Deepseek.Id, "Destination"), 1, pill.Effective(1));
        });
        rig.H.Main.Loop.Run(async () =>
        {
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            var summary = await new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct)).BuildAsync(rig.InitialThread.Id, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, summary, "Outgoing briefing"), true, CancellationToken.None);
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        var thread = rig.Thread;
        Assert.Equal(target.Account.Id, thread.ProviderAccountID!.Value.ToString("D"));
        Assert.Equal(1, thread.PillStepIndex);
        Assert.Equal(target.Account.Id, thread.PillRelay!.AccountId);
        Assert.Equal("chosen-model", thread.Model);
        Assert.Equal("high", thread.Effort);
        Assert.True(thread.FastMode);
        Assert.Equal(1, rig.Session.StopCalls);
        Assert.Equal(0, incoming.StopCalls);
    }

    [Fact]
    public void The_final_tail_is_read_and_acknowledged_before_adoption_and_a_second_source_change_is_refreshed_again()
    {
        using var rig = new ThreadRuntimeTurnRig(settings: app => app.Settings.DeepseekAPIKeyInput = "offline-host-destination");
        rig.Send("Keep 17 and Café.");
        var incoming = new ThreadRuntimeScriptedSession();
        ThreadAccountHost host = null!;
        SwitchTarget target = null!;
        HandshakeSummaryAdapter summaries = null!;
        rig.H.Main.OnMain(() =>
        {
            var destination = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            summaries = new((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            host = new(rig.App, rig.Runtime, new(), summaries, (configuration, id) => new(configuration, incoming));
            target = new(new(destination.Id.ToString("D"), ProviderKind.Deepseek.Id, "Destination"));
            incoming.SendScript = input =>
            {
                if (incoming.Inputs.Count == 2) rig.Session.Emit(new ProviderEvent.MessageCompleted("last", "AFTER-FIRST-FINAL-ACK"));
                incoming.Emit(new ProviderEvent.MessageCompleted("readback", "Keep 17 and Café."));
                incoming.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                return Task.CompletedTask;
            };
        });
        IStandbySession standby = null!;
        rig.H.Main.Loop.Run(async () =>
        {
            standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), true, CancellationToken.None);
        }, AppModelHarness.Bound);
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.MessageCompleted("tail", "LATEST-TAIL")));
        rig.Complete();
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.IsAccountSwitchSafe, AppModelHarness.Bound);
        rig.H.Main.Loop.Run(() => host.SwitchAsync(standby, target, false, CancellationToken.None), AppModelHarness.Bound);
        Assert.Contains(incoming.Inputs, input => input.Text.Contains("LATEST-TAIL", StringComparison.Ordinal));
        Assert.Contains("AFTER-FIRST-FINAL-ACK", incoming.Inputs[^1].Text, StringComparison.Ordinal);
        Assert.Equal(target.Account.Id, rig.Thread.ProviderAccountID!.Value.ToString("D"));
        rig.H.Main.Loop.Run(async () => await standby.DisposeAsync(), AppModelHarness.Bound);
        Assert.Equal(0, incoming.StopCalls);
    }

    [Fact]
    public void A_destination_disabled_during_the_final_readback_is_not_taken_or_adopted()
    {
        using var rig = new ThreadRuntimeTurnRig(settings: app => app.Settings.DeepseekAPIKeyInput = "offline-host-destination");
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.Loop.Run(async () =>
        {
            var destination = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries, (configuration, id) => new(configuration, incoming));
            var target = new SwitchTarget(new(destination.Id.ToString("D"), ProviderKind.Deepseek.Id, "Destination"));
            incoming.SendScript = input =>
            {
                if (incoming.Inputs.Count == 2) rig.App.Settings.Accounts.SetEnabled(destination.Id, false);
                incoming.Emit(new ProviderEvent.MessageCompleted("readback", "Ready."));
                incoming.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                return Task.CompletedTask;
            };
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), true, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.SwitchAsync(standby, target, false, CancellationToken.None));
            Assert.True(incoming.IsRunning);
            Assert.Equal(0, rig.Session.StopCalls);
            Assert.Equal(ProviderKind.Claude, rig.App.Thread(rig.InitialThread.Id)!.Provider);
        }, AppModelHarness.Bound);
        Assert.Equal(1, incoming.StopCalls);
    }

    [Fact]
    public void The_outgoing_briefing_uses_a_private_restricted_session_on_the_same_account_and_never_sends_on_the_live_reply()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek,
            configure: thread => thread with { Model = "active-model", Effort = "active-effort", FastMode = true },
            settings: app => app.Settings.DeepseekAPIKeyInput = "offline-outgoing-briefing");
        rig.Send("Live work continues.");
        var briefing = new ThreadRuntimeScriptedSession();
        Guid? used = null;
        rig.H.Main.Loop.Run(async () =>
        {
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries, (configuration, id) =>
            {
                used = id;
                Assert.Equal("active-model", configuration.Model);
                Assert.Equal("active-effort", configuration.Effort);
                Assert.True(configuration.FastMode);
                return new(configuration, briefing);
            });
            briefing.SendScript = input =>
            {
                Assert.True(input.IsFinalReport);
                briefing.Emit(new ProviderEvent.MessageCompleted("brief", "Actual outgoing meaning and pitfalls."));
                briefing.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                return Task.CompletedTask;
            };
            Assert.Equal("Actual outgoing meaning and pitfalls.", await host.RequestBriefingAsync(await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), CancellationToken.None));
            Assert.Equal(rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id, used);
        }, AppModelHarness.Bound);
        Assert.Single(rig.Session.Inputs);
        Assert.Equal(0, rig.Session.StopCalls);
        Assert.Equal(1, briefing.StopCalls);
        Assert.Equal(RuntimePhase.Running, rig.H.Main.OnMain(() => rig.Runtime.Phase));
    }

    [Fact]
    public void A_candidate_cannot_be_adopted_using_a_different_targets_availability()
    {
        using var rig = new ThreadRuntimeTurnRig(settings: app => app.Settings.DeepseekAPIKeyInput = "offline-host-destination");
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.Loop.Run(async () =>
        {
            var destination = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var other = rig.App.Settings.Accounts.ForService(ProviderKind.Meta)[0];
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries, (configuration, id) => new(configuration, incoming));
            var preparedTarget = new SwitchTarget(new(destination.Id.ToString("D"), destination.Service.Id, "Prepared"));
            var otherTarget = new SwitchTarget(new(other.Id.ToString("D"), other.Service.Id, "Other"));
            incoming.SendScript = input =>
            {
                incoming.Emit(new ProviderEvent.MessageCompleted("readback", "Ready."));
                incoming.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                return Task.CompletedTask;
            };
            await using var standby = await host.StartStandbyAsync(preparedTarget, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), true, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.SwitchAsync(standby, otherTarget, false, CancellationToken.None));
            Assert.Equal(ProviderKind.Claude, rig.App.Thread(rig.InitialThread.Id)!.Provider);
            Assert.Equal(0, rig.Session.StopCalls);
        }, AppModelHarness.Bound);
        Assert.Equal(1, incoming.StopCalls);
    }

    [Fact]
    public void Cancellation_while_the_final_commit_is_queued_preserves_the_source_and_standby_ownership()
    {
        using var rig = new ThreadRuntimeTurnRig(settings: app => app.Settings.DeepseekAPIKeyInput = "offline-host-destination");
        using var cancellation = new CancellationTokenSource();
        var incoming = new ThreadRuntimeScriptedSession();
        var executor = new QueuedCancellationExecutor(rig.H.Main.Loop, cancellation);
        var source = new HandshakeSummarySource(new(new(rig.InitialThread.Id), "Task", "Diff"), rig.H.Scratch);
        var builds = 0;
        rig.H.Main.Loop.Run(async () =>
        {
            var destination = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var summaries = new HandshakeSummaryAdapter((id, ct) =>
            {
                if (++builds == 3) executor.Armed = true;
                return Task.FromResult(source);
            });
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries, (configuration, id) => new(configuration, incoming));
            var target = new SwitchTarget(new(destination.Id.ToString("D"), destination.Service.Id, "Destination"));
            incoming.SendScript = input =>
            {
                incoming.Emit(new ProviderEvent.MessageCompleted("readback", "Ready."));
                incoming.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                return Task.CompletedTask;
            };
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), true, CancellationToken.None);
            using var scope = MainActor.Executor.Override(executor);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.SwitchAsync(standby, target, false, cancellation.Token));
            Assert.True(cancellation.IsCancellationRequested);
            Assert.Equal(ProviderKind.Claude, rig.App.Thread(rig.InitialThread.Id)!.Provider);
            Assert.Equal(0, rig.Session.StopCalls);
            Assert.Equal(0, incoming.StopCalls);
        }, AppModelHarness.Bound);
        Assert.Equal(1, incoming.StopCalls);
    }
}
