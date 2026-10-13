using System.Net;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.2 "Web page watch", 16.2; canvas 5; ED-NEW-dextra-as10-7): a page watch against a local HTTP
// fixture. It reads plain HTML with GET and nothing else, judges the region between the duty's markers (changed with a diff, a
// price below an amount, a text), fires when a condition becomes true and not while it stays true, reports a page that is down
// or has moved as a problem and never offers old text as current, and refuses a page that needs scripts or a sign-in.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-7")]
public sealed class WebWatcherTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 6, 0, 0, TimeSpan.Zero);

    private readonly LocalHttpFixture server = new();
    private readonly WebWatcher watcher;

    public WebWatcherTests() => watcher = new WebWatcher(new WebWatcherOptions { AllowLocalAddresses = true, Timeout = TimeSpan.FromSeconds(10) });

    public void Dispose()
    {
        watcher.Dispose();
        server.Dispose();
    }

    private static string Page(string price, string stock = "In stock") =>
        $"<html><head><title>Barista Pro</title><script>var x = 1;</script></head><body><h1>Barista Pro</h1><div class=\"price\">Price: {price}</div><p>{stock}</p><footer>Visited 5 times</footer></body></html>";

    private Task<PageCheck> Look(PageWatchTrigger trigger, PageCheck? previous = null) => watcher.Check(trigger, previous?.State, Now, CancellationToken.None);

    private PageWatchTrigger Trigger(PageCondition condition = PageCondition.Changed, string path = "/p") =>
        new(server.Url(path), 60) { Condition = condition, StartMarker = "Price:", EndMarker = "In stock" };

    [Fact]
    public async Task A_change_in_the_watched_region_fires_with_a_diff_and_the_first_look_is_only_a_baseline()
    {
        var html = Page("$689");
        server.Reply = _ => new LocalReply(200, html);
        var trigger = Trigger() with { StartMarker = "Barista Pro", EndMarker = "Visited" };
        var first = await Look(trigger);
        Assert.False(first.Fired);
        Assert.Null(first.Problem);
        var same = await Look(trigger, first);
        Assert.False(same.Fired);
        html = Page("$642");
        var changed = await Look(trigger, same);
        Assert.True(changed.Fired);
        Assert.Equal("The page changed.", changed.Detail);
        Assert.Contains("- Price: $689", changed.Diff, StringComparison.Ordinal);
        Assert.Contains("+ Price: $642", changed.Diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_change_outside_the_markers_is_not_a_change()
    {
        var html = Page("$689");
        server.Reply = _ => new LocalReply(200, html);
        var trigger = Trigger();
        var first = await Look(trigger);
        html = html.Replace("Visited 5 times", "Visited 6 times", StringComparison.Ordinal);
        Assert.False((await Look(trigger, first)).Fired);
    }

    [Fact]
    public async Task A_price_below_the_amount_fires_when_it_gets_there_and_not_while_it_stays_there()
    {
        var price = "$689";
        server.Reply = _ => new LocalReply(200, Page(price));
        var trigger = Trigger(PageCondition.PriceBelow) with { PriceBelow = 650 };
        var check = await Look(trigger);
        Assert.False(check.Fired);
        price = "$642";
        check = await Look(trigger, check);
        Assert.True(check.Fired);
        Assert.Equal("The price is $642, below $650.", check.Detail);
        price = "$640";
        check = await Look(trigger, check);
        Assert.False(check.Fired); // still below: it was told once
        price = "$700";
        check = await Look(trigger, check);
        Assert.False(check.Fired);
        price = "$600";
        check = await Look(trigger, check);
        Assert.True(check.Fired); // below again
    }

    [Fact]
    public async Task A_price_already_below_on_the_first_look_fires_once()
    {
        server.Reply = _ => new LocalReply(200, Page("$1,299.00 was $1,499.00"));
        var trigger = Trigger(PageCondition.PriceBelow) with { PriceBelow = 2000 };
        var first = await Look(trigger);
        Assert.True(first.Fired);
        Assert.False((await Look(trigger, first)).Fired);
    }

    [Fact]
    public async Task A_region_without_a_price_is_a_problem_not_a_guess()
    {
        server.Reply = _ => new LocalReply(200, Page("call us"));
        var check = await Look(Trigger(PageCondition.PriceBelow) with { PriceBelow = 650 });
        Assert.False(check.Fired);
        Assert.Contains("could not find a price", check.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_text_fires_when_it_appears_and_again_after_it_went_away()
    {
        var stock = "Out of stock";
        server.Reply = _ => new LocalReply(200, $"<body><div>Status: {stock}</div></body>");
        var trigger = new PageWatchTrigger(server.Url("/s"), 30) { Condition = PageCondition.ContainsText, Text = "in stock", StartMarker = "Status:" };
        var check = await Look(trigger);
        Assert.False(check.Fired); // "Out of stock" holds "stock" but not "in stock"
        stock = "In stock, ships Friday";
        check = await Look(trigger, check);
        Assert.True(check.Fired);
        check = await Look(trigger, check);
        Assert.False(check.Fired);
        stock = "Sold out";
        check = await Look(trigger, check);
        stock = "In stock";
        Assert.True((await Look(trigger, check)).Fired);
    }

    [Fact]
    public async Task A_page_that_is_down_is_a_problem_and_the_old_text_is_never_offered_as_current()
    {
        var up = true;
        server.Reply = _ => up ? new LocalReply(200, Page("$689")) : new LocalReply(503, "maintenance");
        var trigger = Trigger(PageCondition.Changed);
        var first = await Look(trigger);
        up = false;
        var down = await Look(trigger, first);
        Assert.False(down.Fired);
        Assert.Contains("503", down.Problem, StringComparison.Ordinal);
        Assert.Equal(first.State.ToJsonString(), down.State.ToJsonString()); // the last good look is kept, not replaced
        up = true;
        Assert.False((await Look(trigger, down)).Fired); // back and unchanged: still no false alarm
    }

    [Fact]
    public async Task A_region_marker_that_is_no_longer_on_the_page_says_so()
    {
        server.Reply = _ => new LocalReply(200, "<body>A new design with no prices.</body>");
        var check = await Look(Trigger());
        Assert.Contains("is not on the page any more", check.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_nothing_listening_address_is_unreachable()
    {
        server.Dispose();
        var check = await Look(Trigger());
        Assert.Contains("could not be reached", check.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_redirect_is_followed_and_an_endless_one_stops()
    {
        server.Reply = path => path switch
        {
            "/old" => new LocalReply(301, "", "text/plain") { Location = "/new" },
            "/new" => new LocalReply(200, "<body>Welcome here</body>"),
            _ => new LocalReply(302, "", "text/plain") { Location = "/loop" },
        };
        var reading = await watcher.Read(server.Url("/old"), null, null, CancellationToken.None);
        Assert.Equal("Welcome here", reading.Text);
        var loop = await watcher.Read(server.Url("/loop"), null, null, CancellationToken.None);
        Assert.Equal(PageReadStatus.Unreachable, loop.Status);
        Assert.Contains("redirected more than 5 times", loop.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_wants_a_sign_in_is_refused_not_read()
    {
        server.Reply = path => path switch
        {
            "/private" => new LocalReply(401, "no"),
            "/account" => new LocalReply(302, "", "text/plain") { Location = "/login?next=/account" },
            _ => new LocalReply(200, "<body>login form</body>"),
        };
        Assert.Equal(PageReadStatus.SignIn, (await watcher.Read(server.Url("/private"), null, null, CancellationToken.None)).Status);
        var redirected = await watcher.Read(server.Url("/account"), null, null, CancellationToken.None);
        Assert.Equal(PageReadStatus.SignIn, redirected.Status);
        Assert.Equal(WebWatcher.SignInWords, redirected.Problem);
    }

    [Fact]
    public async Task A_page_that_builds_itself_with_scripts_needs_the_browser()
    {
        server.Reply = _ => new LocalReply(200, "<html><head><title>App</title></head><body><div id=\"root\"></div><script src=\"/app.js\"></script></body></html>");
        var reading = await watcher.Read(server.Url("/"), null, null, CancellationToken.None);
        Assert.Equal(PageReadStatus.NeedsBrowser, reading.Status);
        Assert.Equal(WebWatcher.NeedsBrowserWords, reading.Problem);
        server.Reply = _ => new LocalReply(200, "<body><noscript>Please enable JavaScript to use this app.</noscript></body>");
        Assert.Equal(PageReadStatus.NeedsBrowser, (await watcher.Read(server.Url("/"), null, null, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_real_page_with_scripts_and_text_is_read()
    {
        server.Reply = _ => new LocalReply(200, "<html><body><script>track()</script>" + string.Concat(Enumerable.Repeat("<p>A real sentence of the article.</p>", 10)) + "</body></html>");
        var reading = await watcher.Read(server.Url("/"), null, null, CancellationToken.None);
        Assert.True(reading.Ok);
        Assert.Contains("A real sentence", reading.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("track()", reading.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_over_the_size_limit_is_refused_by_its_header_or_as_it_streams()
    {
        using var small = new WebWatcher(new WebWatcherOptions { AllowLocalAddresses = true, MaxBytes = 1024 });
        server.Reply = path => path == "/declared"
            ? new LocalReply(200, "x") { DeclaredLength = 5_000_000 }
            : new LocalReply(200, new string('y', 10_000)) { Chunked = true };
        Assert.Equal(PageReadStatus.TooLarge, (await small.Read(server.Url("/declared"), null, null, CancellationToken.None)).Status);
        Assert.Equal(PageReadStatus.TooLarge, (await small.Read(server.Url("/streamed"), null, null, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Something_that_is_not_text_is_not_read()
    {
        server.Reply = _ => new LocalReply(200, "GIF89a", "image/gif");
        var reading = await watcher.Read(server.Url("/"), null, null, CancellationToken.None);
        Assert.Equal(PageReadStatus.NotText, reading.Status);
    }

    [Fact]
    public async Task A_page_that_never_answers_times_out_with_words()
    {
        using var impatient = new WebWatcher(new WebWatcherOptions { AllowLocalAddresses = true, Timeout = TimeSpan.FromMilliseconds(400) });
        server.Reply = _ => new LocalReply(200, "") { Hang = true };
        var reading = await impatient.Read(server.Url("/"), null, null, CancellationToken.None);
        Assert.Equal(PageReadStatus.Unreachable, reading.Status);
        Assert.Contains("did not answer in time", reading.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancelled_look_is_cancelled_not_reported_as_a_problem()
    {
        using var cancel = new CancellationTokenSource();
        server.Reply = _ => new LocalReply(200, "") { Hang = true };
        var reading = watcher.Read(server.Url("/"), null, null, cancel.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
    }

    [Fact]
    public async Task The_request_is_a_plain_get_that_carries_no_cookie_and_no_credentials_and_keeps_none()
    {
        server.Reply = path => new LocalReply(200, "<body>hello there, a short page</body>") { SetCookie = "session=abc; Path=/" };
        await watcher.Read(server.Url("/one"), null, null, CancellationToken.None);
        await watcher.Read(server.Url("/two"), null, null, CancellationToken.None);
        Assert.Equal(2, server.Requests.Count);
        Assert.All(server.Requests, request =>
        {
            Assert.DoesNotContain("cookie", request.Headers, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("authorization", request.Headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("user-agent: " + WebWatcher.UserAgent, request.Headers, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData("http://localhost/x")]
    [InlineData("http://LOCALHOST:8080/")]
    [InlineData("http://app.localhost/")]
    [InlineData("http://printer.local/")]
    [InlineData("http://db.internal/")]
    [InlineData("http://127.0.0.1:9/")]
    [InlineData("http://127.1.2.3/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://172.16.4.4/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://100.64.0.1/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fd12:3456::1]/")]
    [InlineData("http://[::ffff:10.0.0.1]/")]
    [InlineData("ftp://shop.example/a")]
    [InlineData("file:///C:/secrets.txt")]
    [InlineData("https://user:pw@shop.example/a")]
    public async Task Addresses_on_this_pc_or_its_network_and_other_schemes_are_refused_before_any_request(string url)
    {
        var handler = new RecordingHandler(_ => RecordingHandler.Html("secret"));
        using var strict = new WebWatcher(new WebWatcherOptions { Handler = handler });
        var reading = await strict.Read(url, null, null, CancellationToken.None);
        Assert.Equal(PageReadStatus.Blocked, reading.Status);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("http://8.8.8.8/")]
    [InlineData("https://shop.example/espresso")]
    [InlineData("http://172.32.0.1/")]
    [InlineData("http://[2001:4860:4860::8888]/")]
    public void A_public_address_passes_the_guard(string url)
    {
        using var strict = new WebWatcher(new WebWatcherOptions { Handler = new RecordingHandler(_ => RecordingHandler.Html("x")) });
        Assert.Null(strict.GuardRefusal(new Uri(url)));
    }

    [Fact]
    public async Task A_redirect_to_a_private_address_is_refused_at_the_hop()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Host == "shop.example"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://192.168.1.1/admin") } }
            : RecordingHandler.Html("router admin"));
        using var strict = new WebWatcher(new WebWatcherOptions { Handler = handler });
        var reading = await strict.Read("https://shop.example/a", null, null, CancellationToken.None);
        Assert.Equal(PageReadStatus.Blocked, reading.Status);
        Assert.Single(handler.Requests); // the second request never went
    }

    [Fact]
    public async Task The_real_connection_refuses_this_pc_when_local_addresses_are_not_allowed()
    {
        using var strict = new WebWatcher(new WebWatcherOptions());
        server.Reply = _ => new LocalReply(200, "<body>this must never be read</body>");
        var reading = await strict.Read(server.Url("/"), null, null, CancellationToken.None);
        Assert.Equal(PageReadStatus.Blocked, reading.Status);
        Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.255.255.255", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("172.15.0.1", false)]
    [InlineData("192.168.0.1", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.128.0.1", false)]
    [InlineData("198.18.0.1", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("93.184.216.34", false)]
    [InlineData("::1", true)]
    [InlineData("::", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fdff::1", true)]
    [InlineData("ff02::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("::ffff:8.8.8.8", false)]
    [InlineData("2001:4860:4860::8888", false)]
    public void The_address_table_names_what_is_local(string address, bool local) => Assert.Equal(local, WebWatcher.IsLocal(IPAddress.Parse(address)));
}
