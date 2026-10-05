using System.Net;
namespace Marrow.Services.Tests.Fixtures;
public sealed class OfferByteHandler(IReadOnlyDictionary<string, byte[]> files) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Method != HttpMethod.Get || request.RequestUri?.Host != "fixtures.invalid" || !files.TryGetValue(request.RequestUri.AbsolutePath[1..], out var data))
            throw new InvalidOperationException("Unexpected fixture HTTP request.");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
    }
}
