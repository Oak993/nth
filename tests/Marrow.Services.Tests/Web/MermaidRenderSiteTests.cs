using System.Text;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN.md 7.2 and 6.5 (web surfaces W16c; ED-NEW-web-w16c-1): the hidden renderer's site serves one page and one library file
// and refuses everything else, so the page can neither fetch nor beacon; its policy allows no connection; and it carries nth's
// own runner next to the vendored Mermaid and no bridge.
[Trait("Decision", "ED-NEW-web-w16c-1")]
public sealed class MermaidRenderSiteTests
{
    private static MermaidRenderSite Site() => new(ArtifactRuntimeRig.Libraries);

    private static WebResourceResponse Ask(MermaidRenderSite site, string url, string method = "GET", WebResourceKind kind = WebResourceKind.Document) =>
        site.Answer(new WebResourceRequest(url, method, kind)) ?? throw new InvalidOperationException(url + " was let through to the network.");

    [Fact]
    public void The_site_is_a_widget_shaped_invalid_host_that_the_sandbox_hardening_recognises()
    {
        Assert.Equal("w-nth-diagrams.invalid", MermaidRenderSite.Host);
        Assert.Equal("https://w-nth-diagrams.invalid", MermaidRenderSite.Origin);
        Assert.Equal(SandboxSurface.Widget, SandboxSites.SurfaceOf(MermaidRenderSite.Host));
        Assert.Matches("^https://[amw]-[a-z0-9-]+\\.invalid$", MermaidRenderSite.Origin);
    }

    [Fact]
    public void The_page_loads_the_vendored_mermaid_then_nths_runner_and_nothing_else()
    {
        var site = Site();
        var response = Ask(site, MermaidRenderSite.Address);
        Assert.Equal(200, response.StatusCode);
        var page = Encoding.UTF8.GetString(response.Body);
        Assert.Contains("https://lib-nth.invalid/mermaid/mermaid.min.js", page, StringComparison.Ordinal);
        Assert.Contains("window.nthRenderDiagram", page, StringComparison.Ordinal);
        Assert.Contains("securityLevel: 'strict'", page, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, "<script src="));
        Assert.DoesNotContain("importmap", page, StringComparison.Ordinal);
        Assert.DoesNotContain("window.nth.", page, StringComparison.Ordinal); // no bridge
        Assert.DoesNotContain("postMessage", page, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", page, StringComparison.Ordinal);
        Assert.DoesNotContain("XMLHttpRequest", page, StringComparison.Ordinal);
        Assert.DoesNotContain("eval(", page, StringComparison.Ordinal);
        Assert.DoesNotContain("new Function", page, StringComparison.Ordinal);
        Assert.Equal(200, Ask(site, MermaidRenderSite.Origin + "/index.html").StatusCode);
        Assert.Empty(Ask(site, MermaidRenderSite.Address, "HEAD").Body);
    }

    [Fact]
    public void The_policy_allows_no_connection_the_headers_isolate_the_page_and_nothing_is_cached()
    {
        var response = Ask(Site(), MermaidRenderSite.Address);
        var csp = response.Header("Content-Security-Policy")!;
        Assert.Equal(CspBuilder.ArtifactDefault, csp);
        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("connect-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("form-action 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("worker-src 'none'", csp, StringComparison.Ordinal);
        Assert.Equal("same-origin", response.Header("Cross-Origin-Opener-Policy"));
        Assert.Equal("same-origin", response.Header("Cross-Origin-Resource-Policy"));
        Assert.Equal("nosniff", response.Header("X-Content-Type-Options"));
        Assert.Equal("no-store", response.Header("Cache-Control"));
        Assert.Contains("geolocation=()", response.Header("Permissions-Policy"), StringComparison.Ordinal);
        Assert.StartsWith("text/html", response.Header("Content-Type"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_library_host_gives_the_mermaid_script_and_no_other_file()
    {
        var site = Site();
        var script = Ask(site, "https://lib-nth.invalid/mermaid/mermaid.min.js", kind: WebResourceKind.Script);
        Assert.Equal(200, script.StatusCode);
        Assert.True(script.Body.Length > 1_000_000);
        Assert.StartsWith("text/javascript", script.Header("Content-Type"), StringComparison.Ordinal);
        Assert.Equal("cross-origin", script.Header("Cross-Origin-Resource-Policy"));
        Assert.Equal(403, Ask(site, "https://lib-nth.invalid/react/react.production.min.js").StatusCode);
        Assert.Equal(403, Ask(site, "https://lib-nth.invalid/nth/loader.js").StatusCode);
        Assert.Equal(403, Ask(site, "https://lib-nth.invalid/mermaid/LICENSE").StatusCode);
        Assert.Equal(403, Ask(site, "https://lib-nth.invalid/manifest.json").StatusCode);
        Assert.Equal(403, Ask(site, "https://lib-nth.invalid/../Assets/lib/manifest.json").StatusCode);
        Assert.Equal(405, Ask(site, "https://lib-nth.invalid/mermaid/mermaid.min.js", "POST").StatusCode);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com/collect?d=secret")]
    [InlineData("http://w-nth-diagrams.invalid/")]
    [InlineData("https://w-nth-diagrams.invalid:8443/")]
    [InlineData("https://a-0123456789-notes.invalid/")]
    [InlineData("https://w-other.invalid/")]
    [InlineData("https://m-server-01234567.invalid/")]
    [InlineData("https://evil.lib-nth.invalid/mermaid/mermaid.min.js")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://localhost:3000/")]
    [InlineData("wss://example.com/socket")]
    [InlineData("ftp://example.com/")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("not an address")]
    public void Every_other_address_is_refused_and_recorded_with_nothing_of_its_query(string url)
    {
        var site = Site();
        var response = Ask(site, url, kind: WebResourceKind.Fetch);
        Assert.Equal(403, response.StatusCode);
        Assert.Empty(response.Header("Content-Security-Policy") ?? "");
        var note = Assert.Single(site.Refused);
        Assert.DoesNotContain("secret", note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pages_own_data_is_left_to_the_policy_and_other_paths_of_the_site_are_not_found()
    {
        var site = Site();
        Assert.Null(site.Answer(new WebResourceRequest("data:image/png;base64,AAAA", "GET", WebResourceKind.Image)));
        Assert.Null(site.Answer(new WebResourceRequest("blob:https://w-nth-diagrams.invalid/1234", "GET", WebResourceKind.Other)));
        Assert.Equal(404, Ask(site, MermaidRenderSite.Origin + "/other.js").StatusCode);
        Assert.Equal(404, Ask(site, MermaidRenderSite.Origin + "/diagram-runner.js").StatusCode);
        Assert.Equal(405, Ask(site, MermaidRenderSite.Address, "POST").StatusCode);
        Assert.Empty(site.Refused);
    }

    [Fact]
    public void The_view_the_engine_opens_has_the_site_for_its_handler_and_the_network_stays_closed()
    {
        using var rig = new DiagramRig();
        var task = rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None);
        Assert.True(task.IsCompletedSuccessfully);
        var fake = rig.Fake!;
        Assert.Equal(200, fake.Request(MermaidRenderSite.Address)!.StatusCode);
        Assert.Equal(403, fake.Request("https://example.com/")!.StatusCode);
        Assert.Equal(403, fake.Request("https://lib-nth.invalid/react/react.production.min.js")!.StatusCode);
        Assert.NotNull(fake.Request("http://example.com/")); // answered by the site (refused), never null: nothing goes to the network
    }
}
