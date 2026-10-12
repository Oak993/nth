using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W25 (DESIGN.md 7.3 ask_user; "the card survives restart"). A card is one timeline item in the
// thread's storage JSON: it writes and reads back whole in every state, and a damaged field never costs the thread.
[Trait("Decision", "ED-NEW-web-w25-1")]
public sealed class AskUserCodingTests
{
    private static AskCard Sample() => new()
    {
        Id = "ask-1",
        AskedAt = new DateTimeOffset(2026, 10, 12, 8, 30, 0, TimeSpan.Zero),
        Questions = new ValueList<AskQuestion>(
        [
            new AskQuestion
            {
                Id = "q1", Prompt = "Which database?", Header = "Database", Kind = AskKind.Single, AllowsOther = true,
                Options = new ValueList<AskOption>([new AskOption("PostgreSQL", "Server"), new AskOption("SQLite")]),
            },
            new AskQuestion { Id = "q2", Prompt = "How many GB?", Kind = AskKind.Number, Min = 1, Max = 512, Step = 8, Unit = "GB", WholeNumber = true, Default = "64", Required = false },
            new AskQuestion { Id = "q3", Prompt = "Notes?", Kind = AskKind.Text, Multiline = true, Placeholder = "Anything" },
        ]),
    };

    private static TimelineItem RoundTrip(TimelineItem item) =>
        JsonSerializer.Deserialize<TimelineItem>(JsonSerializer.Serialize(item, Coding.StorageOptions), Coding.StorageOptions)!;

    [Fact]
    public void A_waiting_card_survives_the_storage_round_trip()
    {
        var item = new TimelineItem(Guid.NewGuid(), new TimelineItem.Content.Ask(Sample()));
        var back = RoundTrip(item);
        var card = Assert.IsType<TimelineItem.Content.Ask>(back.TimelineItemContent).Value;
        Assert.Equal(Sample(), card);
        Assert.Equal(item.Id, back.Id);
        Assert.Equal(item.TurnID, back.TurnID);
        Assert.True(card.IsWaiting);
    }

    [Fact]
    public void An_answered_card_keeps_its_answers_and_the_time_it_closed()
    {
        var answers = new ValueMap<string, ValueList<string>>(
        [
            KeyValuePair.Create("q1", new ValueList<string>(["PostgreSQL"])),
            KeyValuePair.Create("q2", new ValueList<string>(["64"])),
        ], StringComparer.Ordinal);
        var answered = Sample().Closed(AskState.Answered, answers);
        var card = Assert.IsType<TimelineItem.Content.Ask>(RoundTrip(new TimelineItem(null, new TimelineItem.Content.Ask(answered))).TimelineItemContent).Value;
        Assert.Equal(AskState.Answered, card.State);
        Assert.Equal(["PostgreSQL"], card.Answers["q1"]);
        Assert.Equal(["64"], card.Answers["q2"]);
        Assert.NotNull(card.ClosedAt);
    }

    [Theory]
    [InlineData(AskState.Skipped)]
    [InlineData(AskState.Stopped)]
    public void A_skipped_or_stopped_card_keeps_no_answers_and_its_state(AskState state)
    {
        var closed = Sample().Closed(state, new ValueMap<string, ValueList<string>>([KeyValuePair.Create("q1", new ValueList<string>(["x"]))], StringComparer.Ordinal));
        Assert.Empty(closed.Answers);
        var card = Assert.IsType<TimelineItem.Content.Ask>(RoundTrip(new TimelineItem(null, new TimelineItem.Content.Ask(closed))).TimelineItemContent).Value;
        Assert.Equal(state, card.State);
    }

    [Fact]
    public void A_damaged_field_or_question_is_dropped_and_the_card_still_reads()
    {
        const string json = """
            {"id":"i","date":"2026-10-12T08:30:00Z","content":{"ask":{"_0":{
              "id":"c","state":"never-heard-of-it","askedAt":"not a date",
              "questions":[{"id":"q1","prompt":"Good?","kind":"single","options":[{"label":"Yes"},{"nolabel":true},{"label":"No"}],"min":"seven"},{"prompt":"no id"},{"id":"q2","prompt":"Fine?","kind":"wat"}],
              "answers":{"q1":["Yes",3],"q2":"oops"}}}}}
            """;
        var item = JsonSerializer.Deserialize<TimelineItem>(json, Coding.StorageOptions)!;
        var card = Assert.IsType<TimelineItem.Content.Ask>(item.TimelineItemContent).Value;
        Assert.Equal(AskState.Waiting, card.State);
        Assert.Equal(2, card.Questions.Count);
        Assert.Equal(["Yes", "No"], card.Questions[0].Options.Select(option => option.Label));
        Assert.Null(card.Questions[0].Min);
        Assert.Equal(AskKind.Text, card.Questions[1].Kind);
        Assert.Equal(["Yes"], card.Answers["q1"]);
        Assert.False(card.Answers.ContainsKey("q2"));
    }

    [Fact]
    public void A_card_is_written_with_the_ask_case_name()
    {
        var text = JsonSerializer.Serialize(new TimelineItem(null, new TimelineItem.Content.Ask(Sample())), Coding.StorageOptions);
        Assert.Contains("\"ask\":{\"_0\":{", text, StringComparison.Ordinal);
    }
}
