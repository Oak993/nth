using System.Net;
using Xunit;
namespace Marrow.Services.Tests.Fixtures;
internal sealed class NvidiaHeldHandler(byte[] body) : HttpMessageHandler
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Hold { get; set; } = true;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal("developer.download.nvidia.com", request.RequestUri!.Host);
        Entered.TrySetResult();
        if (Hold) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    }
}
