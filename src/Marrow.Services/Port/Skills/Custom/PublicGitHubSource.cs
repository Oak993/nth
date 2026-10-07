using System.Net.Http;
using System.Text.Json;

namespace Marrow.Services.Port.Skills.Custom;

/// <summary>Anonymous public GitHub source only. Redirects and credentials are disabled; archive requests are pinned to the resolved full commit.</summary>
public sealed class PublicGitHubSource : ICustomGitHubSource
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(2) };
    private readonly HttpClient client;
    public PublicGitHubSource() : this(Client) { }
    public PublicGitHubSource(HttpClient client) => this.client = client;
    public async Task<CustomGitHubArchive> Fetch(string repository, CancellationToken cancel)
    {
        repository = CustomSourcePaths.Repository(repository);
        var metadata = await Read("https://api.github.com/repos/" + repository + "/commits/HEAD", 1024 * 1024, cancel).ConfigureAwait(false);
        using var json = JsonDocument.Parse(metadata);
        var commit = json.RootElement.GetProperty("sha").GetString() ?? throw new InvalidDataException("GitHub returned no commit.");
        if (commit.Length != 40 || commit.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("GitHub returned an invalid commit.");
        var url = "https://codeload.github.com/" + repository + "/zip/" + commit;
        return new(commit, url, await Read(url, CustomSourcePaths.MaxBytes, cancel).ConfigureAwait(false));
    }
    private async Task<byte[]> Read(string url, long limit, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Marrow-Custom-Sources/1.0");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        // Also reject a redirect followed by an injected transport; production's handler never follows one.
        if (response.RequestMessage?.RequestUri?.AbsoluteUri != url) throw new InvalidDataException("Custom-source redirect refused.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("GitHub source exceeds the byte limit.");
        using var stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > limit) throw new InvalidDataException("GitHub source exceeds the byte limit.");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }
}
