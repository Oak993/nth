using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W8a (E:/Droppy/work/design/web-surfaces/DESIGN.md 13.1 WebPolicyTests: "scheme allow-list per
// surface, ... dev-host detection, external-protocol block list"; 5.2, 8.1, 8.2, 8.3, 2.3). The rules Visus applies before a
// page loads, before an app opens, and what each kind of tab may keep.
[Trait("Decision", "ED-NEW-web-w8a-1")]
public sealed class WebPolicyTests
{
    [Theory]
    [InlineData("https://example.com/", true)]
    [InlineData("http://localhost:5173/", true)]
    [InlineData("about:blank", true)]
    [InlineData("ABOUT:BLANK", true)]
    [InlineData("about:blank#top", true)]
    [InlineData("about:settings", false)]
    [InlineData("file:///C:/Users/dana/notes.html", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("vbscript:msgbox(1)", false)]
    [InlineData("data:text/html,<b>hi</b>", false)]
    [InlineData("blob:https://example.com/0d1f", false)]
    [InlineData("edge://settings", false)]
    [InlineData("chrome://flags", false)]
    [InlineData("view-source:https://example.com/", false)]
    [InlineData("mailto:dana@example.com", false)]
    [InlineData("https://", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Top_level_pages_are_http_https_and_the_blank_page_only(string? url, bool allowed) =>
        Assert.Equal(allowed, WebPolicy.IsTopLevelAllowed(url));

    [Theory]
    [InlineData("https://example.com/frame", true)]
    [InlineData("about:srcdoc", true)]
    [InlineData("data:image/png;base64,AAAA", true)]
    [InlineData("blob:https://example.com/0d1f", true)]
    [InlineData("file:///C:/secret.txt", false)]
    [InlineData("javascript:void(0)", false)]
    [InlineData("edge://settings", false)]
    [InlineData("view-source:https://example.com/", false)]
    public void Frames_may_also_load_srcdoc_data_and_blob(string url, bool allowed) =>
        Assert.Equal(allowed, WebPolicy.IsFrameAllowed(url));

    [Theory]
    [InlineData("https://example.com/", TopLevelVerdict.Allow)]
    [InlineData("about:blank", TopLevelVerdict.Allow)]
    [InlineData("file:///C:/x", TopLevelVerdict.Cancel)]
    [InlineData("javascript:alert(1)", TopLevelVerdict.Cancel)]
    [InlineData("data:text/html,x", TopLevelVerdict.Cancel)]
    [InlineData("edge://settings", TopLevelVerdict.Cancel)]
    [InlineData("ms-msdt:/id PCWDiagnostic", TopLevelVerdict.Cancel)]
    [InlineData("search-ms:query=x", TopLevelVerdict.Cancel)]
    [InlineData("http:///nohost", TopLevelVerdict.Cancel)]
    [InlineData("not a url", TopLevelVerdict.Cancel)]
    [InlineData("mailto:dana@example.com", TopLevelVerdict.External)]
    [InlineData("zoommtg://zoom.us/join?confno=1", TopLevelVerdict.External)]
    public void A_navigation_is_allowed_cancelled_or_left_to_the_app_prompt(string url, TopLevelVerdict verdict) =>
        Assert.Equal(verdict, WebPolicy.CheckTopLevel(url));

    [Theory]
    [InlineData("HTTPS://x", "https")]
    [InlineData("ms-msdt:/id", "ms-msdt")]
    [InlineData("web+app:x", "web+app")]
    [InlineData("1abc:x", null)]
    [InlineData(":x", null)]
    [InlineData("no scheme", null)]
    [InlineData("a b:c", null)]
    public void The_scheme_is_read_strictly(string url, string? scheme) => Assert.Equal(scheme, WebPolicy.SchemeOf(url));

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST.", true)]
    [InlineData("app.localhost", true)]
    [InlineData("printer.local", true)]
    [InlineData("site.test", true)]
    [InlineData("api.internal", true)]
    [InlineData("nas.lan", true)]
    [InlineData("router.home.arpa", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.4.5.6", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("192.168.1.20", true)]
    [InlineData("169.254.10.1", true)]
    [InlineData("::1", true)]
    [InlineData("[::1]", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("::ffff:192.168.0.1", true)]
    [InlineData("172.15.0.1", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("2001:4860::8888", false)]
    [InlineData("example.com", false)]
    [InlineData("localhost.example.com", false)]
    [InlineData("mylocal", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Dev_hosts_are_loopback_private_lan_and_the_reserved_local_names(string? host, bool dev) =>
        Assert.Equal(dev, WebPolicy.IsDevHost(host));

    [Theory]
    [InlineData("https://example.com/a", "example.com")]
    [InlineData("http://localhost:5173/", "localhost:5173")]
    [InlineData("https://example.com:443/", "example.com")]
    [InlineData("https://example.com:8443/", "example.com:8443")]
    [InlineData("https://bücher.example/", "xn--bcher-kva.example")]
    [InlineData("http://[::1]:3000/", "[::1]:3000")]
    [InlineData("about:blank", "New tab")]
    public void The_source_is_the_ascii_host_with_a_non_default_port(string url, string source) =>
        Assert.Equal(source, WebPolicy.SourceName(url));

    [Fact]
    public void A_lookalike_unicode_host_shows_in_its_ascii_form()
    {
        // 8.2: a Cyrillic "а" in "аpple.com" shows as punycode, never as apple.com.
        var source = WebPolicy.SourceName("https://аpple.com/");
        Assert.StartsWith("xn--", source, StringComparison.Ordinal);
        Assert.EndsWith(".com", source, StringComparison.Ordinal);
        Assert.NotEqual("apple.com", source);
        Assert.All(source, c => Assert.True(c < 128));
    }

    [Theory]
    [InlineData("https://Example.com/path?q=1", "https://example.com")]
    [InlineData("http://localhost:5173/x", "http://localhost:5173")]
    [InlineData("mailto:x@y", null)]
    [InlineData("about:blank", null)]
    public void The_origin_is_scheme_host_and_port(string url, string? origin) => Assert.Equal(origin, WebPolicy.OriginOf(url));

    [Theory]
    [InlineData(VisusTabKind.Browse, "https://example.com/", SiteChipKind.Secure, false, "")]
    [InlineData(VisusTabKind.Browse, "http://example.com/", SiteChipKind.NotSecure, true, "Not secure")]
    [InlineData(VisusTabKind.Browse, "about:blank", SiteChipKind.None, false, "")]
    [InlineData(VisusTabKind.Browse, "http://localhost:5173/", SiteChipKind.Preview, true, "Preview")]
    [InlineData(VisusTabKind.Preview, "https://app.localhost/", SiteChipKind.Preview, false, "Preview")]
    [InlineData(VisusTabKind.Private, "https://example.com/", SiteChipKind.Private, false, "Private")]
    [InlineData(VisusTabKind.Private, "http://example.com/", SiteChipKind.Private, true, "Private · Not secure")]
    [InlineData(VisusTabKind.Private, "about:blank", SiteChipKind.Private, false, "Private")]
    [InlineData(VisusTabKind.Agent, "https://example.com/", SiteChipKind.Agent, false, "Agent")]
    [InlineData(VisusTabKind.Agent, "http://example.com/", SiteChipKind.Agent, true, "Agent · Not secure")]
    public void The_site_chip_names_the_tab_kind_and_a_plain_http_page(VisusTabKind kind, string url, SiteChipKind chip, bool notSecure, string label)
    {
        var shown = WebPolicy.ChipFor(kind, url);
        Assert.Equal(chip, shown.Kind);
        Assert.Equal(notSecure, shown.NotSecure);
        Assert.Equal(label, shown.Label);
        Assert.False(string.IsNullOrEmpty(shown.Spoken));
    }

    [Fact]
    public void A_plain_http_dev_server_is_a_preview_not_a_warning()
    {
        // 5.6: dev servers are plain http on localhost; the Preview chip says what it is and does not cry "Not secure".
        var chip = WebPolicy.ChipFor(VisusTabKind.Preview, "http://localhost:3000/");
        Assert.Equal(SiteChipKind.Preview, chip.Kind);
        Assert.Equal("Preview", chip.Label);
    }
}
