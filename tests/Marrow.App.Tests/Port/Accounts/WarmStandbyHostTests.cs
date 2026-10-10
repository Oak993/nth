using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

// OD-0116, ED-439, research section 4: Copilot, Cursor, Grok, Devin, Antigravity (and Pi, OpenCode when they cannot hold history) are warm started: the session opens
// early and receives nothing; the context is the first real turn on that same session. Offline: scripted sessions stand for the CLIs.
[Trait("Rule", "R-TEST-4")]
public sealed class WarmStandbyHostTests
{
    private static (ThreadAccountHost Host, SwitchTarget Target, HandshakeSummaryAdapter Summaries, Guid Account) Make(ThreadRuntimeTurnRig rig, ProviderKind provider,
        Func<SessionConfiguration, Guid, AccountSessionLaunch> launch)
    {
        var account = rig.App.Settings.Accounts.ForService(provider)[0].Id;
        rig.App.Settings.Accounts.MarkSignedIn(account);
        var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
        return (new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries, launch), new SwitchTarget(new(account.ToString("D"), provider.Id, provider.DisplayName)), summaries, account);
    }

    [Theory]
    [InlineData(ProviderKind.Copilot)]
    [InlineData(ProviderKind.Cursor)]
    [InlineData(ProviderKind.Grok)]
    [InlineData(ProviderKind.Devin)]
    [InlineData(ProviderKind.Antigravity)]
    public void A_warm_destination_opens_early_receives_nothing_and_its_first_real_turn_carries_the_context(ProviderKind provider)
    {
        using var rig = new ThreadRuntimeTurnRig();
        var warm = new ThreadRuntimeScriptedSession { StartScript = () => Task.FromResult("warm-session") };
        var launches = 0;
        Guid account = default;
        rig.H.Main.Loop.Run(async () =>
        {
            var (host, target, summaries, id) = Make(rig, provider, (configuration, _) => { launches++; return new(configuration, warm); });
            account = id;
            Assert.True(((IDeferredDeliveryHost)host).DeliversDeferred(target));
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            Assert.Equal(StandbyKind.Deferred, standby.Kind);
            Assert.Equal(1, warm.StartCalls);
            Assert.Null(await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), true, CancellationToken.None));
            Assert.Empty(warm.Inputs);
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        var thread = rig.Thread;
        Assert.Equal((provider, account, (string?)"warm-session"), (thread.Provider, thread.ProviderAccountID, thread.ProviderSessionID));
        Assert.Equal(1, launches);
        Assert.Equal(0, warm.StopCalls);
        Assert.Empty(warm.Inputs);
        rig.Send("the next message");
        var first = Assert.Single(warm.Inputs).Text;
        Assert.Contains("the next message", first, StringComparison.Ordinal);
        Assert.Contains("[nth Handshake]", first, StringComparison.Ordinal);
        Assert.Equal(1, warm.StartCalls);
        rig.Install(warm, observe: false);
        rig.Complete();
        rig.Send("and another");
        Assert.DoesNotContain("[nth Handshake]", warm.Inputs[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Activity_on_the_warm_session_before_the_switch_stops_it_and_the_context_still_goes_with_the_first_real_turn()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var warm = new ThreadRuntimeScriptedSession { StartScript = () => Task.FromResult("warm-session") };
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.Loop.Run(async () =>
        {
            var (host, target, summaries, _) = Make(rig, ProviderKind.Copilot, (configuration, _) => new(configuration, warm));
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), false, CancellationToken.None);
            warm.Emit(new ProviderEvent.MessageDelta("m", "I started working on my own"));
            Assert.Equal(1, warm.StopCalls);
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        Assert.Null(rig.Thread.ProviderSessionID);
        Assert.Empty(warm.Inputs);
        rig.Install(incoming);
        rig.Send("next");
        Assert.Contains("[nth Handshake]", Assert.Single(incoming.Inputs).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_warm_start_that_fails_is_a_plain_deferred_delivery_and_a_cancelled_one_is_stopped()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var broken = new ThreadRuntimeScriptedSession { StartScript = () => throw new IOException("offline refusal") };
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.Loop.Run(async () =>
        {
            var (host, target, summaries, _) = Make(rig, ProviderKind.Copilot, (configuration, _) => new(configuration, broken));
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            Assert.Equal(StandbyKind.Deferred, standby.Kind);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), false, CancellationToken.None);
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        Assert.Null(rig.Thread.ProviderSessionID);
        rig.Install(incoming);
        rig.Send("next");
        Assert.Contains("[nth Handshake]", Assert.Single(incoming.Inputs).Text, StringComparison.Ordinal);

        using var cancelRig = new ThreadRuntimeTurnRig();
        var cancelled = new ThreadRuntimeScriptedSession { StartScript = () => Task.FromResult("warm-session") };
        cancelRig.H.Main.Loop.Run(async () =>
        {
            var (host, target, summaries, _) = Make(cancelRig, ProviderKind.Copilot, (configuration, _) => new(configuration, cancelled));
            var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(cancelRig.InitialThread.Id, CancellationToken.None), null), false, CancellationToken.None);
            await standby.DisposeAsync();
        }, AppModelHarness.Bound);
        Assert.Equal(1, cancelled.StopCalls);
        Assert.Empty(cancelled.Inputs);
        Assert.Equal(ProviderKind.Claude, cancelRig.Thread.Provider);
    }

    [Fact]
    public void Command_Code_has_nothing_to_open_early_and_is_never_launched()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var launches = 0;
        rig.H.Main.Loop.Run(async () =>
        {
            var (host, target, _, _) = Make(rig, ProviderKind.Commandcode, (configuration, _) => { launches++; throw new InvalidOperationException("never launched"); });
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            Assert.Equal(StandbyKind.Deferred, standby.Kind);
        }, AppModelHarness.Bound);
        Assert.Equal(0, launches);
    }

    [Fact]
    public void Pi_that_cannot_prove_history_injection_falls_back_to_a_warm_start()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var injection = new ThreadRuntimeInjectionSession { Supported = false };
        var warm = new ThreadRuntimeScriptedSession { StartScript = () => Task.FromResult("warm-pi") };
        var launches = 0;
        rig.H.Main.Loop.Run(async () =>
        {
            var (host, target, summaries, _) = Make(rig, ProviderKind.Pi, (configuration, _) => new(configuration, ++launches == 1 ? injection : warm));
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            Assert.Equal(StandbyKind.Deferred, standby.Kind);
            Assert.Equal(1, warm.StartCalls);
            Assert.False(injection.IsRunning);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), false, CancellationToken.None);
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        Assert.Equal(("warm-pi", ProviderKind.Pi), (rig.Thread.ProviderSessionID, rig.Thread.Provider));
        Assert.Empty(injection.Inputs);
        rig.Send("next");
        Assert.Contains("[nth Handshake]", Assert.Single(warm.Inputs).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Pi_with_proven_injection_is_adopted_with_no_extra_turn()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var injection = new ThreadRuntimeInjectionSession();
        rig.H.Main.Loop.Run(async () =>
        {
            var (host, target, summaries, _) = Make(rig, ProviderKind.Pi, (configuration, _) => new(configuration, injection));
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            Assert.Equal(StandbyKind.Injected, standby.Kind);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), false, CancellationToken.None);
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        rig.Send("next");
        var turn = Assert.Single(injection.Inputs).Text;
        Assert.EndsWith("next", turn, StringComparison.Ordinal);
        Assert.DoesNotContain("[nth Handshake]", turn, StringComparison.Ordinal);
    }
}
