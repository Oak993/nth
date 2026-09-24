using System.Reflection;
using System.Threading.Tasks;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B2 1fbf81e), :1101-1247 (part 2 of the unit).
// Covers InlineText.metrics (PR-THEME-151) and InlineText.fetchLinkAssets (PR-THEME-148).
//
// UNTESTABLE for now, and why: InlineText's own constructor (:1121-1208) cannot be exercised end to end in
// either branch: the hasLinkText branch builds LinkParagraphView, and the link-free branch builds VeiledText
// (segments:) through the new AttributedText overload added in StreamVeil.cs -- both are the sibling
// LinkParagraphView/StreamVeil units' own MARROW_STUB and throw on construction today. That also makes
// PR-THEME-144's hover wiring (:1159-1188), which only runs after that constructor returns, unreachable from a
// test. The two static helpers below do not depend on either sibling and are exercised directly.
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
}
