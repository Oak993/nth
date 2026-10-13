using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.3 "Shown and reversible ... has an Override button", the canvas's feed line "Answered for you in “Pitch deck v3”: ... · Override"; the
// lean rules' new controls: accessible name, Escape closes and returns focus to the opener, nothing by colour alone, long text and RTL; ED-NEW-dextra-as22-11):
// what the delegate answered is listed under the Decisions tab's cards. Not there until something was answered; each answer shows what, to what, by whom and
// why; Override opens a field, sends the correction, and the row says Corrected.
[Collection("Headless")]
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-11")]
public sealed class DelegateAnsweredListTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static string Time(DelegateRecord record) => TaskWords.Local(record.At).ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture);

    [Fact]
    public void The_section_is_not_there_until_something_was_answered_then_lists_the_newest_first()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeAnswered();
            var list = new AnsweredList(source);
            using var stage = new HomeStage(new ScrollViewer { Content = list }, 600, 700);
            Assert.False(list.IsVisible);
            Assert.Empty(list.Rows);

            var older = FakeAnswered.Record("Postgres", "Which database?", "Thesis outline", minutesAgo: 30);
            var newer = FakeAnswered.Record();
            source.Add(older, newer);
            stage.Layout();
            Assert.True(list.IsVisible);
            Assert.Equal("Answered for you", list.Heading.Text);
            Assert.Equal(3, AutomationProperties.GetHeadingLevel(list.Heading));
            Assert.Equal([newer.Id, older.Id], list.Rows.Select(r => r.Record.Id));

            var row = list.Rows[0];
            Assert.Equal(["Keep the existing chart style", "Which chart style should the new report use?",
                $"Answered by Dextra for you in “Pitch deck v3” · {Time(newer)}", "A decision says all reports keep one style."], row.Lines);
            Assert.NotNull(row.OverrideButton);
        });
    }

    [Fact]
    public void A_new_answer_appears_at_the_top_and_a_row_that_left_the_book_goes()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeAnswered();
            var first = FakeAnswered.Record("One", minutesAgo: 20);
            source.Add(first);
            var list = new AnsweredList(source);
            using var stage = new HomeStage(new ScrollViewer { Content = list }, 600, 700);
            var second = FakeAnswered.Record("Two", minutesAgo: 1);
            source.Add(second);
            stage.Layout();
            Assert.Equal([second.Id, first.Id], list.Rows.Select(r => r.Record.Id));
            source.Records.Remove(first);
            source.Add();
            stage.Layout();
            Assert.Equal([second.Id], list.Rows.Select(r => r.Record.Id));
        });
    }

    [Fact]
    public void Override_has_a_name_that_says_which_answer_and_opens_a_field_that_takes_focus()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeAnswered();
            source.Add(FakeAnswered.Record());
            var list = new AnsweredList(source);
            using var stage = new HomeStage(new ScrollViewer { Content = list }, 600, 700);
            var row = list.Rows.Single();
            Assert.Equal("Override: Keep the existing chart style", AutomationProperties.GetName(row.OverrideButton!));
            Assert.Contains("Answered by Dextra for you", AutomationProperties.GetName(row.Child!), StringComparison.Ordinal);
            Assert.False(row.FieldOpen);

            Click(row.OverrideButton!);
            stage.Layout();
            Assert.True(row.FieldOpen);
            Assert.Equal("What should it have been?", row.Field!.PlaceholderText);
            Assert.Equal("What should it have been?", AutomationProperties.GetName(row.Field));
            Assert.Same(row.Field, stage.Focused);
        });
    }

    [Fact]
    public void Escape_closes_the_field_and_focus_goes_back_to_Override()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeAnswered();
            source.Add(FakeAnswered.Record());
            var list = new AnsweredList(source);
            using var stage = new HomeStage(new ScrollViewer { Content = list }, 600, 700);
            var row = list.Rows.Single();
            Click(row.OverrideButton!);
            stage.Layout();
            row.Field!.Text = "half a thought";
            stage.Press(Key.Escape);
            Assert.False(row.FieldOpen);
            Assert.Same(row.OverrideButton, stage.Focused);
            Assert.Empty(source.Overrides);
        });
    }

    [Fact]
    public void Enter_sends_the_correction_and_the_row_says_Corrected_with_no_Override_left()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeAnswered();
            var record = FakeAnswered.Record();
            source.Add(record);
            var list = new AnsweredList(source);
            using var stage = new HomeStage(new ScrollViewer { Content = list }, 600, 700);
            var row = list.Rows.Single();
            Click(row.OverrideButton!);
            stage.Layout();
            row.Field!.Text = "Use the new chart style for quarterly reports.";
            stage.Press(Key.Enter);
            stage.Layout();

            Assert.Equal([(record.Id, "Use the new chart style for quarterly reports.")], source.Overrides);
            var again = list.Rows.Single();
            Assert.Null(again.OverrideButton);
            Assert.Contains("Corrected: Use the new chart style for quarterly reports.", again.Lines);
            Assert.DoesNotContain(again.Lines, l => l.Contains("not sent", StringComparison.Ordinal));
            Assert.NotNull(stage.Focused);                                                         // focus is on the row, never lost to a button that is gone
            Assert.IsNotType<Button>(stage.Focused);
        });
    }

    [Fact]
    public void The_Send_correction_button_sends_too_and_a_correction_that_could_not_be_sent_says_so()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeAnswered { Answer = (_, _) => new DelegateOverrideResult(true, false, null) };
            source.Add(FakeAnswered.Record());
            var list = new AnsweredList(source);
            using var stage = new HomeStage(new ScrollViewer { Content = list }, 600, 700);
            var row = list.Rows.Single();
            Click(row.OverrideButton!);
            stage.Layout();
            row.Field!.Text = "Use lines.";
            Click(Nodes<Button>(row).Single(b => b.Content is not null && Texts(b).Contains("Send correction")));
            stage.Layout();
            Assert.Equal("Use lines.", Assert.Single(source.Overrides).Correction);
            Assert.Contains("The chat is gone, so it was kept for next time and not sent.", list.Rows.Single().Lines);
        });
    }

    [Fact]
    public void A_refused_correction_says_why_in_the_row_and_leaves_the_field_open_for_another_try()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeAnswered { Answer = (_, _) => DelegateOverrideResult.Refused(DelegateResponder.NothingToSay) };
            source.Add(FakeAnswered.Record());
            var list = new AnsweredList(source);
            using var stage = new HomeStage(new ScrollViewer { Content = list }, 600, 700);
            var row = list.Rows.Single();
            Click(row.OverrideButton!);
            stage.Layout();
            row.Field!.Text = "";
            stage.Press(Key.Enter);
            Assert.Equal(DelegateResponder.NothingToSay, row.StatusText);
            Assert.True(row.FieldOpen);
            Assert.NotNull(row.OverrideButton);
        });
    }

    [Fact]
    public void Long_words_wrap_and_right_to_left_text_stays_inside_a_narrow_list()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeAnswered();
            source.Add(FakeAnswered.Record(new string('w', 400), "كيف يجب أن يبدو التقرير الجديد؟ " + new string('س', 300), "خطة العرض " + new string('ع', 200)));
            var list = new AnsweredList(source);
            using var stage = new HomeStage(new ScrollViewer { Content = list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, 320, 900, FlowDirection.RightToLeft);
            var row = list.Rows.Single();
            // sizes, not places: in a right-to-left window a place is read from the mirrored side
            Assert.True(row.Bounds.Width <= 320.5, $"the row is {row.Bounds.Width} px wide and the window 320");
            Assert.True(row.Bounds.Height > 100, "the long text wraps onto more lines instead of running on");
            Assert.All(Nodes<TextBlock>(row).Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text)), t => Assert.True(t.Bounds.Width <= row.Bounds.Width, $"{t.Text} is wider than its row"));
        });
    }

    [Fact]
    public void With_reduce_motion_the_field_is_there_at_once_with_no_move()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeAnswered();
            source.Add(FakeAnswered.Record());
            var list = new AnsweredList(source);
            using var stage = new HomeStage(new ScrollViewer { Content = list }, 600, 700);
            var row = list.Rows.Single();
            Click(row.OverrideButton!);
            Assert.True(row.FieldOpen);
            stage.Layout();                                                                       // one layout pass, no frames: nothing moves
            Assert.True(row.Field!.IsEffectivelyVisible);
            Assert.True(row.Field.Bounds.Height > 0);
        });
    }

    [Fact]
    public void An_approval_the_delegate_allowed_is_listed_like_a_question()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeAnswered();
            source.Add(FakeAnswered.Record("Allowed", "Run this command? git status") with { Kind = DelegateKind.Approval });
            var list = new AnsweredList(source);
            using var stage = new HomeStage(new ScrollViewer { Content = list }, 600, 700);
            Assert.Equal("Allowed", list.Rows.Single().Lines[0]);
            Assert.NotNull(list.Rows.Single().OverrideButton);
        });
    }

    [Fact]
    public void The_home_shows_the_list_under_the_decision_cards_and_not_at_all_without_a_source()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var jobs = new FakeMissionJobs();
            var decisions = new FakeDecisions();
            var answered = new FakeAnswered();
            answered.Add(FakeAnswered.Record());
            var withIt = new AssistantHomeView(new AssistantHomeParts(jobs, decisions, new FakeGlobalControls()) { Answered = answered });
            using (var stage = new HomeStage(withIt, 1200, 900))
            {
                Assert.NotNull(withIt.Answered);
                Assert.True(withIt.Answered!.IsVisible);
                Assert.Single(withIt.Answered.Rows);
                Assert.Contains(withIt.Answered, withIt.Board.Decisions.GetVisualDescendants());
            }
            var without = new AssistantHomeView(new AssistantHomeParts(jobs, decisions, new FakeGlobalControls()));
            Assert.Null(without.Answered);
        });
    }
}
