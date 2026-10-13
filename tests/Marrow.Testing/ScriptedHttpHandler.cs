using System.Net;
using System.Text;

namespace Marrow.Testing;

/// <summary>
/// A transport for tests that must never reach a network: every request is recorded (address, method and headers as text, since the message is
/// disposed once it is answered) and answered by the script. With no script it answers 404.
/// </summary>
public sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly object gate = new();
    private readonly List<ScriptedRequest> requests = [];

    /// <summary>Answers a request; may throw to model a transport failure. Runs once per request, in the order they arrive.</summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Script { get; set; }

    /// <summary>Every request seen, oldest first.</summary>
    public IReadOnlyList<ScriptedRequest> Requests
    {
        get
        {
            lock (gate) return [.. requests];
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(pair => pair.Key, pair => string.Join(",", pair.Value), StringComparer.OrdinalIgnoreCase);
        lock (gate) requests.Add(new ScriptedRequest(request.RequestUri!, request.Method.Method, headers));
        if (Script is null) return new HttpResponseMessage(HttpStatusCode.NotFound);
        return await Script(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A 200 answer with a body and a content type.</summary>
    public static HttpResponseMessage Ok(string contentType, string body) => Ok(contentType, Encoding.UTF8.GetBytes(body));

    /// <summary>A 200 answer with a body and a content type.</summary>
    public static HttpResponseMessage Ok(string contentType, byte[] body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return response;
    }
}

/// <summary>One request a <see cref="ScriptedHttpHandler"/> saw.</summary>
public sealed record ScriptedRequest(Uri Url, string Method, IReadOnlyDictionary<string, string> Headers);
