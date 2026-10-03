using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// ED-256 (10): the rig every test that shows link text takes (NoLinkNetwork). Its claims, held by these tests:
// main-actor work a test starts runs under the rig's HTTP client; a link host nobody named reaches that client, is refused in
// process and fails the test that showed it; a host whose ask was cancelled before it went out (a popover measured off screen is
// attached and detached in one turn) leaves nothing on the wire but is reported all the same; a named host asks nothing of the client.
// The hosts end in .example, a top-level domain that is reserved and never resolves (RFC 2606), and FaviconCache's own check
// (MarkdownView.swift:919) takes it for a public host, so a control reaches the fetch and the handler without a name that exists.
[Collection("Headless")]
public sealed class NoLinkNetworkTests(HeadlessSession headless)
{
    private static AvaloniaWindow ShowLink(string address)
    {
        var window = Show(new InlineText("see [the link](" + address + ") for it", hasLinks: true));
        for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")] // The rig's own proof: main-actor work started in the test's flow runs under the rig's HTTP client (ED-256 (10))
    public void Main_actor_work_the_test_started_runs_under_the_rigs_http_client() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        HttpMessageHandler? before = URLSession.Transport.Current;
        HttpMessageHandler? seen = null;
        var rig = new NoLinkNetwork();
        try
        {
            var task = new ViewTask<string>("key", _ =>
            {
                seen = URLSession.Transport.Current; // what URLSession.Data reads, once per request, in the flow of the work that asks
                return Task.CompletedTask;
            });
            runtime.OnMain(() => task.Appear());
        }
        finally { Assert.Empty(rig.DisposeAndReport()); }
        Assert.NotNull(seen);
        Assert.NotSame(before, seen);
        Assert.IsType<InProcessHttpHandler>(seen);
        Assert.Same(before, URLSession.Transport.Current); // and the flow has its own client back
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")] // The rig's own proof: a shown link host nobody named reaches the fail-closed client, is refused in process, and disposing the rig fails the test (ED-256 (10))
    public void A_link_host_nobody_named_reaches_the_fail_closed_client_and_is_reported() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        const string host = "rig-control.example";
        using var forget = new HydraMergeFaviconSeed(host, null, null); // the refused ask leaves the host in the failed set
        var rig = new NoLinkNetwork();
        AvaloniaWindow window = null!;
        try
        {
            runtime.OnMain(() => window = ShowLink("https://" + host + "/x"));
            runtime.OnMain(() => { });
            Assert.Equal(["GET https://" + host + "/favicon.ico"], rig.Refused);
            Assert.Equal([host], rig.Asked);
        }
        finally
        {
            runtime.OnMain(() => Close(window));
        }
        var failure = Assert.ThrowsAny<Exception>(rig.Dispose);
        Assert.Contains("refused GET https://" + host + "/favicon.ico", failure.Message, StringComparison.Ordinal);
        Assert.Contains("the favicon of " + host + " was asked for", failure.Message, StringComparison.Ordinal);
        rig.Dispose(); // already disposed: a second disposal changes nothing and fails nothing
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")] // The rig's own proof: an ask cancelled before it went out sends nothing and is reported all the same, as the cache learns the host (ED-256 (10))
    public void A_link_host_whose_ask_was_cancelled_before_it_went_out_sends_nothing_and_is_reported() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        const string host = "rig-measured.example";
        using var forget = new HydraMergeFaviconSeed(host, null, null);
        var rig = new NoLinkNetwork();
        try
        {
            // A popover's scroll measures its content in a hidden window (PopoverScroll.MeasuredHeight): the paragraph is attached and
            // detached in one turn, so its ViewTask is cancelled before the main loop starts it, and URLSession fails a cancelled
            // request before it reaches the client; the cache records the host as one with no icon.
            runtime.OnMain(() => PopoverScroll.MeasuredHeight(new InlineText("see [the link](https://" + host + "/x) for it", hasLinks: true), 460));
            runtime.OnMain(() => { });
            Assert.Empty(rig.Refused);
            Assert.Equal([host], rig.Asked);
        }
        finally { Assert.Equal(["the favicon of " + host + " was asked for"], rig.DisposeAndReport()); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")] // The rig's own proof: a named host awaits its in-flight seed, the fail-closed client sees nothing, and the cache keeps no trace of it (ED-256 (10))
    public void A_named_link_host_asks_nothing_of_the_client_and_leaves_no_trace_in_the_favicon_cache() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        const string host = "rig-named.example";
        using (var rig = new NoLinkNetwork(host))
        {
            AvaloniaWindow window = null!;
            try
            {
                runtime.OnMain(() => window = ShowLink("https://" + host + "/x"));
                runtime.OnMain(() => PopoverScroll.MeasuredHeight(new InlineText("see [the link](https://" + host + "/y) for it", hasLinks: true), 460));
                runtime.OnMain(() => { });
                Assert.Empty(rig.Refused);
                Assert.Empty(rig.Asked);
            }
            finally { runtime.OnMain(() => Close(window)); }
        }
        Assert.False(NoLinkNetwork.CacheKnows(host)); // the cache holds the host in none of its tables
        Assert.Null(FaviconCache.Cached(host));
    });
}
