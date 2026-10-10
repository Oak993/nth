// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-engine; PLAN unit U1). Fetches the model points file from the release feed on
// GitHub (PLAN lead decision: raw.githubusercontent.com/Oak993/marrow-releases/main/ignis/model-points.json; publishing
// it needs the owner's word, so until then the feed answers 404 and the bundled points stay). The fetcher talks to GitHub
// only: any other host is refused before a request is built, redirects are not followed, and it never sends anything to
// a model provider. The wire is URLSession.Transport (the one HTTP seam), so a test answers it in process. The price
// catalogue (IgnisPriceCatalogue, ED-NEW-ignis-data) fetches through the same rule with its own size cap, time limit and
// an If-None-Match check, so an unchanged catalogue costs one 304 answer; both ask for a gzip answer.
using System.Globalization;
using System.Net;
using Marrow.Core.Port;
using Marrow.Core.Port.SelfUpdate;

namespace Marrow.Services.Port.Ignis;

/// <summary>What one fetch found: the body, "not published" (404), or an error.</summary>
public sealed record IgnisFeedResult(string? Body, bool NotPublished, string? Error)
{
    /// <summary>The server answered 304 to an If-None-Match check: the copy held is current.</summary>
    public bool NotModified { get; init; }

    /// <summary>The answer's ETag, when it named one.</summary>
    public string? ETag { get; init; }
}

public static class IgnisPointsFeed
{
    /// <summary>The feed address (the owner's release repository, OD-0100).</summary>
    public const string Url = "https://raw.githubusercontent.com/" + MarrowReleaseFeed.Repository + "/main/ignis/model-points.json";

    /// <summary>The largest body read (the file is a few KB).</summary>
    public const int MaximumBytes = 1 << 20;

    /// <summary>How long one fetch may take in all.</summary>
    public static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(30);

    private static readonly string[] GitHubHosts = ["raw.githubusercontent.com", "github.com", "objects.githubusercontent.com"];

    /// <summary>
    /// Whether the fetcher may ask <paramref name="address"/>: https on a GitHub host, or a loopback address (a test's
    /// stand-in feed; ENGINEERING DECISION). Every model provider's host is therefore refused.
    /// </summary>
    public static bool IsAllowed(Uri address)
    {
        if (!address.IsAbsoluteUri) return false;
        if (address.IsLoopback) return address.Scheme is "http" or "https";
        return address.Scheme == Uri.UriSchemeHttps && GitHubHosts.Any(host => string.Equals(address.Host, host, StringComparison.OrdinalIgnoreCase));
    }

    public static Task<IgnisFeedResult> Fetch(Uri address, CancellationToken cancellationToken) =>
        Fetch(address, MaximumBytes, Timeout, null, "The model points feed must be on GitHub.", cancellationToken);

    /// <summary>
    /// One GET on a GitHub address: at most <paramref name="maximumBytes"/> read within <paramref name="timeout"/>; with
    /// <paramref name="ifNoneMatch"/>, a 304 answer reads as <see cref="IgnisFeedResult.NotModified"/>. Never follows a redirect.
    /// </summary>
    internal static async Task<IgnisFeedResult> Fetch(Uri address, int maximumBytes, TimeSpan timeout, string? ifNoneMatch, string refusal, CancellationToken cancellationToken)
    {
        if (!IsAllowed(address)) return new IgnisFeedResult(null, false, refusal);
        using var client = new HttpClient(URLSession.Transport.Current, disposeHandler: false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.TryAddWithoutValidation("User-Agent", "nth-ignis-points");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (ifNoneMatch is { Length: > 0 }) request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            var etag = response.Headers.ETag?.ToString();
            if (response.StatusCode == HttpStatusCode.NotFound) return new IgnisFeedResult(null, true, null);
            if (response.StatusCode == HttpStatusCode.NotModified && ifNoneMatch is { Length: > 0 })
                return new IgnisFeedResult(null, false, null) { NotModified = true, ETag = etag ?? ifNoneMatch };
            if (response.StatusCode != HttpStatusCode.OK)
                return new IgnisFeedResult(null, false, "The feed answered " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ".");
            if (response.Content.Headers.ContentLength is { } length && length > maximumBytes) return new IgnisFeedResult(null, false, "The feed's file is too large.");
            await using var wire = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
            // The transport does not decompress (Http.cs DefaultHandler); a gzip answer is unpacked here, the cap counting unpacked bytes.
            await using var stream = response.Content.Headers.ContentEncoding.Contains("gzip", StringComparer.OrdinalIgnoreCase)
                ? new System.IO.Compression.GZipStream(wire, System.IO.Compression.CompressionMode.Decompress) : wire;
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, limit.Token).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > maximumBytes) return new IgnisFeedResult(null, false, "The feed's file is too large.");
            }
            return new IgnisFeedResult(System.Text.Encoding.UTF8.GetString(buffer.ToArray()), false, null) { ETag = etag };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new IgnisFeedResult(null, false, "The feed did not answer in time.");
        }
        catch (HttpRequestException error)
        {
            return new IgnisFeedResult(null, false, error.Message);
        }
        catch (InvalidDataException)
        {
            return new IgnisFeedResult(null, false, "The feed's file could not be unpacked.");
        }
    }
}
