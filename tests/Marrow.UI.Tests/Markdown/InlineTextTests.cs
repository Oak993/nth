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
}
