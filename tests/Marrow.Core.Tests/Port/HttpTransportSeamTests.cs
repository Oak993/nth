using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// OD-0057: the test-only handler seam on the app's HTTP client, URLSession.Transport (Ambient, PORT-ONLY). Port-only
// code, so these carry a Rule trait (the decision has no ED id). Nothing here opens a socket: the handler answers
// in-process (InProcessHttpHandler), and the addresses are hosts that exist only in the test.
public sealed class HttpTransportSeamTests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;
    private static readonly Uri Upstream = new("https://seam.example.test/mcp");

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public void The_process_default_is_the_handler_the_port_has_always_used()
    {
        // Production behaviour unchanged: with no override, requests go to the plain SocketsHttpHandler, configured as
        // R-NET-1 says (no redirects of its own, no decompression, no cookies); the client on top has no timeout.
        var handler = Assert.IsType<SocketsHttpHandler>(URLSession.Transport.Current);
        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.False(handler.UseCookies);
        Assert.Equal(Timeout.InfiniteTimeSpan, URLSession.Shared.HttpClientTimeout);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task An_overriding_handler_answers_the_request_in_process_and_sees_what_the_port_sent()
    {
        using var handler = new InProcessHttpHandler
        {
            Answer = _ => InProcessHttpHandler.Reply(403, "MCP server disabled", "text/plain", ("X-Seen", "yes")),
        };
        using var scope = URLSession.Transport.Override(handler);

        var request = new URLRequest(Upstream) { HttpMethod = "POST", HttpBody = "{\"a\":1}"u8.ToArray() }
            .SetValue("Bearer t-1", "Authorization");
        var (data, response) = await URLSession.Shared.Data(request).WaitAsync(Guard);

        var http = Assert.IsType<HTTPURLResponse>(response);
        Assert.Equal(403, http.StatusCode);
        Assert.Equal("yes", http.Value("X-Seen"));
        Assert.Equal("MCP server disabled", System.Text.Encoding.UTF8.GetString(data));
        var seen = Assert.Single(handler.Received);
        Assert.Equal(("POST", Upstream), (seen.Method, seen.Url));
        Assert.Equal("{\"a\":1}", seen.BodyText);
        Assert.Equal("Bearer t-1", seen.Header("Authorization"));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public void The_override_lasts_until_it_is_disposed_and_then_the_default_is_back()
    {
        using var handler = new InProcessHttpHandler();
        var scope = URLSession.Transport.Override(handler);
        Assert.Same(handler, URLSession.Transport.Current);
        scope.Dispose();
        Assert.IsType<SocketsHttpHandler>(URLSession.Transport.Current);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task The_ports_own_rules_still_run_on_top_of_the_handler_so_a_redirect_is_followed()
    {
        // Only the wire is replaced: the unit follows redirects itself (R-NET-1), so a 302 from the fake is followed to
        // the address it names, and the second request is the one answered.
        using var handler = new InProcessHttpHandler
        {
            Answer = request => request.Url.AbsolutePath == "/mcp"
                ? InProcessHttpHandler.Reply(302, "", "text/plain", ("Location", "https://seam.example.test/moved"))
                : InProcessHttpHandler.Reply(200, "arrived", "text/plain"),
        };
        using var scope = URLSession.Transport.Override(handler);

        var (data, response) = await URLSession.Shared.Data(Upstream).WaitAsync(Guard);

        Assert.Equal(200, Assert.IsType<HTTPURLResponse>(response).StatusCode);
        Assert.Equal("arrived", System.Text.Encoding.UTF8.GetString(data));
        Assert.Equal(["/mcp", "/moved"], handler.Received.Select(r => r.Url.AbsolutePath));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task A_transport_failure_raised_by_the_handler_becomes_the_URLError_with_that_exception_behind_it()
    {
        using var handler = new InProcessHttpHandler
        {
            Answer = _ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known.",
                new SocketException((int)SocketError.HostNotFound)),
        };
        using var scope = URLSession.Transport.Override(handler);

        var error = await Assert.ThrowsAsync<URLError>(() => URLSession.Shared.Data(Upstream).WaitAsync(Guard));

        Assert.Equal(URLError.Code.CannotFindHost, error.URLErrorCode);
        Assert.Equal("No such host is known.", SwiftErrors.LocalizedDescription(error));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task An_address_the_test_did_not_script_is_refused_and_never_reaches_a_socket()
    {
        // Fail closed (R-TEST-9, R-TEST-10): with no answer set, a non-loopback address is a refused connection.
        using var handler = new InProcessHttpHandler();
        using var scope = URLSession.Transport.Override(handler);

        var error = await Assert.ThrowsAsync<URLError>(() => URLSession.Shared.Data(Upstream).WaitAsync(Guard));

        Assert.Equal(URLError.Code.CannotConnectToHost, error.URLErrorCode);
        Assert.Single(handler.Received);
    }
}
