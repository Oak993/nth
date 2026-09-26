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
using Marrow.Core.Port;
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
    [Trait("Req", "PR-HYDRA-216")] // App/Views/Chat/HydraHeadPopovers.swift:15, :36-40, :44
    [Trait("Req", "UI-HYDRA-047")]
    public void Header_pushes_the_accessory_to_the_row_trailing_edge_not_immediately_after_the_text_column()
    {
        headless.OnUiThread(() =>
        {
            var header = new HydraPopoverHeader(Persona, title: "Stream the parser", accessory: new CopyButton("x"));
            header.Measure(new Size(460, double.PositiveInfinity)); // :102 HydraHeadReportPopover.width, a real caller's constraint
            header.Arrange(new Rect(header.DesiredSize));

            var row = LogicalQuery.SelfAndDescendants<Control>(header).OfType<Grid>().Single();
            var accessory = LogicalQuery.SelfAndDescendants<Control>(header).OfType<CopyButton>().Single();
            var titleLabel = Text(header, "Stream the parser");

            // :36-40 Spacer(minLength: 0), once :44 .frame(maxWidth: .infinity) stretches the HStack: the accessory
            // sits flush against the row's own trailing edge, not merely somewhere to the right of the text column.
            // A StackPanel has no flexible-spacer primitive, so a `Border{Width=0}` "spacer" contributes zero pixels
            // and the accessory lands immediately after the text column instead (the regression this guards).
            Assert.Equal(row.Bounds.Width - accessory.Bounds.Width, accessory.Bounds.X, 3);
            Assert.True(
                accessory.Bounds.X > titleLabel.Bounds.X + titleLabel.Bounds.Width + 12,
                $"accessory.X was {accessory.Bounds.X}, not to the right of the text column ({titleLabel.Bounds.X + titleLabel.Bounds.Width})");
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
    [Trait("Decision", "ED-197")]
    public void Caption_with_a_finish_time_appends_a_middle_dot_and_the_relative_duration(HydraHeadInfo.Status status, string baseText)
    {
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");

        var startedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var finishedAt = startedAt.AddSeconds(90);
        var head = Head(status: status, startedAt: startedAt, finishedAt: finishedAt);

        // 90s -> "1m 30s" (SwiftFormattersSecondTests.RelativeTime_duration_allows_seconds_minutes_and_hours(90L,
        // "1m 30s")): the {seconds, minutes, hours} ICU register Components.swift:37-40 RelativeTime.duration
        // resolves to (ED-197), not the pre-B3 hand-built "Nm Ss" RelativeTime.Duration in Components.cs still
        // produces (that migration is tracked separately; this call site bypasses it, see Duration below).
        Assert.Equal(baseText + " \U000000B7 1m 30s", HydraHeadReportPopover.Caption(head));
    }

    [Theory]
    [Trait("Req", "PR-HYDRA-214")] // App/Views/Chat/HydraHeadPopovers.swift:124-126
    [Trait("Decision", "ED-197")]
    [InlineData(0.0, "0s")]
    [InlineData(1.0, "1s")]
    [InlineData(60.0, "1m")]     // the zero-valued seconds unit is dropped, not "1m 0s" (RelativeTime.Duration's old register)
    [InlineData(90.0, "1m 30s")]
    [InlineData(3600.0, "1h")]   // hours is an allowed unit here (unlike Hydra.cs/HydraPanel.cs's {seconds, minutes}), so this becomes "1h", not "60m"
    [InlineData(3725.0, "1h 2m")]
    public void Duration_uses_SwiftFormatters_DurationUnits_over_seconds_minutes_and_hours(double elapsedSeconds, string expected)
    {
        // Swift: duration(from:to:) reads UI/Common/Components.swift:37-40 RelativeTime.duration, whose register is
        // {seconds, minutes, hours} (ED-197; SwiftFormattersSecondTests.RelativeTime_duration_allows_seconds_minutes_and_hours
        // is this exact table). RelativeTime.Duration in Components.cs has not itself been migrated onto that
        // register yet, so this asserts the literal ICU output directly rather than delegating to it.
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");

        var from = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var to = from.AddSeconds(elapsedSeconds);

        Assert.Equal(expected, HydraHeadReportPopover.Duration(from, to));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-214")] // App/Views/Chat/HydraHeadPopovers.swift:124-126
    [Trait("Decision", "ED-197")]
    public void Duration_no_longer_matches_the_unmigrated_RelativeTime_Duration_register()
    {
        // Guards against silently reverting to the pre-B3 hand-built "Nm Ss" register (RelativeTime.Duration,
        // Components.cs, not yet migrated under ED-197): for an elapsed span whose two registers disagree - 60s is
        // "1m 0s" in the old register, "1m" (zero unit dropped) in the ICU one - this site must produce the
        // ICU-backed string, not RelativeTime.Duration's.
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");

        var from = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var to = from.AddSeconds(60);

        Assert.Equal("1m", HydraHeadReportPopover.Duration(from, to));
        Assert.Equal("1m 0s", RelativeTime.Duration(to - from)); // the old register, for contrast
        Assert.NotEqual(RelativeTime.Duration(to - from), HydraHeadReportPopover.Duration(from, to));
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

    [Fact]
    [Trait("Req", "PR-HYDRA-213")] // App/Views/Chat/HydraHeadPopovers.swift:74
    [Trait("Decision", "ED-062")]
    public void Report_popover_divider_is_a_1_point_hairline_at_22_percent_primary_text()
    {
        headless.OnUiThread(() =>
        {
            var popover = new HydraHeadReportPopover(Head());
            var divider = LogicalQuery.SelfAndDescendants<Control>(popover).OfType<Border>().Single(b => b.Height == 1);

            // A bare SwiftUI Divider() has no port counterpart; this matches the colour the port's UI layer already
            // establishes for exactly this construct - ChromeDivider (Chrome.cs:545-555) and PopoverDivider
            // (Popovers.cs:200-212) both resolve a bare Divider() to Chrome.PrimaryText.Opacity(0.22) - rather than
            // HydraReportsPopover.cs's unlabelled, differently-coloured border for the same Swift construct.
            Assert.Equal(Of(Chrome.PrimaryText.Opacity(0.22)), SolidColor(divider.Background));
        });
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
