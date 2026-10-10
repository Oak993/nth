using System.Text;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Port.MultiAccount;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Accounts;

// OD-0116, lead decision 2026-10-06: only Claude, after its verified gate, is primed in a standby. Every other CLI destination is never started early:
// the context goes with its first real turn.
[Trait("Rule", "R-TEST-4")]
public sealed class DeferredContextDeliveryTests
{
    // ED-439 (owner decisions OD-0116 and OD-0122, lead brief 2026-10-06): Codex is no longer deferred (it loads history without a model turn, InjectedStandbyHostTests);
    // this test keeps its rule for a service with nothing to open early: Command Code.
    [Fact]
    public void A_non_Claude_CLI_destination_is_never_started_in_standby_and_gets_the_context_with_its_first_turn()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var launches = 0;
        using var launcher = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) => { launches++; throw new InvalidOperationException("A deferred destination must not be launched."); });
        var incoming = new ThreadRuntimeScriptedSession();
        Guid codex = default;
        rig.H.Main.Loop.Run(async () =>
        {
            codex = rig.App.Settings.Accounts.ForService(ProviderKind.Commandcode)[0].Id;
            rig.App.Settings.Accounts.MarkSignedIn(codex);
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries);
            var target = new SwitchTarget(new(codex.ToString("D"), ProviderKind.Commandcode.Id, "Command Code"));
            Assert.True(((IDeferredDeliveryHost)host).DeliversDeferred(target));
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            Assert.Null(await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), true, CancellationToken.None));
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        Assert.Equal(0, launches);
        var thread = rig.Thread;
        Assert.Equal((ProviderKind.Commandcode, codex, (string?)null), (thread.Provider, thread.ProviderAccountID, thread.ProviderSessionID));
        rig.Install(incoming);
        rig.Send("the next message");
        var first = Assert.Single(incoming.Inputs).Text;
        Assert.Contains("the next message", first, StringComparison.Ordinal);
        Assert.Contains("[Nth Handshake]", first, StringComparison.Ordinal);
        rig.Complete();
        rig.Send("and another");
        Assert.DoesNotContain("[Nth Handshake]", incoming.Inputs[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Claude_destination_whose_gate_does_not_pass_is_not_started_and_takes_the_same_deferred_path()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var processes = 0;
        using var version = ClaudeProviderRuntime.Version.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("2.1.100 (Claude Code)"), [])));
        using var process = ClaudeProviderRuntime.Process.Override((_, args, _, _) => { processes++; throw new InvalidOperationException("No process may start."); });
        using var launcher = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
            new(configuration with { Executable = SwiftURL.Create("/offline/old-claude")! }, new ClaudeSession(configuration with { Executable = SwiftURL.Create("/offline/old-claude")! })));
        Guid second = default;
        rig.H.Main.Loop.Run(async () =>
        {
            second = rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id;
            rig.App.Settings.Accounts.MarkSignedIn(second);
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries);
            var target = new SwitchTarget(new(second.ToString("D"), ProviderKind.Claude.Id, "Claude 2"));
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), false, CancellationToken.None);
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        Assert.Equal(0, processes);
        var thread = rig.Thread;
        Assert.Equal((second, (string?)null), (thread.ProviderAccountID, thread.ProviderSessionID));
    }
}
