using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-5 (DESIGN.md rev 5, 4.2; ED-NEW-dextra-as5-1): the answers each card takes. An irreversible step only ever
// takes Do it or Don't, never a grant; Always... needs a structured grant that covers what the card asks; a held,
// answered or withdrawn card takes nothing; a card is bound to its exact inputs.
[Trait("Decision", "ED-NEW-dextra-as5-1")]
[Trait("Feature", "AS-5")]
public sealed class DecisionRulesTests
{
    private static readonly Guid Mission = Guid.NewGuid();

    private static DecisionItem Card(ActionClass actionClass, GrantTarget? target = null, Guid? mission = null) =>
        new(Guid.NewGuid(), DecisionKind.Approval, DecisionRules.Weigh(actionClass), "Run this command?")
        {
            AssistantId = "dextra",
            MissionId = mission ?? Mission,
            Class = actionClass,
            GrantTarget = target,
            Inputs = new([new DecisionInput("command", "curl https://example.test")]),
        };

    [Theory]
    [InlineData(ActionClass.Outward)]
    [InlineData(ActionClass.Commitment)]
    [InlineData(ActionClass.Destructive)]
    [InlineData(ActionClass.Install)]
    [InlineData(ActionClass.Money)]
    [InlineData(ActionClass.PersonalDataOut)]
    public void An_irreversible_step_takes_only_do_it_or_dont(ActionClass actionClass)
    {
        var card = Card(actionClass, GrantTarget.Recipients("a@example.test"));
        Assert.Equal(DecisionWeight.Irreversible, card.Weight);
        Assert.Equal([DecisionAnswerKind.DoIt, DecisionAnswerKind.Dont], card.Answers);
        foreach (var refused in new[] { DecisionAnswer.AllowOnce, DecisionAnswer.ForThisMission, DecisionAnswer.Deny() })
            Assert.NotNull(DecisionRules.Refusal(card, refused));
        var grant = new Grant(Guid.NewGuid(), "dextra", actionClass, GrantTarget.Recipients("a@example.test"), GrantScope.Standing);
        Assert.NotNull(DecisionRules.Refusal(card, DecisionAnswer.Always(grant)));
        Assert.Null(DecisionRules.Refusal(card, DecisionAnswer.DoIt));
        Assert.Null(DecisionRules.Refusal(card, DecisionAnswer.Dont()));
    }

    [Fact]
    public void A_grant_class_offers_allow_once_for_this_mission_always_and_deny()
    {
        var card = Card(ActionClass.WriteElsewhere, GrantTarget.Folder(@"C:\Users\o\Documents\Notes"));
        Assert.Equal(DecisionWeight.Reversible, card.Weight);
        Assert.Equal([DecisionAnswerKind.AllowOnce, DecisionAnswerKind.ForThisMission, DecisionAnswerKind.Always, DecisionAnswerKind.Deny], card.Answers);
        Assert.Null(DecisionRules.Refusal(card, DecisionAnswer.ForThisMission));
    }

    [Fact]
    public void Unknown_and_work_that_waits_for_auto_answering_take_only_allow_once_or_deny()
    {
        Assert.Equal([DecisionAnswerKind.AllowOnce, DecisionAnswerKind.Deny], Card(ActionClass.Unknown).Answers);
        Assert.Equal([DecisionAnswerKind.AllowOnce, DecisionAnswerKind.Deny], Card(ActionClass.Work).Answers);
        Assert.Equal([DecisionAnswerKind.AllowOnce, DecisionAnswerKind.Deny], Card(ActionClass.Run).Answers);
        Assert.NotNull(DecisionRules.Refusal(Card(ActionClass.Unknown), DecisionAnswer.ForThisMission));
    }

    [Fact]
    public void Always_needs_a_live_structured_grant_that_covers_the_card()
    {
        var folder = GrantTarget.Folder(@"C:\Users\o\Documents\Notes");
        var card = Card(ActionClass.WriteElsewhere, folder);
        Grant Make(ActionClass c, GrantTarget t, GrantScope s, string assistant = "dextra") => new(Guid.NewGuid(), assistant, c, t, s);

        Assert.NotNull(DecisionRules.Refusal(card, new DecisionAnswer(DecisionAnswerKind.Always)));
        Assert.NotNull(DecisionRules.Refusal(card, DecisionAnswer.Always(Make(ActionClass.ConnectorWrite, GrantTarget.Connector("calendar"), GrantScope.Standing))));
        Assert.NotNull(DecisionRules.Refusal(card, DecisionAnswer.Always(Make(ActionClass.WriteElsewhere, folder, GrantScope.Standing, "store"))));
        Assert.NotNull(DecisionRules.Refusal(card, DecisionAnswer.Always(Make(ActionClass.WriteElsewhere, folder, GrantScope.Once))));
        Assert.NotNull(DecisionRules.Refusal(card, DecisionAnswer.Always(Make(ActionClass.WriteElsewhere, GrantTarget.Folder(@"C:\Elsewhere"), GrantScope.Standing))));
        Assert.Null(DecisionRules.Refusal(card, DecisionAnswer.Always(Make(ActionClass.WriteElsewhere, GrantTarget.Folder(@"C:\Users\o\Documents"), GrantScope.Standing))));
    }

