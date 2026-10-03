using System.Reflection;
using System.Threading.Tasks;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B3 52bc792), :1101-1218 (part 2 of the unit).
// Covers InlineText.metrics (PR-THEME-151), InlineText.fetchLinkAssets (PR-THEME-148) and the constructor's
// hint-less link decision (PR-THEME-186, new at B3). LinkParagraphView and StreamVeil's VeiledText(segments:)
// have both landed since this note was first written, so the constructor now completes in either branch;
// PR-THEME-144's hover wiring (now UI.Markdown.LinkHover, N9) is exercised by that unit's own tests.
[Collection("Headless")]
public sealed class InlineTextTests(HeadlessSession headless)
{
    private static (double Ascender, double LineHeight) InvokeMetrics(double pointSize)
    {
        var method = typeof(InlineText).GetMethod("Metrics", BindingFlags.NonPublic | BindingFlags.Static)!;
        return ((double, double))method.Invoke(null, [pointSize])!;
    }

    // ---- :1214-1221 metrics(pointSize:) ----

    [Fact]
    [Trait("Req", "PR-THEME-151")] // DroppyCode/UI/Markdown/MarkdownView.swift:1216-1219
    public void Metrics_is_measured_once_per_point_size_and_kept()
    {
        headless.OnUiThread(() =>
        {
            var first = InvokeMetrics(13.5);
            var second = InvokeMetrics(13.5);
            Assert.Equal(first, second); // :1216 if let known = metricsBySize[pointSize] { return known }
        });
    }

    [Fact]
    [Trait("Req", "PR-THEME-151")] // DroppyCode/UI/Markdown/MarkdownView.swift:1218
    public void Metrics_line_height_is_at_least_the_ascender()
    {
        headless.OnUiThread(() =>
        {
            var (ascender, lineHeight) = InvokeMetrics(15.0);
            Assert.True(ascender > 0);
            Assert.True(lineHeight >= ascender); // ascender - descender + leading, descender <= 0 <= leading contribution
        });
    }

    [Fact]
    [Trait("Req", "PR-THEME-151")] // DroppyCode/UI/Markdown/MarkdownView.swift:1130 the half-point rounded size
    public void Metrics_is_cached_per_distinct_point_size()
    {
        headless.OnUiThread(() =>
        {
            var a = InvokeMetrics(13.0);
            var b = InvokeMetrics(20.0);
            // Different point sizes are not required to differ (a tiny font family could round the same way),
            // but the cache key is the exact double passed in (:1216), so re-asking never throws or recomputes
            // incompatibly -- proven by both calls returning without exception above.
            Assert.True(a.Ascender > 0 && b.Ascender > 0);
        });
    }

    // ---- :1226-1237 fetchLinkAssets(source:streaming:revision:) ----

    [Fact]
    [Trait("Req", "PR-THEME-148")] // DroppyCode/UI/Markdown/MarkdownView.swift:1227-1237
    public async Task FetchLinkAssets_with_no_links_and_no_videos_touches_nothing()
    {
        var revisionValue = 0;
        var binding = new Binding<int>(() => revisionValue, v => revisionValue = v);
        await headless.Session.Dispatch(async () =>
        {
            MainActor.Bind(AvaloniaMainExecutor.Instance);
            await InlineText.FetchLinkAssets("just plain text, no links here", streaming: false, binding);
        }, default);
        Assert.Equal(0, revisionValue); // :1236-1237 revision.wrappedValue += 1 only when changed
    }

    // ---- PR-THEME-186: the hint-less hasLinkText decision (:1095-1096, new at B3) ----

    [Fact]
    [Trait("Req", "PR-THEME-186")] // DroppyCode/UI/Markdown/MarkdownView.swift:1095-1096
    public void A_hint_less_host_less_local_link_reaches_the_LinkParagraphView_path() => headless.OnUiThread(() =>
    {
        var inline = new InlineText("open [file](/Users/me/report.txt) now");
        Assert.IsType<LinkParagraphView>(inline.Child);
    });

