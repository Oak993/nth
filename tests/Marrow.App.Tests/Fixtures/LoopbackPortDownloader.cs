using Velopack.Sources;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin (UE-1 Windows review): Velopack's own HTTP downloader (HttpClientFileDownloader) kept on the loopback
// server of a test. Velopack's GithubSource reads a repository on another host than github.com at <scheme>://<host>/api/v3/ and drops the
// port, so the address it builds for http://127.0.0.1:<port>/owner/repo is http://127.0.0.1/api/v3/...; this puts the server's port back.
// Any address that is not on 127.0.0.1 is refused before a socket is opened (R-TEST-10: HTTP in a test is a loopback server).
public sealed class LoopbackPortDownloader(int port) : IFileDownloader
{
    private readonly HttpClientFileDownloader inner = new();

    /// <summary>The whole percent values Velopack's downloader reported for file downloads, in order.</summary>
    public List<int> Percents { get; } = [];

    private string Loopback(string url)
    {
        var uri = new Uri(url);
        if (uri.Host != "127.0.0.1") throw new HttpRequestException(HttpRequestError.ConnectionError, "A test only reaches 127.0.0.1, not " + url + ".");
        return uri.IsDefaultPort ? new UriBuilder(uri) { Port = port }.Uri.AbsoluteUri : url;
    }

    public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
        inner.DownloadString(Loopback(url), headers, timeout);

    public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
        inner.DownloadBytes(Loopback(url), headers, timeout);

    public Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers = null, double timeout = 30,
        CancellationToken cancellationToken = default) =>
        inner.DownloadFile(Loopback(url), targetFile, percent => { lock (Percents) Percents.Add(percent); progress(percent); }, headers, timeout, cancellationToken);
}
