using System.Net.Http;
using System.Text.Json;

namespace Marrow.Services.Port.Skills.Custom;

/// <summary>Anonymous public GitHub source only. Redirects and credentials are disabled; every file/listing request is pinned to the resolved full commit.</summary>
public sealed class PublicGitHubSource : ICustomGitHubSource
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(2) };
    private readonly HttpClient client;
    public PublicGitHubSource() : this(Client) { }
    public PublicGitHubSource(HttpClient client) => this.client = client;
    public async Task<CustomGitHubArchive> Fetch(string repository, CancellationToken cancel)
    {
        var location = CustomSourcePaths.GitHubLocation(repository);
        repository = location.Repository;
        var metadata = await Read("https://api.github.com/repos/" + repository + "/commits/HEAD", 1024 * 1024, cancel).ConfigureAwait(false);
        using var json = JsonDocument.Parse(metadata);
        var commit = json.RootElement.GetProperty("sha").GetString() ?? throw new InvalidDataException("GitHub returned no commit.");
        if (commit.Length != 40 || commit.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("GitHub returned an invalid commit.");
        if (location.SubPath.Length != 0) return await Subtree(repository, location.SubPath, commit, cancel).ConfigureAwait(false);
        var url = "https://codeload.github.com/" + repository + "/zip/" + commit;
        return new(commit, url, await Read(url, CustomSourcePaths.MaxBytes, cancel).ConfigureAwait(false));
    }
    private async Task<CustomGitHubArchive> Subtree(string repository, string subPath, string commit, CancellationToken cancel)
    {
        var directories = new Queue<string>();
        directories.Enqueue(subPath);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { subPath };
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0, metadataTotal = 0;
        while (directories.TryDequeue(out var directory))
        {
            var body = await Read(CustomSourcePaths.ContentsUrl(repository, directory, commit), Math.Min(1024 * 1024, CustomSourcePaths.MaxBytes - metadataTotal), cancel).ConfigureAwait(false);
            metadataTotal += body.LongLength;
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Choose a GitHub sub-folder, not a file.");
            // GitHub's contents API caps listings at 1000 entries. A saturated listing cannot prove completeness.
            if (json.RootElement.GetArrayLength() >= 1000) throw new InvalidDataException("GitHub sub-folder listing may be incomplete.");
            foreach (var entry in json.RootElement.EnumerateArray())
            {
                var path = entry.GetProperty("path").GetString() ?? throw new InvalidDataException("GitHub returned no path.");
                CustomSourcePaths.Require(path);
                if (!path.StartsWith(directory + "/", StringComparison.Ordinal) || path[(directory.Length + 1)..].Contains('/', StringComparison.Ordinal) || !seen.Add(path) || seen.Count > CustomSourcePaths.MaxFiles)
                    throw new InvalidDataException("GitHub returned an unsafe, duplicate or excessive subtree path.");
                var type = entry.GetProperty("type").GetString();
                if (type is not ("file" or "dir") || entry.TryGetProperty("submodule_git_url", out var module) && module.ValueKind != JsonValueKind.Null)
                    throw new InvalidDataException("GitHub sub-folders cannot contain links or submodules.");
                if (type == "file" && (!entry.TryGetProperty("size", out var size) || !size.TryGetInt64(out var length) || length < 0 || length > CustomSourcePaths.MaxBytes - total))
                    throw new InvalidDataException("GitHub returned an invalid file size.");
            }
            foreach (var entry in json.RootElement.EnumerateArray())
            {
                var path = entry.GetProperty("path").GetString()!;
                if (entry.GetProperty("type").GetString() == "dir") { directories.Enqueue(path); continue; }
                var expected = entry.GetProperty("size").GetInt64();
                var bytes = await Read("https://raw.githubusercontent.com/" + repository + "/" + commit + "/" + CustomSourcePaths.EncodeGitHubPath(path), Math.Min(expected, CustomSourcePaths.MaxBytes - total), cancel).ConfigureAwait(false);
                if (bytes.LongLength != expected) throw new InvalidDataException("GitHub file does not match its pinned listing size.");
                total += bytes.LongLength;
                files.Add(path[(subPath.Length + 1)..], bytes);
            }
        }
        return new(commit, CustomSourcePaths.ContentsUrl(repository, subPath, commit), [], subPath, CustomSourcePaths.Validate(files));
    }
    private async Task<byte[]> Read(string url, long limit, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("nth-Custom-Sources/1.0");
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
