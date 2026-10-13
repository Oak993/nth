using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1 "Toast buttons", 4.2 "Keyboard", 12 "Answers come only from cards and toasts"; ED-NEW-dextra-as9-5): a button
// on a toast answers its card only when it carries the nonce of that very toast, only once, only when the toast really had that button, and
// only while the card still waits and still takes a toast's answer. Anything else opens the card for review and answers nothing: a payload
// from outside nth, a stale toast, a forged button, an irreversible card.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-5")]
public sealed class DecisionToastActivationTests
{
    private static (DecisionToastRig Rig, DecisionItem Card, DecisionNotice Notice) Shown(DecisionItem? card = null)
    {
        var rig = new DecisionToastRig();
        var raised = rig.Raise(card ?? DecisionToastRig.Approval());
        rig.Pass(DecisionToasts.Together);
        return (rig, raised, Assert.Single(rig.Channel.Sent));
    }

    [Fact]
    public void Allow_once_on_the_toast_answers_the_card_allow_once_and_takes_its_toast_back()
    {
        var (rig, card, notice) = Shown();
        using var owner = rig;
        Assert.Equal(DecisionToastOutcome.Answered, rig.Toasts.Activate(card.Id, notice.Nonce, NoticeAction.AllowOnce));
        var (answered, answer) = Assert.Single(rig.Answers);
        Assert.Equal(card.Id, answered.Id);
        Assert.Equal(DecisionAnswer.AllowOnce, answer);
        Assert.Equal(DecisionState.Answered, rig.Queue.Find(card.Id)!.State);
        Assert.Equal([card.Id], rig.Channel.Withdrawn);
    }

    [Fact]
    public void Deny_on_the_toast_answers_the_card_deny()
    {
        var (rig, card, notice) = Shown();
        using var owner = rig;
        Assert.Equal(DecisionToastOutcome.Answered, rig.Toasts.Activate(card.Id, notice.Nonce, NoticeAction.Deny));
        Assert.Equal(DecisionAnswerKind.Deny, Assert.Single(rig.Answers).Answer.Kind);
    }

    [Fact]
    public void A_button_works_once_and_a_second_press_of_the_same_toast_opens_the_card_instead()
    {
        var (rig, card, notice) = Shown();
        using var owner = rig;
        Assert.Equal(DecisionToastOutcome.Answered, rig.Toasts.Activate(card.Id, notice.Nonce, NoticeAction.AllowOnce));
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(card.Id, notice.Nonce, NoticeAction.Deny));
        Assert.Single(rig.Answers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public void A_missing_or_wrong_nonce_never_answers_and_does_not_spend_the_right_one(string? nonce)
    {
        var (rig, card, notice) = Shown();
        using var owner = rig;
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(card.Id, nonce, NoticeAction.AllowOnce));
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(card.Id, nonce, NoticeAction.Deny));
        Assert.Empty(rig.Answers);
        Assert.Equal(DecisionToastOutcome.Answered, rig.Toasts.Activate(card.Id, notice.Nonce, NoticeAction.AllowOnce));
    }

    [Fact]
    public void A_toast_nth_never_made_cannot_answer_a_card()
    {
        using var rig = new DecisionToastRig();
        var card = rig.Raise(DecisionToastRig.Approval());                  // no toast was made yet
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(card.Id, "feedfeedfeedfeedfeedfeedfeedfeed", NoticeAction.AllowOnce));
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(Guid.NewGuid(), "x", NoticeAction.Deny));
        Assert.Empty(rig.Answers);
    }

    [Fact]
    public void Open_and_review_open_the_card_and_answer_nothing_whatever_the_nonce()
    {
        var (rig, card, notice) = Shown();
        using var owner = rig;
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(card.Id, notice.Nonce, NoticeAction.Open));
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(card.Id, null, NoticeAction.Review));
        Assert.Empty(rig.Answers);
        Assert.Equal(DecisionToastOutcome.Answered, rig.Toasts.Activate(card.Id, notice.Nonce, NoticeAction.AllowOnce)); // Open did not spend it
    }

    [Fact]
    public void An_irreversible_cards_toast_never_answers_even_with_its_own_nonce_and_a_forged_allow_once()
    {
        var (rig, card, notice) = Shown(DecisionToastRig.Approval("Send the update?", actionClass: ActionClass.Outward));
        using var owner = rig;
        Assert.Equal([NoticeAction.Review], notice.Actions);
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(card.Id, notice.Nonce, NoticeAction.AllowOnce));
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(card.Id, notice.Nonce, NoticeAction.Deny));
        Assert.Empty(rig.Answers);
        Assert.Equal(DecisionState.Open, rig.Queue.Find(card.Id)!.State);
    }

    [Fact]
    public void A_summary_toast_never_answers_any_of_its_cards()
    {
        using var rig = new DecisionToastRig();
        var a = rig.Raise(DecisionToastRig.Approval("a"));
        rig.Raise(DecisionToastRig.Approval("b"));
        rig.Pass(DecisionToasts.Together);
        var notice = Assert.Single(rig.Channel.Sent);
        Assert.True(notice.IsSummary);
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(a.Id, notice.Nonce, NoticeAction.AllowOnce));
        Assert.Empty(rig.Answers);
    }

    [Fact]
    public void A_card_that_was_answered_held_or_withdrawn_since_is_not_answered_again()
    {
        var (rig, card, notice) = Shown();
        using var owner = rig;
        rig.Queue.Hold(c => c.Id == card.Id, DecisionHold.Pause);           // an approval is reversible, so Pause leaves it open; Stop holds it
        rig.Queue.Hold(c => c.Id == card.Id, DecisionHold.Stop);
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(card.Id, notice.Nonce, NoticeAction.AllowOnce));
        Assert.Empty(rig.Answers);
    }

    [Fact]
    public void An_answer_the_card_refuses_opens_it_so_the_owner_sees_why()
    {
        var card = DecisionToastRig.Approval();
        var rig = new DecisionToastRig();
        using var owner = rig;
        var raised = rig.Queue.Raise(card, (_, _) => { }, check: (_, _) => "This answer needs the owner's eyes.");
        rig.Pass(DecisionToasts.Together);
        var notice = Assert.Single(rig.Channel.Sent);
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(raised.Id, notice.Nonce, NoticeAction.AllowOnce));
        Assert.Equal(DecisionState.Open, rig.Queue.Find(raised.Id)!.State);
    }

    [Fact]
    public void A_nonce_is_compared_in_full_so_a_prefix_or_a_longer_text_is_wrong()
    {
        var (rig, card, notice) = Shown();
        using var owner = rig;
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(card.Id, notice.Nonce[..31], NoticeAction.AllowOnce));
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(card.Id, notice.Nonce + "0", NoticeAction.AllowOnce));
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(card.Id, notice.Nonce.ToUpperInvariant(), NoticeAction.AllowOnce));
        Assert.Empty(rig.Answers);
    }
}
