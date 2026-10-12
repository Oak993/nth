using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-16; DESIGN.md 10.4). The words around an ask: the card's titles,
// the too-late line, the answer rows in the chat's timeline, which asks the pill answers, and which lifetime each kind has.
[Trait("Decision", "ED-NEW-pcctl-16")]
public sealed class AskLifetimesTests
{
    private static PcPendingAsk Ask(PcAskKind kind, object payload, string? appName = "Outlook", string? chat = null) =>
        new("ask-1", kind, "chat-1", payload, 0, Task.FromResult(new PcAskOutcome(PcAskStatus.Pending))) { AppName = appName, Chat = chat };

    private static readonly PcConfirmContent Send = new(PcConfirmCategory.Send, "send", "Send", "Outlook", []);

    [Fact]
    public void The_titles_name_the_kind_and_the_app()
    {
        Assert.Equal("Needs your OK. Outlook", Ask(PcAskKind.Approval, new PcApprovalAsk(PcCapability.Act, false, true)).Title);
        Assert.Equal("Needs your OK. Outlook", Ask(PcAskKind.Confirm, Send).Title);
        Assert.Equal("Needs your OK. Excel", Ask(PcAskKind.ShowWindow, new PcShowAsk("Book1"), "Excel").Title);
        Assert.Equal("Waiting for your answer. Excel", Ask(PcAskKind.Question, new PcAskRequest("Which?", [], true, null), "Excel").Title);
        Assert.Equal("Waiting for your answer", Ask(PcAskKind.Question, new PcAskRequest("Which?", [], true, null), null).Title);
        Assert.Equal("Waiting for your answer", Ask(PcAskKind.ProviderQuestion, new PcProviderAsk("Question", "Which?")).Title);
        Assert.Equal("Run a command. Inbox triage", Ask(PcAskKind.ProviderApproval, new PcProviderAsk("Run a command", "npm test"), null, "Inbox triage").Title);
    }

    [Fact]
    public void A_late_click_says_what_expired_and_that_nothing_was_done()
    {
        Assert.Equal("Send in Outlook expired. Nothing was sent. Ask in chat to try again.", AskLifetimes.TooLate(Ask(PcAskKind.Confirm, Send)));
        Assert.Equal("Showing Excel expired. Nothing was shown. Ask in chat to try again.", AskLifetimes.TooLate(Ask(PcAskKind.ShowWindow, new PcShowAsk("Book1"), "Excel")));
        Assert.Equal(2_000, AskLifetimes.TooLateMs);
    }

    [Fact]
    public void Answers_leave_their_row_in_the_chat()
    {
        var approval = Ask(PcAskKind.Approval, new PcApprovalAsk(PcCapability.Act, false, true));
        var question = Ask(PcAskKind.Question, new PcAskRequest("Which file?", ["Use the March file"], true, null), "Excel");

        Assert.Equal("You allowed Outlook for this chat (from the pill)", AskLifetimes.Logged(approval, PcApproval.Chat, PcAnswerSource.Pill));
        Assert.Equal("You answered from the pill: Use the March file", AskLifetimes.Logged(question, PcAnswer.Chose("Use the March file"), PcAnswerSource.Pill));
        Assert.Equal("You cancelled Send in Outlook (from the pill)", AskLifetimes.Logged(Ask(PcAskKind.Confirm, Send), false, PcAnswerSource.Pill));
        Assert.Equal("You confirmed Send in Outlook", AskLifetimes.Logged(Ask(PcAskKind.Confirm, Send), true, PcAnswerSource.Chat));
        Assert.Equal("You answered: later", AskLifetimes.Logged(question, PcAnswer.Typed("later"), PcAnswerSource.Chat));
    }

    [Fact]
    public void The_pill_answers_everything_but_provider_approvals_and_secrets_and_only_with_its_setting_on()
    {
        Assert.True(AskLifetimes.AnsweredInPill(Ask(PcAskKind.Confirm, Send), answerInPill: true));
        Assert.False(AskLifetimes.AnsweredInPill(Ask(PcAskKind.Confirm, Send), answerInPill: false));
        Assert.False(AskLifetimes.AnsweredInPill(Ask(PcAskKind.ProviderApproval, new PcProviderAsk("Edit a file", "a.cs")), true));
        Assert.False(AskLifetimes.AnsweredInPill(Ask(PcAskKind.ProviderQuestion, new PcProviderAsk("Question", "Token?", Secret: true)), true));
        Assert.False(AskLifetimes.AnsweredInPill(Ask(PcAskKind.Question, new PcAskRequest("What is your password?", [], true, null)), true));
        Assert.True(AskLifetimes.AnsweredInPill(Ask(PcAskKind.Question, new PcAskRequest("Which file?", [], true, null)), true));
    }

    [Fact]
    public void Each_kind_has_its_lifetime()
    {
        foreach (var kind in new[] { PcAskKind.Approval, PcAskKind.Confirm, PcAskKind.ShowWindow })
        {
            Assert.True(AskLifetimes.EndsWithStep(kind));
            Assert.True(AskLifetimes.EndsWithTurn(kind));
            Assert.True(AskLifetimes.EndsWithStop(kind));
            Assert.False(AskLifetimes.EndsWhenUserWrites(kind));
            Assert.Null(AskLifetimes.LifetimeMs(kind));
        }
        Assert.False(AskLifetimes.EndsWithStep(PcAskKind.Question));
        Assert.False(AskLifetimes.EndsWithTurn(PcAskKind.Question));
        Assert.False(AskLifetimes.EndsWithStop(PcAskKind.Question));
        Assert.True(AskLifetimes.EndsWhenUserWrites(PcAskKind.Question));
        Assert.Equal(30 * 60 * 1000, AskLifetimes.LifetimeMs(PcAskKind.Question));
        foreach (var kind in new[] { PcAskKind.ProviderQuestion, PcAskKind.ProviderApproval })
        {
            Assert.False(AskLifetimes.EndsWithStep(kind) || AskLifetimes.EndsWithTurn(kind) || AskLifetimes.EndsWhenUserWrites(kind));
            Assert.Null(AskLifetimes.LifetimeMs(kind));
        }
    }
}
