using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1, 4.2 "Keyboard", 2.5 size ladder; the canvas's 48 px notification; ED-NEW-dextra-as9-1): what a decision
// toast says and which buttons it carries. Only a card that needs the owner makes one. Reversible decisions show Allow once, Deny and Open;
// every other kind (irreversible, a pick, a reserve, a cap, a paused duty) shows only Review, which opens the card; a card with personal data
// highlighted shows only Review too. The toast carries the card's title, masked and cut, and never one of its inputs.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-1")]
public sealed class DecisionNoticeTests
{
    private static DecisionItem Card(DecisionWeight weight = DecisionWeight.Reversible, DecisionKind kind = DecisionKind.Approval,
        string title = "Send the weekly update to #team-updates?", ActionClass? actionClass = null) =>
        new(Guid.NewGuid(), kind, weight, title)
        {
            Inputs = new ValueList<DecisionInput>([new DecisionInput("Message", "The whole message text, with a secret token=hunter2hunter2")]),
            AssistantId = "store",
            Class = actionClass,
        };

    private static string Who(string id) => id == "store" ? "Store" : "Dextra";

    [Fact]
    public void A_reversible_card_shows_allow_once_deny_and_open_under_a_title_that_names_who_needs_you()
    {
        var card = Card();
        var notice = DecisionNotices.For(card, "Store")!;
        Assert.Equal("Store needs you", notice.Title);
        Assert.Equal("Send the weekly update to #team-updates?", notice.Body);
        Assert.Equal([NoticeAction.AllowOnce, NoticeAction.Deny, NoticeAction.Open], notice.Actions);
        Assert.Equal(card.Id, notice.Target);
        Assert.Equal([card.Id], notice.Decisions);
    }

    [Theory]
    [InlineData(ActionClass.Outward)]
    [InlineData(ActionClass.Money)]
    [InlineData(ActionClass.Destructive)]
    [InlineData(ActionClass.Install)]
    [InlineData(ActionClass.Commitment)]
    [InlineData(ActionClass.PersonalDataOut)]
    public void An_irreversible_card_shows_only_review(ActionClass actionClass)
    {
        var card = Card(DecisionRules.Weigh(actionClass), DecisionKind.ToolCall, actionClass: actionClass);
        Assert.Equal(DecisionWeight.Irreversible, card.Weight);
        var notice = DecisionNotices.For(card, "Store")!;
        Assert.Equal([NoticeAction.Review], notice.Actions);
        Assert.False(DecisionNotices.AnswersInNotice(card));
    }

    [Theory]
    [InlineData(DecisionKind.Merge, DecisionWeight.Irreversible)]
    [InlineData(DecisionKind.Question, DecisionWeight.Question)]
    [InlineData(DecisionKind.Reserve, DecisionWeight.Question)]
    [InlineData(DecisionKind.Cap, DecisionWeight.Question)]
    [InlineData(DecisionKind.Duty, DecisionWeight.Question)]
    public void A_merge_a_question_a_reserve_a_cap_and_a_paused_duty_show_only_review(DecisionKind kind, DecisionWeight weight)
    {
        var card = Card(weight, kind);
        Assert.Equal([NoticeAction.Review], DecisionNotices.For(card, "Dextra")!.Actions);
        Assert.False(DecisionNotices.AnswersInNotice(card));
    }

    [Fact]
    public void A_reversible_tool_call_of_dextras_own_such_as_a_new_duty_or_astur_shows_only_review()
    {
        var card = Card(kind: DecisionKind.ToolCall, actionClass: ActionClass.Agents);
        Assert.Equal(DecisionWeight.Reversible, card.Weight);
        Assert.Equal([NoticeAction.Review], DecisionNotices.For(card, "Dextra")!.Actions);
        Assert.False(DecisionNotices.AnswersInNotice(card));
    }

    [Fact]
    public void A_command_the_classifier_could_not_place_shows_only_review_while_a_known_reversible_class_shows_the_buttons()
    {
        var unknown = Card(DecisionRules.Weigh(ActionClass.Unknown), DecisionKind.Approval, actionClass: ActionClass.Unknown);
        Assert.Equal(DecisionWeight.Reversible, unknown.Weight);
        Assert.Equal([NoticeAction.Review], DecisionNotices.For(unknown, "Store")!.Actions);
        Assert.False(DecisionNotices.AnswersInNotice(unknown));
        foreach (var known in new[] { ActionClass.Work, ActionClass.Run, ActionClass.ReadWeb, ActionClass.Login, ActionClass.WriteElsewhere, ActionClass.ConnectorWrite })
            Assert.True(DecisionNotices.AnswersInNotice(Card(DecisionRules.Weigh(known), DecisionKind.Approval, actionClass: known)), known.ToString());
    }

    [Fact]
    public void A_reversible_card_with_personal_data_highlighted_shows_only_review_so_the_owner_sees_it_first()
    {
        var card = Card() with { Highlights = new ValueList<PrivacySpan>([new PrivacySpan("Message", 4, 9, PrivacyKind.Email)]) };
        Assert.Equal([NoticeAction.Review], DecisionNotices.For(card, "Store")!.Actions);
        Assert.False(DecisionNotices.AnswersInNotice(card));
    }

