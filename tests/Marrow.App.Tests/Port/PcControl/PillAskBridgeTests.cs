using Marrow.App.Port.PcControl;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus pill v2, the pill's view of the ask broker (OD-0145, ED-NEW-pcctl-26; DESIGN.md 10.4 "What can ask",
// "Queue", "Resolution"). Each ask becomes the pill's header line and card body; each card choice becomes the broker's
// answer for that revision, so the pill and the chat resolve an ask once. A confirm whose content changed at the click
// sends nothing and asks again with the changed rows marked; a late click says so; a draft outlives its ask.
[Trait("Decision", "ED-NEW-pcctl-26")]
public sealed class PillAskBridgeTests
{
    private const string Thread = "chat-1";

    private static PcAskDraft Approval(string? head = null) =>
        new(PcAskKind.Approval, Thread, new PcApprovalAsk(PcCapability.Act, Risky: true, AllowAlways: head is null))
        {
            App = "exe:c:\\office\\outlook.exe", AppName = "Outlook", Head = head, Model = "GPT-6.1 Sol", Chat = "Quarterly report",
        };

    private static PcConfirmContent Mail(string subject) =>
        new(PcConfirmCategory.Send, "send", "Send", "Outlook", [new PcConfirmRow("To", "ana@example.com"), new PcConfirmRow("Subject", subject)]);

    private static PcAskDraft Question() =>
        new(PcAskKind.Question, Thread, new PcAskRequest("Which sheet?", ["Q1", "Q2"], AllowText: true, Window: null)) { AppName = "Excel", Model = "GPT-6.1 Sol", Chat = "Quarterly report" };

    [Fact]
    public void An_approval_becomes_the_header_and_the_card_with_its_path_and_helper()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        using var bridge = new PillAskBridge(broker, () => "GPT-6.1 Mini");
        var changed = 0;
        bridge.Changed += () => changed++;

        var ask = broker.Add(Approval());
        var head = broker.Add(Approval("Head 2"));

