using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (adversarial round 5 of the TimelineRows parity pass, 2026-10-03). Each test fails alone, twice, at 050a3040f.
[Collection("Headless")]
public sealed class TimelineRowsParityReview5Tests(HeadlessSession headless)
{
    // PR-HYDRAMERGE-090: the generic report renders outcome.body "with a markdown point size of 12" (TimelineRows.swift:631
    // .environment(\.markdownPointSize, 12)). A paragraph with a link is InlineText's link branch, LinkParagraphView(pointSize:
    // scaled) (MarkdownView.swift:1094, :1114 at B3; scaled = (12 * 2).rounded() / 2 = 12), whose runs are drawn at that size
    // (LinkParagraphView.swift:111-120); MarkdownView.swift:801-803: "Base point size for link paragraphs ... everything else reads
    // the 13 pt chat default". In the port, HydraReportPopover sets the point size on each MarkdownBlockView after constructing it
    // (TimelineRows.cs HydraReportPopover), and MarkdownBlockView builds its InlineText in its constructor, where InlineText reads
    // MarkdownView.GetMarkdownPointSize(this) with no parent: the default, 13 (MarkdownView.cs InlineText()). So every report
    // paragraph, heading or list item with a link (a merge-failed note's error naming its job, round 4's third InlineData row) is
    // drawn one point larger than the report's other text. Control: the plain paragraph next to it is drawn at 12.
    // Fails alone, twice, at 050a3040f: "the report's link paragraph is drawn at 13 points".
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-HYDRAMERGE-090 (a markdown point size of 12, link text included): TimelineRows.swift:607-640, :631; MarkdownView.swift:801-806, :1071, :1094, :1114
    public void REVIEW_A_report_paragraph_with_a_link_is_drawn_at_the_reports_12_point_markdown_size() => headless.OnUiThread(() =>
    {
        // No network (R-TEST-10; ED-NEW-parity-timelinerows (10); the assertions untouched): the shown report's link paragraph asks for
        // its host's favicon, which awaits an in-flight fetch the rig seeds instead of gitlab.com itself; the rig refuses anything else.
        using var noNetwork = new NoLinkNetwork("gitlab.com");
        using var h = TimelineRowsPrSupport.Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        Avalonia.Controls.Window window = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                var report = new HydraReportPopover("Plain words of the report.\n\nThe pipeline failed: see [the lint job](https://gitlab.com/g/p/-/jobs/12).");
                window = TimelineRowsPrSupport.Show(report);
                var link = Assert.Single(report.GetVisualDescendants().OfType<LinkParagraphView>());
                var linked = Assert.Single(link.GetVisualDescendants().OfType<SelectableTextBlock>());
                var plain = Assert.Single(report.GetVisualDescendants().OfType<SelectableTextBlock>(), block => block != linked);
                Assert.Equal(12, plain.FontSize); // control: the report's own text size
                var sizes = linked.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.FontSize).Distinct().ToArray();
                Assert.True(sizes.SequenceEqual([12.0]), $"the report's link paragraph is drawn at {string.Join(", ", sizes)} points");
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => TimelineRowsPrSupport.Close(window)); }
    });

    // ED-NEW-parity-timelinerows (7) decides that the detail's .lineLimit(12) (TimelineRows.swift:2125) cuts its twelfth line with an
    // ellipsis ("the cut last line ends in an ellipsis", SwiftUI's default truncationMode .tail). The port does so only when the cut
    // falls inside a wrapped line: a detail cut at a hard line break (a multi-line command, a heredoc: twenty short lines) ends its
    // twelfth line with no ellipsis, so the reader cannot tell the command goes on. Whether the Mac draws the ellipsis there is owed
    // a native check (no frame of the reference video shows a detail past twelve lines); this test holds the port to its own ED (7),
    // which either the code or the ED must change to meet. Fails alone, twice, at 050a3040f: "the twelfth line is cut with no ellipsis".
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-101 (the detail capped at twelve lines), ED-NEW-parity-timelinerows (7): TimelineRows.swift:2120-2126
    public void REVIEW_A_tool_detail_cut_at_a_hard_line_break_ends_its_twelfth_line_in_an_ellipsis() => headless.OnUiThread(() =>
    {
        using var h = TimelineRowsPrSupport.Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        Avalonia.Controls.Window window = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                var detailText = string.Join("\n", Enumerable.Range(1, 20).Select(i => "echo line " + i));
                var tool = new ToolDetailView(TimelineRowsPrSupport.Call(ToolCall.Kind.Command, "run") with { Detail = detailText });
                window = TimelineRowsPrSupport.Show(new Border { Width = 500, Child = tool, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left });
                var detail = Assert.Single(tool.GetVisualDescendants().OfType<SelectableTextBlock>());
                Assert.Equal(12, detail.TextLayout.TextLines.Count); // cut at the twelfth line
                Assert.True(detail.TextLayout.TextLines[^1].HasCollapsed, "the twelfth line is cut with no ellipsis");
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => TimelineRowsPrSupport.Close(window)); }
    });
}
