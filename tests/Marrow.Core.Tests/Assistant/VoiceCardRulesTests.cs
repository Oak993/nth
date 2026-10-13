using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-18 (DESIGN.md rev 5, 2.4 "What voice may answer", 16 "done when"): voice never answers Send, Pay, Delete, Install or Commitment cards, however they
// are worded and whatever is said; it answers a reserve choice, a question that asks nothing irreversible, and Allow once on a read-only action.
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-5")]
public sealed class VoiceCardRulesTests
{
    private static readonly string[] Said = ["yes", "no", "do it", "send it", "go ahead", "approve", "deny", "pay it", "delete it", "install it", "the first one", "ok", "don't", "please"];

    private static DecisionItem Card(DecisionKind kind, DecisionWeight weight, ActionClass? cls, string title = "Channel A wants to read the vendor site", params string[] options) =>
        new(Guid.NewGuid(), kind, weight, title)
        {
            Class = cls,
            Options = new ValueList<string>(options),
            Why = "Because it was asked to.",
            AssistantId = "dextra",
        };

    [Theory]
    [InlineData(ActionClass.Outward, "Tap Send on the screen to send it.")]
    [InlineData(ActionClass.PersonalDataOut, "Tap Send on the screen to send it.")]
    [InlineData(ActionClass.Money, "Tap Pay on the screen to pay.")]
    [InlineData(ActionClass.Destructive, "Tap Delete on the screen to delete it.")]
    [InlineData(ActionClass.Install, "Tap Install on the screen to install it.")]
    [InlineData(ActionClass.Commitment, "Tap Commit on the screen to commit.")]
    [InlineData(ActionClass.HardDeny, "Tap the button on the screen to go ahead.")]
    public void Voice_never_answers_a_card_of_an_irreversible_class_whatever_is_said_and_names_the_button(ActionClass cls, string line)
    {
        foreach (var weight in new[] { DecisionWeight.Irreversible, DecisionWeight.Reversible, DecisionWeight.Question })
        {
            foreach (var kind in new[] { DecisionKind.Approval, DecisionKind.ToolCall, DecisionKind.Question })
            {
                var card = Card(kind, weight, cls, "Send the weekly update to the team", "Yes", "No");
                Assert.True(VoiceCardRules.IsIrreversible(card));
                Assert.False(VoiceCardRules.VoiceMayAnswer(card));
                foreach (var words in Said)
                {
                    var move = VoiceCardRules.Decide(card, VoiceCommands.Parse(words), words);
                    Assert.Equal(VoiceCardMoveKind.OnScreen, move.Kind);
                    Assert.Null(move.Answer);
                    Assert.Equal(line, move.Line);
                }
            }
        }
    }

    [Fact]
    public void A_merge_card_and_an_irreversible_weight_are_the_screens_too()
    {
        var merge = Card(DecisionKind.Merge, DecisionWeight.Reversible, null, "Merge the branch", "Merge", "Discard");
        var irreversible = Card(DecisionKind.ToolCall, DecisionWeight.Irreversible, null, "Do something");
        foreach (var card in new[] { merge, irreversible })
        {
            foreach (var words in Said)
            {
                var move = VoiceCardRules.Decide(card, VoiceCommands.Parse(words), words);
                Assert.Equal(VoiceCardMoveKind.OnScreen, move.Kind);
                Assert.Null(move.Answer);
            }
        }
        Assert.Equal("Tap Merge on the screen to merge it.", VoiceCardRules.OnScreenLine(merge));
        Assert.Equal("Tap the button on the screen to go ahead.", VoiceCardRules.OnScreenLine(irreversible));
    }

    [Fact]
    public void Allow_once_and_deny_by_voice_work_on_a_read_only_action_and_nothing_wider()
    {
        foreach (var cls in new[] { ActionClass.ReadWeb, ActionClass.ReadLocal })
        {
            var card = Card(DecisionKind.Approval, DecisionWeight.Reversible, cls);
            Assert.True(VoiceCardRules.VoiceMayAnswer(card));
            var yes = VoiceCardRules.Decide(card, VoiceCommands.Parse("yes go ahead"), "yes go ahead");
            Assert.Equal(DecisionAnswer.AllowOnce, yes.Answer);
            var no = VoiceCardRules.Decide(card, VoiceCommands.Parse("no"), "no");
            Assert.Equal(DecisionAnswerKind.Deny, no.Answer!.Kind);
            var unsure = VoiceCardRules.Decide(card, VoiceCommands.Parse("hmm what"), "hmm what");
            Assert.Equal(VoiceCardMoveKind.Unclear, unsure.Kind);
        }
    }

