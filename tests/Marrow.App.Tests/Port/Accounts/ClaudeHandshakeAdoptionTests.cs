using System.Text;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
namespace Marrow.App.Tests.Port.Accounts;
[Trait("Rule", "R-TEST-4")]
public sealed class ClaudeHandshakeAdoptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Runtime_adoption_requires_actual_CLI_restoration_before_releasing_the_outgoing_session(bool rejected)
    {
        using var rig = new ThreadRuntimeTurnRig();
        using var version = ClaudeProviderRuntime.Version.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("2.1.284 (Claude Code)"), [])));
        using var help = ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, [], [])));
        RuntimeClaudePrimingProcess? process = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, _) => process = new(args, ""));
        ProviderSession? incoming = null;
        rig.H.Main.Loop.Run(async () =>
        {
            var account = rig.App.Settings.Accounts.Add(ProviderKind.Claude);
            rig.App.Settings.Accounts.MarkSignedIn(account.Id);
            rig.App.Settings.Accounts.SetDefaults(account.Id, new() { Model = "offline-model", FastMode = true });
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, rig.App.Settings.Pills, summaries, (config, id) =>
            {
                var launch = config with { Executable = SwiftURL.Create("/offline/claude-adoption-" + Guid.NewGuid().ToString("N"))! };
                incoming = new ClaudeSession(launch);
                return new(launch, incoming);
            });
            var target = new SwitchTarget(new(account.Id.ToString("D"), account.Service.Id, "Destination"));
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), true, CancellationToken.None);
            process!.Failure = rejected ? "restore-rejected" : "";
            if (rejected)
            {
                await Assert.ThrowsAnyAsync<Exception>(() => host.SwitchAsync(standby, target, false, CancellationToken.None));
                Assert.Equal(0, rig.Session.StopCalls);
                Assert.NotEqual(account.Id, rig.App.Thread(rig.InitialThread.Id)!.ProviderAccountID);
                Assert.True(process.Terminated);
            }
            else
            {
                await host.SwitchAsync(standby, target, false, CancellationToken.None);
                Assert.Equal(1, rig.Session.StopCalls);
                Assert.Equal(account.Id, rig.App.Thread(rig.InitialThread.Id)!.ProviderAccountID);
                Assert.Equal("actual-native-session", rig.App.Thread(rig.InitialThread.Id)!.ProviderSessionID);
                Assert.Same(incoming, ThreadRuntimeReflection.Get<ProviderSession>(rig.Runtime, "session"));
                Assert.False(process.Terminated);
            }
            Assert.Single(process.Sent, value => value["request"]?["subtype"]?.JSONValueString == "apply_flag_settings");
            Assert.False(rig.App.Settings.AccountHandshakeEnabled);
        }, AppModelHarness.Bound);
        rig.H.Main.OnMain(() => { incoming?.Stop(); rig.Runtime.CancelPersistence(); });
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
}
