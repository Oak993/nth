using System.Net;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W10, "Show search suggestions" (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.2, 9; ED-NEW-web-w10-7). One small https request to the chosen engine, only for a search
// phrase, with nothing the engine could use to know who asked, bounded in time and size, and any failure is no suggestions. No network: a scripted transport.
[Trait("Decision", "ED-NEW-web-w10-7")]
public sealed class SearchSuggestionSourceTests
{
    private const string Sentinel = "sentinel-secret-phrase-7f3a";

    private static (SearchSuggestionSource Source, ScriptedHttpHandler Handler) Make(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? script = null)
    {
        var handler = new ScriptedHttpHandler { Script = script ?? ((_, _) => Task.FromResult(ScriptedHttpHandler.Ok("application/json", "[\"hel\",[\"hello\",\"help\"]]"))) };
        return (new SearchSuggestionSource(handler), handler);
    }

    [Fact]
    public async Task An_answer_is_read_and_the_one_request_is_https_to_the_engines_own_host_with_no_cookies_or_referrer()
    {
        var (source, handler) = Make();
        var found = await source.AskAsync(SearchEngine.DuckDuckGo, "hel", CancellationToken.None);
        Assert.Equal(["hello", "help"], found);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://duckduckgo.com/ac/?type=list&q=hel", request.Url.AbsoluteUri);
        Assert.Equal("GET", request.Method);
        Assert.False(request.Headers.ContainsKey("Cookie"));
        Assert.False(request.Headers.ContainsKey("Referer"));
        Assert.False(request.Headers.ContainsKey("Authorization"));
        Assert.StartsWith("nth/", request.Headers["User-Agent"], StringComparison.Ordinal);
        Assert.Contains("application/json", request.Headers["Accept"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("user@example.test")]
    [InlineData("C:\\fake\\notes.txt")]
    [InlineData("a")]
    [InlineData("")]
    public async Task Text_that_is_not_a_search_phrase_is_never_sent(string typed)
    {
        var (source, handler) = Make();
        Assert.Empty(await source.AskAsync(SearchEngine.Bing, typed, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Any_answer_but_200_is_no_suggestions_and_a_redirect_is_not_followed(HttpStatusCode status)
    {
        var (source, handler) = Make((_, _) =>
        {
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.Redirect) response.Headers.Location = new Uri("https://evil.example.test/steal");
            return Task.FromResult(response);
        });
        Assert.Empty(await source.AskAsync(SearchEngine.Google, "hello", CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/octet-stream")]
    [InlineData("image/png")]
    public async Task An_answer_that_is_not_json_is_ignored(string contentType)
    {
        var (source, _) = Make((_, _) => Task.FromResult(ScriptedHttpHandler.Ok(contentType, "[\"x\",[\"y\"]]")));
        Assert.Empty(await source.AskAsync(SearchEngine.Brave, "hello", CancellationToken.None));
    }

    [Fact]
    public async Task An_answer_over_16_KB_is_ignored_whether_or_not_it_says_how_long_it_is()
    {
        var items = string.Join(",", Enumerable.Range(0, 3000).Select(n => "\"suggestion number " + n + "\""));
        var big = "[\"q\",[" + items + "]]";
        Assert.True(big.Length > SearchSuggestionRules.MaxResponseBytes);
        var (sized, _) = Make((_, _) => Task.FromResult(ScriptedHttpHandler.Ok("application/json", big)));
        Assert.Empty(await sized.AskAsync(SearchEngine.Bing, "hello", CancellationToken.None));
        var (streamed, _) = Make((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnsizedStream(System.Text.Encoding.UTF8.GetBytes(big))) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        });
        Assert.Empty(await streamed.AskAsync(SearchEngine.Bing, "hello", CancellationToken.None));
    }

    [Fact]
    public async Task A_failing_transport_is_no_suggestions_and_the_log_never_holds_what_was_typed()
    {
        var lines = new List<string>();
        using var sink = Logger.Sink.Override((message, _) => lines.Add(message));
        var (source, handler) = Make((_, _) => throw new HttpRequestException("no route to " + Sentinel));
        Assert.Empty(await source.AskAsync(SearchEngine.DuckDuckGo, Sentinel, CancellationToken.None));
        Assert.Single(handler.Requests);
        var line = Assert.Single(lines);
        Assert.Equal("web: search suggestions unavailable (HttpRequestException)", line);
        Assert.DoesNotContain(Sentinel, line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancelled_ask_throws_to_its_caller_so_a_newer_key_can_replace_it()
    {
        using var cancel = new CancellationTokenSource();
        var (source, _) = Make(async (_, token) =>
        {
            await cancel.CancelAsync();
            token.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.AskAsync(SearchEngine.Bing, "hello", cancel.Token));
    }

    [Fact]
    public async Task A_transport_that_times_out_on_its_own_is_no_suggestions()
    {
        var (source, _) = Make((_, _) => throw new TaskCanceledException("the request timed out"));
        Assert.Empty(await source.AskAsync(SearchEngine.Bing, "hello", CancellationToken.None));
    }

    [Fact]
    public void The_request_gives_up_after_two_seconds() => Assert.Equal(TimeSpan.FromSeconds(2), SearchSuggestionSource.Timeout);
}
