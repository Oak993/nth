using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces security review, finding L6 (DESIGN.md 6.5 "one site per artifact, App or widget", 8.1). An MCP
// App's site name carried 32 bits of hash, so a server could try resource addresses until one landed on another App's site
// and shared its storage; and the hashed text joined the server's name and the address with a newline, so two different
// pairs could hash the same text. The name now carries 128 bits over an unambiguous text and stays one DNS label.
[Trait("Decision", "ED-NEW-web-sec-mcpapps-3")]
public sealed class McpAppSiteNameTests
{
    [Fact]
    public void An_apps_site_name_carries_128_bits_and_stays_one_label()
    {
        var host = SandboxSites.AppHost("shipyard", "ui://shipyard/releases");
        Assert.Matches("^m-shipyard-[0-9a-f]{32}\\.invalid$", host);
        Assert.Equal(host, SandboxSites.AppHost("shipyard", "ui://shipyard/releases"));
        Assert.Equal(SandboxSurface.McpApp, SandboxSites.SurfaceOf(host));
        Assert.True(SandboxSites.IsSandboxOrigin(SandboxSites.Origin(host)));
        Assert.Equal(host, SandboxSites.Site(host));

        var longest = SandboxSites.AppHost(new string('s', 200), "ui://" + new string('r', 500));
        Assert.True(longest.Split('.')[0].Length <= SandboxSites.MaxLabelLength);
        Assert.Matches("-[0-9a-f]{32}\\.invalid$", longest);
        Assert.Equal(SandboxSurface.McpApp, SandboxSites.SurfaceOf(longest));
    }

    [Fact]
    public void No_two_server_and_address_pairs_share_a_site()
    {
        // The same label and, before, the same hashed text "shipyard\n\nui://a".
        Assert.NotEqual(SandboxSites.AppHost("shipyard", "\nui://a"), SandboxSites.AppHost("shipyard\n", "ui://a"));
        // The same label from two names.
        Assert.NotEqual(SandboxSites.AppHost("shipyard", "ui://a"), SandboxSites.AppHost("Shipyard", "ui://a"));
        Assert.NotEqual(SandboxSites.AppHost("shipyard", "ui://a"), SandboxSites.AppHost("shipyard", "ui://b"));

        var hosts = Enumerable.Range(0, 2000).Select(i => SandboxSites.AppHost("shipyard", "ui://shipyard/page-" + i)).ToList();
        Assert.Equal(hosts.Count, hosts.Distinct(StringComparer.Ordinal).Count());
    }
}
