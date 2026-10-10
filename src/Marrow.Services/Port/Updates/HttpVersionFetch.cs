// PORT-ONLY: first-release feature unit UE-2. The one place "Check now" touches the network: a GET of a public listing (BUILD-PLAN.md 2.4). Listed in the port's network inventory (ED-308).
namespace Marrow.Services.Port.Updates;

public static class HttpVersionFetch
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10), DefaultRequestHeaders = { { "User-Agent", "nth-update-check" } } };

    /// <summary>An https GET with a ten second limit; any other scheme is refused. Tests supply an offline HttpClient handler.</summary>
    public static Task<string> Get(Uri uri, CancellationToken cancellation) => Get(uri, cancellation, Client);

    public static async Task<string> Get(Uri uri, CancellationToken cancellation, HttpClient client)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Only https listings are read.");
        try
        {
            using var response = await client.GetAsync(uri, cancellation);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellation);
        }
        // HttpClient uses TaskCanceledException for its own timeout as well as caller cancellation.
        // Only the latter cancels the operation; the orchestrator already displays ordinary check errors.
        catch (OperationCanceledException error) when (!cancellation.IsCancellationRequested)
        {
            throw new TimeoutException(error.Message, error);
        }
    }
}
