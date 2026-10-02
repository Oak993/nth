using System.Net.Sockets;
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.MCP;

// Tests for the hub's two request deadlines (DroppyCode/Services/MCP/MCPHub.swift): tools/list 60 seconds, one
// request per cursor page (:182), and tools/call 120 seconds, one per call (:207), on both transports. They are
// measured on the manual clock (TestRuntime), never waited for: the hub's deadline is a timer on SwiftClock
// (stdio: MCPWire.swift:170-186 through MCPStdioConnection.request; http: the request's own timeout through
// URLSession), so a test holds the server's answer, moves the clock to just before the deadline and sees the
// request still waiting, then just past it and sees it fail. The stdio server is mcp-deadline-fixture.ps1
// (powershell.exe running a scripted stand-in, R-TEST-10), the http server is Marrow.Testing's
// LoopbackHttpServer on 127.0.0.1; nothing here reaches a real MCP server or the network.
public sealed class MCPHubDeadlineTests
{
    private static readonly TimeSpan Bound = HubDeadlineRig.Bound;

    // ---- stdio ----

    [Fact]
    [Trait("Req", "PR-MCP-189")] // MCPHub.swift:182 rpc(method: "tools/list", timeout: 60), stdio: MCPStdioConnection.request(timeout:)
    public async Task A_stdio_tools_list_page_that_stays_unanswered_fails_the_listing_at_sixty_seconds()
    {
        using var rig = new HubDeadlineRig();
        await rig.WarmUp("s");
        var baseline = rig.ArmedTimers;

        var listing = rig.Hub.Tools();
        await rig.WaitSeen("seen-list-1");
        await rig.WaitArmed(baseline);

        rig.Advance(59.9);
        await rig.AssertStillWaiting(listing, baseline);

        rig.Advance(0.2); // 60.1 seconds after the request went out
        var tools = await listing.WaitAsync(Bound);
        // The listing failed (a stdio overrun is "The server didn't answer in time."), so the hub drops the server
        // from the list (:59-63) instead of returning anything for it.
        Assert.Empty(tools);
    }