    [Theory]
    [InlineData(ActionClass.Work)]
    [InlineData(ActionClass.Run)]
    [InlineData(ActionClass.Login)]
    [InlineData(ActionClass.WriteElsewhere)]
    [InlineData(ActionClass.ConnectorWrite)]
    [InlineData(ActionClass.Agents)]
    [InlineData(ActionClass.Unknown)]
    public void An_approval_that_is_not_read_only_is_answered_on_the_screen(ActionClass cls)
    {
        var card = Card(DecisionKind.Approval, DecisionWeight.Reversible, cls);
        Assert.False(VoiceCardRules.VoiceMayAnswer(card));
        Assert.Equal(VoiceCardMoveKind.OnScreen, VoiceCardRules.Decide(card, new VoiceCommand(VoiceCommandKind.Yes), "yes").Kind);
    }

    [Fact]
    public void A_reserve_choice_is_picked_by_its_words_or_its_place_and_nothing_else_is_taken()
    {
        var card = Card(DecisionKind.Reserve, DecisionWeight.Reversible, null, "Espresso research needs 4 percent more on Claude", "Move the rest to Codex A", "Wait for the reset", "Use my reserve once", "Stop the task");
        Assert.True(VoiceCardRules.VoiceMayAnswer(card));

        var byWords = VoiceCardRules.Decide(card, VoiceCommands.Parse("move the rest to codex a"), "move the rest to codex a");
        Assert.Equal(DecisionAnswer.Pick("Move the rest to Codex A"), byWords.Answer);
        var inSentence = VoiceCardRules.Decide(card, VoiceCommands.Parse("please wait for the reset"), "please wait for the reset");
        Assert.Equal("Wait for the reset", inSentence.Answer!.Text);
        var byPlace = VoiceCardRules.Decide(card, VoiceCommands.Parse("the second one"), "the second one");
        Assert.Equal("Wait for the reset", byPlace.Answer!.Text);
        var last = VoiceCardRules.Decide(card, VoiceCommands.Parse("the last one"), "the last one");
        Assert.Equal("Stop the task", last.Answer!.Text);

        foreach (var words in new[] { "yes", "no", "tomorrow", "the ninth one", "banana" })
        {
            var move = VoiceCardRules.Decide(card, VoiceCommands.Parse(words), words);
            Assert.Equal(VoiceCardMoveKind.Unclear, move.Kind);
            Assert.Null(move.Answer);
            Assert.StartsWith("Say ", move.Line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Yes_and_no_pick_a_question_whose_options_are_those_words()
    {
        var card = Card(DecisionKind.Question, DecisionWeight.Question, null, "Use the shorter intro for video 42", "Yes", "No");
        Assert.Equal("Yes", VoiceCardRules.Decide(card, VoiceCommands.Parse("yes"), "yes").Answer!.Text);
        Assert.Equal("No", VoiceCardRules.Decide(card, VoiceCommands.Parse("nope"), "nope").Answer!.Text);
    }

    [Fact]
    public void A_question_about_something_irreversible_is_answered_on_the_screen_whatever_kind_it_is()
    {
        foreach (var title in new[] { "Send it now or later", "Should I delete the old backups", "Pay with the saved card", "Install the plugin", "Publish the video", "Confirm the booking" })
        {
            var card = Card(DecisionKind.Question, DecisionWeight.Question, null, title, "Now", "Later");
            Assert.False(VoiceCardRules.VoiceMayAnswer(card), title);
            Assert.Equal(VoiceCardMoveKind.OnScreen, VoiceCardRules.Decide(card, VoiceCommands.Parse("now"), "now").Kind);
        }
        var inOption = Card(DecisionKind.Question, DecisionWeight.Question, null, "Which one", "Send the report", "Keep it");
        Assert.False(VoiceCardRules.VoiceMayAnswer(inOption));
    }

    [Fact]
    public void A_free_text_question_takes_the_spoken_answer_as_the_discussion()
    {
        var card = Card(DecisionKind.Question, DecisionWeight.Question, null, "What should the new channel be called");
        var move = VoiceCardRules.Decide(card, VoiceCommand.NotACommand, "call it Lenses and Light");
        Assert.Equal(VoiceCardMoveKind.Answer, move.Kind);
        Assert.Equal(DecisionAnswerKind.Discuss, move.Answer!.Kind);
        Assert.Equal("call it Lenses and Light", move.Answer.Text);
        Assert.Equal(VoiceCardMoveKind.Unclear, VoiceCardRules.Decide(card, VoiceCommand.NotACommand, "  ").Kind);
        Assert.Equal(VoiceCardMoveKind.Unclear, VoiceCardRules.Decide(card, new VoiceCommand(VoiceCommandKind.Yes), "yes").Kind);
    }

    [Fact]
    public void The_Which_one_question_holds_the_owners_message_and_is_still_answerable_by_voice()
    {
        var card = Card(DecisionKind.Question, DecisionWeight.Question, null, "Which one: Channel A or Channel B", "Channel A", "Channel B") with
        {
            Inputs = new ValueList<DecisionInput>([new DecisionInput(VoiceCardRules.OwnWords, "send the report and delete the draft")]),
        };
        Assert.True(VoiceCardRules.VoiceMayAnswer(card));
        Assert.Equal("Channel B", VoiceCardRules.Decide(card, VoiceCommands.Parse("channel b"), "channel b").Answer!.Text);
    }

    [Fact]
    public void A_cap_card_and_a_paused_duty_card_are_picked_the_same_way()
    {
        foreach (var kind in new[] { DecisionKind.Cap, DecisionKind.Duty })
        {
            var card = Card(kind, DecisionWeight.Reversible, null, "It reached its cap", "Raise the cap by 5 dollars", "Keep it paused");
            Assert.Equal("Keep it paused", VoiceCardRules.Decide(card, VoiceCommands.Parse("keep it paused"), "keep it paused").Answer!.Text);
        }
    }

    [Fact]
    public void A_held_answered_or_withdrawn_card_is_not_answered_by_voice()
    {
        foreach (var state in new[] { DecisionState.Held, DecisionState.Answered, DecisionState.Withdrawn })
        {
            var card = Card(DecisionKind.Approval, DecisionWeight.Reversible, ActionClass.ReadWeb) with { State = state };
            Assert.False(VoiceCardRules.VoiceMayAnswer(card));
            Assert.Null(VoiceCardRules.Decide(card, new VoiceCommand(VoiceCommandKind.Yes), "yes").Answer);
        }
    }

    [Fact]
    public void A_card_is_offered_in_one_sentence_that_says_how_to_answer_or_that_it_needs_a_tap()
    {
        var readOnly = Card(DecisionKind.Approval, DecisionWeight.Reversible, ActionClass.ReadWeb, "Channel A wants to read the vendor site. Really!");
        var offer = VoiceCardRules.Offer(readOnly);
        Assert.Equal("Channel A wants to read the vendor site Really; say allow once or deny.", offer);
        Assert.Equal(1, SpokenText.CountSentences(offer));

        var send = Card(DecisionKind.Approval, DecisionWeight.Irreversible, ActionClass.Outward, "Store: send the weekly update?");
        Assert.Equal("Store: send the weekly update needs your tap on the screen.", VoiceCardRules.Offer(send));

        var many = Card(DecisionKind.Reserve, DecisionWeight.Reversible, null, "Paused at your reserve", "Move it", "Wait", "Use the reserve", "Stop");
        Assert.Equal("Paused at your reserve; say Move it, Wait, Use the reserve, or the screen.", VoiceCardRules.Offer(many));
        Assert.Equal(1, SpokenText.CountSentences(VoiceCardRules.Offer(many)));
    }
}
