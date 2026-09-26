using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class HydraHeadPopoversTests(HeadlessSession headless)
{
    private static HydraPersona Persona => HydraRoster.Personas[0];

    private static HydraHeadInfo Head(
        HydraHeadInfo.Status status = HydraHeadInfo.Status.Completed,
        string? summary = "The work is done.",
        string task = "Stream the parser",
        DateTimeOffset? startedAt = null,
        DateTimeOffset? finishedAt = null) =>
        new(0, task, HydraHeadInfo.Kind.Native, HydraHeadInfo.Origin.Delegated)
        {
            HydraHeadInfoStatus = status,
            Summary = summary,
            StartedAt = startedAt ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            FinishedAt = finishedAt,
        };

    private static Color Of(SwiftColor color) => color.Resolve(ThemeVariant.Default);

    private static Color? SolidColor(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

    private static Run NameRun(Control root, string name) =>
        LogicalQuery.SelfAndDescendants<Control>(root)
            .OfType<TextBlock>()
            .SelectMany(block => block.Inlines?.OfType<Run>() ?? Enumerable.Empty<Run>())
            .Single(run => run.Text == name);

    private static TextBlock Text(Control root, string value) =>
        LogicalQuery.SelfAndDescendants<Control>(root)
            .OfType<TextBlock>()
            .Single(block => block.Text == value);

    // ---- HydraPopoverHeader (PR-HYDRA-216, UI-HYDRA-047) ----

    [Fact]
    [Trait("Req", "PR-HYDRA-216")] // App/Views/Chat/HydraHeadPopovers.swift:16-17
    [Trait("Req", "UI-HYDRA-047")]
    public void Header_glyph_is_28_points_with_a_one_point_top_margin()
    {
        headless.OnUiThread(() =>
        {
            var header = new HydraPopoverHeader(Persona, status: HydraHeadInfo.Status.Completed);
            var glyph = LogicalQuery.SelfAndDescendants<Control>(header).OfType<HydraGlyph>().Single();

            Assert.Equal(28, glyph.Size);
            Assert.Equal(HydraHeadInfo.Status.Completed, glyph.Status);
            Assert.Equal(new Thickness(0, 1, 0, 0), glyph.Margin);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-216")] // App/Views/Chat/HydraHeadPopovers.swift:20
    [Trait("Req", "UI-HYDRA-047")]
    [Trait("Decision", "ED-059")]
    public void Header_name_uses_14_point_semibold_primary_text()
    {
        headless.OnUiThread(() =>
        {
            var header = new HydraPopoverHeader(Persona);
            var run = NameRun(header, Persona.Name);

            Assert.Equal(14, run.FontSize);
            Assert.Equal(FontWeight.SemiBold, run.FontWeight);
            Assert.Equal(Of(Chrome.PrimaryText), SolidColor(run.Foreground));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-047")] // App/Views/Chat/HydraHeadPopovers.swift:21, :29
    public void Header_omits_the_caption_and_title_when_neither_is_given()
    {
        headless.OnUiThread(() =>
        {
            var header = new HydraPopoverHeader(Persona);
            var texts = LogicalQuery.SelfAndDescendants<Control>(header).OfType<TextBlock>()
                .Where(block => block.Text is not null)
                .Select(block => block.Text)
                .ToArray();

            Assert.DoesNotContain(texts, value => value != Persona.Name);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-047")] // App/Views/Chat/HydraHeadPopovers.swift:21, :29
    public void Header_omits_the_caption_and_title_when_both_are_empty_strings()
    {
        headless.OnUiThread(() =>
        {
            var header = new HydraPopoverHeader(Persona, caption: "", title: "");
            var texts = LogicalQuery.SelfAndDescendants<Control>(header).OfType<TextBlock>()
                .Where(block => block.Text is not null)
                .Select(block => block.Text)
                .ToArray();

            Assert.DoesNotContain(texts, value => value != Persona.Name);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-216")] // App/Views/Chat/HydraHeadPopovers.swift:21-27
    [Trait("Req", "UI-HYDRA-047")]
    [Trait("Decision", "ED-059")]
    public void Header_caption_is_11_point_medium_monospaced_and_one_line_in_the_given_colour()
    {
        headless.OnUiThread(() =>
        {
            var header = new HydraPopoverHeader(Persona, caption: "Finished", captionColor: Chrome.Danger);
            var label = Text(header, "Finished");

            Assert.Equal(11, label.FontSize);
            Assert.Equal(FontWeight.Medium, label.FontWeight);
            Assert.Equal(Of(Chrome.Danger), SolidColor(label.Foreground));
            Assert.Equal(TextWrapping.NoWrap, label.TextWrapping);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-047")] // App/Views/Chat/HydraHeadPopovers.swift:8
    [Trait("Decision", "ED-062")]
    public void Header_caption_defaults_to_secondary_text_when_no_colour_is_given()
    {
        headless.OnUiThread(() =>
        {
            var header = new HydraPopoverHeader(Persona, caption: "Brief");
            var label = Text(header, "Brief");

            Assert.Equal(Of(Chrome.SecondaryText), SolidColor(label.Foreground));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-216")] // App/Views/Chat/HydraHeadPopovers.swift:29-34
    [Trait("Req", "UI-HYDRA-047")]
    [Trait("Decision", "ED-059")]
    public void Header_title_is_12_point_callout_at_85_percent_primary_text_wrapped_to_3_lines()
    {
        headless.OnUiThread(() =>
        {
            var header = new HydraPopoverHeader(Persona, title: "Stream the parser");
            var label = Text(header, "Stream the parser");

            Assert.Equal(12, label.FontSize);
            Assert.Equal(3, label.MaxLines);
            Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
            Assert.Equal(Of(Chrome.PrimaryText.Opacity(0.85)), SolidColor(label.Foreground));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-047")] // App/Views/Chat/HydraHeadPopovers.swift:38-40
    public void Header_omits_the_accessory_when_none_is_given()
    {
        headless.OnUiThread(() =>
        {
            var header = new HydraPopoverHeader(Persona);

            Assert.Empty(LogicalQuery.SelfAndDescendants<Control>(header).OfType<CopyButton>());
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-047")] // App/Views/Chat/HydraHeadPopovers.swift:38-40
    public void Header_shows_the_accessory_when_one_is_given()
    {
        headless.OnUiThread(() =>
        {
            var header = new HydraPopoverHeader(Persona, accessory: new CopyButton("x"));

            Assert.Single(LogicalQuery.SelfAndDescendants<Control>(header).OfType<CopyButton>());
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-216")] // App/Views/Chat/HydraHeadPopovers.swift:42-46
    [Trait("Req", "UI-HYDRA-047")]
    [Trait("Decision", "ED-062")]
    public void Header_padding_is_16_by_14_over_a_3_and_a_half_percent_overlay()
    {
        headless.OnUiThread(() =>
        {
            var header = new HydraPopoverHeader(Persona);
            var background = Assert.IsType<Border>(header.Child);

            Assert.Equal(new Thickness(16, 14, 16, 14), background.Padding);
            Assert.Equal(Of(Chrome.Overlay(0.035)), SolidColor(background.Background));
        });
    }

    // ---- HydraHeadReportPopover (PR-HYDRA-213, PR-HYDRA-214, UI-HYDRA-048/049/050) ----

    [Fact]
    [Trait("Req", "PR-HYDRA-213")] // App/Views/Chat/HydraHeadPopovers.swift:55, :102
    public void Report_popover_is_460_points_wide()
    {
        headless.OnUiThread(() =>
        {
            var popover = new HydraHeadReportPopover(Head());

            Assert.Equal(460, popover.Width);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-213")] // App/Views/Chat/HydraHeadPopovers.swift:66-72
    public void Report_popover_header_carries_the_head_persona_status_and_task()
    {
        headless.OnUiThread(() =>
        {
            var head = Head(status: HydraHeadInfo.Status.Failed, task: "Stream the parser");
            var popover = new HydraHeadReportPopover(head);
            var glyph = LogicalQuery.SelfAndDescendants<Control>(popover).OfType<HydraGlyph>().Single();

            Assert.Equal(HydraHeadInfo.Status.Failed, glyph.Status);
            NameRun(popover, head.Persona.Name);
            Text(popover, "Stream the parser");
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-213")] // App/Views/Chat/HydraHeadPopovers.swift:72
    [Trait("Req", "UI-HYDRA-049")]
    public void Report_popover_shows_the_copy_button_only_when_the_head_has_a_summary()
    {
        headless.OnUiThread(() =>
        {
            var withReport = new HydraHeadReportPopover(Head(summary: "Done."));
            var withoutReport = new HydraHeadReportPopover(Head(summary: null));
            var blankReport = new HydraHeadReportPopover(Head(summary: "   "));

            Assert.Single(LogicalQuery.SelfAndDescendants<Control>(withReport).OfType<CopyButton>());
            Assert.Empty(LogicalQuery.SelfAndDescendants<Control>(withoutReport).OfType<CopyButton>());
            Assert.Empty(LogicalQuery.SelfAndDescendants<Control>(blankReport).OfType<CopyButton>());
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-213")] // App/Views/Chat/HydraHeadPopovers.swift:80-83
    [Trait("Req", "UI-HYDRA-050")]
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    public void Report_popover_with_an_empty_summary_shows_the_exact_callout_label_in_secondary_text()
    {
        headless.OnUiThread(() =>
        {
            var popover = new HydraHeadReportPopover(Head(summary: null));
            var label = Text(popover, "No report yet.");

            Assert.Equal(12, label.FontSize);
            Assert.Equal(Of(Chrome.SecondaryText), SolidColor(label.Foreground));
            Assert.Empty(LogicalQuery.SelfAndDescendants<Control>(popover).OfType<MarkdownBlockView>());
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-213")] // App/Views/Chat/HydraHeadPopovers.swift:84-94
    public void Report_popover_renders_every_block_eagerly_with_the_markdown_environment_values()
    {
        headless.OnUiThread(() =>
        {
            var popover = new HydraHeadReportPopover(Head(summary: "One.\n\nTwo."));
            var blocks = LogicalQuery.SelfAndDescendants<Control>(popover).OfType<MarkdownBlockView>().ToArray();

            Assert.Equal(2, blocks.Length);
            foreach (var block in blocks)
            {
                Assert.Equal(12, MarkdownView.GetMarkdownPointSize(block));
                Assert.False(MarkdownView.GetMarkdownDimmed(block));
                Assert.True(MarkdownView.GetMarkdownBlockSelection(block));
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-214")] // App/Views/Chat/HydraHeadPopovers.swift:106-111
    [Trait("Req", "UI-HYDRA-048")]
    public void Caption_is_exactly_working_while_running_with_no_duration_even_if_finished_at_is_set()
    {
        var head = Head(status: HydraHeadInfo.Status.Running, finishedAt: new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero));

        Assert.Equal("Working", HydraHeadReportPopover.Caption(head));
    }

    [Theory]
    [InlineData(HydraHeadInfo.Status.Completed, "Finished")]
    [InlineData(HydraHeadInfo.Status.Failed, "Failed")]
    [InlineData(HydraHeadInfo.Status.Stopped, "Stopped")]
    [Trait("Req", "PR-HYDRA-214")] // App/Views/Chat/HydraHeadPopovers.swift:106-112
    [Trait("Req", "UI-HYDRA-048")]
    public void Caption_without_a_finish_time_is_exactly_the_base_word(HydraHeadInfo.Status status, string expected)
    {
        var head = Head(status: status, finishedAt: null);

        Assert.Equal(expected, HydraHeadReportPopover.Caption(head));
    }

    [Theory]
    [InlineData(HydraHeadInfo.Status.Completed, "Finished")]
    [InlineData(HydraHeadInfo.Status.Failed, "Failed")]
    [InlineData(HydraHeadInfo.Status.Stopped, "Stopped")]
    [Trait("Req", "PR-HYDRA-214")] // App/Views/Chat/HydraHeadPopovers.swift:112-113
    [Trait("Req", "UI-HYDRA-048")]
    public void Caption_with_a_finish_time_appends_a_middle_dot_and_the_relative_duration(HydraHeadInfo.Status status, string baseText)
    {
        var startedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var finishedAt = startedAt.AddSeconds(90);
        var head = Head(status: status, startedAt: startedAt, finishedAt: finishedAt);

        // The exact duration string is UNRESOLVED (B3_PLAN.md 6.1 E1, RelativeTime.duration's Duration.UnitsFormatStyle
        // output on the reference runtime): this asserts the composition (base word, " · ", the shared
        // duration) against RelativeTime.Duration itself rather than a literal string, so it holds however that
        // shared helper's own open item settles.
        Assert.Equal(baseText + " \U000000B7 " + RelativeTime.Duration(finishedAt - startedAt), HydraHeadReportPopover.Caption(head));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-214")] // App/Views/Chat/HydraHeadPopovers.swift:124-126
    public void Duration_delegates_to_the_shared_relative_time_duration_over_the_elapsed_span()
    {
        var from = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var to = from.AddSeconds(3725);

        Assert.Equal(RelativeTime.Duration(to - from), HydraHeadReportPopover.Duration(from, to));
    }

    [Theory]
    [InlineData(HydraHeadInfo.Status.Failed)]
    [InlineData(HydraHeadInfo.Status.Stopped)]
    [InlineData(HydraHeadInfo.Status.Completed)]
    [InlineData(HydraHeadInfo.Status.Running)]
    [Trait("Req", "PR-HYDRA-214")] // App/Views/Chat/HydraHeadPopovers.swift:116-121
    [Trait("Req", "UI-HYDRA-048")]
    [Trait("Decision", "ED-062")]
    public void CaptionColor_is_danger_for_failed_warning_for_stopped_and_secondary_otherwise(HydraHeadInfo.Status status)
    {
        var expected = status switch
        {
            HydraHeadInfo.Status.Failed => Chrome.Danger,
            HydraHeadInfo.Status.Stopped => Chrome.Warning,
            _ => Chrome.SecondaryText,
        };

        Assert.Equal(Of(expected), Of(HydraHeadReportPopover.CaptionColor(status)));
    }

    // ---- HydraBriefPopover (PR-HYDRA-215, UI-HYDRA-051) ----

    [Fact]
    [Trait("Req", "PR-HYDRA-215")] // App/Views/Chat/HydraHeadPopovers.swift:139, :168
    public void Brief_popover_is_520_points_wide()
    {
        headless.OnUiThread(() =>
        {
            var popover = new HydraBriefPopover(Persona, "Stream the parser", "Some text");

            Assert.Equal(520, popover.Width);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-215")] // App/Views/Chat/HydraHeadPopovers.swift:148
    [Trait("Req", "UI-HYDRA-051")]
    public void Brief_popover_header_glyph_carries_no_status_badge()
    {
        headless.OnUiThread(() =>
        {
            var popover = new HydraBriefPopover(Persona, "Stream the parser", "Some text");
            var glyph = LogicalQuery.SelfAndDescendants<Control>(popover).OfType<HydraGlyph>().Single();

            Assert.Null(glyph.Status);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-215")] // App/Views/Chat/HydraHeadPopovers.swift:148
    [Trait("Req", "UI-HYDRA-051")]
    [Trait("Decision", "ED-062")]
    public void Brief_popover_caption_is_exactly_brief_in_secondary_text()
    {
        headless.OnUiThread(() =>
        {
            var popover = new HydraBriefPopover(Persona, "Stream the parser", "Some text");
            var label = Text(popover, "Brief");

            Assert.Equal(Of(Chrome.SecondaryText), SolidColor(label.Foreground));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-215")] // App/Views/Chat/HydraHeadPopovers.swift:148
    public void Brief_popover_copy_button_is_unconditional_even_with_empty_text()
    {
        headless.OnUiThread(() =>
        {
            var popover = new HydraBriefPopover(Persona, null, "");

            Assert.Single(LogicalQuery.SelfAndDescendants<Control>(popover).OfType<CopyButton>());
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-215")] // App/Views/Chat/HydraHeadPopovers.swift:150-161
    public void Brief_popover_renders_every_block_with_the_markdown_environment_values()
    {
        headless.OnUiThread(() =>
        {
            var popover = new HydraBriefPopover(Persona, "Stream the parser", "One.\n\nTwo.");
            var blocks = LogicalQuery.SelfAndDescendants<Control>(popover).OfType<MarkdownBlockView>().ToArray();

            Assert.Equal(2, blocks.Length);
            foreach (var block in blocks)
            {
                Assert.Equal(12, MarkdownView.GetMarkdownPointSize(block));
                Assert.False(MarkdownView.GetMarkdownDimmed(block));
                Assert.True(MarkdownView.GetMarkdownBlockSelection(block));
            }
        });
    }
}