    [Fact]
    public void A_held_answered_or_withdrawn_card_takes_no_answer()
    {
        var card = Card(ActionClass.WriteElsewhere, GrantTarget.Folder(@"C:\x"));
        Assert.Equal("Held by Stop.", DecisionRules.Refusal(card with { State = DecisionState.Held, HeldBy = DecisionHold.Stop }, DecisionAnswer.Deny()));
        Assert.Equal("Held by Pause.", DecisionRules.Refusal(card with { State = DecisionState.Held, HeldBy = DecisionHold.Pause }, DecisionAnswer.Deny()));
        Assert.NotNull(DecisionRules.Refusal(card with { State = DecisionState.Answered }, DecisionAnswer.AllowOnce));
        Assert.NotNull(DecisionRules.Refusal(card with { State = DecisionState.Withdrawn }, DecisionAnswer.AllowOnce));
    }

    [Fact]
    public void Pause_holds_only_irreversible_and_merge_cards()
    {
        Assert.True(DecisionRules.HeldByPause(Card(ActionClass.Outward)));
        Assert.False(DecisionRules.HeldByPause(Card(ActionClass.WriteElsewhere)));
        Assert.True(DecisionRules.HeldByPause(new DecisionItem(Guid.NewGuid(), DecisionKind.Merge, DecisionWeight.Irreversible, "Merge?")));
    }

    [Fact]
    public void A_question_takes_its_own_options_or_discuss()
    {
        var question = new DecisionItem(Guid.NewGuid(), DecisionKind.Question, DecisionWeight.Question, "Keep SQLite or move to Postgres?")
        {
            Options = new(["Keep SQLite", "Move to Postgres"]),
        };
        Assert.Equal([DecisionAnswerKind.Pick, DecisionAnswerKind.Discuss], question.Answers);
        Assert.Null(DecisionRules.Refusal(question, DecisionAnswer.Pick("Keep SQLite")));
        Assert.NotNull(DecisionRules.Refusal(question, DecisionAnswer.Pick("Use MongoDB")));
        Assert.NotNull(DecisionRules.Refusal(question, DecisionAnswer.Discuss("  ")));
        Assert.Null(DecisionRules.Refusal(question, DecisionAnswer.Discuss("Why not both?")));
        Assert.Equal([DecisionAnswerKind.Discuss], (question with { Options = ValueList<string>.Empty }).Answers);
    }

    [Fact]
    public void A_merge_card_takes_merge_rebase_first_or_discard_and_two_of_them_keep_the_irreversible_rule()
    {
        var merge = new DecisionItem(Guid.NewGuid(), DecisionKind.Merge, DecisionWeight.Irreversible, "Merge 'Fix the login timeout' (38 lines) into main?");
        Assert.Equal([DecisionAnswerKind.Merge, DecisionAnswerKind.RebaseFirst, DecisionAnswerKind.Discard], merge.Answers);
        Assert.True(DecisionRules.IsIrreversibleButton(DecisionAnswerKind.Merge));
        Assert.True(DecisionRules.IsIrreversibleButton(DecisionAnswerKind.Discard));
        Assert.True(DecisionRules.IsIrreversibleButton(DecisionAnswerKind.DoIt));
        Assert.False(DecisionRules.IsIrreversibleButton(DecisionAnswerKind.RebaseFirst));
        Assert.False(DecisionRules.IsIrreversibleButton(DecisionAnswerKind.AllowOnce));
    }

    [Fact]
    public void A_card_is_bound_to_its_exact_inputs()
    {
        var a = Card(ActionClass.Outward);
        var same = a with { Id = Guid.NewGuid() };
        var edited = a with { Inputs = new([new DecisionInput("command", "curl https://example.test/x")]) };
        var split = a with { Inputs = new([new DecisionInput("comm", "andcurl https://example.test")]) };
        Assert.Equal(a.Binding, same.Binding);
        Assert.NotEqual(a.Binding, edited.Binding);
        Assert.NotEqual(a.Binding, split.Binding);
        Assert.Equal(64, a.Binding.Length);
    }
}
