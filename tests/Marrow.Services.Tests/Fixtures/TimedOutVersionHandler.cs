namespace Marrow.Services.Tests.Fixtures;

internal sealed class TimedOutVersionHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) =>
        Task.FromException<HttpResponseMessage>(new TaskCanceledException("Version listing timed out."));
}
