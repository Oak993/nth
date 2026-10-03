using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
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
// Both fail alone, twice, at 9af15a67f: "... the selection is drawn with no brush (nothing is drawn)"; both pass with a probe (not
// committed) that gives LinkTextView's text block the same attach-time SelectionBrush. The file also holds a favicon REVIEW test
// (PR-CHAT-077) and a wrap REVIEW test (PR-CHAT-101, found in passing), each documented at the test.
[Collection("Headless")]
public sealed class TimelineRowsParityReview4Tests(HeadlessSession headless)
{
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-166 and PR-CHAT-077 (a bubble with a link): TimelineRows.swift:119-123; MarkdownView.swift:1134-1191; LinkParagraphView.swift:312-313
    public void REVIEW_A_selection_in_a_sent_bubble_with_a_bare_domain_is_drawn_in_the_system_selected_text_colour() => headless.OnUiThread(() =>
    {
        // No network (R-TEST-10; ED-256 (10); the assertions untouched): the bubble's link paragraph asks for its
        // host's favicon, which awaits an in-flight fetch the rig seeds instead of example.com itself; the rig refuses anything else.
        using var noNetwork = new NoLinkNetwork("example.com");
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
        // No network (R-TEST-10; ED-256 (10); the assertions untouched): the shown report's link paragraph asks for
        // its host's favicon, which awaits an in-flight fetch the rig seeds instead of gitlab.com itself; the rig refuses anything else.
        using var noNetwork = new NoLinkNetwork("gitlab.com");
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

    // PR-CHAT-077 ("a bare domain types as a link with its favicon in front"). The bubble's InlineText (TimelineRows.swift:119-122)
    // takes the link path; its favicon is fetched by `.task(id: hostKey)` (MarkdownView.swift:1189-1191, fetchLinkAssets :1225-1238),
    // which bumps `@State faviconRevision` (:1113) once an icon arrives, and LinkParagraphView(revision: faviconRevision) (:1148)
    // draws the text again with the icon in front. In the port, InlineText's hasLinkText branch keeps the revision in a local
    // (`var revision = 0` and a Binding over it) that nothing reads, so a bubble shown before its host's icon was cached never shows
    // it. No network: FaviconCache.Image awaits a seeded in-flight task (NoLinkNetwork.Pending, R-TEST-10). The controls check
    // that the fetch ran to its end (RichLink.invalidate dropped the source, MarkdownView.swift:1236-1237) and that a bubble built
    // after the icon arrived does show it. Fails alone, twice, at 9af15a67f; passes with a probe (not committed) that has the revision
    // binding's setter draw the host text view again.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-077 (the favicon of a bubble's bare domain): TimelineRows.swift:119-122; MarkdownView.swift:1113, :1148, :1189-1191, :1225-1238
    public void REVIEW_A_favicon_that_arrives_after_a_bubble_with_a_bare_domain_is_shown_in_front_of_its_link() => headless.OnUiThread(() =>
    {
        const string host = "example.com";
        const string text = "see example.com for it";
        using var noNetwork = new NoLinkNetwork(); // R-TEST-10, ED-256 (10): the host's fetch is the one it names below
        using var h = TimelineRowsPrSupport.Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var icon = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(4, 4), new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
        var pending = noNetwork.Pending(host);
        Avalonia.Controls.Window window = null!;
        UserMessageRow row = null!;
        void Pump()
        {
            for (var pass = 0; pass < 10; pass++)
            {
                h.Main.RunUntilIdle();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window?.UpdateLayout();
            }
        }
        try
        {
            h.Main.OnMain(() =>
            {
                row = new UserMessageRow(TimelineRowsPrSupport.UserEntry(text, Guid.NewGuid()), h.Runtime, canRevert: false);
                window = TimelineRowsPrSupport.Show(row);
            });
            Pump();
            h.Main.OnMain(() =>
            {
                Assert.Single(row.GetVisualDescendants().OfType<LinkParagraphView>());
                Assert.Empty(Favicons(row)); // the icon is still on its way
                Assert.True(PrettyCached(text), "the bubble's text was not built through RichLink.prettyAttributed");
            });
            // The fetch finishes: image(for:) keeps the icon (:932-934) and hands it to the task that awaited it.
            using var stored = new HydraMergeFaviconSeed(host, icon, null);
            pending.SetResult(icon);
            Pump();
            h.Main.OnMain(() =>
            {
                // Control: the link task ran to its end. It drops the source from the pretty cache (RichLink.invalidate) just before it
                // bumps the revision; a bubble drawn again with the icon builds the source anew, so either is the task's end.
                Assert.True(!PrettyCached(text) || Favicons(row).Contains(icon), "control: the link task did not run to its end (RichLink.invalidate was not called)");
                var fresh = new UserMessageRow(TimelineRowsPrSupport.UserEntry(text, Guid.NewGuid()), h.Runtime, canRevert: false);
                var second = TimelineRowsPrSupport.Show(fresh);
                try { Assert.True(Favicons(fresh).Contains(icon), "control: a bubble built after the icon arrived does not draw it"); }
                finally { TimelineRowsPrSupport.Close(second); }
                Assert.True(Favicons(row).Contains(icon), "the bubble on screen when its host's icon arrived never draws it in front of its link"); // the bubble that was on screen when the icon arrived
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => TimelineRowsPrSupport.Close(window)); }
    });

    // PR-CHAT-101 (not among the claimed records; a defect of TimelineRows.cs found in passing). A tool's detail and its output are
    // SwiftUI Texts (TimelineRows.swift:2121-2125, :2150-2155) with no fixedSize: they wrap at the width the row offers, the detail
    // up to .lineLimit(12) lines, the output inside its .frame(maxWidth: .infinity) plate. The port builds both with
    // SelectableText(wraps: false), so a long line runs on in one line past the plate's edge. Fails alone, twice, at 9af15a67f ("one
    // line 3768 points wide in a 400-point column"); passes with a probe (not committed) that builds both with wraps: true.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-101 (a long line of the detail and of the output): TimelineRows.swift:2120-2126, :2149-2155
    public void REVIEW_A_long_line_of_a_tools_detail_and_of_its_output_wraps_at_the_rows_width() => headless.OnUiThread(() =>
    {
        using var h = TimelineRowsPrSupport.Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        Avalonia.Controls.Window window = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                var line = string.Join(" ", Enumerable.Repeat("segment", 80));
                var tool = new ToolDetailView(TimelineRowsPrSupport.Call(ToolCall.Kind.Command, "run") with { Detail = "run " + line, Output = "out " + line });
                window = TimelineRowsPrSupport.Show(new Border { Width = 400, Child = tool, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left });
                var texts = tool.GetVisualDescendants().OfType<SelectableTextBlock>().ToArray();
                Assert.Equal(2, texts.Length);
                foreach (var block in texts)
                {
                    Assert.True(block.TextLayout.TextLines.Count > 1,
                        $"\"{block.Text![..10]}...\": one line {block.TextLayout.Width:0} points wide in a {block.Bounds.Width:0}-point column");
                    Assert.True(block.TextLayout.Width <= 400, $"\"{block.Text![..10]}...\" is {block.TextLayout.Width:0} points wide");
                }
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => TimelineRowsPrSupport.Close(window)); }
    });

    private static Avalonia.Media.IImage?[] Favicons(Control root) => root.GetVisualDescendants().OfType<SelectableTextBlock>()
        .SelectMany(block => block.Inlines ?? [])
        .OfType<Avalonia.Controls.Documents.InlineUIContainer>()
        .Select(container => (container.Child as Image)?.Source)
        .ToArray();

    private static bool PrettyCached(string source)
    {
        var cache = typeof(RichLink).GetField("prettyCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        return (bool)cache.GetType().GetMethod("Contains")!.Invoke(cache, [source])!;
    }
}