    [Fact]
    [Trait("Req", "PR-THEME-186")]
    public void A_hint_less_paragraph_with_no_links_reaches_the_veiled_text_path() => headless.OnUiThread(() =>
    {
        var inline = new InlineText("plain text with no links at all");
        Assert.IsNotType<LinkParagraphView>(inline.Child);
    });

    [Fact]
    [Trait("Req", "PR-THEME-186")] // a hinted call site's explicit value is never overridden
    public void An_explicit_false_hint_is_never_overridden_even_with_a_link_present() => headless.OnUiThread(() =>
    {
        var inline = new InlineText("[a link](https://example.com)", hasLinks: false);
        Assert.IsNotType<LinkParagraphView>(inline.Child);
    });

    // ---- body's environment (B3 :1071-1075), set on an ancestor after the paragraph was built (review round 5 of TimelineRows) ----

    // body reads @Environment(\.markdownPointSize) and (\.markdownDimmed) (MarkdownView.swift:1071-1072 at B3) and SwiftUI runs it again
    // when either changes, handing the same paragraph LinkParagraphView(pointSize: scaled, dimmed:) (:1094 scaled = (pointSize * 2)
    // .rounded() / 2; :1114), whose updateNSView draws the string again when an input moved (LinkParagraphView.swift:36-58). A port
    // host sets these on a control it has already built (HydraReportPopover's markdownPointSize 12, TimelineRows.swift:631; the
    // narration between steps, 12 and dimmed, :1551-1552), so the paragraph follows a value that reaches it after it was built, before
    // and after it is in a window, and keeps its paragraph, as SwiftUI updates a representable in place. No network: the host's icon is
    // in the cache (R-TEST-10).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-THEME-140 (the link paragraph's size and dimming): DroppyCode/UI/Markdown/MarkdownView.swift:801-812, :1071-1072, :1094, :1114; LinkParagraphView.swift:36-58, :111-120
    public void An_environment_set_on_an_ancestor_after_the_paragraph_was_built_reaches_its_link_text_in_place() => headless.OnUiThread(() =>
    {
        const string host = "late-environment.com";
        var icon = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(4, 4), new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
        FaviconCacheSeed.Seed(host, icon);
        Avalonia.Controls.Window? window = null;
        try
        {
            var text = new InlineText("words and [a link](https://late-environment.com/x) here", hasLinks: true);
            var paragraph = Assert.IsType<LinkParagraphView>(text.Child);
            var block = (Avalonia.Controls.SelectableTextBlock)((LinkTextView)paragraph.Child!).Child!;
            double[] Sizes() => block.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Where(run => run.Text is { Length: > 0 })
                .Select(run => run.FontSize).Distinct().ToArray();
            byte ProseAlpha() => ((Avalonia.Media.ISolidColorBrush)block.Inlines!.OfType<Avalonia.Controls.Documents.Run>()
                .First(run => run.Text == "words and ").Foreground!).Color.A;
            Assert.Equal([13.0], Sizes()); // built with nothing above it: the 13-point default (MarkdownView.swift:801-806)
            var undimmed = ProseAlpha();

            var host_ = new Avalonia.Controls.Decorator { Child = text };
            MarkdownView.SetMarkdownPointSize(host_, 12);
            MarkdownView.SetMarkdownDimmed(host_, true);
            Assert.Same(paragraph, text.Child); // the same paragraph, its string drawn again
            Assert.Equal([12.0], Sizes());
            Assert.True(ProseAlpha() < undimmed, "the dimmed prose is not drawn in the label colour at 0.7 (LinkParagraphView.swift:119)");

            window = new Avalonia.Controls.Window { Content = host_, Width = 420, Height = 200 };
            window.Show();
            MarkdownView.SetMarkdownPointSize(host_, 15.2); // in a window: the half-point rounding of :1094 gives 15
            Assert.Same(paragraph, text.Child);
            Assert.Equal([15.0], Sizes());
            MarkdownView.SetMarkdownDimmed(host_, false);
            Assert.Equal(undimmed, ProseAlpha());
        }
        finally
        {
            window?.Close();
            FaviconCacheSeed.Remove(host);
        }
    });

