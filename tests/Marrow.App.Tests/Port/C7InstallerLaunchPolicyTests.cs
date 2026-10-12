using Marrow.App.Port.Skills;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Providers;
using Xunit;

namespace Marrow.App.Tests.Port;

[Collection("MCPHub processes")]
public sealed class C7InstallerLaunchPolicyTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Stop_prevents_the_actual_queued_title_from_reaching_the_shell_boundary()
    {
        using var shell = new C7OfflineShell();
        using var reader = MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(install: false,
            configure: thread => thread with { Title = ChatThread.Untitled, HasCustomTitle = false });
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(provider =>
            provider == ProviderKind.Claude ? SwiftURL.Create("file:///offline/fake-cli") : null);
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.TextGeneration = TextGenerationChoice.Claude;
            typeof(ProviderRegistry).GetProperty(nameof(ProviderRegistry.Statuses))!.SetValue(rig.App.Providers,
                rig.App.Providers.Statuses.Mutating(rows => rows[ProviderKind.Claude] =
                    new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }));
            Assert.NotNull(rig.App.TextEngine(ProviderKind.Claude));
            ThreadRuntimeReflection.Call<object>(rig.Runtime, "GenerateTitle", "Queued offline title", Array.Empty<Attachment>());
            Assert.Single(rig.App.PendingInstallerTurns);
            var recorder = new ShellRecorder();
            var control = new NthControlHost(new AppShell(new AppDelegate { Model = rig.App },
                () => { recorder.Exit(); rig.App.SaveBeforeQuit(); }, recorder.AfterWillTerminate),
                processId: 4242, executable: @"C:\Apps\nth\current\nth.exe");
            var answer = control.Answer(NthControlRequest.QuitAndStop with { Id = Guid.NewGuid().ToString("N") });
            Assert.Equal(NthControlReply.Ok, answer.Response.Reply);
            var prepared = answer.PrepareCommit!(CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(NthControlReply.Ok, prepared.Response.Reply);
            Assert.Null(prepared.Commit!(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(1, recorder.Exits);
        });
        rig.H.Main.Loop.RunUntil(() => !rig.App.PendingInstallerTurns.Any(), AppModelHarness.Bound);
        Assert.Empty(rig.App.PendingInstallerTurns);
        Assert.Empty(shell.Calls);
    }
    // ED-NEW-inst-c7-5: cancellation ownership must not impose the restart-only resume identity policy.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-247")]
    public void A_normal_CLI_turn_keeps_first_launch_and_lost_resume_fallback_policy(bool lostResume)
    {
        using var shell = new C7OfflineShell();
        using var skills = ProviderSkills.Prepare.Override((_, _, _, _) => Task.FromResult<Generation?>(null));
        using var reader = MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(install: false,
            configure: thread => thread with { HasCustomTitle = true, ProviderSessionID = lostResume ? "original-session" : null },
            settings: app => app.Settings.Accounts.MarkSignedIn(app.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id));
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(provider =>
            provider == ProviderKind.Claude ? SwiftURL.Create("file:///offline/fake-cli") : null);
        var launches = new List<SessionConfiguration>();
        var sessions = new List<ThreadRuntimeScriptedSession>();
        using var factory = AccountSessionFactory.Launcher.Override((_, configuration, _, _) =>
        {
            launches.Add(configuration);
            var candidate = new ThreadRuntimeScriptedSession();
            if (configuration.ResumeID is not null)
                candidate.StartScript = () => Task.FromException<string>(new ProviderError(new ProviderError.Case.Failed("no conversation found")));
            sessions.Add(candidate);
            return new(configuration, candidate);
        });
        rig.Send("Exact offline CLI prompt");
        Assert.Equal(lostResume ? 2 : 1, launches.Count);
        if (lostResume)
        {
            Assert.Equal("original-session", launches[0].ResumeID);
            Assert.Empty(sessions[0].Inputs);
            Assert.True(sessions[0].StopCalls > 0);
        }
        Assert.Null(launches[^1].ResumeID);
        Assert.Contains("Exact offline CLI prompt", Assert.Single(sessions[^1].Inputs).Text, StringComparison.Ordinal);
        Assert.Equal(RuntimePhase.Running, rig.Runtime.Phase);
        Assert.Equal("scripted-session", rig.Thread.ProviderSessionID);
        rig.H.Main.OnMain(() => sessions[^1].Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null)));
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
    }
}
