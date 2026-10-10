using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

// OD-0116, OD-0120, OD-0122, ED-439: the host prepares a Codex destination by history injection, adopts it with no extra turn, and falls back to deferred delivery
// without losing the context. Offline: a scripted session stands for the CLI.
[Trait("Rule", "R-TEST-4")]
public sealed class InjectedStandbyHostTests
{
    private static (ThreadAccountHost Host, SwitchTarget Target, HandshakeSummaryAdapter Summaries, Guid Account) Make(ThreadRuntimeTurnRig rig, ThreadRuntimeInjectionSession fake)
    {
        var codex = rig.App.Settings.Accounts.ForService(ProviderKind.Codex)[0].Id;
        rig.App.Settings.Accounts.MarkSignedIn(codex);
        var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
        var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries, (configuration, id) => new(configuration, fake));
        return (host, new SwitchTarget(new(codex.ToString("D"), ProviderKind.Codex.Id, "Codex")), summaries, codex);
    }

    [Fact]
    public void A_Codex_destination_is_prepared_by_injection_and_adopted_without_any_extra_turn_or_preamble()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var fake = new ThreadRuntimeInjectionSession();
        Guid codex = default;
        rig.H.Main.Loop.Run(async () =>
        {
            var (host, target, summaries, account) = Make(rig, fake);
            codex = account;
            Assert.False(((IDeferredDeliveryHost)host).DeliversDeferred(target));
            Assert.False(await ((IDeferredDeliveryHost)host).ResolveDeliversDeferredAsync(target, CancellationToken.None));
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            Assert.Equal(StandbyKind.Injected, standby.Kind);
            Assert.Null(await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), false, CancellationToken.None));
            Assert.Single(fake.Injected);
            Assert.Empty(fake.Inputs);
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        var thread = rig.Thread;
        Assert.Equal((ProviderKind.Codex, codex, (string?)"standby-thread"), (thread.Provider, thread.ProviderAccountID, thread.ProviderSessionID));
        Assert.True(fake.Ended);
        Assert.Equal(0, fake.DiscardCalls);
        Assert.Equal(1, rig.Session.StopCalls);
        rig.Send("the next message");
        var turn = Assert.Single(fake.Inputs);
        Assert.Equal("the next message", turn.Text);
        Assert.DoesNotContain("[Nth Handshake]", turn.Text, StringComparison.Ordinal);
        Assert.Equal(["start-standby", "inject", "end-standby", "turn"], fake.Log);
        // The handover turn carries the user's own settings.
        Assert.Equal((thread.RuntimeMode, thread.InteractionMode), (turn.RuntimeMode, turn.InteractionMode));
    }

    [Fact]
    public void Activity_on_the_waiting_thread_falls_back_to_deferred_delivery_with_the_context_on_the_first_real_turn()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var fake = new ThreadRuntimeInjectionSession();
        var incoming = new ThreadRuntimeScriptedSession();
        Guid codex = default;
        rig.H.Main.Loop.Run(async () =>
        {
            var (host, target, summaries, account) = Make(rig, fake);
            codex = account;
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), false, CancellationToken.None);
            fake.Violate("turn/started");
            Assert.Equal(StandbyKind.Deferred, standby.Kind);
            Assert.True(((IDeferredDeliveryHost)host).DeliversDeferred(target));
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        var thread = rig.Thread;
        Assert.Equal((ProviderKind.Codex, codex, (string?)null), (thread.Provider, thread.ProviderAccountID, thread.ProviderSessionID));
        Assert.Equal(1, fake.DiscardCalls);
        Assert.Empty(fake.Inputs);
        rig.Install(incoming);
        rig.Send("the next message");
        var first = Assert.Single(incoming.Inputs).Text;
        Assert.Contains("the next message", first, StringComparison.Ordinal);
        Assert.Contains("[Nth Handshake]", first, StringComparison.Ordinal);
    }

    [Fact]
    public void A_CLI_that_cannot_hold_history_gets_the_deferred_standby_and_never_runs_a_turn_early()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var fake = new ThreadRuntimeInjectionSession { Supported = false };
        rig.H.Main.Loop.Run(async () =>
        {
            var (host, target, summaries, _) = Make(rig, fake);
            Assert.True(await ((IDeferredDeliveryHost)host).ResolveDeliversDeferredAsync(target, CancellationToken.None));
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            Assert.Equal(StandbyKind.Deferred, standby.Kind);
            Assert.True(((IDeferredDeliveryHost)host).DeliversDeferred(target));
            Assert.Null(await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), true, CancellationToken.None));
            Assert.Empty(fake.Injected);
            Assert.Empty(fake.Inputs);
        }, AppModelHarness.Bound);
        Assert.False(fake.IsRunning);
    }

    [Fact]
    public void Cancelling_a_prepared_standby_discards_the_waiting_thread_and_leaves_the_conversation_alone()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var fake = new ThreadRuntimeInjectionSession();
        rig.H.Main.Loop.Run(async () =>
        {
            var (host, target, summaries, _) = Make(rig, fake);
            var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), false, CancellationToken.None);
            await standby.DisposeAsync();
        }, AppModelHarness.Bound);
        Assert.Equal(1, fake.DiscardCalls);
        Assert.Empty(fake.Inputs);
        Assert.Equal(ProviderKind.Claude, rig.Thread.Provider);
        Assert.Equal(0, rig.Session.StopCalls);
    }

    [Fact]
    public void A_handover_that_fails_still_hands_the_context_over_with_the_first_real_turn()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var fake = new ThreadRuntimeInjectionSession { EndFails = true };
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.Loop.Run(async () =>
        {
            var (host, target, summaries, _) = Make(rig, fake);
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), false, CancellationToken.None);
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        Assert.Null(rig.Thread.ProviderSessionID);
        Assert.Equal(1, fake.DiscardCalls);
        rig.Install(incoming);
        rig.Send("next");
        Assert.Contains("[Nth Handshake]", Assert.Single(incoming.Inputs).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_inject_still_hands_the_context_over_with_the_first_real_turn()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var fake = new ThreadRuntimeInjectionSession { InjectFails = true };
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.Loop.Run(async () =>
        {
            var (host, target, summaries, _) = Make(rig, fake);
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), false, CancellationToken.None);
            Assert.Equal(StandbyKind.Deferred, standby.Kind);
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        rig.Install(incoming);
        rig.Send("next");
        Assert.Contains("[Nth Handshake]", Assert.Single(incoming.Inputs).Text, StringComparison.Ordinal);
    }
}
