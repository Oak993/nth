using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveAccountRestartSafetyTests
{
    [Fact]
    public void An_automatic_wait_resume_never_falls_back_to_a_fresh_CLI_conversation_without_its_context()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(provider => provider == ProviderKind.Claude ? SwiftURL.Create("file:///offline/fake-cli") : null);
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, configure: thread => thread with { ProviderSessionID = "original-session" });
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Wait);
            rig.App.Settings.AutoContinueAfterLimit = false;
        });
        var rejected = new ThreadRuntimeScriptedSession { StartScript = () => Task.FromException<string>(new ProviderError(new ProviderError.Case.Failed("no conversation found"))) };
        var fresh = new ThreadRuntimeScriptedSession();
        var launches = new List<SessionConfiguration>();
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, isFresh) =>
        {
            launches.Add(configuration);
            return new(registry.PrepareSession(configuration, id, useAccountDefaults: false), configuration.ResumeID is null ? fresh : rejected);
        });
        FinishAndExit(rig);
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromSeconds(31)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.Loop.RunUntilIdle();
        Assert.Equal("original-session", Assert.Single(launches).ResumeID);
        Assert.Equal(0, fresh.StartCalls);
        Assert.Empty(fresh.Inputs);
        Assert.Null(ThreadRuntimeReflection.Get<ProviderSession>(rig.Runtime, "session"));
        Assert.Equal("original-session", rig.Thread.ProviderSessionID);
        Assert.Equal(TurnStatus.Failed, Assert.Single(rig.Runtime.Turns).Status);
        Assert.Single(rig.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.User);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_wait_restart_cannot_commit_after_detachment_or_a_new_recorded_session_identity(bool detach)
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, configure: thread => thread with { ProviderSessionID = "original-session" });
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            Assert.True(APIKeychain.SetKey("offline-second", rig.App.Settings.Accounts.Add(ProviderKind.Deepseek)));
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Wait);
            rig.App.Settings.AutoContinueAfterLimit = false;
        });
        var start = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var candidate = new ThreadRuntimeScriptedSession { StartScript = () => start.Task };
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
            new(registry.PrepareSession(configuration, id, useAccountDefaults: false), candidate));
        FinishAndExit(rig);
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromSeconds(31)));
        rig.H.Main.Loop.RunUntil(() => candidate.StartCalls == 1, AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            if (detach) rig.Runtime.CancelPersistence();
            else rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with { ProviderSessionID = "owner-selected-session" });
            candidate.Emit(new ProviderEvent.SessionReady("late-session"));
        });
        start.SetResult("late-session");
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.Loop.RunUntilIdle();
        Assert.Null(ThreadRuntimeReflection.Get<ProviderSession>(rig.Runtime, "session"));
        Assert.Equal(detach ? "original-session" : "owner-selected-session", rig.Thread.ProviderSessionID);
        Assert.True(candidate.StopCalls > 0);
        Assert.Empty(candidate.Inputs);
        Assert.Equal(TurnStatus.Failed, Assert.Single(rig.Runtime.Turns).Status);
        Assert.Empty(http.Received);
    }

    [Fact]
    public void A_successful_automatic_CLI_resume_continues_the_original_turn_after_its_reported_reset()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(provider => provider == ProviderKind.Claude ? SwiftURL.Create("file:///offline/fake-cli") : null);
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, configure: thread => thread with { ProviderSessionID = "original-session" });
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Wait);
            rig.App.Settings.AutoContinueAfterLimit = false;
        });
        var candidate = new ThreadRuntimeScriptedSession { StartScript = () => Task.FromResult("original-session") };
        var launches = new List<SessionConfiguration>();
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
        {
            launches.Add(configuration);
            return new(registry.PrepareSession(configuration, id, useAccountDefaults: false), candidate);
        });
        FinishAndExit(rig);
        var interrupted = Assert.Single(rig.Runtime.Turns).Id;
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromSeconds(31)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.Loop.RunUntilIdle();
        Assert.Equal("original-session", Assert.Single(launches).ResumeID);
        Assert.Single(candidate.Inputs);
        Assert.Equal(interrupted, Assert.Single(rig.Runtime.Turns).Id);
        Assert.Equal(TurnStatus.Running, rig.Runtime.Turns[0].Status);
        Assert.Equal("original-session", rig.Thread.ProviderSessionID);
        Assert.Equal(0, candidate.StopCalls);
        Assert.Single(rig.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.User);
    }

    [Fact]
    public void An_automatic_CLI_resume_rejects_a_provider_that_silently_reports_a_fresh_conversation()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(provider => provider == ProviderKind.Claude ? SwiftURL.Create("file:///offline/fake-cli") : null);
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, configure: thread => thread with { ProviderSessionID = "original-session" });
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Wait);
            rig.App.Settings.AutoContinueAfterLimit = false;
        });
        var candidate = new ThreadRuntimeScriptedSession { StartScript = () => Task.FromResult("fresh-session") };
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
            new(registry.PrepareSession(configuration, id, useAccountDefaults: false), candidate));
        FinishAndExit(rig);
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromSeconds(31)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.Loop.RunUntilIdle();
        Assert.Empty(candidate.Inputs);
        Assert.True(candidate.StopCalls > 0);
        Assert.Null(ThreadRuntimeReflection.Get<ProviderSession>(rig.Runtime, "session"));
        Assert.Equal("original-session", rig.Thread.ProviderSessionID);
        Assert.Equal(TurnStatus.Failed, Assert.Single(rig.Runtime.Turns).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void An_exited_CLI_without_a_recorded_conversation_identity_cannot_start_fresh_during_automatic_wait_recovery(string? identity)
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(provider => provider == ProviderKind.Claude ? SwiftURL.Create("file:///offline/fake-cli") : null);
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, configure: thread => thread with { ProviderSessionID = identity });
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Wait);
            rig.App.Settings.AutoContinueAfterLimit = false;
        });
        var candidate = new ThreadRuntimeScriptedSession { StartScript = () => Task.FromResult(identity ?? "fresh-session") };
        var launches = 0;
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
        {
            launches++;
            return new(registry.PrepareSession(configuration, id, useAccountDefaults: false), candidate);
        });
        FinishAndExit(rig);
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromSeconds(31)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.Loop.RunUntilIdle();
        Assert.Equal(0, launches);
        Assert.Equal(0, candidate.StartCalls);
        Assert.Empty(candidate.Inputs);
        Assert.Equal(identity, rig.Thread.ProviderSessionID);
        Assert.Equal(TurnStatus.Failed, Assert.Single(rig.Runtime.Turns).Status);
        Assert.Null(ThreadRuntimeReflection.Get<ProviderSession>(rig.Runtime, "session"));
    }

    private static void FinishAndExit(ThreadRuntimeTurnRig rig)
    {
        rig.Send("Preserve this full conversation and interrupted turn.");
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.MessageCompleted("prefix", "Recorded prefix. "));
            rig.Session.Emit(new ProviderEvent.UsageLimit(rig.H.Main.Time.GetUtcNow().AddSeconds(30)));
        });
        rig.Complete(TurnStatus.Failed);
        rig.H.Main.OnMain(() =>
        {
            rig.Session.IsRunning = false;
            rig.Session.Emit(new ProviderEvent.Exited(null));
        });
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
}
