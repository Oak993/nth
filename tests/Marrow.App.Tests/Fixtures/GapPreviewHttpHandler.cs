namespace Marrow.App.Tests.Fixtures;

// GAP-2: refuse and count every destination, including loopback, before any socket can open.
internal sealed class GapPreviewHttpHandler : HttpMessageHandler
{
    internal int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        throw new InvalidOperationException("Preview touched HTTP");
    }
}
