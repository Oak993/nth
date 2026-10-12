using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5 Network). The nth-connect tag is written by the artifact, so its entries
// are untrusted: the consent card lists exact sites only, each with its registrable domain, and says which entries it
// dropped (wildcards, IP addresses, other schemes, nth's own and local names).
[Trait("Decision", "ED-NEW-web-w13-2")]
public sealed class ArtifactNetworkTests
{
    [Fact]
    public void The_tag_lists_exact_sites_with_their_registrable_domains()
    {
        const string page = "<html><head><meta name=\"nth-connect\" content=\"https://api.open-meteo.com, tiles.maps.example.co.uk/\">"
            + "<meta name='description' content='weather.example.com'></head></html>";
        var request = ArtifactNetwork.Declared(page);
        Assert.Equal(["api.open-meteo.com", "tiles.maps.example.co.uk"], request.HostNames);
        Assert.Equal("open-meteo.com", request.Hosts[0].RegistrableDomain);
        Assert.Equal("example.co.uk", request.Hosts[1].RegistrableDomain);
        Assert.Empty(request.Dropped);
        Assert.Equal("Anything this artifact shows, computes or you type into it can be sent to these sites.", ArtifactNetwork.Warning);
    }

    [Fact]
    public void Wildcards_ip_addresses_and_non_sites_are_dropped_and_named()
    {
        var request = ArtifactNetwork.Check(["*.example.com", "*", "93.184.216.34", "[::1]", "http://plain.example", "wss://socket.example",
            "localhost", "a-1234567890-x.invalid", "lib-nth.invalid", "user@host.example", "host.example:8443", "nodot", "ok.example"]);
        Assert.Equal(["ok.example"], request.HostNames);
        Assert.Equal(12, request.Dropped.Count);
        Assert.Contains(request.Dropped, d => d.Source == "*.example.com" && d.Reason == "a wildcard");
        Assert.Contains(request.Dropped, d => d.Source == "93.184.216.34" && d.Reason == "an IP address");
        Assert.Contains(request.Dropped, d => d.Source == "http://plain.example" && d.Reason == "not an https site");
    }

    [Fact]
    public void Duplicates_collapse_and_the_list_is_capped()
    {
        var entries = Enumerable.Range(0, ArtifactNetwork.MaxHosts + 3).Select(i => "s" + i + ".example").Prepend("S0.Example").ToList();
        var request = ArtifactNetwork.Check(entries);
        Assert.Equal(ArtifactNetwork.MaxHosts, request.Hosts.Count);
        Assert.Equal("s0.example", request.Hosts[0].Host);
        Assert.Equal(3, request.Dropped.Count(d => d.Reason == "too many sites"));
    }

    [Fact]
    public void A_page_without_the_tag_asks_for_nothing()
    {
        Assert.True(ArtifactNetwork.Declared("<p>no network</p>").IsEmpty);
        Assert.True(ArtifactNetwork.Declared(null).IsEmpty);
    }
}
