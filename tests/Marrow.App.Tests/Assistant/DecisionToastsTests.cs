using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1, 4.2 "Batching"; ED-NEW-dextra-as9-1, -2, -3, -6): a decision card that needs the owner makes one toast, a
// moment after it is raised so cards that arrive together are one summary and a card answered at once makes none; never one a second sooner
// than the gap after the last; never while the Assistant home is on screen; never for a card he may raise himself; and a toast is taken back
// as soon as its card is answered, held or withdrawn.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-2")]
public sealed class DecisionToastsTests
{
    private static readonly TimeSpan Window = DecisionToasts.Together;

    [Fact]
    public void The_windows_are_a_second_to_gather_and_four_seconds_between_toasts()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), DecisionToasts.Together);
        Assert.Equal(TimeSpan.FromSeconds(4), DecisionToasts.Gap);
    }

    [Fact]
    public void A_card_that_needs_the_owner_makes_one_toast_after_the_window_and_not_before()
    {
        using var rig = new DecisionToastRig();
        var card = rig.Raise(DecisionToastRig.Approval("Run npm test?"));
        rig.Pass(Window - TimeSpan.FromMilliseconds(1));
        Assert.Empty(rig.Channel.Sent);
        rig.Pass(TimeSpan.FromMilliseconds(1));
        var notice = Assert.Single(rig.Channel.Sent);
        Assert.Equal("Store needs you", notice.Title);
        Assert.Equal("Run npm test?", notice.Body);
        Assert.Equal(card.Id, notice.Target);
        Assert.Equal([NoticeAction.AllowOnce, NoticeAction.Deny, NoticeAction.Open], notice.Actions);
        Assert.Matches("^[0-9a-f]{32}$", notice.Nonce);
        rig.Pass(TimeSpan.FromMinutes(5));
        Assert.Single(rig.Channel.Sent);
    }

    [Fact]
    public void Each_toast_has_a_nonce_of_its_own()
    {
        using var rig = new DecisionToastRig();
        rig.Raise(DecisionToastRig.Approval("one"));
        rig.Pass(Window);
        rig.Raise(DecisionToastRig.Approval("two"));
        rig.Pass(DecisionToasts.Gap);
        Assert.Equal(2, rig.Channel.Sent.Count);
        Assert.NotEqual(rig.Channel.Sent[0].Nonce, rig.Channel.Sent[1].Nonce);
    }

    [Fact]
    public void An_irreversible_card_shows_review_only_in_its_toast()
    {
        using var rig = new DecisionToastRig();
        rig.Raise(DecisionToastRig.Approval("Send the weekly update to #team-updates?", actionClass: ActionClass.Outward));
        rig.Pass(Window);
        var notice = Assert.Single(rig.Channel.Sent);
        Assert.Equal([NoticeAction.Review], notice.Actions);
        Assert.Equal("Send the weekly update to #team-updates?", notice.Body);
    }

    [Fact]
    public void Cards_that_arrive_together_make_one_summary_that_only_reviews()
    {
        using var rig = new DecisionToastRig();
        var first = rig.Raise(DecisionToastRig.Approval("one", "store"));
        rig.Pass(TimeSpan.FromMilliseconds(300));
        rig.Raise(DecisionToastRig.Approval("two", "dextra"));
        rig.Pass(TimeSpan.FromMilliseconds(300));
        rig.Raise(DecisionToastRig.Approval("three", "store"));
        rig.Pass(Window);
        var notice = Assert.Single(rig.Channel.Sent);
        Assert.Equal("3 decisions need you", notice.Title);
        Assert.Equal("Store and Dextra are waiting on you.", notice.Body);
        Assert.Equal([NoticeAction.Review], notice.Actions);
        Assert.Equal(first.Id, notice.Target);
    }

    [Fact]
    public void A_card_answered_inside_the_window_makes_no_toast_and_the_others_still_do()
    {
        using var rig = new DecisionToastRig();
        var answered = rig.Raise(DecisionToastRig.Approval("answered at once"));
        var waiting = rig.Raise(DecisionToastRig.Approval("still waiting"));
        Assert.Null(rig.Queue.Answer(answered.Id, DecisionAnswer.AllowOnce));
        rig.Pass(Window);
        var notice = Assert.Single(rig.Channel.Sent);
        Assert.Equal(waiting.Id, notice.Target);
        Assert.Equal("still waiting", notice.Body);

        using var alone = new DecisionToastRig();
        var gone = alone.Raise(DecisionToastRig.Approval());
        alone.Queue.Answer(gone.Id, DecisionAnswer.Deny());
        alone.Pass(TimeSpan.FromMinutes(1));
        Assert.Empty(alone.Channel.Sent);
    }

    [Fact]
    public void A_card_just_after_a_toast_waits_for_the_gap_so_a_drip_never_floods_the_screen()
    {
        using var rig = new DecisionToastRig();
        rig.Raise(DecisionToastRig.Approval("first"));
        rig.Pass(Window);                                                   // the first toast, at 1 s
        Assert.Single(rig.Channel.Sent);
        rig.Pass(TimeSpan.FromMilliseconds(500));
        rig.Raise(DecisionToastRig.Approval("second"));                     // at 1.5 s
        rig.Pass(TimeSpan.FromMilliseconds(3499));                          // 4.999 s
        Assert.Single(rig.Channel.Sent);
        rig.Pass(TimeSpan.FromMilliseconds(1));                             // 5 s: four seconds after the first
        Assert.Equal(2, rig.Channel.Sent.Count);
        Assert.Equal("second", rig.Channel.Sent[1].Body);
    }

    [Fact]
    public void A_card_long_after_the_last_toast_waits_only_the_window()
    {
        using var rig = new DecisionToastRig();
        rig.Raise(DecisionToastRig.Approval("first"));
        rig.Pass(TimeSpan.FromMinutes(2));
        rig.Raise(DecisionToastRig.Approval("second"));
        rig.Pass(Window);
        Assert.Equal(2, rig.Channel.Sent.Count);
    }

    [Fact]
    public void While_the_home_is_on_screen_no_toast_is_made_and_none_is_made_later_for_the_same_card()
    {
        using var rig = new DecisionToastRig { HomeOnScreen = true };
        rig.Raise(DecisionToastRig.Approval());
        rig.Pass(Window);
        Assert.Empty(rig.Channel.Sent);
        rig.HomeOnScreen = false;                                           // the owner leaves the home: the card is in the sidebar's count
        rig.Pass(TimeSpan.FromMinutes(10));
        Assert.Empty(rig.Channel.Sent);
        rig.Raise(DecisionToastRig.Approval("a new one"));                  // a card raised now does
        rig.Pass(Window);
        Assert.Equal("a new one", Assert.Single(rig.Channel.Sent).Body);
    }

    [Fact]
    public void A_suppressed_batch_does_not_start_the_gap()
    {
        using var rig = new DecisionToastRig { HomeOnScreen = true };
        rig.Raise(DecisionToastRig.Approval());
        rig.Pass(Window);
        rig.HomeOnScreen = false;
        rig.Raise(DecisionToastRig.Approval("now"));
        rig.Pass(Window);                                                   // nothing was shown before, so only the window is waited
        Assert.Equal("now", Assert.Single(rig.Channel.Sent).Body);
    }

    [Fact]
    public void A_card_the_owner_may_raise_himself_makes_no_toast()
    {
        using var rig = new DecisionToastRig();
        rig.Raise(DecisionToastRig.Approval("optional") with { Priority = DecisionPriority.Offered });
        rig.Pass(TimeSpan.FromMinutes(1));
        Assert.Empty(rig.Channel.Sent);
    }

    [Fact]
    public void A_waiting_card_raised_twice_with_the_same_binding_makes_one_toast()
    {
        using var rig = new DecisionToastRig();
        var card = DecisionToastRig.Approval("same", request: "r1");
        rig.Raise(card);
        rig.Raise(card with { Id = Guid.NewGuid() });
        rig.Pass(Window);
        Assert.Equal([NoticeAction.AllowOnce, NoticeAction.Deny, NoticeAction.Open], Assert.Single(rig.Channel.Sent).Actions);
    }

    [Fact]
    public void A_toast_is_taken_back_when_its_card_is_answered_in_the_app()
    {
        using var rig = new DecisionToastRig();
        var card = rig.Raise(DecisionToastRig.Approval());
        rig.Pass(Window);
        Assert.Empty(rig.Channel.Withdrawn);
        Assert.Null(rig.Queue.Answer(card.Id, DecisionAnswer.AllowOnce));
        Assert.Equal([card.Id], rig.Channel.Withdrawn);
        rig.Queue.Answer(card.Id, DecisionAnswer.Deny());                   // answering again changes nothing
        Assert.Single(rig.Channel.Withdrawn);
    }

    [Fact]
    public void A_toast_is_taken_back_when_its_card_is_held_by_stop_or_withdrawn()
    {
        using var rig = new DecisionToastRig();
        var held = rig.Raise(DecisionToastRig.Approval("held"));
        rig.Pass(Window);
        rig.Queue.Hold(c => c.Id == held.Id, DecisionHold.Stop);
        Assert.Equal([held.Id], rig.Channel.Withdrawn);

        var withdrawn = rig.Raise(DecisionToastRig.Approval("withdrawn"));
        rig.Pass(DecisionToasts.Gap);
        Assert.True(rig.Queue.Withdraw(withdrawn.Id, "the provider cancelled it"));
        Assert.Equal([held.Id, withdrawn.Id], rig.Channel.Withdrawn);
    }

    [Fact]
    public void A_summary_is_taken_back_only_when_none_of_its_cards_waits()
    {
        using var rig = new DecisionToastRig();
        var a = rig.Raise(DecisionToastRig.Approval("a"));
        var b = rig.Raise(DecisionToastRig.Approval("b"));
        rig.Pass(Window);
        Assert.Equal(a.Id, Assert.Single(rig.Channel.Sent).Target);
        rig.Queue.Answer(a.Id, DecisionAnswer.AllowOnce);
        Assert.Empty(rig.Channel.Withdrawn);
        rig.Queue.Answer(b.Id, DecisionAnswer.AllowOnce);
        Assert.Equal([a.Id], rig.Channel.Withdrawn);
    }

    [Fact]
    public void A_card_released_again_after_a_hold_makes_no_new_toast()
    {
        using var rig = new DecisionToastRig();
        var card = rig.Raise(DecisionToastRig.Approval());
        rig.Pass(Window);
        rig.Queue.Hold(c => c.Id == card.Id, DecisionHold.Stop);
        rig.Queue.Release(c => c.Id == card.Id);
        rig.Pass(TimeSpan.FromMinutes(1));
        Assert.Single(rig.Channel.Sent);
    }

    [Fact]
    public void A_channel_that_says_no_leaves_nothing_to_answer_from()
    {
        using var rig = new DecisionToastRig();
        rig.Channel.Takes = false;
        var card = rig.Raise(DecisionToastRig.Approval());
        rig.Pass(Window);
        var notice = Assert.Single(rig.Channel.Sent);
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(card.Id, notice.Nonce, NoticeAction.AllowOnce));
        Assert.Empty(rig.Answers);
    }

    [Fact]
    public void A_burst_of_cards_is_bounded_and_still_one_summary()
    {
        using var rig = new DecisionToastRig();
        for (var i = 0; i < DecisionToasts.MaxPending + 50; i++) rig.Raise(DecisionToastRig.Approval("card " + i));
        rig.Pass(Window);
        var notice = Assert.Single(rig.Channel.Sent);
        Assert.Equal(DecisionToasts.MaxPending, notice.Decisions.Count);
        Assert.Equal($"{DecisionToasts.MaxPending} decisions need you", notice.Title);
    }

    [Fact]
    public void Only_the_newest_toasts_keep_a_button_that_answers()
    {
        using var rig = new DecisionToastRig();
        var cards = new List<DecisionItem>();
        for (var i = 0; i < DecisionToasts.MaxShown + 1; i++)
        {
            cards.Add(rig.Raise(DecisionToastRig.Approval("card " + i)));
            rig.Pass(DecisionToasts.Gap);
        }
        Assert.Equal(DecisionToasts.MaxShown + 1, rig.Channel.Sent.Count);
        var oldest = rig.Channel.Sent[0];
        var newest = rig.Channel.Sent[^1];
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(oldest.Target, oldest.Nonce, NoticeAction.AllowOnce));
        Assert.Equal(DecisionToastOutcome.Answered, rig.Toasts.Activate(newest.Target, newest.Nonce, NoticeAction.AllowOnce));
        Assert.Contains(oldest.Target, rig.Channel.Withdrawn);
    }

    [Fact]
    public void After_dispose_no_card_makes_a_toast_and_the_toasts_still_up_are_taken_back()
    {
        using var rig = new DecisionToastRig();
        var shown = rig.Raise(DecisionToastRig.Approval("shown"));
        rig.Pass(Window);
        rig.Raise(DecisionToastRig.Approval("pending"));
        rig.Toasts.Dispose();
        rig.Pass(TimeSpan.FromMinutes(1));
        Assert.Single(rig.Channel.Sent);
        Assert.Equal([shown.Id], rig.Channel.Withdrawn);
        rig.Raise(DecisionToastRig.Approval("later"));
        rig.Pass(TimeSpan.FromMinutes(1));
        Assert.Single(rig.Channel.Sent);
        Assert.Equal(0, rig.Clock.ActiveTimers);
        rig.Toasts.Dispose();                                               // twice is fine
    }

    [Fact]
    public void A_channel_that_fails_never_throws_into_the_queue_and_the_fault_names_no_card_text()
    {
        const string secret = "sk-SENTINEL0123456789abcdefghijklmnop";
        using var rig = new DecisionToastRig();
        rig.Channel.Throws = new InvalidOperationException("the shell refused " + secret);
        var card = rig.Raise(DecisionToastRig.Approval("Run curl with " + secret));
        rig.Pass(Window);                                                   // nothing throws out of the timer
        rig.Raise(DecisionToastRig.Approval("another " + secret));
        rig.Pass(DecisionToasts.Gap);
        var fault = Assert.Single(rig.Faults);                              // told once, not once per card
        Assert.Equal("toast.send.failed InvalidOperationException", fault);
        Assert.DoesNotContain(secret, fault, StringComparison.Ordinal);
        Assert.DoesNotContain("curl", fault, StringComparison.Ordinal);
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(card.Id, "anything", NoticeAction.AllowOnce)); // nothing is left to answer from
        Assert.Null(rig.Queue.Answer(card.Id, DecisionAnswer.AllowOnce));   // the card still works in the app
    }

    [Fact]
    public void A_channel_that_fails_to_take_a_toast_back_is_not_an_error()
    {
        using var rig = new DecisionToastRig();
        var card = rig.Raise(DecisionToastRig.Approval());
        rig.Pass(Window);
        rig.Channel.Throws = new InvalidOperationException("the shell refused");
        Assert.Null(rig.Queue.Answer(card.Id, DecisionAnswer.AllowOnce));
        Assert.Equal("toast.withdraw.failed InvalidOperationException", Assert.Single(rig.Faults));
    }
}