        Assert.Equal(2, changed);
        Assert.Equal(new[] { ask.Id, head.Id }, bridge.Asks.Select(a => a.Id));
        var shown = bridge.Asks[0];
        Assert.Equal(new PillAsk(ask.Id, PillAskKind.Approval, "Outlook", "Quarterly report", "GPT-6.1 Sol"), shown);
        Assert.Equal("Head 2", bridge.Asks[1].Head);
        Assert.Equal(
            new PillApprovalBody(ask.Id, 0, "Outlook", "c:\\office\\outlook.exe", Risky: true, UseAsked: true, AllowAlways: true, "This chat", "GPT-6.1 Mini"),
            bridge.Body(ask.Id));
        Assert.Equal("Head 2", Assert.IsType<PillApprovalBody>(bridge.Body(head.Id)).Who);
        Assert.False(Assert.IsType<PillApprovalBody>(bridge.Body(head.Id)).AllowAlways);
    }

    [Fact]
    public async Task A_card_choice_resolves_the_ask_with_the_chips_choice()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        using var bridge = new PillAskBridge(broker);
        var ask = broker.Add(Approval());

        Assert.Equal(PcResolve.Done, bridge.Answer(new PillCardAnswer(ask.Id, 0, PillCardChoice.AllowChat, Use: false)));

        var outcome = await ask.Outcome.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new PcAskOutcome(PcAskStatus.Answered, new PcApprovalReply(PcApproval.Chat, false), PcAnswerSource.Pill), outcome);
        Assert.Empty(bridge.Asks);
    }

    [Fact]
    public void A_click_after_the_chat_answered_does_nothing_and_says_so()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        using var bridge = new PillAskBridge(broker);
        var late = new List<(string Id, string Text)>();
        bridge.TooLate += (id, text) => late.Add((id, text));
        var ask = broker.Add(Approval());
        _ = bridge.Asks;
        Assert.Equal(PcResolve.Done, broker.Resolve(ask.Id, 0, PcApproval.Denied, PcAnswerSource.Chat));

        Assert.Equal(PcResolve.TooLate, bridge.Answer(new PillCardAnswer(ask.Id, 0, PillCardChoice.AllowChat)));
        Assert.Equal((ask.Id, AskLifetimes.TooLate(ask)), Assert.Single(late));
    }

    [Fact]
    public async Task A_confirm_whose_content_changed_sends_nothing_and_asks_again_with_the_rows_marked()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var live = Mail("Q3 numbers");
        using var bridge = new PillAskBridge(broker, reread: _ => live);
        var ask = broker.Add(new PcAskDraft(PcAskKind.Confirm, Thread, Mail("Q2 numbers")) { AppName = "Outlook" });

        Assert.Equal(PcResolve.Changed, bridge.Answer(new PillCardAnswer(ask.Id, 0, PillCardChoice.Confirm)));
        Assert.False(ask.Outcome.IsCompleted);
        var body = Assert.IsType<PillConfirmBody>(bridge.Body(ask.Id));
        Assert.Equal(1, body.Revision);
        Assert.Equal(live.ChangedLine(["Subject"]), body.ChangedNote);
        Assert.Equal(new[] { false, true }, body.Rows.Select(r => r.Changed));
        Assert.Equal("Q3 numbers", body.Rows[1].Value);

        // the old revision is refused; the new one confirms
        Assert.Equal(PcResolve.Changed, bridge.Answer(new PillCardAnswer(ask.Id, 0, PillCardChoice.Cancel)));
        Assert.Equal(PcResolve.Done, bridge.Answer(new PillCardAnswer(ask.Id, 1, PillCardChoice.Confirm)));
        Assert.Equal(new PcConfirmReply(true), (await ask.Outcome.WaitAsync(TimeSpan.FromSeconds(5))).Answer);
    }

    [Fact]
    public async Task Cancel_with_a_note_reaches_the_broker_as_the_confirm_reply()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        using var bridge = new PillAskBridge(broker);
        var ask = broker.Add(new PcAskDraft(PcAskKind.Confirm, Thread, Mail("Q2 numbers")) { AppName = "Outlook" });

        Assert.Equal(PcResolve.Done, bridge.Answer(new PillCardAnswer(ask.Id, 0, PillCardChoice.Cancel, Text: "Change the subject first")));
        Assert.Equal(new PcConfirmReply(false, "Change the subject first"), (await ask.Outcome.WaitAsync(TimeSpan.FromSeconds(5))).Answer);
    }

    [Fact]
    public async Task A_question_takes_a_chip_or_text_and_Dismiss_withdraws_it()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        using var bridge = new PillAskBridge(broker);
        var chip = broker.Add(Question());
        var typed = broker.Add(Question());
        var dismissed = broker.Add(Question());
        Assert.Equal(new PillQuestionBody(chip.Id, 0, "GPT-6.1 Sol", "Which sheet?", ((PcAskRequest)chip.Payload).Choices), bridge.Body(chip.Id));

        Assert.Equal(PcResolve.Done, bridge.Answer(new PillCardAnswer(chip.Id, 0, PillCardChoice.Chip, Text: "Q2")));
        Assert.Equal(PcResolve.Done, bridge.Answer(new PillCardAnswer(typed.Id, 0, PillCardChoice.Send, Text: "the second one")));
        bridge.Draft(dismissed.Id, "half an answer");
        Assert.Equal(PcResolve.Done, bridge.Answer(new PillCardAnswer(dismissed.Id, 0, PillCardChoice.Dismiss)));

        Assert.Equal(PcAnswer.Chose("Q2"), (await chip.Outcome.WaitAsync(TimeSpan.FromSeconds(5))).Answer);
        Assert.Equal(PcAnswer.Typed("the second one"), (await typed.Outcome.WaitAsync(TimeSpan.FromSeconds(5))).Answer);
        Assert.Equal(PcWithdrawal.Dismissed, (await dismissed.Outcome.WaitAsync(TimeSpan.FromSeconds(5))).Reason);
        Assert.Empty(bridge.Asks);
    }

    [Fact]
    public void A_draft_outlives_its_ask_until_it_goes_to_the_chat_or_is_discarded()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        using var bridge = new PillAskBridge(broker);
        var sent = new List<(string Thread, string Text)>();
        bridge.SendToChat += (thread, text) => sent.Add((thread, text));
        var stopped = broker.Add(Question());
        var answered = broker.Add(Question());
        bridge.Draft(stopped.Id, "half an answer");
        bridge.Draft(answered.Id, "my own words");

        Assert.True(broker.Withdraw(stopped.Id, PcWithdrawal.TurnEnded));
        Assert.Equal(PcResolve.Done, broker.Resolve(answered.Id, 0, PcAnswer.Chose("Q1"), PcAnswerSource.Chat));

        Assert.Equal(new[] { true, true }, bridge.Asks.Select(a => a.Withdrawn));
        Assert.Equal(new PillWithdrawnBody(stopped.Id, 1, AnsweredInChat: false, "half an answer"), bridge.Body(stopped.Id));
        Assert.Equal(new PillWithdrawnBody(answered.Id, 1, AnsweredInChat: true, "my own words"), bridge.Body(answered.Id));
        Assert.Equal(PcResolve.Refused, bridge.Answer(new PillCardAnswer(stopped.Id, 1, PillCardChoice.Send, Text: "x")));

        Assert.Equal(PcResolve.Done, bridge.Answer(new PillCardAnswer(stopped.Id, 1, PillCardChoice.SendToChat, Text: "half an answer")));
        Assert.Equal((Thread, "half an answer"), Assert.Single(sent));
        Assert.Equal(PcResolve.Done, bridge.Answer(new PillCardAnswer(answered.Id, 1, PillCardChoice.Discard)));
        Assert.Single(sent);
        Assert.Empty(bridge.Asks);
    }

    [Fact]
    public void Open_chat_takes_the_draft_to_the_chat_of_the_ask()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        using var bridge = new PillAskBridge(broker);
        var opened = new List<(string Thread, string Id, string? Draft)>();
        bridge.OpenChat += (thread, id, draft) => opened.Add((thread, id, draft));
        var provider = broker.Add(new PcAskDraft(PcAskKind.ProviderApproval, Thread, new PcProviderAsk("Run a command", "npm test")) { Model = "Claude", Chat = "Build" });
        var question = broker.Add(Question());
        bridge.Draft(question.Id, "the second one");

        Assert.Equal(PillAskKind.Provider, bridge.Asks[0].Kind);
        Assert.Equal("Run a command", bridge.Asks[0].Title);
        Assert.Equal(new PillProviderBody(provider.Id, 0, "Claude wants to run a command.", "npm test"), bridge.Body(provider.Id));
        Assert.Equal(PcResolve.Done, bridge.Answer(new PillCardAnswer(provider.Id, 0, PillCardChoice.OpenChat)));
        Assert.Equal(PcResolve.Done, bridge.Answer(new PillCardAnswer(question.Id, 0, PillCardChoice.OpenChat)));

        Assert.Equal(new[] { (Thread, provider.Id, (string?)null), (Thread, question.Id, "the second one") }, opened);
        Assert.Equal(2, broker.Pending.Count);
    }
}
