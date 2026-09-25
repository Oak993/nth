using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class HydraReportsPopoverTests(HeadlessSession headless)
{
    private static string FirstRosterName => HydraRoster.Personas[0].Name;

    // "## " is required: HydraReportDigest.Parse (:44-84) only recognizes a line as a head section header
    // when it starts with "## " (matching HydraReportsPopoverUiTests.SingleHeadReport's "## Juno: ..." fixture).
    // Without it, Parse finds zero head sections and the whole text becomes unparsed intro.
    private static string SingleHeadText(string landing, string body) =>
        "## " + FirstRosterName + ": Stream the parser (high)\n"
        + landing + "\n\n"
        + body;

    private static HydraReportsPopover SingleHeadPopover(string landing, string body) =>
        new("Stream the parser", SingleHeadText(landing, body), Array.Empty<HydraPersona>());

    private static TextBlock Text(HydraReportsPopover root, string value) =>
        LogicalQuery.SelfAndDescendants<Control>(root)
            .OfType<TextBlock>()
            .Single(block => block.Text == value);

    private static TextBlock FilePath(HydraReportsPopover root, string path) =>
        LogicalQuery.SelfAndDescendants<Control>(root)
            .OfType<TextBlock>()
            .Single(block => block.Inlines is { } inlines
                && string.Concat(inlines.OfType<Run>().Select(run => run.Text)) == path);

    private static Color? SolidColor(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

    private static Color Of(SwiftColor color) => color.Resolve(ThemeVariant.Default);

    [Fact]
    [Trait("Req", "UI-HYDRA-045")] // App/Views/Chat/HydraReportsPopover.swift:591-626
    public void File_rows_show_additions_and_minus_sign_deletions_for_each_file()
    {
        headless.OnUiThread(() =>
        {
            var root = SingleHeadPopover(
                "Landed in your checkout: src/A/One.swift (+12 \U000022123), src/B/Two.swift (+1 \U000022120) with conflicts",
                "Done.");

            Assert.Equal("+12", Text(root, "+12").Text);
            Assert.Equal("\U000022123", Text(root, "\U000022123").Text);
            Assert.Equal("+1", Text(root, "+1").Text);
            Assert.Equal("\U000022120", Text(root, "\U000022120").Text);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-045")] // App/Views/Chat/HydraReportsPopover.swift:618-626
    [Trait("Decision", "ED-062")]
    public void File_path_directory_and_filename_use_secondary_and_primary_text()
    {
        headless.OnUiThread(() =>
        {
            var root = SingleHeadPopover(
                "Landed in your checkout: src/A/One.swift (+12 \U000022123)",
                "Done.");
            var path = FilePath(root, "src/A/One.swift");
            var runs = path.Inlines!.OfType<Run>().ToArray();

            Assert.Equal(2, runs.Length);
            Assert.Equal("src/A/", runs[0].Text);
            Assert.Equal(Of(Chrome.SecondaryText), SolidColor(runs[0].Foreground));
            Assert.Equal("One.swift", runs[1].Text);
            Assert.Equal(Of(Chrome.PrimaryText), SolidColor(runs[1].Foreground));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-045")] // App/Views/Chat/HydraReportsPopover.swift:609,624
    [Trait("Decision", "ED-062")]
    public void Conflicted_filename_is_warning_coloured_and_row_tooltip_names_the_conflict()
    {
        headless.OnUiThread(() =>
        {
            var root = SingleHeadPopover(
                "Landed in your checkout: src/B/Two.swift (+1 \U000022120) with conflicts",
                "Done.");
            var path = FilePath(root, "src/B/Two.swift");
            var filename = path.Inlines!.OfType<Run>().Last();
            var row = Assert.IsType<StackPanel>(path.Parent);

            Assert.Equal("Two.swift", filename.Text);
            Assert.Equal(Of(Chrome.Warning), SolidColor(filename.Foreground));
            Assert.Equal("src/B/Two.swift (with conflicts)", ToolTip.GetTip(row));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-045")] // App/Views/Chat/HydraReportsPopover.swift:609,624
    [Trait("Decision", "ED-062")]
    public void Unconflicted_filename_uses_primary_text_and_tooltip_is_the_bare_path()
    {
        headless.OnUiThread(() =>
        {
            var root = SingleHeadPopover(
                "Landed in your checkout: src/A/One.swift (+12 \U000022123)",
                "Done.");
            var path = FilePath(root, "src/A/One.swift");
            var filename = path.Inlines!.OfType<Run>().Last();
            var row = Assert.IsType<StackPanel>(path.Parent);

            Assert.Equal(Of(Chrome.PrimaryText), SolidColor(filename.Foreground));
            Assert.Equal("src/A/One.swift", ToolTip.GetTip(row));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-045")] // App/Views/Chat/HydraReportsPopover.swift:592-615
    public void Empty_file_list_draws_no_file_rows_or_counts()
    {
        headless.OnUiThread(() =>
        {
            var root = SingleHeadPopover("Changed nothing.", "No report.");
            var text = LogicalQuery.SelfAndDescendants<Control>(root).OfType<TextBlock>();

            Assert.DoesNotContain(text, block => block.Text is { } value
                && (value.StartsWith("+", StringComparison.Ordinal)
                    || value.StartsWith("\U00002212", StringComparison.Ordinal)));
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Control>(root)
                .OfType<TextBlock>(), block => block.Inlines is { } inlines
                    && string.Concat(inlines.OfType<Run>().Select(run => run.Text))
                        is "src/A/One.swift" or "src/B/Two.swift");
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-045")] // App/Views/Chat/HydraReportsPopover.swift:594,612-614
    [Trait("Decision", "ED-062")]
    public void File_rows_are_grouped_in_a_six_point_rounded_overlay_with_eight_by_six_padding()
    {
        headless.OnUiThread(() =>
        {
            var root = SingleHeadPopover(
                "Landed in your checkout: src/A/One.swift (+12 \U000022123)",
                "Done.");
            var group = LogicalQuery.SelfAndDescendants<Control>(root)
                .OfType<Border>()
                .Single(border => border.Padding == new Thickness(8, 6, 8, 6));

            Assert.Equal(6, group.CornerRadius.TopLeft);
            Assert.Equal(Of(Chrome.Overlay(0.05)), SolidColor(group.Background));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-045")] // App/Views/Chat/HydraReportsPopover.swift:598-599
    public void File_path_text_has_truncation_enabled()
    {
        headless.OnUiThread(() =>
        {
            var root = SingleHeadPopover(
                "Landed in your checkout: src/A/One.swift (+12 \U000022123)",
                "Done.");

            Assert.NotEqual(TextTrimming.None, FilePath(root, "src/A/One.swift").TextTrimming);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-045")] // App/Views/Chat/HydraReportsPopover.swift:608
    [Trait("Decision", "ED-059")]
    public void File_path_and_count_text_use_the_eleven_point_subheadline_size()
    {
        headless.OnUiThread(() =>
        {
            var root = SingleHeadPopover(
                "Landed in your checkout: src/A/One.swift (+12 \U000022123)",
                "Done.");

            Assert.Equal(11, FilePath(root, "src/A/One.swift").FontSize);
            Assert.Equal(11, Text(root, "+12").FontSize);
            Assert.Equal(11, Text(root, "\U000022123").FontSize);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-046")] // App/Views/Chat/HydraReportsPopover.swift:631-634
    [Trait("Decision", "ED-062")]
    [Trait("Decision", "ED-059")]
    public void Missing_report_shows_the_exact_subheadline_label_in_secondary_text()
    {
        headless.OnUiThread(() =>
        {
            var root = SingleHeadPopover("Changed nothing.", "No report.");
            var label = Text(root, "No report.");

            Assert.Equal("No report.", label.Text);
            Assert.Equal(11, label.FontSize);
            Assert.Equal(Of(Chrome.SecondaryText), SolidColor(label.Foreground));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-046")] // App/Views/Chat/HydraReportsPopover.swift:635-648
    [Trait("Decision", "ED-059")]
    public void A_single_head_report_is_rendered_eagerly_with_markdown_environment_values()
    {
        headless.OnUiThread(() =>
        {
            var root = SingleHeadPopover(
                "Changed nothing.",
                "| Header |\n| --- |\n| Cell |");
            var block = Assert.Single(LogicalQuery.SelfAndDescendants<Control>(root)
                .OfType<MarkdownBlockView>());

            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Control>(root).OfType<ProgressBar>(), _ => true);
            Assert.Equal(12, MarkdownView.GetMarkdownPointSize(block));
            Assert.False(MarkdownView.GetMarkdownDimmed(block));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-046")] // App/Views/Chat/HydraReportsPopover.swift:649-653
    public void An_expanded_unparsed_head_shows_a_small_progress_view_with_vertical_padding()
    {
        headless.OnUiThread(() =>
        {
            var first = HydraRoster.Personas[0].Name;
            var second = HydraRoster.Personas[1].Name;
            var text = "## " + first + ": First\nChanged nothing.\n\nReport.\n\n"
                + "## " + second + ": Second\nChanged nothing.\n\nNo report.";
            var root = new HydraReportsPopover("Batch", text, Array.Empty<HydraPersona>());
            var toggle = LogicalQuery.SelfAndDescendants<Control>(root)
                .OfType<Button>()
                .Single(button => LogicalQuery.SelfAndDescendants<Control>(button)
                    .OfType<TextBlock>()
                    .Any(block => block.Text == first));

            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var spinner = Assert.Single(LogicalQuery.SelfAndDescendants<Control>(root).OfType<ProgressBar>());
            Assert.Equal(new Thickness(0, 8, 0, 8), spinner.Margin);
        });
    }
}
