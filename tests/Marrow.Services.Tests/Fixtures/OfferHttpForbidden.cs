namespace Marrow.Services.Tests.Fixtures;
public sealed class OfferHttpForbidden : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new InvalidOperationException("The offer test forbids HTTP.");
}
