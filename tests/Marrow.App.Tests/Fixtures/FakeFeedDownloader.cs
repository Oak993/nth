using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Velopack.Sources;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin (UE-1): Velopack's IFileDownloader seam answered in this process, so a GithubSource reads a fake
// "marrow-releases" repository and no request leaves the test (R-TEST-10): every address it is not given a body for is refused, as a
// closed network would. It records each address asked for, in order.
public sealed class FakeFeedDownloader : IFileDownloader
{
    private readonly Dictionary<string, byte[]> bodies = new(StringComparer.Ordinal);

    public List<string> Requested { get; } = [];

    /// <summary>Whole percent values reported for file downloads, in order.</summary>
    public List<int> Percents { get; } = [];

    /// <summary>Serves a body at an address.</summary>
    public FakeFeedDownloader Serve(string url, byte[] body)
    {
        bodies[url] = body;
        return this;
    }

    /// <summary>
    /// A GitHub release list with one release holding the package and its feed file, as `vpk upload github` leaves them (releases.win.json
    /// next to the full nupkg), served at the address GithubSource asks for; <paramref name="packageBytes"/> may differ from what the feed's
    /// checksum says (a corrupt download).
    /// </summary>
    public FakeFeedDownloader ServeRelease(string apiListUrl, string version, byte[] packageBytes, string? notes = "Notes for the release.", byte[]? checksumOf = null)
    {
        var file = $"NthApp-{version}-full.nupkg";
        var hashed = checksumOf ?? packageBytes;
        var feed = JsonSerializer.Serialize(new
        {
            Assets = new[]
            {
                new
                {
                    PackageId = "NthApp", Version = version, Type = "Full", FileName = file,
                    SHA1 = Convert.ToHexString(SHA1.HashData(hashed)), SHA256 = Convert.ToHexString(SHA256.HashData(hashed)),
                    Size = hashed.Length, NotesMarkdown = notes, NotesHTML = notes is null ? null : "<p>" + notes + "</p>",
                },
            },
        });
        var release = new[]
        {
            new
            {
                name = "Marrow " + version, prerelease = false, published_at = "2026-10-04T10:00:00Z",
                assets = new[]
                {
                    new { name = "releases.win.json", url = "https://fake.invalid/assets/1", browser_download_url = "https://fake.invalid/dl/releases.win.json" },
                    // Velopack reads releases.<channel>.json and its default channel is the system's own, so a test run on another system than
                    // Windows asks for its own name: the same feed under both names serves the Windows run and any other
                    new { name = "releases.linux.json", url = "https://fake.invalid/assets/3", browser_download_url = "https://fake.invalid/dl/releases.linux.json" },
                    new { name = file, url = "https://fake.invalid/assets/2", browser_download_url = "https://fake.invalid/dl/" + file },
                },
            },
        };
        Serve(apiListUrl, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(release)));
        Serve("https://fake.invalid/dl/releases.win.json", Encoding.UTF8.GetBytes(feed));
        Serve("https://fake.invalid/dl/releases.linux.json", Encoding.UTF8.GetBytes(feed));
        Serve("https://fake.invalid/dl/" + file, packageBytes);
        return this;
    }

    private byte[] Body(string url)
    {
        Requested.Add(url);
        // GithubSource adds paging and draft filters to the list address: match by prefix of what a test served
        var key = bodies.Keys.FirstOrDefault(k => url == k || url.StartsWith(k + "?", StringComparison.Ordinal) || url.StartsWith(k + "&", StringComparison.Ordinal));
        return key is null
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "The fake network has no answer for " + url + ".")
            : bodies[key];
    }

    public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
        Task.FromResult(Encoding.UTF8.GetString(Body(url)));

    public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
        Task.FromResult(Body(url));

    public async Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers = null, double timeout = 30,
        CancellationToken cancellationToken = default)
    {
        var body = Body(url);
        await using var file = File.Create(targetFile);
        const int Slices = 4;
        for (var i = 0; i < Slices; i++)
        {
            var from = body.Length * i / Slices;
            var to = body.Length * (i + 1) / Slices;
            await file.WriteAsync(body.AsMemory(from, to - from), cancellationToken);
            var percent = (i + 1) * 100 / Slices;
            Percents.Add(percent);
            progress(percent);
        }
    }
}
