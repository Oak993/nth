using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Port.MultiAccount;
using Xunit;

namespace Marrow.App.Tests.Port;

[Collection("MCPHub processes")]
public sealed class C7InstallerAccountResumeReviewTests
{
    // ED-NEW-inst-c7-5: the real account continuation entry must guard before changing the original turn/session.
    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_settled_account_reply_cannot_restart_while_idle_quit_holds_admission()
    {
        using var shell = new C7OfflineShell();
        using var reader = MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("Continue this exact recorded reply.");
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
            var configuration = new SessionConfiguration
            {
                Provider = ProviderKind.Claude, AccountId = account.Id,
                WorkingDirectory = SwiftURL.Create("marrow-native-path:" + rig.Directory.Replace('\\', '/'))!,
                Environment = ValueMap<string, string>.Empty, Model = "offline-model",
                RuntimeMode = rig.App.Thread(rig.InitialThread.Id)!.RuntimeMode,
                InteractionMode = rig.App.Thread(rig.InitialThread.Id)!.InteractionMode,
            };
            var recorder = new ShellRecorder();
            var host = new NthControlHost(new AppShell(new AppDelegate { Model = rig.App }, recorder.Exit, recorder.AfterWillTerminate),
                processId: 4242, executable: @"C:\Apps\nth\current\nth.exe");
            var original = rig.App.Thread(rig.InitialThread.Id);
            Assert.Equal(NthControlReply.Ok, host.Answer(NthControlRequest.QuitIfIdle).Response.Reply);

            Assert.Throws<InvalidOperationException>(() => rig.Runtime.AdoptHandshakeSession(incoming, "offline-incoming", configuration, resumeReply: true));

            Assert.Equal(original, rig.App.Thread(rig.InitialThread.Id));
            Assert.Equal(TurnStatus.Failed, Assert.Single(rig.Runtime.Turns).Status);
            Assert.False(rig.Runtime.IsRunning);
            Assert.Empty(incoming.Inputs);
            Assert.Empty(recorder.Calls);
        });
    }
}
