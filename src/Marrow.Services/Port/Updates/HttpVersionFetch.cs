// PORT-ONLY: first-release feature unit UE-2. The one place "Check now" touches the network: a GET of a public listing (BUILD-PLAN.md 2.4). Listed in the port's network inventory (ED-NEW-f-ue-2).
namespace Marrow.Services.Port.Updates;

public static class HttpVersionFetch
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10), DefaultRequestHeaders = { { "User-Agent", "marrow-update-check" } } };

    /// <summary>An https GET with a ten second limit; any other scheme is refused. Tests never call this (they give the listing their own fetch).</summary>
    public static async Task<string> Get(Uri uri, CancellationToken cancellation)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Only https listings are read.");
        using var response = await Client.GetAsync(uri, cancellation);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellation);
    }
}