    [Fact]
    [Trait("Req", "PR-MCP-189")] // MCPHub.swift:180-182 the deadline is per REQUEST: a multi-page walk is not held to one 60 seconds
    public async Task Every_tools_list_page_gets_its_own_sixty_seconds_so_a_multi_page_walk_outlasts_one_deadline()
    {
        using var rig = new HubDeadlineRig(pages: 4);
        await rig.WarmUp("s");
        var baseline = rig.ArmedTimers;

        var listing = rig.Hub.Tools();
        for (var page = 1; page <= 4; page++)
        {
            await rig.WaitSeen($"seen-list-{page}");
            await rig.WaitArmed(baseline);
            // 55 seconds into this page: past one shared 60-second deadline after page 2, inside this page's own.
            rig.Advance(55);
            await rig.AssertStillWaiting(listing, baseline);
            rig.Release($"release-list-{page}");
        }

        var tools = await listing.WaitAsync(Bound);
        Assert.Equal(4, tools.Count); // all four pages came back: 220 seconds of waiting, 55 per page
        Assert.Equal(Enumerable.Range(1, 4).Select(page => $"t{page}").Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-MCP-225")] // MCPHub.swift:207 rpc(method: "tools/call", timeout: 120), stdio
    public async Task A_stdio_tools_call_that_stays_unanswered_fails_at_one_hundred_twenty_seconds_and_each_call_gets_its_own()
    {
        using var rig = new HubDeadlineRig();
        await rig.WarmUp("s");
        var baseline = rig.ArmedTimers;

        var first = rig.Hub.Call("mcp__s__slow", new JSONValue.Null());
        await rig.WaitSeen("seen-call-1");
        await rig.WaitArmed(baseline);
        rig.Advance(119.9);
        await rig.AssertStillWaiting(first, baseline);
        rig.Advance(0.2); // 120.1 seconds
        var firstError = await Assert.ThrowsAsync<MCPHubError>(() => first).WaitAsync(Bound);
        Assert.Equal("The server didn't answer in time.", firstError.ErrorDescription); // :258-259

        // The deadline is per call, not per turn: a second call starts a fresh 120 seconds, however long ago the
        // first began.
        rig.Release("release-call-1"); // the server finishes the abandoned call, then reads the next
        var second = rig.Hub.Call("mcp__s__slow", new JSONValue.Null());
        await rig.WaitSeen("seen-call-2");
        await rig.WaitArmed(baseline);
        rig.Advance(119.9);
        await rig.AssertStillWaiting(second, baseline);
        rig.Advance(0.2);
        var secondError = await Assert.ThrowsAsync<MCPHubError>(() => second).WaitAsync(Bound);
        Assert.Equal("The server didn't answer in time.", secondError.ErrorDescription);
    }

    // ---- http ----

    private static byte[] Json(string text) => Encoding.UTF8.GetBytes(text);

    private static async Task AnswerInitialize(LoopbackHttpServer server)
    {
        var init = await server.NextExchange();
        await init.Respond(200, "OK", Json("""{"jsonrpc":"2.0","id":1,"result":{}}"""), ("Content-Type", "application/json"));
        var notified = await server.NextExchange();
        await notified.Respond(202, "Accepted", []);
    }

    [Fact]
    [Trait("Req", "PR-MCP-189")] // MCPHub.swift:182, :335 the same 60 seconds on http: the request's own timeout, per page
    public async Task An_http_tools_list_page_times_out_at_sixty_seconds_and_each_page_has_its_own_sixty()
    {
        await using var server = new LoopbackHttpServer();
        using var runtime = new TestRuntime();
        var hub = new MCPHub();
        await hub.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance)
        {
            ["s"] = new MCPServerSpec.Http(server.Url("/mcp").ToString(), ValueMap<string, string>.Empty),
        });

        var listing = hub.Tools();
        await AnswerInitialize(server);

        // Page 1 is answered after 50 seconds, with a cursor to page 2.
        var page1 = await server.NextExchange();
        runtime.Advance(TimeSpan.FromSeconds(50));
        await HubDeadlineRig.AssertStillWaiting(listing);
        await page1.Respond(200, "OK",
            Json("""{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"first"}],"nextCursor":"p2"}}"""),
            ("Content-Type", "application/json"));

        // Page 2 is a new request: 59.9 seconds into IT the listing is still waiting (110 seconds after page 1 began,
        // so a deadline shared by the walk would be long gone), and 60.1 seconds in it fails.
        var page2 = await server.NextExchange();
        Assert.Contains("p2", Encoding.UTF8.GetString(page2.Body), StringComparison.Ordinal);
        runtime.Advance(TimeSpan.FromSeconds(59.9));
        await HubDeadlineRig.AssertStillWaiting(listing);
        runtime.Advance(TimeSpan.FromSeconds(0.2));
        // The walk failed on page 2, so Swift prints the error and drops the server from the list (:59-63, :68) and
        // tools() returns [] (nothing of page 1 either: the cache is only written after the whole walk, :55-56).
        // The print needs the error's localizedDescription; OD-0056 gives a URLError the Windows error's own text, so
        // this ends in the empty list and does not fault with NotSupportedException any more.
        var tools = await listing.WaitAsync(Bound);
        Assert.Empty(tools);

        // The server stays dropped (:55 failed.insert, :54 `if failed.contains(id) { continue }`): the next listing
        // returns [] at once. The server never answers another request, so a listing that asked it again would still
        // be waiting for it (its own sixty seconds are on the manual clock, which nobody moves now).
        Assert.Empty(await hub.Tools().WaitAsync(Bound));
    }

    [Fact]
    [Trait("Req", "PR-MCP-225")] // MCPHub.swift:207, :335 the same 120 seconds on http
    public async Task An_http_tools_call_times_out_at_one_hundred_twenty_seconds()
    {
        await using var server = new LoopbackHttpServer();
        using var runtime = new TestRuntime();
        var hub = new MCPHub();
        await hub.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance)
        {
            ["s"] = new MCPServerSpec.Http(server.Url("/mcp").ToString(), ValueMap<string, string>.Empty),
        });

        var call = hub.Call("mcp__s__slow", new JSONValue.Null());
        await AnswerInitialize(server);
        var held = await server.NextExchange(); // the tools/call request: never answered
        Assert.Contains("tools/call", Encoding.UTF8.GetString(held.Body), StringComparison.Ordinal);

        runtime.Advance(TimeSpan.FromSeconds(119.9));
        await HubDeadlineRig.AssertStillWaiting(call);
        runtime.Advance(TimeSpan.FromSeconds(0.2));
        // Swift wraps the platform's timeout error as MCPHubError.server(error.localizedDescription) (:77-79), where
        // Foundation says "The request timed out.". OD-0056: the port shows the Windows networking error's own
        // message for the same failure, here the one for a connection that did not answer in time (Winsock's
        // WSAETIMEDOUT, asked of Windows itself so the test follows the machine's language).
        var error = await Assert.ThrowsAsync<MCPHubError>(() => call).WaitAsync(Bound);
        var windows = new SocketException((int)SocketError.TimedOut).Message;
        var wrapped = Assert.IsType<MCPHubError.Case.Server>(error.ErrorCase);
        Assert.Equal(windows, wrapped.Message);
        Assert.Equal(windows, error.ErrorDescription);
        Assert.NotEqual("The request timed out.", error.ErrorDescription);
    }
}
