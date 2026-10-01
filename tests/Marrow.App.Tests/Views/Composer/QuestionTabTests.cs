using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/QuestionTab.swift (B2 1fbf81e), :1-332.
[Collection("Headless")]
public sealed class QuestionTabTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-188")] // App/Views/Composer/QuestionTab.swift:5-20
    [Trait("Req", "UI-COMPOSER-066")] // App/Views/Composer/QuestionTab.swift:5-21
    public void The_question_badge_shows_its_count_and_separate_caption()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var badge = new QuestionBadgeRow(Request(Question("q", choices: [Choice("A")])), null!);
            var text = LogicalQuery.SelfAndDescendants<TextBlock>(badge).Select(block => block.Text).ToArray();

            Assert.Contains("1 question", text);
            Assert.Contains("to answer", text);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-189")] // App/Views/Composer/QuestionTab.swift:30-65,105-117,169
    [Trait("Req", "PR-COMPOSER-194")] // App/Views/Composer/QuestionTab.swift:108,172-187
    [Trait("Req", "PR-COMPOSER-195")] // App/Views/Composer/QuestionTab.swift:82-103,122-131
    [Trait("Req", "PR-COMPOSER-196")] // App/Views/Composer/QuestionTab.swift:132-167
    [Trait("Req", "PR-COMPOSER-198")] // App/Views/Composer/QuestionTab.swift:189-194
    [Trait("Req", "UI-COMPOSER-067")] // App/Views/Composer/QuestionTab.swift:30-170
    [Trait("Req", "UI-COMPOSER-068")] // App/Views/Composer/QuestionTab.swift:82-103
    [Trait("Req", "UI-COMPOSER-072")] // App/Views/Composer/QuestionTab.swift:132-137
    [Trait("Req", "UI-COMPOSER-073")] // App/Views/Composer/QuestionTab.swift:122-131
    [Trait("Req", "UI-COMPOSER-074")] // App/Views/Composer/QuestionTab.swift:139-141
    [Trait("Req", "UI-COMPOSER-075")] // App/Views/Composer/QuestionTab.swift:145-155
    [Trait("Req", "UI-COMPOSER-076")] // App/Views/Composer/QuestionTab.swift:156-163
    [Trait("Decision", "ED-059")]
    public void A_multi_question_sheet_has_the_measured_page_area_counter_and_footer_actions()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var request = Request(
                Question("one", prompt: "First", choices: [Choice("A")]),
                Question("two", prompt: "Second", choices: [Choice("B")]));
            var sheet = new QuestionSheet(request, null!);
            var texts = LogicalQuery.SelfAndDescendants<TextBlock>(sheet)
                .Select(block => block.Text)
                .ToArray();

            sheet.Measure(Size.Infinity);

            Assert.Equal(460, sheet.DesiredSize.Width);
            Assert.Contains("1 of 2", texts);
            Assert.Contains("2 questions still to answer", texts);
            Assert.Contains("Skip", texts);
            Assert.Contains("Next", texts);
            // PR-COMPOSER-196: on a non-final page (page 0 of 2) with anything unanswered, the footer shows a
            // prominent Next only; Send appears once the page is final or every question is answered, neither of
            // which holds here (page 0 of 2, nothing answered yet), so it is correctly absent, not present-disabled.
            Assert.DoesNotContain("Send", texts);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-196")] // App/Views/Composer/QuestionTab.swift:132-163
    public void The_true_final_pages_footer_shows_only_a_disabled_Send_with_no_Next()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var request = Request(
                Question("one", prompt: "First", choices: [Choice("A")]),
                Question("two", prompt: "Second", choices: [Choice("B")]));
            var sheet = new QuestionSheet(request, null!);
            var advance = typeof(QuestionSheet).GetMethod("Advance", BindingFlags.Instance | BindingFlags.NonPublic)!;
            advance.Invoke(sheet, [0d]); // move from page 0 (first) to page 1 (final, still unanswered); delay 0 is synchronous

            var texts = LogicalQuery.SelfAndDescendants<TextBlock>(sheet).Select(block => block.Text).ToArray();
            Assert.Contains("2 of 2", texts);
            // Page 1 of 2 is the true last page (no page beyond it), so the bordered Next this page could otherwise
            // offer ("Next appears bordered when there are later pages") does not apply here; only Send shows.
            Assert.DoesNotContain("Next", texts);
            Assert.Contains("Send", texts);
            Assert.False(
                LogicalQuery.SelfAndDescendants<Button>(sheet)
                    .Single(button => ButtonText(button) == "Send")
                    .IsEnabled);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-190")] // App/Views/Composer/QuestionTab.swift:49-74
    [Trait("Req", "PR-COMPOSER-191")] // App/Views/Composer/QuestionTab.swift:247-254,277-287
    [Trait("Req", "PR-COMPOSER-192")] // App/Views/Composer/QuestionTab.swift:221-224,256-303
    [Trait("Req", "UI-COMPOSER-069")] // App/Views/Composer/QuestionTab.swift:199-312
    [Trait("Req", "UI-COMPOSER-070")] // App/Views/Composer/QuestionTab.swift:247-254,277-287
    [Trait("Decision", "ED-059")]
    public void Recommended_choices_show_the_stripped_label_and_recommended_pill()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var request = Request(Question(
                "q",
                prompt: "Choose",
                choices:
                [
                    Choice("Plan (Recommended)"),
                    Choice("Another RECOMMENDED option"),
                    Choice("Plain")
                ],
                allowsMultiple: true));
            var sheet = new QuestionSheet(request, null!);
            var texts = LogicalQuery.SelfAndDescendants<TextBlock>(sheet)
                .Select(block => block.Text)
                .ToArray();

            Assert.Equal(2, texts.Count(text => text == "Recommended"));
            Assert.Contains("Plan", texts);
            Assert.Contains("Another RECOMMENDED option", texts);
            Assert.DoesNotContain("Plan (Recommended)", texts);
            Assert.Contains("Plain", texts);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-193")] // App/Views/Composer/QuestionTab.swift:208-245
    [Trait("Req", "UI-COMPOSER-071")] // App/Views/Composer/QuestionTab.swift:225-240
    [Trait("Decision", "ED-059")]
    public void A_question_without_choices_shows_the_your_answer_field()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var sheet = new QuestionSheet(
                Request(Question("q", header: "Details", prompt: "Describe it")),
                null!);
            var text = LogicalQuery.SelfAndDescendants<TextBlock>(sheet)
                .Select(block => block.Text)
                .ToArray();

            Assert.Contains("DETAILS", text);
            Assert.Contains("Describe it", text);
            Assert.Contains(
                LogicalQuery.SelfAndDescendants<TextBox>(sheet),
                field => field.PlaceholderText?.ToString() == "Your answer");
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-197")] // App/Views/Composer/QuestionTab.swift:316-331
    public void Collected_answers_append_trimmed_written_text_after_picked_labels()
    {
        var request = Request(
            Question("picked", choices: [Choice("First"), Choice("Second")]),
            Question("written"));
        var selections = new Dictionary<string, ValueList<string>>(SwiftStringComparer.Instance)
        {
            ["picked"] = ValueList<string>.Empty.Appending("First").Appending("Second")
        };
        var written = new Dictionary<string, string>(SwiftStringComparer.Instance)
        {
            ["picked"] = " \nextra\t ",
            ["written"] = " answer\n"
        };

        var answers = QuestionAnswers.Collect(request, selections, written);

        Assert.Equal(["First", "Second", "extra"], answers["picked"].ToArray());
        Assert.Equal(["answer"], answers["written"].ToArray());
        Assert.Equal(0, QuestionAnswers.UnansweredCount(request, answers));
    }

    private static string ButtonText(Button button) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(button).Select(block => block.Text).FirstOrDefault() ?? "";

    [Fact]
    [Trait("Req", "PR-COMPOSER-193")] // App/Views/Composer/QuestionTab.swift:225-240 TextField/SecureField("Your answer") .textFieldStyle(.roundedBorder)
    [Trait("Req", "UI-COMPOSER-071")] // App/Views/Composer/QuestionTab.swift:225-240
    public void The_answer_field_is_drawn_and_a_secret_answer_is_masked()
    {
        // The app carries no theme package: a TextBox without a template draws nothing (0 points high), so the field must
        // carry the port's rounded-border text-field template (ChatView's .roundedBorder fields use the same one).
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var sheet = new QuestionSheet(Request(Question("q", prompt: "Paste the code", isSecret: true)), null!);
            var window = new Avalonia.Controls.Window { Content = sheet, Width = 640, Height = 400 };
            window.Show();
            try
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var field = LogicalQuery.SelfAndDescendants<TextBox>(sheet).Single();
                Assert.True(field.Bounds.Height > 0, "the answer field has no height: " + field.Bounds);
                Assert.Contains(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(field).OfType<TextBlock>(), text => text.Text == "Your answer" && text.IsEffectivelyVisible);
                field.Text = "123456";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var presenter = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(field).OfType<Avalonia.Controls.Presenters.TextPresenter>().Single();
                Assert.Equal("123456", presenter.Text);
                Assert.Equal('•', presenter.PasswordChar); // :231 SecureField
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-075")] // App/Views/Composer/QuestionTab.swift:139-160 Skip .bordered, Send .borderedProminent
    public void The_footer_buttons_are_drawn_skip_bordered_and_send_prominent()
    {
        // An Avalonia Button without a template showed only its label in this theme-less app; the footer uses
        // SystemButtons (the Windows standard and accent buttons, proposal ED-229 (4)).
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var sheet = new QuestionSheet(Request(Question("q", prompt: "Pick", choices: [Choice("A")])), null!);
            var window = new Avalonia.Controls.Window { Content = sheet, Width = 640, Height = 400 };
            window.Show();
            try
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Button Named(string title) => LogicalQuery.SelfAndDescendants<Button>(sheet).Single(b => b.Content is TextBlock { Text: var t } && t == title);
                Border Card(Button b) => Assert.IsType<Border>(Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualChildren(b)));
                var skip = Named("Skip");
                var send = Named("Send");
                Assert.True(skip.Bounds.Height > 0 && send.Bounds.Height > 0);
                Assert.Equal(new Thickness(1), Card(skip).BorderThickness); // bordered: a hairline bezel
                Assert.Equal(new Thickness(0), Card(send).BorderThickness); // prominent: the accent fill
                Assert.True(send.IsDefault);
            }
            finally { window.Close(); }
        });
    }

    private static QuestionRequest Request(params QuestionRequest.Question[] questions)
    {
        var values = ValueList<QuestionRequest.Question>.Empty;
        foreach (var question in questions)
            values = values.Appending(question);
        return new QuestionRequest("request", values);
    }

    private static QuestionRequest.Question Question(
        string id,
        string header = "",
        string prompt = "Prompt",
        QuestionRequest.Choice[]? choices = null,
        bool allowsMultiple = false,
        bool allowsOther = false,
        bool isSecret = false)
    {
        var values = ValueList<QuestionRequest.Choice>.Empty;
        foreach (var choice in choices ?? [])
            values = values.Appending(choice);
        return new QuestionRequest.Question(id, header, prompt, values, allowsMultiple, allowsOther, isSecret);
    }

    private static QuestionRequest.Choice Choice(string label, string? detail = null) => new(label, detail);

    [Fact]
    [Trait("Req", "UI-COMPOSER-067")] // App/Views/Composer/QuestionTab.swift:203-208,236-238 the header Text wraps at the page width
    public void A_long_question_header_wraps_within_the_page_width()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var header = string.Join(" ", Enumerable.Repeat("very long header", 20));
            var sheet = new QuestionSheet(Request(Question("q", header: header, choices: [Choice("A")])), null!);
            var block = LogicalQuery.SelfAndDescendants<TextBlock>(sheet).Single(text => text.Text == header.ToUpperInvariant());
            block.Measure(new Size(428, double.PositiveInfinity)); // the page's content width: 460 less its 16 point padding either side
            Assert.True(block.TextLayout.TextLines.Count > 1, "the header measured one long line: " + block.DesiredSize);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-076")] // App/Views/Composer/QuestionTab.swift:117-162 HStack spacing 8 only between children: the last button is flush with the footer edge
    public void The_incomplete_state_next_button_has_no_trailing_margin()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var sheet = new QuestionSheet(Request(
                Question("one", prompt: "First", choices: [Choice("A")]),
                Question("two", prompt: "Second", choices: [Choice("B")])), null!);
            var next = LogicalQuery.SelfAndDescendants<Button>(sheet).Single(button => ButtonText(button) == "Next");
            Assert.Equal(0, next.Margin.Right);
        });
    }
}

// STOP App.Views.Composer.QuestionTab #1 (verifier, 2026-09-29)
// Kind: coverage gap, not a known code defect.
// Swift: App/Views/Composer/QuestionTab.swift:58 measures every page with selections: .constant([:]) and written: .constant([:]).
// Record: UI-COMPOSER-067. The QuestionSheet constructor probe now uses constant empty bindings (source-matched), but no test
// proves it: the probe is a discarded local and headless font metrics do not reproduce a weight-dependent wrap.
// Needed: a real-font capture where a recommended (semibold) choice wraps differently from the unselected one.
