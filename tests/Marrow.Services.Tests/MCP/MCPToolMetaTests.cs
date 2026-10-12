using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;

namespace Marrow.Services.Tests.MCP;

// PORT-ONLY: web surfaces W18 (DESIGN.md 7.6, ED-NEW-web-w18-1). How a tool's _meta names its MCP App view and says who
// may call it, and how a UI resource's _meta.ui is read. Pure functions over JSON; no server process.
[Trait("Decision", "ED-NEW-web-w18-1")]
public sealed class MCPToolMetaTests
{
    private static JSONValue Json(string text) => JSONValue.Parse(text)!;

    [Fact]
    public void The_nested_ui_resource_uri_wins_over_the_flat_key_and_the_openai_key()
    {
        var meta = Json("""{"ui":{"resourceUri":"ui://a/nested"},"ui/resourceUri":"ui://b/flat","openai/outputTemplate":"ui://c/openai"}""");
        Assert.Equal("ui://a/nested", MCPToolMeta.UiResourceUri(meta));
    }

    [Fact]
    public void The_flat_key_is_read_when_there_is_no_nested_one_and_the_openai_key_after_that()
    {
        Assert.Equal("ui://b/flat", MCPToolMeta.UiResourceUri(Json("""{"ui/resourceUri":"ui://b/flat","openai/outputTemplate":"ui://c/openai"}""")));
        Assert.Equal("ui://c/openai", MCPToolMeta.UiResourceUri(Json("""{"openai/outputTemplate":"ui://c/openai"}""")));
    }

    [Fact]
    public void An_address_that_is_not_ui_scheme_is_ignored_and_the_next_candidate_is_tried()
    {
        Assert.Null(MCPToolMeta.UiResourceUri(Json("""{"ui":{"resourceUri":"https://evil.example/x"}}""")));
        Assert.Null(MCPToolMeta.UiResourceUri(Json("""{"ui":{"resourceUri":"file:///C:/secrets.html"}}""")));
        Assert.Null(MCPToolMeta.UiResourceUri(Json("""{"ui":{"resourceUri":"ui://"}}""")));
        Assert.Null(MCPToolMeta.UiResourceUri(Json("""{"ui":{"resourceUri":5}}""")));
        Assert.Equal("ui://b/flat", MCPToolMeta.UiResourceUri(Json("""{"ui":{"resourceUri":"https://evil.example/x"},"ui/resourceUri":"ui://b/flat"}""")));
        Assert.Equal("UI://Case/ok", MCPToolMeta.UiResourceUri(Json("""{"ui":{"resourceUri":"UI://Case/ok"}}""")));
    }

    [Fact]
    public void A_missing_meta_or_one_without_a_view_has_no_ui_resource()
    {
        Assert.Null(MCPToolMeta.UiResourceUri(null));
        Assert.Null(MCPToolMeta.UiResourceUri(Json("{}")));
        Assert.Null(MCPToolMeta.UiResourceUri(Json("""{"ui":{"visibility":["app"]}}""")));
    }

    [Fact]
    public void An_address_over_the_length_cap_is_ignored()
    {
        var longUri = "ui://x/" + new string('a', 2_100);
        Assert.Null(MCPToolMeta.UiResourceUri(Json("{\"ui\":{\"resourceUri\":\"" + longUri + "\"}}")));
    }

    [Theory]
    [InlineData("""{}""", MCPToolVisibility.Both)]
    [InlineData("""{"ui":{}}""", MCPToolVisibility.Both)]
    [InlineData("""{"ui":{"visibility":"app"}}""", MCPToolVisibility.Both)]
    [InlineData("""{"ui":{"visibility":["model","app"]}}""", MCPToolVisibility.Both)]
    [InlineData("""{"ui":{"visibility":["app"]}}""", MCPToolVisibility.App)]
    [InlineData("""{"ui":{"visibility":["model"]}}""", MCPToolVisibility.Model)]
    [InlineData("""{"ui":{"visibility":["app","bogus"]}}""", MCPToolVisibility.App)]
    [InlineData("""{"ui":{"visibility":[]}}""", MCPToolVisibility.None)]
    [InlineData("""{"ui":{"visibility":["bogus"]}}""", MCPToolVisibility.None)]
    public void Visibility_is_both_unless_an_array_says_otherwise(string meta, MCPToolVisibility expected)
    {
        Assert.Equal(expected, MCPToolMeta.Visibility(Json(meta)));
    }

