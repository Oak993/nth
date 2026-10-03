using System.Text;
using Marrow.App;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using static Marrow.App.Tests.Fixtures.ReleaseFeed;

namespace Marrow.App.Tests;

// ED-256 (10), R-TEST-10: what LoopbackFeedServer (Fixtures) holds besides its socket. While a server lives, the flow's HTTP client is
// a fail-closed handler: a request to the server goes through to it, a request to any other address is refused in process and listed
// in Refused, and the flow has its own client back when the server is disposed. The release feed a test serves names its download in
// an asset link, and the checker's size HEAD (UpdateChecker.swift:221-229) asks that address: a feed that names
// https://example.com/Marrow.exe, as ten of the checker's tests and the Windows toast test do, sent that HEAD to the internet until
// the server took the client (found by the network audit). The hosts end in .example, a reserved top-level domain that never resolves
// (RFC 2606).
public sealed class LoopbackFeedServerOfflineTests
{
    private static string Suffix => GitLabRelease.Link.ArtifactSuffix;

    [Fact]
    [Trait("Rule", "R-TEST-10")] // The fixture's claim: a request to another address is refused in process and listed (ED-256 (10))
    public async Task A_request_to_another_address_is_refused_in_process_and_listed_while_the_server_lives()
    {
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json("[]"));
        var head = new URLRequest(new Uri("https://offline-rig.example/Marrow" + Suffix)) { HttpMethod = "HEAD" };

        await Assert.ThrowsAnyAsync<Exception>(() => URLSession.Shared.Data(head));

        Assert.Equal(["HEAD https://offline-rig.example/Marrow" + Suffix], server.Refused);
        Assert.Empty(server.Requests); // it never reached the server either
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")] // The fixture's claim: a request to the server itself still goes through to its socket and is not listed as refused (ED-256 (10))
    public async Task A_request_to_the_server_itself_goes_through_and_is_not_refused()
    {
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json("[1]"));

        var (data, response) = await URLSession.Shared.Data(new URLRequest(new Uri(server.Url("/feed"))));

        Assert.Equal(200, Assert.IsType<HTTPURLResponse>(response).StatusCode);
        Assert.Equal("[1]", Encoding.UTF8.GetString(data));
        Assert.Equal("GET", Assert.Single(server.Requests).Method);
        Assert.Empty(server.Refused);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")] // The fixture's claim: the flow has its own client back when the server is disposed (ED-256 (10))
    public void The_flow_has_its_own_http_client_back_when_the_server_is_disposed()
    {
        HttpMessageHandler? before = URLSession.Transport.Current;
        HttpMessageHandler? during;
        using (new LoopbackFeedServer(_ => CannedResponse.Json("[]")))
        {
            during = URLSession.Transport.Current;
        }

        Assert.NotSame(before, during);
        Assert.IsType<InProcessHttpHandler>(during);
        Assert.Same(before, URLSession.Transport.Current);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-018")] // UpdateChecker.swift:171-173, :221-229: the size HEAD goes to the asset link's own address; a feed that names another host leaves the size nil, and the HEAD stays in process (ED-256 (10))
    [Trait("Rule", "R-TEST-10")]
    public void The_size_head_of_a_feed_that_names_another_host_is_refused_in_process_and_leaves_the_size_nil()
    {
        using var runtime = new TestRuntime();
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(
            Feed(Release("v2.0.0", [Link("Marrow" + Suffix, "https://offline-rig.example/Marrow" + Suffix)]))));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));

        Assert.True(runtime.Loop.Run(() => checker.Check()));

        Assert.Null(runtime.OnMain(() => checker.Update)!.Size);
        Assert.Equal(["HEAD https://offline-rig.example/Marrow" + Suffix], server.Refused);
        Assert.Equal("GET", Assert.Single(server.Requests).Method); // only the feed itself reached the server
    }
}
