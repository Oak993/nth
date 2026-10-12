using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: approved DESIGN 5.6, 13.1; ED-NEW-web-w9-1. Banner fixtures, never real servers.
[Trait("Decision", "ED-NEW-web-w9-1")]
public sealed class DevServerDetectorTests
{
    public static IEnumerable<object[]> Banners()
    {
        // Forty banner lines covering every framework named in DESIGN 13.1, both generations and host forms.
        string[] lines = [
            "  ➜  Local:   http://localhost:5173/", "\u001b[32m➜ Local: http://localhost:5174/\u001b[0m",
            "ready - started server on 0.0.0.0:3000, url: http://localhost:3000", "- Local: http://localhost:3001",
            "Listening on http://localhost:3000", "➜ Local: http://localhost:3001/",
            "┃ Local http://localhost:4321/", "Local: http://localhost:4322/",
            "** Angular Live Development Server is listening on localhost:4200 **", "Local: http://localhost:4201/",
            "You can now view app in the browser. Local: http://localhost:3000", "Local: http://localhost:3001/",
            "Remix App Server started at http://localhost:3000", "http://localhost:3002 (http://127.0.0.1:3002)",
            "➜ Local: http://localhost:5173/", "SvelteKit: http://localhost:5174/",
            "│ Local: http://localhost:6006/ │", "Storybook: http://localhost:6007/",
            "Web Server is available at http://localhost:1313/", "Web Server is available at http://127.0.0.1:1313/",
            "Server address: http://127.0.0.1:4000/", "Server address: http://localhost:4001/",
            " * Running on http://127.0.0.1:5000", " * Running on http://127.0.0.1:5001",
            "Starting development server at http://127.0.0.1:8000/", "Starting development server at http://localhost:8001/",
            "* Listening on http://127.0.0.1:3000", "* Listening on http://[::1]:3000",
            "Now listening on: http://localhost:5000", "Now listening on: https://localhost:5001",
            "Available on: http://127.0.0.1:8080", "Available on: http://localhost:8081",
            "Serving HTTP on 0.0.0.0 port 8000 (http://0.0.0.0:8000/) ...", "Serving HTTP on :: port 8001 (http://[::]:8001/) ...",
            "listening on port 3000", "Listening on port 8888", "Now listening on: http://0.0.0.0:8080",
            "Local: https://app.localhost:5173/store", "Local: http://[::1]:4200/", "Local: http://192.168.7.25:8080/"
        ];
        string[] expected = [
            "http://localhost:5173/", "http://localhost:5174/", "http://localhost:3000/", "http://localhost:3001/",
            "http://localhost:3000/", "http://localhost:3001/", "http://localhost:4321/", "http://localhost:4322/",
            "http://localhost:4200/", "http://localhost:4201/", "http://localhost:3000/", "http://localhost:3001/",
            "http://localhost:3000/", "http://localhost:3002/", "http://localhost:5173/", "http://localhost:5174/",
            "http://localhost:6006/", "http://localhost:6007/", "http://localhost:1313/", "http://127.0.0.1:1313/",
            "http://127.0.0.1:4000/", "http://localhost:4001/", "http://127.0.0.1:5000/", "http://127.0.0.1:5001/",
            "http://127.0.0.1:8000/", "http://localhost:8001/", "http://127.0.0.1:3000/", "http://[::1]:3000/",
            "http://localhost:5000/", "https://localhost:5001/", "http://127.0.0.1:8080/", "http://localhost:8081/",
            "http://localhost:8000/", "http://localhost:8001/", "http://localhost:3000/", "http://localhost:8888/",
            "http://localhost:8080/", "https://app.localhost:5173/store", "http://[::1]:4200/", "http://192.168.7.25:8080/"
        ];
        for (var i = 0; i < lines.Length; i++) yield return [lines[i], expected[i]];
    }

    [Theory, MemberData(nameof(Banners))]
    public void Corpus_finds_a_dev_server(string line, string expected)
    {
        var found = DevServerDetector.Detect(line, ["192.168.7.25"]);
        Assert.NotEmpty(found);
        Assert.Equal(expected, found[0]);
        Assert.All(found, url => Assert.True(Uri.TryCreate(url, UriKind.Absolute, out _)));
    }

    [Theory]
    [InlineData("GET https://example.com:3000/", 0)]
    [InlineData("redirect http://192.168.7.99:8000", 0)]
    [InlineData("http://localhost.evil.com:3000/", 0)]
    [InlineData("http://user:secret@localhost:3000/", 0)]
    [InlineData("http://localhost:0", 0)]
    [InlineData("https://example.com/help listening on port 3000", 0)]
    [InlineData("Now listening on: http://0.0.0.0:8080", 1)]
    public void Non_local_urls_do_not_become_previews(string line, int count) => Assert.Equal(count, DevServerDetector.Detect(line).Count);

    [Fact]
    public void Wildcard_is_localhost_and_first_session_match_wins()
    {
        var detector = new DevServerDetector();
        detector.Feed("Local: http://0.0.0.0:5173/");
        detector.Feed("Local: http://localhost:5174/");
        detector.Feed("Local: http://localhost:5173/");
        Assert.Equal("http://localhost:5173/", detector.Primary);
        Assert.Equal(2, detector.Servers.Count);
        Assert.Equal("https://dev.test/", new DevServerDetector("https://dev.test/").Primary);
    }

    [Fact]
    public void Chunked_ansi_urls_and_crlf_are_reassembled_once_and_buffers_are_bounded()
    {
        var detector = new DevServerDetector();
        detector.Feed("\u001b[3"); detector.Feed("2mLocal: http://local"); detector.Feed("host:5173/\u001b[0m\r\n");
        Assert.Equal("http://localhost:5173/", detector.Primary);
        detector.Feed(new string('x', 100_000));
        Assert.InRange(detector.BufferedCharacters, 0, 16_384);
    }
}
