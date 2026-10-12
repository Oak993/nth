using Marrow.App.Port;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Xunit;

namespace Marrow.App.Tests.Port;

[Collection("MCPHub processes")]
public sealed class C7InstallerPrivateWorkReviewTests
{
    // ED-NEW-inst-c7-5: pending private AI work is real conversation work even with runtime Phase.Idle.
    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_pending_actual_private_briefing_keeps_idle_quit_busy()
    {
        using var shell = new C7OfflineShell();
        using var reader = MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, settings: app => app.Settings.DeepseekAPIKeyInput = "offline-source");
        var privateSession = new ThreadRuntimeScriptedSession();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        privateSession.SendScript = async _ =>
        {
            await release.Task;
            privateSession.Emit(new ProviderEvent.MessageCompleted("offline-brief", "Recorded private facts."));
            privateSession.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        };
        Task<string?>? pending = null;
        try
        {
            rig.H.Main.OnMain(() =>
            {
                var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), new C7RecordedSummary(),
                    (configuration, _) => new(configuration, privateSession));
                pending = host.RequestBriefingAsync(C7RecordedSummary.Recorded, CancellationToken.None);
            });
            rig.H.Main.Loop.RunUntil(() => privateSession.Inputs.Count == 1, AppModelHarness.Bound);
            rig.H.Main.OnMain(() =>
            {
                Assert.False(rig.Runtime.IsRunning);
                var recorder = new ShellRecorder();
                var control = Control(rig.App, recorder);
                Assert.Equal(NthControlReply.Busy, control.Answer(NthControlRequest.QuitIfIdle).Response.Reply);
                Assert.Empty(recorder.Calls);
            });
        }
        finally
        {
            release.TrySetResult();
            if (pending is not null) rig.H.Main.Loop.Run(async () => { await pending; }, AppModelHarness.Bound);
        }
    }

    // Guard the actual primed preparation before its factory; injected/warm/deferred plans have no AI turn.
    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_held_primed_standby_is_refused_before_its_private_session_factory()
    {
        using var shell = new C7OfflineShell();
        using var reader = MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, settings: app =>
        {
            app.Settings.DeepseekAPIKeyInput = "offline-source";
            app.Settings.MetaAPIKeyInput = "offline-target";
        });
        rig.H.Main.Loop.Run(async () =>
        {
            var launches = 0;
            var scripted = new ThreadRuntimeScriptedSession();
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), new C7RecordedSummary(),
                (configuration, _) => { launches++; return new(configuration, scripted); });
            var target = rig.App.Settings.Accounts.ForService(ProviderKind.Meta)[0];
            var recorder = new ShellRecorder();
            Assert.Equal(NthControlReply.Ok, Control(rig.App, recorder).Answer(NthControlRequest.QuitIfIdle).Response.Reply);
            IStandbySession? standby = null;
            Exception? error;
            try
            {
                error = await Record.ExceptionAsync(async () => standby = await host.StartStandbyAsync(
                    new(new(target.Id.ToString("D"), target.Service.Id, "Offline target")), CancellationToken.None));
            }
            finally { if (standby is not null) await standby.DisposeAsync(); }
            Assert.IsType<InvalidOperationException>(error);
            Assert.Equal(0, launches);
            Assert.Empty(scripted.Inputs);
            Assert.Empty(recorder.Calls);
        }, AppModelHarness.Bound);
    }

    // Context stays fake while dispatch forwards to the actual production account head and scripted factory.
    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_pending_actual_opinion_context_makes_idle_quit_busy_before_private_dispatch()
    {
        using var shell = new C7OfflineShell();
        using var reader = MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false, settings: app => app.Settings.DeepseekAPIKeyInput = "offline-source");
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = new List<ThreadRuntimeScriptedSession>();
        using var launch = AccountSessionFactory.Launcher.Override((_, configuration, _, _) =>
        {
            var scripted = AnsweringSession(); sessions.Add(scripted);
            return new(configuration, scripted);
        });
        LiveThreadPorts? owner = null;
        Task<Guid>? pending = null;
        try
        {
            rig.H.Main.OnMain(() =>
            {
                var target = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
                Assert.True(APIKeychain.SetKey("offline-next", target));
                owner = new LiveThreadPorts(rig.App, decorateHeads: inner => new C7ConversationContext(inner, ct => release.Task.WaitAsync(ct)));
                pending = owner.Opinions.AskAsync(rig.InitialThread.Id, "Captured private opinion", [target.Id]);
                Assert.False(pending.IsCompleted);
                Assert.Empty(sessions);
                var recorder = new ShellRecorder();
                Assert.Equal(NthControlReply.Busy, Control(rig.App, recorder).Answer(NthControlRequest.QuitIfIdle).Response.Reply);
                Assert.Empty(recorder.Calls);
            });
        }
        finally
        {
            release.TrySetResult("Recorded offline context.");
            try
            {
                if (pending is not null && owner is not null)
                    rig.H.Main.Loop.Run(async () =>
                    {
                        var round = await pending;
                        await owner.Opinions.WaitAsync(rig.InitialThread.Id, round);
                        Assert.Empty(owner.Opinions.Errors(rig.InitialThread.Id, round));
                        Assert.All(sessions, session => Assert.Single(session.Inputs));
                    }, AppModelHarness.Bound);
            }
            finally { if (owner is not null) rig.H.Main.OnMain(owner.Dispose); }
        }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_held_actual_opinion_does_not_create_a_prompt_or_dispatch_private_heads()
    {
        using var shell = new C7OfflineShell();
        using var reader = MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false, settings: app => app.Settings.DeepseekAPIKeyInput = "offline-source");
        var sessions = new List<ThreadRuntimeScriptedSession>();
        using var launch = AccountSessionFactory.Launcher.Override((_, configuration, _, _) =>
        {
            var scripted = AnsweringSession(); sessions.Add(scripted);
            return new(configuration, scripted);
        });
        rig.H.Main.Loop.Run(async () =>
        {
            var target = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            Assert.True(APIKeychain.SetKey("offline-next", target));
            using var owner = new LiveThreadPorts(rig.App, decorateHeads: inner => new C7ConversationContext(inner, _ => Task.FromResult("Recorded offline context.")));
            var recorder = new ShellRecorder();
            Assert.Equal(NthControlReply.Ok, Control(rig.App, recorder).Answer(NthControlRequest.QuitIfIdle).Response.Reply);
            Guid? round = null;
            var error = await Record.ExceptionAsync(async () => round = await owner.Opinions.AskAsync(rig.InitialThread.Id, "Held private opinion", [target.Id]));
            if (round is { } started)
            {
                await owner.Opinions.WaitAsync(rig.InitialThread.Id, started);
                Assert.Empty(owner.Opinions.Errors(rig.InitialThread.Id, started));
            }
            Assert.IsType<InvalidOperationException>(error);
            Assert.Empty(owner.Opinions.Rounds(rig.InitialThread.Id));
            Assert.Empty(sessions);
            Assert.Empty(rig.Runtime.Entries);
            Assert.Empty(recorder.Calls);
        }, AppModelHarness.Bound);
    }

    private static NthControlHost Control(AppModel model, ShellRecorder recorder) =>
        new(new AppShell(new AppDelegate { Model = model }, recorder.Exit, recorder.AfterWillTerminate),
            processId: 4242, executable: @"C:\Apps\nth\current\nth.exe");

    private static ThreadRuntimeScriptedSession AnsweringSession()
    {
        var scripted = new ThreadRuntimeScriptedSession();
        scripted.SendScript = _ =>
        {
            scripted.Emit(new ProviderEvent.MessageCompleted("offline-answer", "Recorded offline answer."));
            scripted.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
            return Task.CompletedTask;
        };
        return scripted;
    }
}
