using System.Globalization;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W25 (DESIGN.md 7.3 ask_user). The words the model reads back, the message that restarts a turn when
// the call is gone, and the line a card shows for an answer.
[Trait("Decision", "ED-NEW-web-w25-1")]
public sealed class AskUserWordsTests
{
    private static readonly AskQuestion Database = new()
    {
        Id = "q1", Prompt = "Which database?", Kind = AskKind.Single,
        Options = new ValueList<AskOption>([new AskOption("PostgreSQL"), new AskOption("SQLite")]),
    };

    private static readonly AskQuestion Size = new() { Id = "q2", Prompt = "How many GB?", Kind = AskKind.Number, Unit = "GB" };

    private static readonly AskQuestion Features = new()
    {
        Id = "q3", Prompt = "Which features?", Kind = AskKind.Multiple,
        Options = new ValueList<AskOption>([new AskOption("Search"), new AskOption("Export")]),
    };

    private static AskCard Answered(params AskQuestion[] questions)
    {
        var answers = new List<KeyValuePair<string, ValueList<string>>>
        {
            KeyValuePair.Create("q1", new ValueList<string>(["PostgreSQL"])),
            KeyValuePair.Create("q2", new ValueList<string>(["64"])),
            KeyValuePair.Create("q3", new ValueList<string>(["Search", "Export"])),
        };
        return new AskCard { Id = "c", Questions = new ValueList<AskQuestion>(questions) }
            .Closed(AskState.Answered, new ValueMap<string, ValueList<string>>(answers, StringComparer.Ordinal));
    }

    [Fact]
    public void The_model_reads_each_question_with_its_answer()
    {
        var text = AskUserWords.ToolResult(Answered(Database, Size, Features));
        Assert.Equal(
            "The user answered your questions.\n\nQuestion 1: Which database?\nAnswer 1: PostgreSQL\n\nQuestion 2: How many GB?\nAnswer 2: 64 GB\n\nQuestion 3: Which features?\nAnswer 3: Search, Export",
            text);
        Assert.StartsWith("The user answered your question.\n", AskUserWords.ToolResult(Answered(Database)), StringComparison.Ordinal);
    }

    [Fact]
    public void An_optional_question_left_empty_is_said_to_be_empty()
    {
        var card = new AskCard { Id = "c", Questions = new ValueList<AskQuestion>([Database with { Required = false }]) }.Closed(AskState.Answered);
        Assert.Contains("Answer 1: (no answer; they left it empty)", AskUserWords.ToolResult(card), StringComparison.Ordinal);
    }

    [Fact]
    public void Skipped_and_stopped_results_tell_the_model_what_to_do_next()
    {
        Assert.Contains("Carry on with your best judgement", AskUserWords.SkippedResult, StringComparison.Ordinal);
        Assert.Contains("Do not continue", AskUserWords.StoppedResult, StringComparison.Ordinal);
    }

    [Fact]
    public void The_message_that_restarts_a_turn_names_the_question_so_nothing_is_needed_from_the_lost_call()
    {
        Assert.Equal("My answer to your question \"Which database?\": PostgreSQL\n\nPlease carry on from there.", AskUserWords.ResumeMessage(Answered(Database)));
        Assert.Equal(
            "My answers to your questions:\n- Which database? PostgreSQL\n- How many GB? 64 GB\n\nPlease carry on from there.",
            AskUserWords.ResumeMessage(Answered(Database, Size)));
    }

    [Fact]
    public void A_card_line_shows_a_number_with_its_unit_in_the_persons_culture()
    {
        var values = new ValueList<string>(["1234.5"]);
        Assert.Equal("1234.5 GB", AskUserWords.Line(Size, values, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("1234,5 GB", AskUserWords.Line(Size, values, CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("No answer", AskUserWords.Line(Database, null));
        Assert.Equal("Search, Export", AskUserWords.Line(Features, new ValueList<string>(["Search", "Export"])));
    }

    [Fact]
    public void The_waiting_state_is_the_designs_words()
    {
        Assert.Equal("Waiting for your answer", AskUserWords.Waiting);
    }

    [Fact]
    public void A_handoff_describes_the_question_and_the_answer()
    {
        var text = AskUserWords.Describe(Answered(Database));
        Assert.Contains("Question to the user (answered):", text, StringComparison.Ordinal);
        Assert.Contains("Answer: PostgreSQL", text, StringComparison.Ordinal);
    }
}
