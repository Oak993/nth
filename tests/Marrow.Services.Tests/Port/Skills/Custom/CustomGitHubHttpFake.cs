using System.Net;
using System.Net.Http;
using System.Text;

namespace Marrow.Services.Tests.Port.Skills.Custom;

internal sealed class CustomGitHubHttpFake : HttpMessageHandler
{
    internal readonly Dictionary<string, byte[]> Responses = [];
    internal readonly List<string> Requests = [];
    internal void Json(string url, string body) => Responses.Add(url, Encoding.UTF8.GetBytes(body));
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var url = request.RequestUri!.AbsoluteUri;
        Requests.Add(url);
        if (!Responses.TryGetValue(url, out var bytes)) throw new InvalidOperationException("Unexpected fixture HTTP request: " + url);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(bytes) });
    }
}
