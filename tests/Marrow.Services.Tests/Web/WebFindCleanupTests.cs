using System.Text.Json;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

[Trait("Decision", "ED-NEW-web-w8c-2")]
public sealed class WebFindCleanupTests
{
    [Fact]
    public async Task Cancellation_after_issued_page_work_clears_that_world_before_returning()
    {
        using var page = (FakeWebView)new FakeWebEngine().CreateView(new WebProfiles(@"C:\nth-test\find", "en-US").For(WebSurfaceKind.VisusTab));
        using var cancellation = new CancellationTokenSource();
        var cleared = false;
        page.CdpHandler = (method, json, _) =>
        {
            if (method == "Page.getFrameTree") return Task.FromResult("{\"frameTree\":{\"frame\":{\"id\":\"root\"}}}");
            if (method == "Page.createIsolatedWorld") return Task.FromResult("{\"executionContextId\":17}");
            using var p = JsonDocument.Parse(json);
            if (p.RootElement.GetProperty("expression").GetString()!.Contains("delete globalThis.__nthFind", StringComparison.Ordinal)) cleared = true;
            else cancellation.Cancel();
            return Task.FromResult("{\"result\":{\"value\":{\"count\":1,\"index\":0}}}");
        };
        var find = new WebFindFallback(page);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => find.StartAsync("price", false, cancellation.Token));
        Assert.True(cleared);
    }

    [Fact]
    public async Task A_stale_frame_does_not_prevent_other_frames_from_being_cleared()
    {
        using var page = (FakeWebView)new FakeWebEngine().CreateView(new WebProfiles(@"C:\nth-test\find", "en-US").For(WebSurfaceKind.VisusTab));
        var cleanup = false;
        var cleared = new List<int>();
        page.CdpHandler = (method, json, _) =>
        {
            using var p = JsonDocument.Parse(json);
            if (method == "Page.getFrameTree") return Task.FromResult("{\"frameTree\":{\"frame\":{\"id\":\"17\"},\"childFrames\":[{\"frame\":{\"id\":\"18\"}},{\"frame\":{\"id\":\"19\"}}]}}");
            if (method == "Page.createIsolatedWorld") return Task.FromResult("{\"executionContextId\":" + p.RootElement.GetProperty("frameId").GetString() + "}");
            var context = p.RootElement.GetProperty("contextId").GetInt32();
            if (cleanup) { cleared.Add(context); if (context == 18) return Task.FromResult("{\"exceptionDetails\":{\"text\":\"removed frame\"}}"); }
            return Task.FromResult("{\"result\":{\"value\":{\"count\":1,\"index\":0}}}");
        };
        var find = new WebFindFallback(page);
        Assert.Equal(new WebFindResult(3, 0), await find.StartAsync("price", false, CancellationToken.None));
        cleanup = true;
        await Assert.ThrowsAnyAsync<Exception>(() => find.StopAsync(CancellationToken.None));
        Assert.Equal(new[] { 17, 18, 19 }, cleared);
        // Keep the failed cleanup record, so a retry can finish rather than silently forgetting it.
        cleared.Clear();
        await Assert.ThrowsAnyAsync<Exception>(() => find.StopAsync(CancellationToken.None));
        Assert.Equal(new[] { 18 }, cleared);
    }

    [Fact]
    public async Task A_frame_whose_search_failed_is_still_cleaned_up()
    {
        using var page = (FakeWebView)new FakeWebEngine().CreateView(new WebProfiles(@"C:\nth-test\find", "en-US").For(WebSurfaceKind.VisusTab));
        var cleanup = false;
        var cleared = 0;
        page.CdpHandler = (method, _, _) => Task.FromResult(method switch
        {
            "Page.getFrameTree" => "{\"frameTree\":{\"frame\":{\"id\":\"root\"}}}",
            "Page.createIsolatedWorld" => "{\"executionContextId\":17}",
            _ when !cleanup => "{\"exceptionDetails\":{\"text\":\"search failed after creating marks\"}}",
            _ => Clear(),
        });
        string Clear() { cleared++; return "{\"result\":{\"value\":{\"count\":0,\"index\":-1}}}"; }
        var find = new WebFindFallback(page);
        await Assert.ThrowsAsync<InvalidOperationException>(() => find.StartAsync("price", false, CancellationToken.None));
        cleanup = true;
        await find.StopAsync(CancellationToken.None);
        Assert.Equal(1, cleared);
    }
}
