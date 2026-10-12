// PORT-ONLY: web surfaces W25, ask_user (DESIGN.md 7.3, 10, 14 W22 moves every new string into the resource files; ED-NEW-web-w25-1).
// Every word the ask_user card shows or speaks of its own, in one place. The model's words (the questions and the choices) come from the
// call; the words the model reads back are Marrow.Core.Models.AskUserWords.
using System.Globalization;
using Marrow.Core.Models;

namespace Marrow.App.Port.AskUser;

public static class AskCardStrings
{
    /// <summary>The state a waiting turn and a waiting card show (DESIGN.md 7.3).</summary>
    public const string Waiting = AskUserWords.Waiting;

    public const string Question = "Question";
    public const string Answered = "Answered";
    public const string Skipped = "Skipped";
    public const string Stopped = "Stopped";
    public const string Skip = "Skip";
    public const string SkipHelp = "Tell the agent you would rather not answer";
    public const string SendAnswer = "Send answer";
    public const string SendAnswers = "Send answers";
    public const string SendHelp = "Sends your answer to the agent";
    public const string SendAnswersHelp = "Sends your answers to the agent";
    public const string SomethingElse = "Something else";
    public const string YourAnswer = "Your answer";
    public const string Optional = "Optional";
    public const string Decrease = "Decrease";
    public const string Increase = "Increase";
    public const string NoAnswer = "No answer";
    public const string Required = "Required";

    public static string Title(int count) =>
        count == 1 ? Question : string.Format(CultureInfo.InvariantCulture, "{0} questions", count);

    public static string StillToAnswer(int count) =>
        count == 1 ? "1 question still to answer" : string.Format(CultureInfo.InvariantCulture, "{0} questions still to answer", count);

    public static string Range(AskQuestion question, CultureInfo culture) => (question.Min, question.Max) switch
    {
        ({ } low, { } high) => AskUserWords.Shown(low, culture) + " to " + AskUserWords.Shown(high, culture),
        ({ } low, null) => "At least " + AskUserWords.Shown(low, culture),
        (null, { } high) => "At most " + AskUserWords.Shown(high, culture),
        _ => "",
    };

    /// <summary>What a screen reader hears when a card appears: the state, then the first question.</summary>
    public static string Announcement(AskCard card) => Waiting + ". " + card.Questions[0].Prompt;

    public static string CardName(AskCard card) => card.State switch
    {
        AskState.Waiting => Title(card.Questions.Count) + ", " + Waiting.ToLowerInvariant(),
        AskState.Answered => Title(card.Questions.Count) + ", " + Answered.ToLowerInvariant(),
        AskState.Skipped => Title(card.Questions.Count) + ", " + Skipped.ToLowerInvariant(),
        _ => Title(card.Questions.Count) + ", " + Stopped.ToLowerInvariant(),
    };
}
