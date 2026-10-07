using System.Net;
using Marrow.Services.Tests.Fixtures;
using Xunit.Sdk;

namespace Marrow.Services.Tests.Port.Voice.Models;

[Trait("Decision", "ED-351")]
public sealed class NvidiaMemoryHandlerTests
{
    [Fact]
    public async Task Concurrent_requests_are_counted_without_losing_any_completed_request()
    {
        const int workers = 8;
        const int each = 512;
        using var handler = new NvidiaMemoryHandler(new() { ["/count.zip"] = [1, 2, 3] });
        using var client = new HttpClient(handler);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = 0;
        var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
        {
            if (Interlocked.Increment(ref waiting) == workers) entered.SetResult();
            await start.Task;
            for (var i = 0; i < each; i++)
            {
                using var response = await client.GetAsync("https://developer.download.nvidia.com/count.zip");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(new byte[] { 1, 2, 3 }, await response.Content.ReadAsByteArrayAsync());
            }
        })).ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        start.SetResult();
        await Task.WhenAll(tasks);
        Assert.Equal(workers * each, handler.Requests);
    }

    [Fact]
    public async Task One_request_still_fails_the_existing_two_request_expectation()
    {
        using var handler = new NvidiaMemoryHandler(new() { ["/count.zip"] = [1, 2, 3] });
        using var client = new HttpClient(handler);
        using var response = await client.GetAsync("https://developer.download.nvidia.com/count.zip");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, handler.Requests);
        Assert.Throws<EqualException>(() => Assert.Equal(2, handler.Requests));
    }
}
