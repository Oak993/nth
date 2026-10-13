using Marrow.App.Assistant;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.HomeSamples;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-18 (DESIGN.md rev 5, 2.4 "Spoken status on request ... two or three sentences. It never speaks unprompted"; 16 "done when": status answers stay within 3
// sentences): what Dextra says about the board, whatever the lists hold.
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-6")]
public sealed class VoiceStatusTests
{
    private static DecisionItem Card(string title, params string[] options) =>
        new(Guid.NewGuid(), DecisionKind.Question, DecisionWeight.Question, title) { Options = new ValueList<string>(options), AssistantId = "dextra", Raised = Now };

    // a sentence mark is a stop, bang or query before a space or the end (a decimal point inside a number is not one)
    private static int Marks(string text) => System.Text.RegularExpressions.Regex.Matches(text, @"[.!?](\s|$)").Count;

    [Fact]
    public void Nothing_running_and_nothing_waiting_is_said_in_one_short_sentence_each()
    {
        Assert.Equal("Nothing is running.", VoiceStatus.Compose(VoiceStatusAsk.Running, [], []).Text);
        Assert.Equal("Nothing needs you.", VoiceStatus.Compose(VoiceStatusAsk.NeedsYou, [], []).Text);
        Assert.Equal("Nothing is running. Nothing needs you.", VoiceStatus.Compose(VoiceStatusAsk.Running | VoiceStatusAsk.NeedsYou, [], []).Text);
    }

    [Fact]
    public void Running_missions_are_counted_and_named_a_few_at_a_time()
    {
        var jobs = new[]
        {
            Job("Fix the login timeout", MissionState.Working),
            Job("Write the release notes", MissionState.Working),
            Job("Next video: Why lenses fog up", MissionState.Working),
            Job("Compare espresso machines", MissionState.Working),
            Job("Yesterday's report", MissionState.Done),
        };
        var answer = VoiceStatus.Compose(VoiceStatusAsk.Running, jobs, []);
        Assert.Equal("4 missions are running. That is Fix the login timeout, Write the release notes, Next video: Why lenses fog up and 1 more.", answer.Text);
        Assert.Null(answer.Focus);
        Assert.Equal("One mission is running. That is Fix the login timeout.", VoiceStatus.Compose(VoiceStatusAsk.Running, [Job("Fix the login timeout", MissionState.Working)], []).Text);
    }

    [Fact]
    public void Paused_work_is_said_as_paused_and_not_as_running()
    {
        var text = VoiceStatus.Compose(VoiceStatusAsk.Running, [Job("Fix the login timeout", MissionState.Paused), Job("Notes", MissionState.Paused)], []).Text;
        Assert.StartsWith("2 missions are paused.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_waiting_card_is_counted_offered_and_becomes_the_card_a_yes_or_a_place_would_answer()
    {
        var first = Card("Which model for the thumbnail", "Opus", "Sonnet");
        var answer = VoiceStatus.Compose(VoiceStatusAsk.NeedsYou, [], [first, Card("Another")]);

        Assert.Equal("2 things need you. Which model for the thumbnail; say Opus or Sonnet.", answer.Text);
        Assert.Equal(first.Id, answer.Focus!.Id);
        Assert.StartsWith("One thing needs you.", VoiceStatus.Compose(VoiceStatusAsk.NeedsYou, [], [first]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_answered_or_withdrawn_card_is_not_waiting_any_more()
    {
        var done = Card("Old") with { State = DecisionState.Answered };
        Assert.Equal("Nothing needs you.", VoiceStatus.Compose(VoiceStatusAsk.NeedsYou, [], [done]).Text);
    }

    [Fact]
    public void However_many_missions_and_cards_there_are_the_answer_is_three_sentences_at_most_with_no_sentence_marks_from_their_titles()
    {
        var weird = new[]
        {
            "Fix the login. Then deploy! Really?", "A", new string('x', 400), "Line one\nLine two", "Version 2.3 notes", "“Quoted” (brackets) [more]",
        };
        foreach (var count in new[] { 0, 1, 2, 3, 4, 9, 40, 200 })
        {
            var jobs = Enumerable.Range(0, count).Select(i => Job(weird[i % weird.Length] + i, i % 3 == 0 ? MissionState.Paused : MissionState.Working)).ToArray();
            var cards = Enumerable.Range(0, count).Select(i => Card(weird[(i + 2) % weird.Length], i % 2 == 0 ? ["Yes", "No"] : [])).ToArray();
            foreach (var ask in new[] { VoiceStatusAsk.Running, VoiceStatusAsk.NeedsYou, VoiceStatusAsk.Running | VoiceStatusAsk.NeedsYou })
            {
                var text = VoiceStatus.Compose(ask, jobs, cards).Text;
                Assert.InRange(SpokenText.CountSentences(text), 1, SpokenText.StatusSentences);
                Assert.True(Marks(text) <= SpokenText.StatusSentences, $"{count} items, {ask}: {text}");
                Assert.DoesNotContain("\n", text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_digest_is_read_in_three_sentences_and_a_missing_digest_is_said_so()
    {
        var digest = "Overnight, the invoice was filed. The dependency audit came back clean. The shop login fix is in tests. Channel A's title checkpoint is around eleven. That's everything for today.";
        var text = VoiceStatus.Compose(VoiceStatusAsk.Digest, [], [], () => digest).Text;
        Assert.Equal("Overnight, the invoice was filed. The dependency audit came back clean. The shop login fix is in tests.", text);
        Assert.Equal("There is no digest yet.", VoiceStatus.Compose(VoiceStatusAsk.Digest, [], [], () => null).Text);
        Assert.Equal("There is no digest yet.", VoiceStatus.Compose(VoiceStatusAsk.Digest, [], []).Text);
    }
}
