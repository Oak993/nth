// PORT-ONLY: Tactus, the card's chat buttons on screen (ED-NEW-pcctl-39; DESIGN.md 10.4 "'Open chat' opens it at the ask
// with the draft carried over", 10.7 "Send to chat"). Open chat puts the card's draft in that chat's composer, then opens
// the chat (with no draft it only opens it); Send to chat sends the draft there and opens nothing; a draft that cannot go
// waits in the chat's composer and the chat opens, so nothing typed is lost. A recording inbox and the pill on fakes.
using Marrow.App.Port.PcControl;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-39")]
public sealed class TactusScreenChatInboxTests(HeadlessSession headless)
{
    private const string Chat = "chat-a";

    private static PcPendingAsk Question(PcScreenRig rig) =>
        rig.Assembly.Asks.Add(new PcAskDraft(PcAskKind.Question, Chat, new PcAskRequest("Which sheet?", ["Q1", "Q2"], AllowText: true, Window: null))
        {
            AppName = "Excel",
            Model = "GPT-6.1 Sol",
            Chat = "Quarterly report",
        });

    [Fact]
    public void Open_chat_puts_the_card_s_draft_in_the_chat_s_composer_and_opens_the_chat() => headless.OnUiThread(() =>
    {
        using var rig = new PcScreenRig();
        var question = Question(rig);
        var provider = rig.Assembly.Asks.Add(new PcAskDraft(PcAskKind.ProviderApproval, Chat, new PcProviderAsk("Run a command", "npm test")) { Model = "Claude", Chat = "Build" });
        rig.Cards.Draft(question.Id, "the second one");

        Assert.Equal(PcResolve.Done, rig.Cards.Answer(new PillCardAnswer(question.Id, 0, PillCardChoice.OpenChat)));
        Assert.Equal([("carry", Chat, "the second one")], rig.Inbox.Calls);
        Assert.Equal([(Chat, (string?)"the second one")], rig.Chats);

        // nothing typed: the chat only opens
        Assert.Equal(PcResolve.Done, rig.Cards.Answer(new PillCardAnswer(provider.Id, 0, PillCardChoice.OpenChat)));
        Assert.Single(rig.Inbox.Calls);
        Assert.Equal((Chat, (string?)null), rig.Chats[1]);
    });

    [Fact]
    public void Send_to_chat_sends_the_draft_there_and_a_draft_that_cannot_go_waits_in_the_composer_with_the_chat_open() => headless.OnUiThread(() =>
    {
        using var rig = new PcScreenRig();
        var stopped = Question(rig);
        var other = Question(rig);
        rig.Cards.Draft(stopped.Id, "half an answer");
        rig.Cards.Draft(other.Id, "my own words");
        Assert.True(rig.Assembly.Asks.Withdraw(stopped.Id, PcWithdrawal.TurnEnded));
        Assert.True(rig.Assembly.Asks.Withdraw(other.Id, PcWithdrawal.TurnEnded));

        Assert.Equal(PcResolve.Done, rig.Cards.Answer(new PillCardAnswer(stopped.Id, 1, PillCardChoice.SendToChat, Text: "half an answer")));
        Assert.Equal([("send", Chat, "half an answer")], rig.Inbox.Calls);
        Assert.Empty(rig.Chats);

        rig.Inbox.Sends = false;
        Assert.Equal(PcResolve.Done, rig.Cards.Answer(new PillCardAnswer(other.Id, 1, PillCardChoice.SendToChat, Text: "my own words")));
        Assert.Equal([("send", Chat, "my own words"), ("carry", Chat, "my own words")], rig.Inbox.Calls.Skip(1));
        Assert.Equal([(Chat, (string?)"my own words")], rig.Chats);
    });
}
