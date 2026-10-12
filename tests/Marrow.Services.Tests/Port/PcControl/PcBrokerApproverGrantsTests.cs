using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-25; DESIGN.md 4.1, 10.4 approval body). The approval card's answer
// through the broker into the grants store: "Always allow" is saved, "Allow for this chat" is kept for that chat, a
// See-only answer to a step that needs Use denies the step with its reason and covers later See steps, "Reply instead"
// gives the model the user's reason once, a head's answer is never saved as Always, and the logged row says See-only.
[Trait("Decision", "ED-NEW-pcctl-25")]
public sealed class PcBrokerApproverGrantsTests
{
    private static readonly PcAppIdentity Outlook = new("c:\\office\\outlook.exe");

    private static (PcAskBroker Broker, PcGrants Grants, PcBrokerApprover Approver) Make()
    {
        var broker = new PcAskBroker(new PcManualClock());
        var grants = new PcGrants(new InMemorySettingsStore());
        return (broker, grants, new PcBrokerApprover(broker, grants));
    }

    [Fact]
    public async Task Always_allow_with_Use_is_saved()
    {
        var (broker, grants, approver) = Make();
        using var held = broker;

        var asking = approver.Ask(new PcAskSource("chat-1", "Outlook"), Outlook, "Outlook", PcCapability.Act, true, true, CancellationToken.None);
        var ask = Assert.Single(broker.Pending);
        Assert.Equal(PcResolve.Done, broker.Resolve(ask.Id, 0, new PcApprovalReply(PcApproval.Always, Use: true), PcAnswerSource.Pill));

        Assert.Equal(PcApproval.Always, await asking.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(PcApproval.Always, approver.Standing(Outlook, PcCapability.Act));
        Assert.Equal("Outlook", Assert.Single(grants.Always).Name);
    }

    [Fact]
    public async Task See_only_denies_a_Use_step_with_its_reason_and_covers_later_See_steps()
    {
        var (broker, grants, approver) = Make();
        using var held = broker;

        var asking = approver.Ask(new PcAskSource("chat-1", "Outlook"), Outlook, "Outlook", PcCapability.Act, true, true, CancellationToken.None);
        var ask = Assert.Single(broker.Pending);
        var reply = new PcApprovalReply(PcApproval.Chat, Use: false);
        Assert.Equal("You let this chat see Outlook (from the pill)", AskLifetimes.Logged(ask, reply, PcAnswerSource.Pill));
        Assert.Equal(PcResolve.Done, broker.Resolve(ask.Id, 0, reply, PcAnswerSource.Pill));

        Assert.Equal(PcApproval.Denied, await asking.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(PcBrokerApprover.SeeOnly, approver.Reason("chat-1", Outlook, PcCapability.Act));
        Assert.Null(approver.Reason("chat-1", Outlook, PcCapability.Act));

        // a See step of the same chat is covered without a new card; another chat asks
        Assert.Equal(PcApproval.Chat, await approver.Ask(new PcAskSource("chat-1", "Outlook"), Outlook, "Outlook", PcCapability.See, true, true, CancellationToken.None));
        Assert.Empty(broker.Pending);
        _ = approver.Ask(new PcAskSource("chat-2", "Outlook"), Outlook, "Outlook", PcCapability.See, true, true, CancellationToken.None);
        Assert.Single(broker.Pending);
        Assert.Null(grants.Standing(Outlook, PcCapability.See));
    }

    [Fact]
    public async Task Reply_instead_gives_the_model_the_reason_with_Dont_allow()
    {
        var (broker, grants, approver) = Make();
        using var held = broker;

        var asking = approver.Ask(new PcAskSource("chat-1", "Outlook"), Outlook, "Outlook", PcCapability.Act, true, true, CancellationToken.None);
        var ask = Assert.Single(broker.Pending);
        Assert.Equal(PcResolve.Done, broker.Resolve(ask.Id, 0, new PcApprovalReply(PcApproval.Denied, true, "Use the web version"), PcAnswerSource.Pill));

        Assert.Equal(PcApproval.Denied, await asking.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Use the web version", approver.Reason("chat-1", Outlook, PcCapability.Act));
        Assert.Empty(grants.All);
    }

    [Fact]
    public async Task A_heads_answer_is_kept_for_its_chat_only()
    {
        var (broker, grants, approver) = Make();
        using var held = broker;

        var asking = approver.Ask(new PcAskSource("chat-1", "Outlook", "Head 2"), Outlook, "Outlook", PcCapability.Act, true, allowAlways: true, CancellationToken.None);
        var ask = Assert.Single(broker.Pending);
        Assert.Equal(PcResolve.Refused, broker.Resolve(ask.Id, 0, new PcApprovalReply(PcApproval.Always, true), PcAnswerSource.Pill));
        Assert.Equal(PcResolve.Done, broker.Resolve(ask.Id, 0, new PcApprovalReply(PcApproval.Chat, true), PcAnswerSource.Pill));

        Assert.Equal(PcApproval.Chat, await asking.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(grants.Always);
        Assert.Equal(PcGrantScope.Chat, Assert.Single(grants.All).Scope);
    }

    [Fact]
    public async Task A_plain_answer_from_the_chat_still_works()
    {
        var (broker, grants, approver) = Make();
        using var held = broker;

        var asking = approver.Ask(new PcAskSource("chat-1", "Outlook"), Outlook, "Outlook", PcCapability.Act, true, true, CancellationToken.None);
        var ask = Assert.Single(broker.Pending);
        Assert.Equal(PcResolve.Done, broker.Resolve(ask.Id, 0, PcApproval.Chat, PcAnswerSource.Chat));

        Assert.Equal(PcApproval.Chat, await asking.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(PcApproval.Chat, grants.ForChat("chat-1", Outlook, PcCapability.Act));
    }
}