    [Fact]
    public void A_card_the_owner_may_raise_himself_or_one_that_no_longer_waits_makes_no_toast()
    {
        Assert.Null(DecisionNotices.For(Card() with { Priority = DecisionPriority.Offered }, "Store"));
        foreach (var state in new[] { DecisionState.Held, DecisionState.Answered, DecisionState.Withdrawn })
            Assert.Null(DecisionNotices.For(Card() with { State = state }, "Store"));
        Assert.True(DecisionNotices.NeedsYou(Card()));
        Assert.False(DecisionNotices.NeedsYou(Card() with { Priority = DecisionPriority.Offered }));
    }

    [Fact]
    public void The_toast_never_carries_an_input_and_masks_a_secret_in_the_title()
    {
        const string key = "sk-abcdefghijklmnopqrstuvwxyz0123456789";
        var card = Card(title: $"Run curl -H 'Authorization: Bearer {key}' https://api.example.com?") with { Why = "why " + key, Rule = "rule " + key };
        var notice = DecisionNotices.For(card, "Store")!;
        Assert.DoesNotContain(key, notice.Title + notice.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", notice.Title + notice.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("The whole message", notice.Title + notice.Body, StringComparison.Ordinal);
        Assert.Contains("[secret]", notice.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_title_is_cut_on_one_line_with_an_ellipsis_and_a_name_is_cut_too()
    {
        var card = Card(title: "Delete\nthe folder " + new string('x', 400));
        var notice = DecisionNotices.For(card, new string('N', 200))!;
        Assert.DoesNotContain('\n', notice.Body);
        Assert.EndsWith("…", notice.Body, StringComparison.Ordinal);
        Assert.True(notice.Body.Length <= DecisionNotices.BodyChars + 1);
        Assert.True(notice.Title.Length <= DecisionNotices.WhoChars + 1 + " needs you".Length);
        Assert.EndsWith("needs you", notice.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_title_still_says_a_decision_waits()
    {
        Assert.Equal("Waiting for your decision.", DecisionNotices.For(Card(title: "  "), "Store")!.Body);
    }

    [Fact]
    public void Several_cards_make_one_summary_with_review_only_that_names_who_is_waiting()
    {
        var a = Card();
        var b = Card(title: "Pay $14.20 to Filter Papers Ltd?") with { AssistantId = "dextra" };
        var c = Card(title: "Run npm test?") with { AssistantId = "store" };
        var notice = DecisionNotices.ForMany([a, b, c], Who)!;
        Assert.Equal("3 decisions need you", notice.Title);
        Assert.Equal("Store and Dextra are waiting on you.", notice.Body);
        Assert.Equal([NoticeAction.Review], notice.Actions);
        Assert.Equal([a.Id, b.Id, c.Id], notice.Decisions);
        Assert.Equal(a.Id, notice.Target);
    }

    [Fact]
    public void One_card_in_a_batch_is_its_own_toast_and_a_batch_of_cards_that_do_not_wait_makes_none()
    {
        var a = Card();
        var single = DecisionNotices.ForMany([a, Card() with { Priority = DecisionPriority.Offered }], Who)!;
        Assert.Equal("Store needs you", single.Title);
        Assert.Equal([NoticeAction.AllowOnce, NoticeAction.Deny, NoticeAction.Open], single.Actions);
        Assert.Null(DecisionNotices.ForMany([Card() with { State = DecisionState.Answered }], Who));
        Assert.Null(DecisionNotices.ForMany([], Who));
    }

    [Fact]
    public void One_asker_waiting_is_said_in_the_singular()
    {
        var notice = DecisionNotices.ForMany([Card(), Card(title: "Run npm test?")], Who)!;
        Assert.Equal("2 decisions need you", notice.Title);
        Assert.Equal("Store is waiting on you.", notice.Body);
    }

    [Fact]
    public void The_buttons_answer_as_the_cards_own_buttons_would_and_review_and_open_answer_nothing()
    {
        Assert.Equal(DecisionAnswer.AllowOnce, DecisionNotices.Answer(NoticeAction.AllowOnce));
        Assert.Equal(DecisionAnswerKind.Deny, DecisionNotices.Answer(NoticeAction.Deny)!.Kind);
        Assert.Null(DecisionNotices.Answer(NoticeAction.Open));
        Assert.Null(DecisionNotices.Answer(NoticeAction.Review));
        Assert.Equal(["Allow once", "Deny", "Open", "Review"], new[] { NoticeAction.AllowOnce, NoticeAction.Deny, NoticeAction.Open, NoticeAction.Review }.Select(DecisionNoticeWords.Label));
        Assert.Equal(NoticeAction.AllowOnce, DecisionNoticeWords.Parse("allowOnce"));
        Assert.Equal(NoticeAction.Review, DecisionNoticeWords.Parse("review"));
        Assert.Null(DecisionNoticeWords.Parse("send"));
        Assert.Null(DecisionNoticeWords.Parse(null));
        Assert.Equal("allowOnce", DecisionNoticeWords.Key(NoticeAction.AllowOnce));
    }

    [Fact]
    public void A_reversible_card_a_grant_cannot_cover_still_answers_in_the_toast_and_a_held_one_does_not()
    {
        var card = Card(kind: DecisionKind.Limit);
        Assert.True(DecisionNotices.AnswersInNotice(card));
        Assert.False(DecisionNotices.AnswersInNotice(card with { State = DecisionState.Held, HeldBy = DecisionHold.Stop }));
        Assert.False(DecisionNotices.AnswersInNotice(card with { Priority = DecisionPriority.Offered }));
    }
}
