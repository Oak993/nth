using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1 "Duty opt-in", 4.2 "Batching"; ED-NEW-dextra-as9-2, -3, -9): a "Notify me" duty's finding is news, not a decision. It
// waits the same second as a card, so findings that arrive together are one summary; it never follows another toast within the gap, decision or duty; it
// waits behind a decision toast that is due; it is not shown while the Assistant home is in front, nor later; and it keeps nothing a press could answer.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-9")]
public sealed class DutyToastsTests
{
    private static readonly TimeSpan Window = DecisionToasts.Together;

    private static DutyFinding Finding(string duty = "Espresso price", string? report = "The price fell to $640.") => new(Guid.NewGuid(), duty, report);

    [Fact]
    public void A_finding_makes_one_duty_toast_after_the_window_and_not_before()
    {
        using var rig = new DecisionToastRig();
        var finding = Finding();
        rig.Toasts.NotifyDuty(finding);
        rig.Pass(Window - TimeSpan.FromMilliseconds(1));
        Assert.Empty(rig.Channel.SentDuty);
        rig.Pass(TimeSpan.FromMilliseconds(1));
        var notice = Assert.Single(rig.Channel.SentDuty);
        Assert.Equal("Espresso price", notice.Title);
        Assert.Equal("The price fell to $640.", notice.Body);
        Assert.Equal(finding.Mission, notice.Target);
        Assert.Empty(rig.Channel.Sent);                                          // it is not a decision
        rig.Pass(TimeSpan.FromMinutes(5));
        Assert.Single(rig.Channel.SentDuty);
    }

    [Fact]
    public void Findings_that_arrive_together_are_one_summary()
    {
        using var rig = new DecisionToastRig();
        rig.Toasts.NotifyDuty(Finding("Espresso price"));
        rig.Pass(TimeSpan.FromMilliseconds(400));
        rig.Toasts.NotifyDuty(Finding("Inbox", "Three invoices came in."));
        rig.Pass(Window);
        var notice = Assert.Single(rig.Channel.SentDuty);
        Assert.Equal("2 duties have news", notice.Title);
        Assert.Equal("Espresso price and Inbox have something for you.", notice.Body);
        Assert.True(notice.IsSummary);
    }

    [Fact]
    public void A_second_duty_toast_waits_out_the_gap_after_the_first()
    {
        using var rig = new DecisionToastRig();
        rig.Toasts.NotifyDuty(Finding("One"));
        rig.Pass(Window);
        rig.Toasts.NotifyDuty(Finding("Two"));
        rig.Pass(Window);
        Assert.Single(rig.Channel.SentDuty);
        rig.Pass(DecisionToasts.Gap - Window - TimeSpan.FromMilliseconds(1));
        Assert.Single(rig.Channel.SentDuty);
        rig.Pass(TimeSpan.FromMilliseconds(1));
        Assert.Equal(["One", "Two"], rig.Channel.SentDuty.Select(n => n.Title));
    }

    [Fact]
    public void A_decision_card_and_a_finding_in_one_window_make_the_decision_toast_first_and_the_duty_toast_after_the_gap()
    {
        using var rig = new DecisionToastRig();
        rig.Toasts.NotifyDuty(Finding());
        rig.Raise(DecisionToastRig.Approval("Run npm test?"));
        rig.Pass(Window);
        Assert.Single(rig.Channel.Sent);
        Assert.Empty(rig.Channel.SentDuty);
        rig.Pass(DecisionToasts.Gap - TimeSpan.FromMilliseconds(1));
        Assert.Empty(rig.Channel.SentDuty);
        rig.Pass(TimeSpan.FromMilliseconds(1));
        Assert.Single(rig.Channel.SentDuty);
        Assert.Single(rig.Channel.Sent);
    }

    [Fact]
    public void A_decision_card_after_a_duty_toast_waits_out_the_same_gap()
    {
        using var rig = new DecisionToastRig();
        rig.Toasts.NotifyDuty(Finding());
        rig.Pass(Window);
        rig.Raise(DecisionToastRig.Approval("Run npm test?"));
        rig.Pass(Window);
        Assert.Empty(rig.Channel.Sent);
        rig.Pass(DecisionToasts.Gap - Window);
        Assert.Single(rig.Channel.Sent);
    }

