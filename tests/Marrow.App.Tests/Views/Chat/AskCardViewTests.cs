using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.App.Port.AskUser;
using Marrow.App.Views.Chat;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Web;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// PORT-ONLY: web surfaces W25 (DESIGN.md 7.3 ask_user: a blocking question card in the timeline with single, multiple, text and number
// answers; 7.5 look; 10 accessibility). The card is mounted in a real timeline in a shown window and driven the way a person would: choices
// chosen, fields typed in, Send and Skip pressed. The words the card shows are asserted exactly.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w25-1")]
public sealed class AskCardViewTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("22345678-abcd-1234-abcd-123456789abc");

    private static AskCard Parse(string json, string id = "ask-1") => AskUserSchema.Parse(JsonNode.Parse(json), id).Card!;

    private static readonly string Database = """{"questions":[{"question":"Which database?","type":"single","options":[{"label":"PostgreSQL","description":"A server"},"SQLite"],"header":"Storage"}]}""";

    private static readonly string Everything = """
        {"questions":[
         {"question":"Which database?","type":"single","options":["PostgreSQL","SQLite"]},
         {"question":"Which features?","type":"multiple","options":["Search","Export","Sharing"],"allow_other":false},
         {"question":"Anything else?","type":"text","placeholder":"Notes","required":false},
         {"question":"How many GB?","type":"number","min":1,"max":64,"integer":true,"step":4,"unit":"GB","default":8}]}
        """;

    // A running turn with the card in it, as a card whose call is gone would stand (waiting, with no live call behind it).
    private static (AskCardRow Row, AskCardForm Form) Mounted(TimelineMount mount, AskCard card)
    {
        mount.Model.Settings.ShowReasoning = true;
        mount.SetEntries([User("prompt", Turn, "Set up storage"), Entry("ask", new TimelineItem.Content.Ask(card), Turn)]);
        mount.SetTurnStartedAt(SwiftDate.Now);
        mount.SetPhase(RuntimePhase.Running);
        mount.Settle();
        var row = LogicalQuery.SelfAndDescendants<AskCardRow>(mount.View).Single();
        return (row, row.Form!);
    }

    private static AskCard Now(TimelineMount mount) =>
        ((TimelineItem.Content.Ask)mount.Runtime.Entries.Single(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Ask).Item.TimelineItemContent).Value;

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Choose(AskCardForm form, string question, string label) =>
        form.PlatesOf(question).Single(plate => plate.Label == label).Button.IsChecked = true;

    private static string[] Texts(Control root) => LogicalQuery.SelfAndDescendants<TextBlock>(root).Select(block => block.Text ?? "").Where(text => text.Length != 0).ToArray();

    [Fact]
    public void A_waiting_card_shows_its_question_its_choices_and_the_state_in_the_designs_words() => ThreadTimelineRig.Run(headless, mount =>
    {
        var (_, form) = Mounted(mount, Parse(Database));
        var texts = Texts(form);
        Assert.Contains("Question", texts);
        Assert.Contains("Waiting for your answer", texts);
        Assert.Contains("STORAGE", texts);
        Assert.Contains("Which database?", LogicalQuery.SelfAndDescendants<SelectableTextBlock>(form).Select(block => block.Text));
        Assert.Contains("PostgreSQL", texts);
        Assert.Contains("A server", texts);
        Assert.Equal(["PostgreSQL", "SQLite"], form.PlatesOf("q1").Select(plate => plate.Label));
        Assert.Contains("Skip", texts);
        Assert.Contains("Send answer", texts);
        Assert.True(form.PlatesOf("q1").All(plate => plate.Button is RadioButton));
        Assert.Equal("Question, waiting for your answer", Avalonia.Automation.AutomationProperties.GetName(form));
    });

    [Fact]
    public void Send_waits_until_the_question_is_answered_and_the_hint_says_how_many_are_left() => ThreadTimelineRig.Run(headless, mount =>
    {
        var (_, form) = Mounted(mount, Parse(Everything));
        Assert.False(form.SendButton.IsEnabled);
        Assert.Equal("2 questions still to answer", form.Hint.Text); // the single and the several-choices question; the text question is optional and the number starts at its default
    });

    [Fact]
    public void A_single_choice_answers_the_card_and_the_card_becomes_a_summary_of_the_answer() => ThreadTimelineRig.Run(headless, mount =>
    {
        var (row, form) = Mounted(mount, Parse(Database));
        Assert.False(form.SendButton.IsEnabled);
        Assert.Equal("1 question still to answer", form.Hint.Text);
        Choose(form, "q1", "SQLite");
        Assert.True(form.SendButton.IsEnabled);
        Assert.Equal("", form.Hint.Text);
        Press(form.SendButton);
        mount.Settle();

        var card = Now(mount);
        Assert.Equal(AskState.Answered, card.State);
        Assert.Equal(["SQLite"], card.Answers["q1"]);
        Assert.Null(row.Form);
        var summary = Assert.IsType<AskCardSummary>(row.Summary);
        var texts = Texts(summary);
        Assert.Contains("Answered", texts);
        Assert.Contains("Which database?", texts);
        Assert.Contains("SQLite", LogicalQuery.SelfAndDescendants<SelectableTextBlock>(summary).Select(block => block.Text));
        Assert.DoesNotContain("Send answer", texts);
    });

    [Fact]
    public void Picking_one_choice_clears_the_other_and_something_else_takes_the_place_of_a_choice() => ThreadTimelineRig.Run(headless, mount =>
    {
        var (_, form) = Mounted(mount, Parse(Database));
        Choose(form, "q1", "PostgreSQL");
        Choose(form, "q1", "SQLite");
        Assert.Equal([false, true], form.PlatesOf("q1").Select(plate => plate.IsSelected));

        form.FieldOf("q1")!.Text = "MariaDB";
        Assert.Equal([false, false], form.PlatesOf("q1").Select(plate => plate.IsSelected));
        Assert.True(form.SendButton.IsEnabled);

        Choose(form, "q1", "PostgreSQL");
        Assert.Equal("", form.FieldOf("q1")!.Text);
        Press(form.SendButton);
        mount.Settle();
        Assert.Equal(["PostgreSQL"], Now(mount).Answers["q1"]);
    });

    [Fact]
    public void The_arrow_keys_move_through_a_single_choice_and_choose_as_they_go() => ThreadTimelineRig.Run(headless, mount =>
    {
        var (_, form) = Mounted(mount, Parse("""{"questions":[{"question":"Pick","options":["A","B","C"]}]}"""));
        var plates = form.PlatesOf("q1");
        void Press(AskChoicePlate plate, Key key) => plate.Button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        Press(plates[0], Key.Down);
        Assert.Equal([false, true, false], plates.Select(plate => plate.IsSelected));
        Press(plates[1], Key.Down);
        Press(plates[2], Key.Down); // wraps to the first
        Assert.Equal([true, false, false], plates.Select(plate => plate.IsSelected));
        Press(plates[0], Key.Up); // and back to the last
        Assert.Equal([false, false, true], plates.Select(plate => plate.IsSelected));
        Assert.True(form.SendButton.IsEnabled);
    });

    [Fact]
    public void Something_else_is_the_answer_when_it_is_typed() => ThreadTimelineRig.Run(headless, mount =>
    {
        var (_, form) = Mounted(mount, Parse(Database));
        form.FieldOf("q1")!.Text = "  MariaDB ";
        Press(form.SendButton);
        mount.Settle();
        Assert.Equal(["MariaDB"], Now(mount).Answers["q1"]);
    });

    [Fact]
    public void Several_choices_are_checked_and_unchecked_and_all_of_them_are_sent() => ThreadTimelineRig.Run(headless, mount =>
    {
        var (_, form) = Mounted(mount, Parse("""{"questions":[{"question":"Which features?","type":"multiple","options":["Search","Export","Sharing"]}]}"""));
        Assert.True(form.PlatesOf("q1").All(plate => plate.Button is CheckBox));
        Choose(form, "q1", "Search");
        Choose(form, "q1", "Sharing");
        Choose(form, "q1", "Export");
        form.PlatesOf("q1").Single(plate => plate.Label == "Export").Button.IsChecked = false;
        Assert.Equal([true, false, true], form.PlatesOf("q1").Select(plate => plate.IsSelected));
        Press(form.SendButton);
        mount.Settle();
        Assert.Equal(["Search", "Sharing"], Now(mount).Answers["q1"]);
    });

    [Fact]
    public void Four_questions_start_with_their_defaults_and_send_every_answer() => ThreadTimelineRig.Run(headless, mount =>
    {
        var (_, form) = Mounted(mount, Parse(Everything));
        Assert.Equal("8", form.FieldOf("q4")!.Text);
        Assert.False(form.SendButton.IsEnabled);
        Choose(form, "q1", "PostgreSQL");
        Assert.False(form.SendButton.IsEnabled); // the several-choices question is still empty
        Choose(form, "q2", "Search");
        Assert.True(form.SendButton.IsEnabled);
        form.FieldOf("q3")!.Text = "Keep it small";
        Assert.Equal("Send answers", Texts(form.SendButton).Single());
        Press(form.SendButton);
        mount.Settle();
        var card = Now(mount);
        Assert.Equal(AskState.Answered, card.State);
        Assert.Equal(["PostgreSQL"], card.Answers["q1"]);
        Assert.Equal(["Search"], card.Answers["q2"]);
        Assert.Equal(["Keep it small"], card.Answers["q3"]);
        Assert.Equal(["8"], card.Answers["q4"]);
    });

    [Fact]
    public void The_number_field_steps_with_its_buttons_and_arrow_keys_inside_its_range() => ThreadTimelineRig.Run(headless, mount =>
    {
        var (_, form) = Mounted(mount, Parse(Everything));
        var field = form.FieldOf("q4")!;
        var buttons = LogicalQuery.SelfAndDescendants<Button>(form).Where(button => Avalonia.Automation.AutomationProperties.GetName(button) is "Increase" or "Decrease").ToDictionary(button => Avalonia.Automation.AutomationProperties.GetName(button)!);
        Press(buttons["Increase"]);
        Assert.Equal("12", field.Text);
        Press(buttons["Decrease"]);
        Press(buttons["Decrease"]);
        Assert.Equal("4", field.Text);
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up });
        Assert.Equal("8", field.Text);
        for (var i = 0; i < 30; i++) Press(buttons["Increase"]);
        Assert.Equal("64", field.Text);
        for (var i = 0; i < 30; i++) Press(buttons["Decrease"]);
        Assert.Equal("1", field.Text);
        Assert.Contains("1 to 64", Texts(form));
        Assert.Contains("GB", Texts(form));
    });

    [Fact]
    public void A_number_outside_its_range_shows_why_and_cannot_be_sent() => ThreadTimelineRig.Run(headless, mount =>
    {
        var (_, form) = Mounted(mount, Parse("""{"questions":[{"question":"How many GB?","type":"number","min":1,"max":64,"integer":true}]}"""));
        var field = form.FieldOf("q1")!;
        field.Text = "900";
        Assert.False(form.SendButton.IsEnabled);
        Assert.True(form.ProblemOf("q1").IsVisible);
        Assert.Equal("At most 64.", form.ProblemOf("q1").Text);
        field.Text = "2.5";
        Assert.Equal("Enter a whole number.", form.ProblemOf("q1").Text);
        field.Text = "abc";
        Assert.Equal("Enter a number.", form.ProblemOf("q1").Text);
        field.Text = "12";
        Assert.False(form.ProblemOf("q1").IsVisible);
        Assert.True(form.SendButton.IsEnabled);
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        mount.Settle();
        Assert.Equal(["12"], Now(mount).Answers["q1"]);
    });

    [Fact]
    public void Return_sends_from_a_single_line_field_and_a_multiline_field_needs_Ctrl_Return() => ThreadTimelineRig.Run(headless, mount =>
    {
        var (_, form) = Mounted(mount, Parse("""{"questions":[{"question":"Why?","type":"text","multiline":true}]}"""));
        var field = form.FieldOf("q1")!;
        Assert.True(field.AcceptsReturn);
        field.Text = "Because";
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        mount.Settle();
        Assert.Equal(AskState.Waiting, Now(mount).State);
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control });
        mount.Settle();
        Assert.Equal(["Because"], Now(mount).Answers["q1"]);
    });

    [Fact]
    public void Skip_closes_the_card_as_skipped() => ThreadTimelineRig.Run(headless, mount =>
    {
        var (row, form) = Mounted(mount, Parse(Database));
        Press(form.SkipButton);
        mount.Settle();
        Assert.Equal(AskState.Skipped, Now(mount).State);
        Assert.Contains("Skipped", Texts(Assert.IsType<AskCardSummary>(row.Summary)));
        Assert.DoesNotContain(Texts(row.Summary!), text => text == "SQLite");
    });

    [Fact]
    public void A_stopped_card_says_so_and_a_closed_card_takes_no_more_input() => ThreadTimelineRig.Run(headless, mount =>
    {
        var (row, form) = Mounted(mount, Parse(Database));
        var entry = mount.Runtime.Entries.Single(e => e.TimelineEntryKind == TimelineEntry.Kind.Ask);
        entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Ask(Now(mount).Closed(AskState.Stopped)) };
        mount.Settle();
        Assert.Contains("Stopped", Texts(Assert.IsType<AskCardSummary>(row.Summary)));
        Choose(form, "q1", "SQLite"); // the old form is gone from the row; sending through it does nothing
        Press(form.SendButton);
        Assert.Equal(AskState.Stopped, Now(mount).State);
    });

    [Fact]
    public void A_live_call_gets_the_answer_when_the_card_is_sent() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetPhase(RuntimePhase.Running);
        var wait = mount.Runtime.AskHost.Ask(Parse(Database), CancellationToken.None);
        mount.Settle();
        var row = LogicalQuery.SelfAndDescendants<AskCardRow>(mount.View).Single();
        Assert.True(mount.Runtime.IsWaitingForAnswer);
        Choose(row.Form!, "q1", "PostgreSQL");
        Press(row.Form!.SendButton);
        mount.Settle();
        Assert.Equal(AskOutcomeKind.Answered, AskOutcomes.Of(wait).Kind);
        Assert.False(mount.Runtime.IsWaitingForAnswer);
    });

    [Fact]
    public void The_working_line_says_the_turn_waits_for_the_answer_and_goes_back_to_working_after() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries([User("prompt", Turn, "Set up storage")]);
        mount.SetTurnStartedAt(SwiftDate.Now);
        mount.SetPhase(RuntimePhase.Running);
        mount.Settle();
        var line = LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View).Single();
        Assert.DoesNotContain("Waiting for your answer", Texts(line));

        var wait = mount.Runtime.AskHost.Ask(Parse(Database), CancellationToken.None);
        mount.Settle();
        Assert.Contains("Waiting for your answer", Texts(LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View).Single()));

        mount.Runtime.SkipAsk("ask-1");
        mount.Settle();
        Assert.DoesNotContain("Waiting for your answer", Texts(LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View).Single()));
        Assert.True(wait.IsCompleted);
    });

    [Fact]
    public void A_finished_turn_keeps_its_questions_in_view_when_it_is_folded() => ThreadTimelineRig.Run(headless, mount =>
    {
        var answered = Parse(Database).Closed(AskState.Answered, new ValueMap<string, ValueList<string>>([KeyValuePair.Create("q1", new ValueList<string>(["SQLite"]))], StringComparer.Ordinal));
        mount.SetEntries([User("prompt", Turn, "Set up storage"), Entry("ask", new TimelineItem.Content.Ask(answered), Turn), Reply("reply", Turn, "SQLite it is."), End("end", Turn)]);
        mount.Settle();
        var summary = LogicalQuery.SelfAndDescendants<AskCardSummary>(mount.View).Single();
        Assert.Contains("Which database?", Texts(summary));
        Assert.Contains("SQLite", LogicalQuery.SelfAndDescendants<SelectableTextBlock>(summary).Select(block => block.Text));
    });

    [Fact]
    public void The_controls_are_named_for_a_screen_reader() => ThreadTimelineRig.Run(headless, mount =>
    {
        var (_, form) = Mounted(mount, Parse(Database));
        Assert.Equal("PostgreSQL, A server", Avalonia.Automation.AutomationProperties.GetName(form.PlatesOf("q1")[0].Button));
        Assert.Equal("SQLite", Avalonia.Automation.AutomationProperties.GetName(form.PlatesOf("q1")[1].Button));
        Assert.Equal("Which database?", Avalonia.Automation.AutomationProperties.GetName(form.FieldOf("q1")!));
        Assert.Contains(LogicalQuery.SelfAndDescendants<StackPanel>(form), panel => Avalonia.Automation.AutomationProperties.GetName(panel) == "Which database?"
            && Avalonia.Automation.AutomationProperties.GetControlTypeOverride(panel) == Avalonia.Automation.Peers.AutomationControlType.Group); // the choices are one named group
        Assert.Equal("Send answer", Avalonia.Automation.AutomationProperties.GetName(form.SendButton));
        Assert.Equal("Skip", Avalonia.Automation.AutomationProperties.GetName(form.SkipButton));
        Assert.Contains(LogicalQuery.SelfAndDescendants<StackPanel>(form), panel => Avalonia.Automation.AutomationProperties.GetLiveSetting(panel) == Avalonia.Automation.AutomationLiveSetting.Polite
            && Avalonia.Automation.AutomationProperties.GetName(panel) == "Waiting for your answer. Which database?");
    });
}
