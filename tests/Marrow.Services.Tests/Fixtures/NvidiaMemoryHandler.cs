using System.Net;
using Xunit;
namespace Marrow.Services.Tests.Fixtures;
internal sealed class NvidiaMemoryHandler(Dictionary<string, byte[]> bodies) : HttpMessageHandler
{
    private int requests;
    public int Requests => Volatile.Read(ref requests);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref requests);
        Assert.Equal("developer.download.nvidia.com", request.RequestUri!.Host);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bodies[request.RequestUri.AbsolutePath]) });
    }
}
