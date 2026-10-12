using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5 CSP, 7.6 MCP Apps). The artifact policy is the design's, word for word; a
// network grant opens connect-src and img-src to exactly the granted sites; an MCP App's policy never grows past what its
// server declared and the user allowed.
[Trait("Decision", "ED-NEW-web-w13-1")]
public sealed class CspBuilderTests
{
    private const string Design =
        "default-src 'none'; script-src 'self' 'unsafe-inline' https://lib-nth.invalid; " +
        "style-src 'self' 'unsafe-inline' https://lib-nth.invalid; img-src 'self' data: blob:; " +
        "font-src https://lib-nth.invalid data:; connect-src 'none'; frame-src 'none'; form-action 'none'; " +
        "base-uri 'none'; worker-src 'none'; manifest-src 'none'; media-src 'self' data: blob:";

    [Fact]
    public void The_artifact_policy_is_the_designs()
    {
        Assert.Equal(Design, CspBuilder.ArtifactDefault);
        Assert.Equal(Design, CspBuilder.Artifact());
        Assert.Equal(Design, CspBuilder.Artifact([]));
        var directives = CspBuilder.Parse(CspBuilder.ArtifactDefault);
        Assert.DoesNotContain("'unsafe-eval'", directives["script-src"]);
        Assert.Equal(["'none'"], directives["connect-src"]);
    }

    [Fact]
    public void A_network_grant_opens_connect_and_images_to_exactly_those_sites()
    {
        var directives = CspBuilder.Parse(CspBuilder.Artifact(["API.Example.com", "tiles.example.org"]));
        Assert.Equal(["https://api.example.com", "https://tiles.example.org"], directives["connect-src"]);
        Assert.Equal(["'self'", "data:", "blob:", "https://api.example.com", "https://tiles.example.org"], directives["img-src"]);
        Assert.Equal(["'none'"], directives["frame-src"]);
        Assert.Equal(["'none'"], directives["form-action"]);
        Assert.Equal(CspBuilder.Parse(Design)["script-src"], directives["script-src"]);
    }

    [Fact]
    public void An_mcp_app_gets_only_what_was_declared_and_allowed()
    {
        var declared = new McpUiCsp(["https://api.shipyard.dev", "https://evil.example"], ["https://cdn.shipyard.dev"], [], []);
        Assert.Equal(CspBuilder.ArtifactDefault, CspBuilder.McpApp(declared, null, allowBroad: false));

        var allowed = new McpUiCsp(["https://api.shipyard.dev", "https://never-declared.example"], ["https://cdn.shipyard.dev"], ["https://frames.example"], []);
        var directives = CspBuilder.Parse(CspBuilder.McpApp(declared, allowed, allowBroad: false));
        Assert.Equal(["https://api.shipyard.dev"], directives["connect-src"]);
        Assert.Contains("https://cdn.shipyard.dev", directives["script-src"]);
        Assert.Contains("https://cdn.shipyard.dev", directives["img-src"]);
        Assert.Equal(["'none'"], directives["frame-src"]);
        Assert.DoesNotContain(directives.Values.SelectMany(v => v), s => s.Contains("never-declared", StringComparison.Ordinal) || s.Contains("evil", StringComparison.Ordinal));
    }

    [Fact]
    public void Declared_sources_that_could_inject_or_widen_are_dropped_with_a_reason()
    {
        var result = CspBuilder.Clean(["https://ok.example", "https://a.example; script-src *", "*", "*.example.com", "'unsafe-eval'", "javascript:alert(1)", "10.0.0.1", "plain.example"], allowBroad: false, allowIp: false);
        Assert.Equal(["https://ok.example", "https://plain.example"], result.Kept);
        Assert.Equal(6, result.Dropped.Count);
        Assert.All(result.Dropped, d => Assert.False(string.IsNullOrEmpty(d.Reason)));
    }
}
