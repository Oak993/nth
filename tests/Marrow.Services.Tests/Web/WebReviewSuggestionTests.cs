using System.Net;
using System.Net.Http.Headers;
using Marrow.Core.Web;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

[Trait("Decision", "ED-NEW-web-review-12")]
public sealed class WebReviewSuggestionTests
{
    [Fact]
    public async Task The_two_second_deadline_includes_a_stalled_response_body()
    {
        using var body = new HeldSuggestionStream();
        using var handler = new ScriptedHttpHandler { Script = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        } };
        var clock = new ManualTimeProvider();
        using var source = new SearchSuggestionSource(handler) { Clock = clock };
        var call = source.AskAsync(SearchEngine.Bing, "hello", CancellationToken.None);
        try
        {
            Assert.True(body.ReadStarted);
            clock.Advance(TimeSpan.FromSeconds(3));
            Assert.True(body.ReadToken.IsCancellationRequested, "The body reader did not receive its advertised deadline.");
            Assert.Empty(await call);
        }
        finally { body.Release(); }
    }
}
