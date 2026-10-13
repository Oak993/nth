using System.Text.Json;
using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W11, model access (DESIGN.md 5.10): the address rules (internal schemes and the deny list, the
// cross-origin rule's sources, the query spelled out for the card), the compact accessibility tree with stable refs, and the
// key presses web_key takes.
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class WebAgentPageTests
{
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Users/me/secret.txt")]
    [InlineData("data:text/html,hi")]
    [InlineData("edge://settings")]
    [InlineData("https://user:pw@a.test/")]
    [InlineData("https://vault.bitwarden.com/#/vault")]
    [InlineData("https://myaccount.google.com/security")]
    [InlineData("https://github.com/settings/tokens")]
    public void TheseAddressesAreNeverOpened(string url) => Assert.NotNull(WebAgentUrls.DeniedReason(url));

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://localhost:5173/")]
    [InlineData("https://github.com/anthropics")]
    [InlineData("https://myaccount.google.com/personal-info")]
    public void OrdinaryAddressesMayBeOpened(string url) => Assert.Null(WebAgentUrls.DeniedReason(url));

    [Fact]
    public void AnotherSiteAfterAReadNamesTheSitesItCouldCarryTextFrom()
    {
        string[] read = ["https://mail.a.test", "https://docs.b.test"];

        Assert.Equal(["https://mail.a.test", "https://docs.b.test"], WebAgentUrls.CrossOriginSources(read, "https://evil.test/x?q=1"));
        Assert.Equal(["https://docs.b.test"], WebAgentUrls.CrossOriginSources(read, "https://mail.a.test/inbox"));
        Assert.Empty(WebAgentUrls.CrossOriginSources([], "https://evil.test/"));
        Assert.Equal(["https://docs.b.test"], WebAgentUrls.CrossOriginSources(read, "https://evil.test/", (from, _) => from == "https://mail.a.test"));
    }

    [Fact]
    public void QueryValuesAreSpelledOutAndMarkedWhenTheyCameFromAPage()
    {
        var fields = WebAgentUrls.QueryFields("https://evil.test/c?q=quarterly%20numbers&lang=en#k=v", value => value == "quarterly numbers");

        Assert.Equal(3, fields.Count);
        Assert.Equal(new WebQueryField("q", "quarterly numbers", true), fields[0]);
        Assert.Equal(new WebQueryField("lang", "en", false), fields[1]);
        Assert.Equal(new WebQueryField("k", "v", false), fields[2]);
    }

    [Fact]
    public void TheCardHighlightsEveryQueryValue()
    {
        var parts = WebAgentUrls.Parts("https://evil.test/c?q=secret&x=1");

        Assert.Equal("https://evil.test/c", parts[0].Text);
        Assert.False(parts[0].Highlight);
        Assert.Equal(["secret", "1"], parts.Where(p => p.Highlight).Select(p => p.Text));
        Assert.Equal("https://evil.test/c?q=secret&x=1", string.Concat(parts.Select(p => p.Text)));
    }

    [Fact]
    public void AddressesAreFoundInTextAndNormalised()
    {
        Assert.Equal(["https://a.test/x", "http://b.test"], WebAgentUrls.FindAddresses("See https://a.test/x, and http://b.test."));
        Assert.Equal("https://a.test", WebAgentUrls.Normalize("https://A.test/#top"));
        Assert.Equal("https://a.test/p?q=1", WebAgentUrls.Normalize("https://a.test/p?q=1#x"));
    }

    [Fact]
    public void SearchResultsAreKnownAsSuch()
    {
        Assert.True(WebAgentUrls.IsSearchResults(SearchEngines.QueryUrl(SearchEngines.Default, "visus")));
        Assert.False(WebAgentUrls.IsSearchResults("https://example.com/search?q=visus"));
    }

    private const string Tree = """
        {"nodes":[
          {"nodeId":"1","role":{"value":"RootWebArea"},"name":{"value":"Invite people"},"backendDOMNodeId":1,"childIds":["2","3","6","8"]},
          {"nodeId":"2","role":{"value":"heading"},"name":{"value":"Invite people"},"backendDOMNodeId":2,"childIds":["9"],"properties":[{"name":"level","value":{"type":"integer","value":1}}]},
          {"nodeId":"9","role":{"value":"StaticText"},"name":{"value":"Invite people"},"childIds":[]},
          {"nodeId":"3","role":{"value":"generic"},"name":{"value":""},"backendDOMNodeId":3,"childIds":["4","5"]},
          {"nodeId":"4","role":{"value":"textbox"},"name":{"value":"Email"},"value":{"value":"lee@example.com"},"backendDOMNodeId":4,"childIds":[],"properties":[{"name":"focused","value":{"type":"boolean","value":true}},{"name":"required","value":{"type":"boolean","value":true}}]},
          {"nodeId":"5","role":{"value":"textbox"},"name":{"value":"Password"},"value":{"value":"hunter2"},"backendDOMNodeId":5,"childIds":[]},
          {"nodeId":"6","role":{"value":"link"},"name":{"value":"Help"},"backendDOMNodeId":6,"childIds":[],"properties":[{"name":"url","value":{"type":"string","value":"https://a.test/help"}}]},
          {"nodeId":"8","role":{"value":"button"},"name":{"value":"Send invite"},"backendDOMNodeId":8,"childIds":[]}
        ]}
        """;

    private static IReadOnlyList<WebAxNode> Nodes()
    {
        using var document = JsonDocument.Parse(Tree);
        return WebAgentTree.Parse(document.RootElement.Clone());
    }

    [Fact]
    public void TheTreeIsCompactLinesWithRefsStatesAndLinks()
    {
        var refs = new WebRefs();

        var text = WebAgentTree.Format(Nodes(), refs, secrets: new HashSet<long> { 5 }, pageUrl: "https://a.test/invite");

        var lines = text.Text.Split('\n');
        Assert.Equal("e1 page \"Invite people\"", lines[0]);
        Assert.Contains(lines, l => l.Trim() == "e2 heading \"Invite people\" level=1");
        Assert.Contains(lines, l => l.Trim() == "e3 textbox \"Email\" focused required value=\"lee@example.com\"");
        Assert.Contains(lines, l => l.Trim() == "e4 textbox \"Password\"");
        Assert.Contains(lines, l => l.Trim() == "e5 link \"Help\" -> /help");
        Assert.Contains(lines, l => l.Trim() == "e6 button \"Send invite\"");
        Assert.DoesNotContain("hunter2", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, l => l.Contains("generic", StringComparison.Ordinal));
        Assert.Equal(["https://a.test/help"], text.Links);
    }

    [Fact]
    public void RefsStayTheSameAcrossReadsAndResolveBack()
    {
        var refs = new WebRefs();
        WebAgentTree.Format(Nodes(), refs);

        var again = WebAgentTree.Find(Nodes(), refs, "send");

        Assert.Equal(["e6 button \"Send invite\""], again);
        Assert.Equal(new WebNodeKey(8, null), refs.Resolve("e6"));
        Assert.Null(refs.Resolve("e99"));
        Assert.Null(refs.Resolve("button"));
    }

    [Fact]
    public void ALongTreeIsCutAndSaysSo()
    {
        var text = WebAgentTree.Format(Nodes(), new WebRefs(), maxChars: 60);

        Assert.True(text.Omitted > 0);
        Assert.Contains("more lines left out", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void KeysParseIntoPresses()
    {
        var (presses, error) = WebAgentKeys.Parse("tab shift+tab ctrl+a enter a F5");

        Assert.Null(error);
        Assert.Equal(6, presses.Count);
        Assert.Equal(("Tab", 9), (presses[0].Key, presses[0].KeyCode));
        Assert.Equal(WebAgentKeys.Shift, presses[1].Modifiers);
        Assert.Equal(WebAgentKeys.Ctrl, presses[2].Modifiers);
        Assert.Null(presses[2].Text);
        Assert.True(presses[3].IsEnter);
        Assert.Equal("a", presses[4].Text);
        Assert.Equal(("F5", 116), (presses[5].Key, presses[5].KeyCode));
    }

    [Theory]
    [InlineData("win+r")]
    [InlineData("hyper+x")]
    [InlineData("launchrocket")]
    [InlineData("")]
    public void KeysThisToolNeverPressesAreRefused(string keys) => Assert.NotNull(WebAgentKeys.Parse(keys).Error);

    [Fact]
    public void TheDevicePresetsMatchTheDeviceFrame()
    {
        Assert.Equal(new WebDeviceSize(390, 844, 3, true), WebAgentDevices.Presets["phone"]);
        Assert.Null(WebAgentDevices.Presets["desktop"]);
    }

    [Fact]
    public void EveryWebToolHasASchemaAndTheActionsAreTheOnesThatAsk()
    {
        Assert.Equal(16, WebToolSchemas.All.Count);
        Assert.All(WebToolSchemas.All, spec => Assert.StartsWith("web_", spec.Name, StringComparison.Ordinal));
        Assert.All(WebToolSchemas.All, spec => Assert.Equal("object", spec.Schema()["type"]!.GetValue<string>()));
        Assert.True(WebToolSchemas.SiteActions.SetEquals([WebToolSchemas.Click, WebToolSchemas.Type, WebToolSchemas.Key, WebToolSchemas.Select]));
        Assert.True(WebToolSchemas.IsWebTool("web_read"));
        Assert.False(WebToolSchemas.IsWebTool("pc_click"));
    }
}