    [Fact]
    public void A_card_answered_inside_the_window_leaves_the_finding_to_make_its_toast_alone()
    {
        using var rig = new DecisionToastRig();
        var card = rig.Raise(DecisionToastRig.Approval());
        rig.Toasts.NotifyDuty(Finding());
        Assert.Null(rig.Queue.Answer(card.Id, DecisionAnswer.AllowOnce));
        rig.Pass(Window);
        Assert.Empty(rig.Channel.Sent);
        Assert.Single(rig.Channel.SentDuty);
    }

    [Fact]
    public void While_the_home_is_in_front_the_finding_is_not_shown_and_not_shown_later_either()
    {
        using var rig = new DecisionToastRig { HomeOnScreen = true };
        rig.Toasts.NotifyDuty(Finding());
        rig.Pass(Window);
        rig.HomeOnScreen = false;
        rig.Pass(TimeSpan.FromMinutes(5));
        Assert.Empty(rig.Channel.SentDuty);
        rig.Toasts.NotifyDuty(Finding("After"));
        rig.Pass(Window);
        Assert.Equal("After", Assert.Single(rig.Channel.SentDuty).Title);       // a finding after he leaves toasts as usual
    }

    [Fact]
    public void A_finding_waiting_behind_a_decision_toast_is_dropped_if_the_home_comes_to_the_front_meanwhile()
    {
        using var rig = new DecisionToastRig();
        rig.Toasts.NotifyDuty(Finding());
        rig.Raise(DecisionToastRig.Approval());
        rig.Pass(Window);
        Assert.Single(rig.Channel.Sent);
        rig.HomeOnScreen = true;
        rig.Pass(DecisionToasts.Gap);
        Assert.Empty(rig.Channel.SentDuty);
    }

    [Fact]
    public void A_channel_that_cannot_show_it_is_not_a_fault_and_one_that_throws_is_said_once_by_name_and_error_kind_only()
    {
        using var rig = new DecisionToastRig();
        rig.Channel.Takes = false;
        rig.Toasts.NotifyDuty(Finding());
        rig.Pass(Window);
        Assert.Single(rig.Channel.SentDuty);
        Assert.Empty(rig.Faults);

        rig.Channel.Throws = new InvalidOperationException("sentinel-secret-sk-abcdefghijklmnopqrstuvwxyz0123456789");
        rig.Toasts.NotifyDuty(Finding("Two"));
        rig.Pass(DecisionToasts.Gap);
        rig.Toasts.NotifyDuty(Finding("Three"));
        rig.Pass(DecisionToasts.Gap);
        Assert.Equal(["toast.duty.failed InvalidOperationException"], rig.Faults);
        Assert.DoesNotContain("sentinel-secret", string.Concat(rig.Faults), StringComparison.Ordinal);
    }

    [Fact]
    public void A_duty_toast_keeps_no_button_that_answers_and_needs_nothing_taken_back()
    {
        using var rig = new DecisionToastRig();
        var finding = Finding();
        rig.Toasts.NotifyDuty(finding);
        rig.Pass(Window);
        Assert.Equal(DecisionToastOutcome.Opened, rig.Toasts.Activate(finding.Mission, "0123456789abcdef0123456789abcdef", NoticeAction.AllowOnce));
        Assert.Empty(rig.Answers);
        rig.Toasts.Dispose();
        Assert.Empty(rig.Channel.Withdrawn);
    }

    [Fact]
    public void Disposing_cancels_a_finding_still_waiting_and_a_finding_after_that_is_ignored()
    {
        var rig = new DecisionToastRig();
        rig.Toasts.NotifyDuty(Finding());
        rig.Toasts.Dispose();
        rig.Toasts.NotifyDuty(Finding("After"));
        rig.Pass(TimeSpan.FromMinutes(1));
        Assert.Empty(rig.Channel.SentDuty);
    }

    [Fact]
    public void A_burst_of_findings_is_capped_and_a_missing_finding_is_refused()
    {
        using var rig = new DecisionToastRig();
        for (var i = 0; i < DecisionToasts.MaxPending + 50; i++) rig.Toasts.NotifyDuty(Finding("Duty " + i));
        rig.Pass(Window);
        var notice = Assert.Single(rig.Channel.SentDuty);
        Assert.Equal(DecisionToasts.MaxPending, notice.Missions.Count);
        Assert.Throws<ArgumentNullException>(() => rig.Toasts.NotifyDuty(null!));
    }
}
