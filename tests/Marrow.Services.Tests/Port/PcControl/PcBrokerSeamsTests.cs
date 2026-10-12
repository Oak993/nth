using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-16; DESIGN.md 4.1, 4.3, 10.4). The engine's approval, confirm and
// show-window seams through the ask broker: one entry answered from either view; a Hydra head is never offered Always;
// when the engine stops waiting the ask is withdrawn and a late click does nothing; Standing stays with the grant store.
[Trait("Decision", "ED-NEW-pcctl-16")]
public sealed class PcBrokerSeamsTests
{
    private static readonly PcAppIdentity Outlook = new("c:\\office\\outlook.exe");

    [Fact]
    public async Task An_approval_is_one_broker_entry_answered_from_the_pill()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var approver = new PcBrokerApprover(broker, (_, _) => null);

        var asking = approver.Ask(new PcAskSource("chat-1", "Outlook"), Outlook, "Outlook", PcCapability.Act, risky: true, allowAlways: true, CancellationToken.None);
        var ask = Assert.Single(broker.Pending);
        Assert.Equal(PcAskKind.Approval, ask.Kind);
        Assert.Equal(Outlook.Key, ask.App);
        Assert.Equal(new PcApprovalAsk(PcCapability.Act, true, true), ask.Payload);

        Assert.Equal(PcResolve.Done, broker.Resolve(ask.Id, 0, PcApproval.Always, PcAnswerSource.Pill));
        Assert.Equal(PcApproval.Always, await asking.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_head_is_never_offered_Always()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var approver = new PcBrokerApprover(broker, (_, _) => null);

        _ = approver.Ask(new PcAskSource("chat-1", "Outlook", "Head 3"), Outlook, "Outlook", PcCapability.See, false, allowAlways: true, CancellationToken.None);

        var ask = Assert.Single(broker.Pending);
        Assert.Equal("Head 3", ask.Head);
        Assert.Equal(PcResolve.Refused, broker.Resolve(ask.Id, 0, PcApproval.Always, PcAnswerSource.Pill));
    }

    [Fact]
    public async Task When_the_engine_stops_waiting_the_ask_is_withdrawn_and_a_late_click_does_nothing()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var confirmer = new PcBrokerConfirmer(broker);
        using var withdraw = new CancellationTokenSource();

        var asking = confirmer.Confirm(new PcAskSource("chat-1", "Outlook"), "send", "Send", "Outlook", withdraw.Token);
        var ask = Assert.Single(broker.Pending);
        Assert.Equal("nth wants to click Send in Outlook.", Assert.IsType<PcConfirmContent>(ask.Payload).FirstLine);
        await withdraw.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(broker.Pending);
        Assert.Equal(PcResolve.TooLate, broker.Resolve(ask.Id, 0, true, PcAnswerSource.Pill));
        Assert.Equal("Send in Outlook expired. Nothing was sent. Ask in chat to try again.", AskLifetimes.TooLate(ask));
    }

    [Fact]
    public async Task Confirm_and_show_answer_from_either_view()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var confirmer = new PcBrokerConfirmer(broker);

        var confirm = confirmer.Confirm(new PcAskSource("chat-1"), "delete", "Delete", "Explorer", CancellationToken.None);
        broker.Resolve(broker.Pending[0].Id, 0, true, PcAnswerSource.Chat);
        Assert.True(await confirm.WaitAsync(TimeSpan.FromSeconds(5)));

        var show = confirmer.Show(new PcAskSource("chat-1"), "Excel", "Book1", CancellationToken.None);
        var ask = Assert.Single(broker.Pending);
        Assert.Equal(PcAskKind.ShowWindow, ask.Kind);
        broker.Resolve(ask.Id, 0, PcShowAnswer.Show, PcAnswerSource.Pill);
        Assert.Equal(PcShowAnswer.Show, await show.WaitAsync(TimeSpan.FromSeconds(5)));

        var notNow = confirmer.Show(new PcAskSource("chat-1"), "Excel", "Book1", CancellationToken.None);
        broker.EndTurn("chat-1");
        Assert.Equal(PcShowAnswer.NotNow, await notNow.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Standing_grants_come_from_the_grant_store()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var approver = new PcBrokerApprover(broker, (app, capability) => app == Outlook && capability == PcCapability.See ? PcApproval.Always : null);

        Assert.Equal(PcApproval.Always, approver.Standing(Outlook, PcCapability.See));
        Assert.Null(approver.Standing(Outlook, PcCapability.Act));
        Assert.Empty(broker.Pending);
    }
}
