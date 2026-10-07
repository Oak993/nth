using Xunit;
namespace Marrow.App.Tests.Fixtures;
internal sealed class CudaHeldRequest : HttpMessageHandler
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Assert.Equal("example.invalid", request.RequestUri!.Host);
        Entered.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        throw new InvalidOperationException("The fake request must be cancelled");
    }
}
