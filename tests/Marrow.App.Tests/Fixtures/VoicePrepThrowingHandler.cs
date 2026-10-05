namespace Marrow.App.Tests.Fixtures;

/// <summary>VT-15: an HTTP transport that fails every request and counts them, so a test can prove whether the network was reached (it never is: it throws).</summary>
public sealed class VoicePrepThrowingHandler : HttpMessageHandler
{
    private int calls;

    public int Calls => Volatile.Read(ref calls);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        throw new HttpRequestException("the scripted transport refuses every request");
    }
}
