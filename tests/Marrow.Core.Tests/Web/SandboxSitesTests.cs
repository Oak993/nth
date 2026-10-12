using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5 "one site per artifact", 7.6 MCP Apps, 8.1). Every sandboxed surface gets
// its own registrable domain under ".invalid", so the browser keeps each in its own process, storage and cookie jar.
[Trait("Decision", "ED-NEW-web-w13-1")]
public sealed class SandboxSitesTests
{
    private static readonly Guid ChatA = Guid.Parse("4f0c2b8e-7d1a-4c3e-9b5f-0a1b2c3d4e5f");
    private static readonly Guid ChatB = Guid.Parse("4f0c2b8e-7d1a-4c3e-9b5f-0a1b2c3d4e60");

    [Fact]
    public void Each_artifact_has_its_own_site_even_with_the_same_id_in_two_chats()
    {
        var hosts = new[]
        {
            SandboxSites.ArtifactHost(ChatA, "pricing"),
            SandboxSites.ArtifactHost(ChatA, "pricing-v2"),
            SandboxSites.ArtifactHost(ChatB, "pricing"),
            SandboxSites.WidgetHost("pricing"),
            SandboxSites.AppHost("shipyard", "ui://shipyard/releases"),
            SandboxSites.LibraryHost,
        };

        Assert.Equal(hosts.Length, hosts.Distinct(StringComparer.Ordinal).Count());
        // "invalid" is not on the Public Suffix List, so its default rule makes every label its own registrable domain.
        foreach (var host in hosts) Assert.Equal(host, SandboxSites.Site(host));
        Assert.Equal(hosts.Length, hosts.Select(SandboxSites.Site).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void An_artifact_host_is_stable_and_one_dns_label()
    {
        var host = SandboxSites.ArtifactHost(ChatA, "pricing");
        Assert.Equal(host, SandboxSites.ArtifactHost(ChatA, "pricing"));
        Assert.Matches("^a-[0-9a-f]{10}-pricing\\.invalid$", host);
        var longest = SandboxSites.ArtifactHost(ChatA, new string('x', 48));
        Assert.True(longest.Split('.')[0].Length <= SandboxSites.MaxLabelLength);
        Assert.Equal(SandboxSurface.Artifact, SandboxSites.SurfaceOf(longest));
    }

    [Fact]
    public void Labels_never_meet_after_cutting_and_never_end_in_a_hyphen()
    {
        var a = SandboxSites.Label(new string('a', 70) + "one", 40);
        var b = SandboxSites.Label(new string('a', 70) + "two", 40);
        Assert.NotEqual(a, b);
        Assert.True(a.Length <= 40);
        Assert.Equal("my-server", SandboxSites.Label("My Server!", 40));
        Assert.Matches("^[0-9a-f]{8}$", SandboxSites.Label("***", 40));
    }

    [Fact]
    public void Surfaces_origins_and_addresses_are_recognised_exactly()
    {
        var host = SandboxSites.ArtifactHost(ChatA, "pricing");
        Assert.Equal(SandboxSurface.Widget, SandboxSites.SurfaceOf(SandboxSites.WidgetHost("chart")));
        Assert.Equal(SandboxSurface.McpApp, SandboxSites.SurfaceOf(SandboxSites.AppHost("shipyard", "ui://a")));
        Assert.Equal(SandboxSurface.Library, SandboxSites.SurfaceOf(SandboxSites.LibraryHost));
        Assert.Null(SandboxSites.SurfaceOf("evil.example.com"));
        Assert.Null(SandboxSites.SurfaceOf("x.a-123.invalid"));
        Assert.True(SandboxSites.IsSandboxOrigin("https://" + host));
        Assert.False(SandboxSites.IsSandboxOrigin("http://" + host));
        Assert.False(SandboxSites.IsSandboxOrigin("https://" + host + ":444"));
        Assert.False(SandboxSites.IsSandboxOrigin("https://" + host + "/page"));
        Assert.Equal("https://" + host, SandboxSites.OriginOf("https://" + host.ToUpperInvariant() + "/a?b"));
        Assert.Null(SandboxSites.OriginOf("javascript:alert(1)"));
    }
}
