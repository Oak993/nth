namespace Marrow.Services.Tests.Fixtures;

// SK-3 test fixture: counts the requests that reach the transport, to prove "refused before any connection" without trusting the downloader.
public sealed class CountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private int sent;

    public int Sent => Volatile.Read(ref sent);

    public List<string> Urls { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref sent);
        lock (Urls) Urls.Add(request.RequestUri!.ToString());
        return base.SendAsync(request, cancellationToken);
    }
}
