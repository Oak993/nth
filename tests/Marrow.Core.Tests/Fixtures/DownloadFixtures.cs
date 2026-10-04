using System.Net.Http;
using Marrow.Testing;

namespace Marrow.Core.Tests.Fixtures;

// PORT-ONLY (SK-0): small helpers the download-harness tests share. The harness itself is Marrow.Testing.Downloads.
internal static class DownloadFixtures
{
    internal static HttpClient Client(HttpMessageHandler handler) =>
        new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

    internal static async Task<(HttpResponseMessage Response, byte[] Body)> Get(HttpClient client, Uri url, string? range = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (range is not null) request.Headers.TryAddWithoutValidation("Range", range);
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).WaitAsync(LoopbackHttpServer.Guard);
        var body = await response.Content.ReadAsByteArrayAsync().WaitAsync(LoopbackHttpServer.Guard);
        return (response, body);
    }

    /// <summary>Waits (bounded by the hang guard) until the server's trickle has armed its next wait on the manual clock.</summary>
    internal static async Task UntilTimerArmed(ManualTimeProvider clock)
    {
        using var stop = new CancellationTokenSource(LoopbackHttpServer.Guard);
        while (clock.ActiveTimers == 0) await Task.Delay(1, stop.Token);
    }
}
