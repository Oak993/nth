using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (adversarial round 4 of the TimelineRows parity pass, 2026-10-03). Round 3's fix gave the shared selection recipe
// (VeiledText.SelectableText: the I-beam and SwiftColor.SelectedTextBackgroundColor as the SelectionBrush) to the notice, the tool's
// detail and output and the head popover's five lines. A text with a link takes another path: InlineText's hasLinkText branch
// (MarkdownView.swift:1134-1191) builds LinkParagraphView, whose LinkTextView draws into its own SelectableTextBlock
// (LinkParagraphView.cs LinkTextView(): no SelectionBrush, and RenderText sets none). With no control theme loaded that brush is null,
// so a drag selects text that is never painted. On the Mac the same text is an NSTextView with isSelectable = true
// (LinkParagraphView.swift:312-313), which draws its selection in the system's selected-text colour.
// Two sites of this unit's claimed records take that path:
//  - the sent bubble (TimelineRows.swift:119-123 InlineText(parts.body, hugsContent: true) .textSelection(.enabled)): PR-CHAT-077
//    "Its text is selectable and renders like the answers do, so a bare domain types as a link", PR-CHAT-166 (:123);
//  - the generic report popover (TimelineRows.swift:607-640, .textSelection(.enabled) at :633): PR-HYDRAMERGE-090 "text selection on",
//    PR-CHAT-166 (:633). Any paragraph of the report with a link (here a markdown link in a merge-failed note's error line) takes
//    the link path. (The bare address line itself is drawn as veiled text by this port's parser; whether it is a link on the Mac
//    is the shared MarkdownView unit's question, not asserted here.)
// Both fail alone, twice, at 9af15a67f: "... the selection is drawn with no brush (nothing is drawn)".
[Collection("Headless")]
public sealed class TimelineRowsParityReview4Tests(HeadlessSession headless)
{
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-166 and PR-CHAT-077 (a bubble with a link): TimelineRows.swift:119-123; MarkdownView.swift:1134-1191; LinkParagraphView.swift:312-313
    public void REVIEW_A_selection_in_a_sent_bubble_with_a_bare_domain_is_drawn_in_the_system_selected_text_colour() => headless.OnUiThread(() =>
    {
        using var h = TimelineRowsPrSupport.Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        Avalonia.Controls.Window window = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                var row = new UserMessageRow(TimelineRowsPrSupport.UserEntry("see example.com for it", Guid.NewGuid()), h.Runtime, canRevert: false);
                window = TimelineRowsPrSupport.Show(row);
                Assert.Single(row.GetVisualDescendants().OfType<LinkParagraphView>()); // the link path, as PR-CHAT-077's own test shows
                var system = SwiftColor.SelectedTextBackgroundColor.Resolve(window.ActualThemeVariant);
                Assert.NotEqual(0, system.A);
                var text = Assert.Single(row.GetVisualDescendants().OfType<SelectableTextBlock>());
                text.SelectAll();
                Assert.False(string.IsNullOrEmpty(text.SelectedText)); // selected ...
                Assert.True((text.SelectionBrush as Avalonia.Media.ISolidColorBrush)?.Color == system,
                    $"the bubble's link text: the selection is drawn with {text.SelectionBrush?.ToString() ?? "no brush (nothing is drawn)"}"); // ... and drawn
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => TimelineRowsPrSupport.Close(window)); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-HYDRAMERGE-090 and PR-CHAT-166 (the generic report's link paragraph): TimelineRows.swift:607-640, :633, :1111-1120
    public void REVIEW_A_selection_in_the_merge_failed_notes_generic_report_is_drawn_in_the_system_selected_text_colour() => headless.OnUiThread(() =>
    {
        using var h = TimelineRowsPrSupport.Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        Avalonia.Controls.Window window = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                // outcome.body of a merge-failed note (AppModel+HydraMerge.swift:371: the address, a blank line, the error's description,
                // a blank line, the retry line; PR-HYDRAMERGE-088), the error naming its job with a markdown link.
                var report = new HydraReportPopover("https://gitlab.com/g/p/-/merge_requests/7\n\nThe pipeline failed: see [the lint job](https://gitlab.com/g/p/-/jobs/12).\n\nThe next try brings the request up to date with `main` and merges it.");
                window = TimelineRowsPrSupport.Show(report);
                var system = SwiftColor.SelectedTextBackgroundColor.Resolve(window.ActualThemeVariant);
                Assert.NotEqual(0, system.A);
                var texts = report.GetVisualDescendants().OfType<SelectableTextBlock>().ToArray();
                Assert.Single(report.GetVisualDescendants().OfType<LinkParagraphView>()); // the paragraph with the link takes the link path
                Assert.True(texts.Length >= 2, $"{texts.Length} selectable texts in the report"); // the paragraphs, the one with the link among them
                foreach (var text in texts)
                {
                    text.SelectAll();
                    Assert.False(string.IsNullOrEmpty(text.SelectedText)); // selected ...
                    Assert.True((text.SelectionBrush as Avalonia.Media.ISolidColorBrush)?.Color == system,
                        $"the report's \"{text.Inlines?.Text ?? text.Text}\": the selection is drawn with {text.SelectionBrush?.ToString() ?? "no brush (nothing is drawn)"}"); // ... and drawn
                }
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => TimelineRowsPrSupport.Close(window)); }
    });
}