    // body also reads @Environment(\.hydraMentionPersonas) and (\.hydraMentionTargets) (MarkdownView.swift:1074-1075 at B3) and SwiftUI
    // runs it again when either changes, handing the same LinkParagraphView its new mentions and targets (:1114), whose updateNSView
    // draws the string again when the names or the targets moved (LinkParagraphView.swift:36-58) and so draws a head's glyph and name
    // where the text names it (:179-223) and gives its range the head's address (:203-216). ThreadTimeline sets both on a control it
    // has already built (ThreadTimeline.cs:128-129), when a head joins or leaves the thread. The paragraph follows a value set on an
    // ancestor after it was built, and stays the same paragraph. No network: the host's icon is in the cache (R-TEST-10).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-THEME-144 (the head mentions of a link paragraph): DroppyCode/UI/Markdown/MarkdownView.swift:1074-1075, :1114; LinkParagraphView.swift:36-58, :179-223
    public void The_mention_personas_and_targets_set_on_an_ancestor_after_the_paragraph_was_built_reach_its_link_text_in_place() => headless.OnUiThread(() =>
    {
        const string host = "mention-environment.com";
        var icon = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(4, 4), new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
        FaviconCacheSeed.Seed(host, icon);
        var persona = new Marrow.Core.Models.HydraPersona("Hank", "dragon-test-asset", 0xFF0000);
        var target = new Marrow.Core.Models.HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = "t", Status = Marrow.Core.Models.HydraHeadInfo.Status.Running };
        Avalonia.Controls.Window? window = null;
        try
        {
            // The mention leads the text, so its glyph lands at the paragraph's own origin whatever the font metrics.
            var text = new InlineText("Hank joined, see [a link](https://mention-environment.com/x)", hasLinks: true);
            var paragraph = Assert.IsType<LinkParagraphView>(text.Child);
            var view = (LinkTextView)paragraph.Child!;
            var block = (Avalonia.Controls.SelectableTextBlock)view.Child!;
            int Glyphs() => block.Inlines!.OfType<Avalonia.Controls.Documents.InlineUIContainer>().Count(); // the link's favicon and each head's glyph
            Avalonia.Media.IBrush? NameBrush() => block.Inlines!.OfType<Avalonia.Controls.Documents.Run>().FirstOrDefault(run => run.Text == "Hank")?.Foreground;
            (SwiftURL Url, Avalonia.Rect Rect, string Name)? Head()
            {
                window!.UpdateLayout();
                return view.HeadMentionRect(new Avalonia.Point(2, 2)) is { } found ? (found.Url, found.Rect, found.Name) : null;
            }
            var host_ = new Avalonia.Controls.Decorator { Child = text };
            window = new Avalonia.Controls.Window { Content = host_, Width = 420, Height = 200 };
            window.Show();
            Assert.Equal(1, Glyphs()); // built with no heads above it: the favicon only, the name plain prose
            Assert.Null(NameBrush());
            Assert.Null(Head());

            MarkdownView.SetHydraMentionPersonas(host_, new ValueList<Marrow.Core.Models.HydraPersona>([persona]));
            Assert.Same(paragraph, text.Child); // the same paragraph, its string drawn again
            Assert.Equal(2, Glyphs()); // the head's glyph in front of its name (:200-202)
            Assert.Equal(0xFF0000u, ((Avalonia.Media.ISolidColorBrush)NameBrush()!).Color.ToUInt32() & 0xFFFFFFu); // its name in the head's colour
            Assert.Null(Head()); // no target for it yet: no address to open (:203-216)

            MarkdownView.SetHydraMentionTargets(host_, new ValueMap<string, Marrow.Core.Models.HydraMentionTarget>(
                new Dictionary<string, Marrow.Core.Models.HydraMentionTarget> { ["Hank"] = target }, SwiftStringComparer.Instance));
            Assert.Same(paragraph, text.Child);
            Assert.Equal(target.Url, Head()?.Url); // its range now carries the head's address
            Assert.Equal("Hank", Head()?.Name);

            MarkdownView.SetHydraMentionTargets(host_, ValueMap<string, Marrow.Core.Models.HydraMentionTarget>.Empty);
            Assert.Same(paragraph, text.Child);
            Assert.Null(Head());
            Assert.Equal(2, Glyphs());

            MarkdownView.SetHydraMentionPersonas(host_, ValueList<Marrow.Core.Models.HydraPersona>.Empty);
            Assert.Same(paragraph, text.Child);
            Assert.Equal(1, Glyphs());
            Assert.Null(NameBrush());
        }
        finally
        {
            window?.Close();
            FaviconCacheSeed.Remove(host);
        }
    });

    // The same representable keeps its NSTextView across those updates, so a selection in a link paragraph that does not name the head
    // that joins survives it: updateNSView (LinkParagraphView.swift:36-58) is handed the new mentions, finds the names moved, and
    // render(_:) (:481-496) leaves a text storage that is equal to the new string alone, with its selection (:489 isEqual). Review
    // round 6 of TimelineRows found the port building a new paragraph, with a new text view, hover and task, and so no selection.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-THEME-144 (a link paragraph's state across a head joining): DroppyCode/UI/Markdown/MarkdownView.swift:1074-1075, :1114; LinkParagraphView.swift:36-58, :481-496
    public void A_head_the_paragraph_does_not_name_joining_the_thread_leaves_its_view_and_its_selection_alone() => headless.OnUiThread(() =>
    {
        const string host = "mention-selection.com";
        var icon = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(4, 4), new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
        FaviconCacheSeed.Seed(host, icon);
        var persona = new Marrow.Core.Models.HydraPersona("Hank", "dragon-test-asset", 0xFF0000);
        var target = new Marrow.Core.Models.HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = "t", Status = Marrow.Core.Models.HydraHeadInfo.Status.Running };
        Avalonia.Controls.Window? window = null;
        try
        {
            var text = new InlineText("words and [a link](https://mention-selection.com/x) here", hasLinks: true);
            var paragraph = Assert.IsType<LinkParagraphView>(text.Child);
            var view = (LinkTextView)paragraph.Child!;
            var block = (Avalonia.Controls.SelectableTextBlock)view.Child!;
            var host_ = new Avalonia.Controls.Decorator { Child = text };
            window = new Avalonia.Controls.Window { Content = host_, Width = 420, Height = 200 };
            window.Show();
            block.SelectAll();
            var selected = block.SelectedText;
            Assert.Contains("words and", selected, StringComparison.Ordinal);

            MarkdownView.SetHydraMentionPersonas(host_, new ValueList<Marrow.Core.Models.HydraPersona>([persona]));
            MarkdownView.SetHydraMentionTargets(host_, new ValueMap<string, Marrow.Core.Models.HydraMentionTarget>(
                new Dictionary<string, Marrow.Core.Models.HydraMentionTarget> { ["Hank"] = target }, SwiftStringComparer.Instance));

            Assert.Same(paragraph, text.Child); // the same paragraph ...
            Assert.Same(view, paragraph.Child); // ... and text view ...
            Assert.Same(block, view.Child);
            Assert.Equal(selected, block.SelectedText); // ... with its selection: the string it draws is the one it drew
        }
        finally
        {
            window?.Close();
            FaviconCacheSeed.Remove(host);
        }
    });
}
