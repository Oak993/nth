using Marrow.Services.Web;
using System.Text.Json;

namespace Marrow.Services.Tests.Web;

[Trait("Decision", "ED-NEW-web-w8c-1")]
public sealed class WebFindTests
{
    private static dynamic Fallback(IWebEngineView page)
    {
        var type = typeof(IWebEngineView).Assembly.GetType("Marrow.Services.Web.WebFindFallback");
        Assert.NotNull(type);
        return Activator.CreateInstance(type, page)!;
    }

    [Fact]
    public void Find_reports_result_changes_after_next_and_previous()
    {
        Assert.NotNull(typeof(IWebEngineView).GetEvent("FindResultChanged"));
    }

    [Fact]
    public void Older_runtimes_have_a_cdp_find_fallback()
    {
        Assert.NotNull(typeof(IWebEngineView).Assembly.GetType("Marrow.Services.Web.WebFindFallback"));
    }

    [Fact]
    public async Task Fallback_uses_an_isolated_world_and_returns_the_page_count()
    {
        var engine = new FakeWebEngine();
        using var page = (FakeWebView)engine.CreateView(new WebProfiles(@"C:\nth-test\find", "en-US").For(WebSurfaceKind.VisusTab));
        page.CdpHandler = (method, _, _) => Task.FromResult(method switch
        {
            "Page.getFrameTree" => "{\"frameTree\":{\"frame\":{\"id\":\"root\"}}}",
            "Page.createIsolatedWorld" => "{\"executionContextId\":17}",
            "Runtime.evaluate" => "{\"result\":{\"value\":{\"count\":3,\"index\":0}}}",
            _ => throw new InvalidOperationException(method),
        });
        var type = typeof(IWebEngineView).Assembly.GetType("Marrow.Services.Web.WebFindFallback");
        Assert.NotNull(type);
        dynamic find = Activator.CreateInstance(type, page)!;
        WebFindResult result = await find.StartAsync("price", true, CancellationToken.None);
        Assert.Equal(new WebFindResult(3, 0), result);
        Assert.Equal("Page.getFrameTree", page.CdpCalls[0].Method);
        var world = Assert.Single(page.CdpCalls, c => c.Method == "Page.createIsolatedWorld");
        using var options = JsonDocument.Parse(world.Params);
        Assert.Equal("root", options.RootElement.GetProperty("frameId").GetString());
        Assert.False(options.RootElement.TryGetProperty("grantUniveralAccess", out _));
        var evaluations = page.CdpCalls.Where(c => c.Method == "Runtime.evaluate").ToList();
        Assert.NotEmpty(evaluations);
        Assert.All(evaluations, call =>
        {
            using var parameters = JsonDocument.Parse(call.Params);
            Assert.Equal(17, parameters.RootElement.GetProperty("contextId").GetInt32());
            Assert.True(parameters.RootElement.GetProperty("returnByValue").GetBoolean());
        });
    }

    [Fact]
    public async Task Fallback_aggregates_frames_and_wraps_the_active_match()
    {
        var engine = new FakeWebEngine();
        using var page = (FakeWebView)engine.CreateView(new WebProfiles(@"C:\nth-test\find", "en-US").For(WebSurfaceKind.VisusTab));
        page.CdpHandler = (method, json, _) =>
        {
            using var parameters = JsonDocument.Parse(json);
            return Task.FromResult(method switch
            {
                "Page.getFrameTree" => "{\"frameTree\":{\"frame\":{\"id\":\"root\"},\"childFrames\":[{\"frame\":{\"id\":\"child\"}}]}}",
                "Page.createIsolatedWorld" => parameters.RootElement.GetProperty("frameId").GetString() == "root" ? "{\"executionContextId\":17}" : "{\"executionContextId\":18}",
                "Runtime.evaluate" => parameters.RootElement.GetProperty("contextId").GetInt32() == 17 ? "{\"result\":{\"value\":{\"count\":2,\"index\":0}}}" : "{\"result\":{\"value\":{\"count\":1,\"index\":0}}}",
                _ => throw new InvalidOperationException(method),
            });
        };
        dynamic find = Fallback(page);
        WebFindResult result = await find.StartAsync("price", false, CancellationToken.None);
        Assert.Equal(new WebFindResult(3, 0), result);
        result = await find.MoveAsync(true, CancellationToken.None);
        Assert.Equal(new WebFindResult(3, 2), result);
        result = await find.MoveAsync(false, CancellationToken.None);
        Assert.Equal(new WebFindResult(3, 0), result);
        await find.StopAsync(CancellationToken.None);
        Assert.Equal(2, page.CdpCalls.Count(call => call.Method == "Page.createIsolatedWorld"));
        var scripts = page.CdpCalls.Where(call => call.Method == "Runtime.evaluate").Select(call => JsonDocument.Parse(call.Params).RootElement.GetProperty("expression").GetString()).ToArray();
        Assert.Contains(scripts, script => script!.Contains("CSS.highlights.delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fallback_script_failure_is_not_reported_as_no_matches()
    {
        var engine = new FakeWebEngine();
        using var page = (FakeWebView)engine.CreateView(new WebProfiles(@"C:\nth-test\find", "en-US").For(WebSurfaceKind.VisusTab));
        page.CdpHandler = (method, _, _) => Task.FromResult(method switch
        {
            "Page.getFrameTree" => "{\"frameTree\":{\"frame\":{\"id\":\"root\"}}}",
            "Page.createIsolatedWorld" => "{\"executionContextId\":17}",
            _ => "{\"exceptionDetails\":{\"text\":\"fixture failure\"}}",
        });
        dynamic find = Fallback(page);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await find.StartAsync("price", false, CancellationToken.None));
    }

    [Fact]
    public async Task Fallback_cancellation_prevents_page_work()
    {
        var engine = new FakeWebEngine();
        using var page = (FakeWebView)engine.CreateView(new WebProfiles(@"C:\nth-test\find", "en-US").For(WebSurfaceKind.VisusTab));
        dynamic find = Fallback(page);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await find.StartAsync("price", false, cancellation.Token));
        Assert.Empty(page.CdpCalls);
    }
}
