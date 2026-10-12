using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W8a (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.2 "The address field ... Typing a URL or host
// navigates; anything else searches with the engine set in Settings (DuckDuckGo by default ...). A leading ? forces a
// search. Local paths and file:, javascript:, data: and the browser's own pages are refused at the top level", 8.3 app
// links; 13.3 "address field rules"). What the address field does with what was typed.
[Trait("Decision", "ED-NEW-web-w8a-1")]
public sealed class VisusAddressTests
{
    private static AddressResult Resolve(string text, Func<string, bool>? registered = null) =>
        VisusAddress.Resolve(text, SearchEngine.DuckDuckGo, registered) ?? throw new Xunit.Sdk.XunitException("no result for " + text);

    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("example.com/docs?x=1", "https://example.com/docs?x=1")]
    [InlineData("www.example.co.uk", "https://www.example.co.uk/")]
    [InlineData("example.dev:8443/x", "https://example.dev:8443/x")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("HTTPS://EXAMPLE.COM/A", "https://example.com/A")]
    [InlineData("http://example.com/", "http://example.com/")]
    [InlineData("localhost", "http://localhost/")]
    [InlineData("localhost:5173", "http://localhost:5173/")]
    [InlineData("localhost:5173/app", "http://localhost:5173/app")]
    [InlineData("app.localhost", "http://app.localhost/")]
    [InlineData("printer.local", "http://printer.local/")]
    [InlineData("192.168.1.20:8080", "http://192.168.1.20:8080/")]
    [InlineData("1.2.3.4", "http://1.2.3.4/")]
    [InlineData("[::1]:3000", "http://[::1]:3000/")]
    [InlineData("myserver:8080", "http://myserver:8080/")]
    [InlineData("about:blank", "about:blank")]
    public void Addresses_go_public_hosts_on_https_dev_hosts_on_http(string typed, string url)
    {
        var result = Resolve(typed);
        Assert.Equal(AddressAction.Navigate, result.Action);
        Assert.Equal(url, result.Url);
    }

    [Theory]
    [InlineData("how to cook rice")]
    [InlineData("myserver")]
    [InlineData("notes.txt")]
    [InlineData("1.5")]
    [InlineData("user@example.com")]
    [InlineData("define:word")]
    [InlineData("http://example.com/a b")]
    [InlineData("c#")]
    [InlineData("-bad-.com")]
    public void Anything_else_searches(string typed)
    {
        var result = Resolve(typed);
        Assert.Equal(AddressAction.Search, result.Action);
        Assert.Equal(typed, result.Query);
        Assert.Equal("https://duckduckgo.com/?q=" + Uri.EscapeDataString(typed), result.Url);
    }

    [Fact]
    public void A_leading_question_mark_forces_a_search()
    {
        var result = Resolve("?example.com");
        Assert.Equal(AddressAction.Search, result.Action);
        Assert.Equal("example.com", result.Query);
        Assert.Null(VisusAddress.Resolve("?", SearchEngine.DuckDuckGo));
        Assert.Null(VisusAddress.Resolve("  ? ", SearchEngine.DuckDuckGo));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Nothing_typed_does_nothing(string? typed) => Assert.Null(VisusAddress.Resolve(typed, SearchEngine.DuckDuckGo));

    [Theory]
    [InlineData(@"C:\Users\dana\notes.html", VisusAddress.LocalFileRefusal)]
    [InlineData("C:/Users/dana/notes.html", VisusAddress.LocalFileRefusal)]
    [InlineData("D:", VisusAddress.LocalFileRefusal)]
    [InlineData(@"\\server\share\a.html", VisusAddress.LocalFileRefusal)]
    [InlineData("~/notes.html", VisusAddress.LocalFileRefusal)]
    [InlineData(@".\index.html", VisusAddress.LocalFileRefusal)]
    [InlineData("../index.html", VisusAddress.LocalFileRefusal)]
    [InlineData("file:///C:/Users/dana/notes.html", VisusAddress.LocalFileRefusal)]
    [InlineData("javascript:alert(1)", VisusAddress.ScriptRefusal)]
    [InlineData("JavaScript:alert(1)", VisusAddress.ScriptRefusal)]
    [InlineData("vbscript:msgbox(1)", VisusAddress.ScriptRefusal)]
    [InlineData("data:text/html,<b>x</b>", VisusAddress.DataRefusal)]
    [InlineData("about:settings", VisusAddress.InternalRefusal)]
    [InlineData("edge://settings", VisusAddress.InternalRefusal)]
    [InlineData("chrome://flags", VisusAddress.InternalRefusal)]
    [InlineData("view-source:https://example.com/", VisusAddress.InternalRefusal)]
    [InlineData("devtools://devtools/bundled/inspector.html", VisusAddress.InternalRefusal)]
    [InlineData("ms-msdt:/id", "Visus does not open ms-msdt: links.")]
    [InlineData("search-ms:query=calc", "Visus does not open search-ms: links.")]
    [InlineData("ms-officecmd:x", "Visus does not open ms-officecmd: links.")]
    public void Local_files_scripts_data_internal_pages_and_blocked_schemes_are_refused(string typed, string why)
    {
        var result = Resolve(typed);
        Assert.Equal(AddressAction.Refused, result.Action);
        Assert.Null(result.Url);
        Assert.Equal(why, result.Refusal);
    }

    [Theory]
    [InlineData("mailto:dana@example.com")]
    [InlineData("tel:+15550100")]
    [InlineData("zoommtg://zoom.us/join?confno=1")]
    [InlineData("acme-sync://open/item")]
    public void App_links_open_through_the_prompt(string typed)
    {
        var result = Resolve(typed);
        Assert.Equal(AddressAction.External, result.Action);
        Assert.Equal(typed, result.Url);
    }

    [Fact]
    public void A_scheme_an_app_on_this_pc_registered_is_an_app_link_else_a_search()
    {
        Assert.Equal(AddressAction.Search, Resolve("acme:open").Action);
        Assert.Equal(AddressAction.External, Resolve("acme:open", scheme => scheme == "acme").Action);
    }

    [Fact]
    public void Each_engine_has_its_results_page_and_duckduckgo_is_the_default()
    {
        Assert.Equal(SearchEngine.DuckDuckGo, SearchEngines.Default);
        Assert.Equal(5, SearchEngines.All.Count);
        Assert.Equal("https://www.bing.com/search?q=a%20b", SearchEngines.QueryUrl(SearchEngine.Bing, "a b"));
        Assert.Equal("https://www.google.com/search?q=a%20b", SearchEngines.QueryUrl(SearchEngine.Google, "a b"));
        Assert.Equal("https://duckduckgo.com/?q=a%20b", SearchEngines.QueryUrl(SearchEngine.DuckDuckGo, "a b"));
        Assert.Equal("https://search.brave.com/search?q=a%20b", SearchEngines.QueryUrl(SearchEngine.Brave, "a b"));
        Assert.Equal("https://www.startpage.com/sp/search?query=a%20b", SearchEngines.QueryUrl(SearchEngine.Startpage, "a b"));
        Assert.Equal("https://www.bing.com/search?q=rice", VisusAddress.Resolve("rice", SearchEngine.Bing)!.Url);
        Assert.Equal("https://duckduckgo.com/?q=%26%3D%23", SearchEngines.QueryUrl(SearchEngine.DuckDuckGo, "&=#"));
    }

    [Theory]
    [InlineData("https://example.com/", "example.com")]
    [InlineData("https://docs.example.dev/css/container-queries", "docs.example.dev/css/container-queries")]
    [InlineData("http://localhost:5173/", "localhost:5173")]
    [InlineData("https://example.com/a?b=1#c", "example.com/a?b=1#c")]
    [InlineData("about:blank", "")]
    [InlineData(null, "")]
    public void The_field_shows_the_address_without_its_scheme(string? url, string shown) =>
        Assert.Equal(shown, VisusAddress.Display(url));

    [Theory]
    [InlineData("https://example.com/a", "https://example.com/a")]
    [InlineData("about:blank", "")]
    public void While_editing_the_field_holds_the_whole_address(string url, string shown) =>
        Assert.Equal(shown, VisusAddress.Editing(url));
}
