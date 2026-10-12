using System.Net;
using System.Text;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>PORT-ONLY (AS-10): an HTTP handler that answers from a function and remembers every request, so a test sees what a page watch sent and what it never sent.</summary>
public sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public static HttpResponseMessage Html(string body, HttpStatusCode status = HttpStatusCode.OK, string type = "text/html") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, type) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(answer(request));
    }
}
