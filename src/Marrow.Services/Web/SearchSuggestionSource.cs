using System.Net;
using System.Net.Http.Headers;
using Marrow.Core.Port;
using Marrow.Core.Web;

// PORT-ONLY: web surfaces W10, the engine's own suggestions for the address field (DESIGN.md 5.2, 9 "Show search suggestions (off)"; ED-NEW-web-w10-7).
// No Swift counterpart. One small request to the chosen search engine while the reader types, only when Settings has the switch on and
// only for a search phrase (SearchSuggestionRules). The request is https to a fixed host per engine with no cookies, no credentials, no
// redirects and no referrer, it gives up after a short time, and it reads at most 16 KB. Whatever goes wrong (no network, a slow or odd
// answer) is no suggestions. The log names the failure and never the text that was typed.

namespace Marrow.Services.Web;

public sealed class SearchSuggestionSource : IDisposable
{
    /// <summary>How long a request may take before it is given up.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static readonly Logger Log = new(Logger.Subsystem, "web");

    private readonly HttpClient client;

    /// <param name="handler">The transport; tests hand a fake. The default has no cookies, no redirects and no proxy credentials.</param>
    public SearchSuggestionSource(HttpMessageHandler? handler = null)
    {
        client = new HttpClient(handler ?? new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false, UseProxy = true, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }, disposeHandler: true)
        {
            Timeout = Timeout,
            MaxResponseContentBufferSize = SearchSuggestionRules.MaxResponseBytes,
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("nth", "1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/javascript", 0.5));
    }

    /// <summary>The suggested searches for what was typed; empty when the text may not be sent or anything failed.</summary>
    public async Task<IReadOnlyList<string>> AskAsync(SearchEngine engine, string typed, CancellationToken cancellationToken, Func<string, bool>? isRegisteredScheme = null)
    {
        if (SearchSuggestionRules.RequestUrl(engine, typed, isRegisteredScheme) is not { } url) return [];
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = null;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) return [];
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!(type.Contains("json", StringComparison.OrdinalIgnoreCase) || type.Contains("javascript", StringComparison.OrdinalIgnoreCase))) return [];
            if (response.Content.Headers.ContentLength is { } length && length > SearchSuggestionRules.MaxResponseBytes) return [];
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[SearchSuggestionRules.MaxResponseBytes + 1];
            var read = 0;
            while (read < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
                if (count == 0) break;
                read += count;
            }
            if (read > SearchSuggestionRules.MaxResponseBytes) return [];
            return SearchSuggestionRules.Parse(System.Text.Encoding.UTF8.GetString(buffer, 0, read), typed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
        {
            Log.Error($"web: search suggestions unavailable ({error.GetType().Name})");
            return [];
        }
    }

    public void Dispose() => client.Dispose();
}
