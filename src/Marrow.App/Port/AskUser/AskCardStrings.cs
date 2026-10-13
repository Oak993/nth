// PORT-ONLY: web surfaces W25, ask_user (DESIGN.md 7.3, 10, 14 W22 moves every new string into the resource files; ED-NEW-web-w25-1).
// Every word the ask_user card shows or speaks of its own, in one place (keys of ask-en.json, read when they are shown). The model's words
// (the questions and the choices) come from the call; the words the model reads back are Marrow.Core.Models.AskUserWords.
using System.Globalization;
using Marrow.Core.Localisation;
using Marrow.Core.Models;

namespace Marrow.App.Port.AskUser;

public static class AskCardStrings
{
    /// <summary>The state a waiting turn and a waiting card show (DESIGN.md 7.3).</summary>
    public static string Waiting => AskUserWords.Waiting;

    public static string Question => UiText.Get("ask.card.question");
    public static string Answered => UiText.Get("ask.state.answered");
    public static string Skipped => UiText.Get("ask.state.skipped");
    public static string Stopped => UiText.Get("ask.state.stopped");
    public static string Skip => UiText.Get("ask.card.skip");
    public static string SkipHelp => UiText.Get("ask.card.skip_help");
    public static string SendAnswer => UiText.Get("ask.card.send_answer");
    public static string SendAnswers => UiText.Get("ask.card.send_answers");
    public static string SendHelp => UiText.Get("ask.card.send_help");
    public static string SendAnswersHelp => UiText.Get("ask.card.send_answers_help");
    public static string SomethingElse => UiText.Get("ask.card.something_else");
    public static string YourAnswer => UiText.Get("ask.card.your_answer");
    public static string Optional => UiText.Get("ask.card.optional");
    public static string Decrease => UiText.Get("ask.card.decrease");
    public static string Increase => UiText.Get("ask.card.increase");
    public static string NoAnswer => UiText.Get("ask.answer.none");
    public static string Required => UiText.Get("ask.card.required");

    public static string Title(int count) => UiText.PluralWith(CultureInfo.InvariantCulture, "ask.card.title", count);

    public static string StillToAnswer(int count) => UiText.PluralWith(CultureInfo.InvariantCulture, "ask.card.still_to_answer", count);

    public static string Range(AskQuestion question, CultureInfo culture) => (question.Min, question.Max) switch
    {
        ({ } low, { } high) => UiText.Format("ask.range.between", AskUserWords.Shown(low, culture), AskUserWords.Shown(high, culture)),
        ({ } low, null) => UiText.Format("ask.range.at_least", AskUserWords.Shown(low, culture)),
        (null, { } high) => UiText.Format("ask.range.at_most", AskUserWords.Shown(high, culture)),
        _ => "",
    };

    /// <summary>What a screen reader hears when a card appears: the state, then the first question.</summary>
    public static string Announcement(AskCard card) => UiText.Format("ask.card.announcement", Waiting, card.Questions[0].Prompt);

    public static string CardName(AskCard card) => card.State switch
    {
        AskState.Waiting => UiText.Format("ask.card.name", Title(card.Questions.Count), UiText.Get("ask.card.state_waiting")),
        AskState.Answered => UiText.Format("ask.card.name", Title(card.Questions.Count), UiText.Get("ask.card.state_answered")),
        AskState.Skipped => UiText.Format("ask.card.name", Title(card.Questions.Count), UiText.Get("ask.card.state_skipped")),
        _ => UiText.Format("ask.card.name", Title(card.Questions.Count), UiText.Get("ask.card.state_stopped")),
    };
}