    [Fact]
    public void A_missing_meta_is_visible_to_both()
    {
        Assert.Equal(MCPToolVisibility.Both, MCPToolMeta.Visibility(null));
    }

    [Theory]
    [InlineData("text/html;profile=mcp-app", true)]
    [InlineData("Text/HTML; profile=mcp-app", true)]
    [InlineData("text/html;profile=\"mcp-app\"", true)]
    [InlineData("text/html;profile=mcp-app;charset=utf-8", true)]
    [InlineData("text/html", false)]
    [InlineData("text/plain;profile=mcp-app", false)]
    [InlineData("text/html;profile=mcp-apps", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_mcp_app_page_mime_type_counts_as_an_app_page(string? mime, bool expected)
    {
        Assert.Equal(expected, MCPToolMeta.IsAppPageMime(mime));
    }

    [Fact]
    public void The_ui_meta_of_a_resource_reads_the_domains_permissions_domain_and_border()
    {
        var ui = MCPUiMeta.Read(Json("""
            {"ui":{"csp":{"connectDomains":["api.example.com"],"resourceDomains":["cdn.example.com","*.example.org"],
            "frameDomains":["embed.example.com"],"baseUriDomains":["base.example.com"]},
            "permissions":{"camera":{},"geolocation":{}},"domain":"board.example.com","prefersBorder":true}}
            """));
        Assert.Equal(["api.example.com"], ui.Csp.ConnectDomains.ToArray());
        Assert.Equal(["cdn.example.com", "*.example.org"], ui.Csp.ResourceDomains.ToArray());
        Assert.Equal(["embed.example.com"], ui.Csp.FrameDomains.ToArray());
        Assert.Equal(["base.example.com"], ui.Csp.BaseUriDomains.ToArray());
        Assert.Equal(new MCPUiPermissions(Camera: true, Microphone: false, Geolocation: true, ClipboardWrite: false), ui.Permissions);
        Assert.Equal("board.example.com", ui.Domain);
        Assert.True(ui.PrefersBorder);
    }

    [Fact]
    public void The_ui_meta_drops_blank_and_non_string_domains_and_wrong_typed_members()
    {
        var ui = MCPUiMeta.Read(Json("""{"ui":{"csp":{"connectDomains":["ok.example.com","",5,null,"  "],"resourceDomains":"not-an-array"},"domain":7,"prefersBorder":"yes"}}"""));
        Assert.Equal(["ok.example.com"], ui.Csp.ConnectDomains.ToArray());
        Assert.Empty(ui.Csp.ResourceDomains);
        Assert.Null(ui.Domain);
        Assert.Null(ui.PrefersBorder);
        Assert.Equal(MCPUiPermissions.None, ui.Permissions);
    }

    [Fact]
    public void A_resource_with_no_ui_meta_asks_for_nothing()
    {
        Assert.Equal(MCPUiMeta.Empty, MCPUiMeta.Read(null));
        Assert.Equal(MCPUiMeta.Empty, MCPUiMeta.Read(Json("""{"other":1}""")));
        Assert.True(MCPUiMeta.Read(Json("""{"ui":{}}""")).Csp.IsEmpty);
    }

    [Fact]
    public void A_resource_content_reads_its_text_or_decodes_its_blob_as_utf8()
    {
        Assert.Equal("hi", new MCPResourceContent("x://a") { Text = "hi" }.TextOrDecodedBlob());
        Assert.Equal("<p>caf\u00E9</p>", new MCPResourceContent("x://a") { Blob = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("<p>caf\u00E9</p>")) }.TextOrDecodedBlob());
        Assert.Null(new MCPResourceContent("x://a") { Blob = "%%%not base64" }.TextOrDecodedBlob());
        Assert.Null(new MCPResourceContent("x://a") { Blob = Convert.ToBase64String([0xFF, 0xFE, 0xFD]) }.TextOrDecodedBlob());
        Assert.Null(new MCPResourceContent("x://a").TextOrDecodedBlob());
    }
}
