using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;

namespace Marrow.Services.Tests.MCP;

// PORT-ONLY: web surfaces W18 (DESIGN.md 7.6, ED-NEW-web-w18-1). A tools/call result is no longer flattened to text and
// thrown away: the text stays what it was for a caller that can only read text, and the images, resource links,
// structuredContent and _meta are kept next to it, then reduced to a ToolPayload under the persistence caps.
[Trait("Decision", "ED-NEW-web-w18-1")]
public sealed class MCPResultReaderTests
{
    private static JSONValue Json(string text) => JSONValue.Parse(text)!;

    [Fact]
    public void A_plain_text_result_is_the_joined_text_with_nothing_extra_and_no_payload()
    {
        var result = MCPResultReader.Read(Json("""{"content":[{"type":"text","text":"one"},{"type":"text","text":"two"}]}"""));
        Assert.Equal("one\ntwo", result.Text);
        Assert.False(result.IsError);
        Assert.Empty(result.Images);
        Assert.Empty(result.Resources);
        Assert.Null(result.StructuredContent);
        Assert.Null(result.Meta);
        Assert.Null(result.ToPayload());
    }

    [Fact]
    public void The_flattened_text_of_mixed_content_is_exactly_what_it_was_before_the_upgrade()
    {
        var result = MCPResultReader.Read(Json("""
            {"content":[{"type":"text","text":"first line"},{"type":"image"},
            {"type":"resource","resource":{"uri":"file:///r.txt"}},{"type":"resource"},{"type":"other"}]}
            """));
        Assert.Equal("first line\n[image]\n[resource: file:///r.txt]\n[resource: ]", result.Text);
    }

    [Fact]
    public void An_image_keeps_its_data_and_type_next_to_the_placeholder_text()
    {
        var result = MCPResultReader.Read(Json("""{"content":[{"type":"text","text":"chart"},{"type":"image","data":"iVBORw0KGgo=","mimeType":"image/jpeg"},{"type":"image","data":"AAAA"}]}"""));
        Assert.Equal("chart\n[image]\n[image]", result.Text);
        Assert.Equal([new MCPResultImage("image/jpeg", "iVBORw0KGgo="), new MCPResultImage("image/png", "AAAA")], result.Images.ToArray());
    }

    [Fact]
    public void An_image_without_data_is_only_the_placeholder_and_audio_has_its_own()
    {
        var result = MCPResultReader.Read(Json("""{"content":[{"type":"image"},{"type":"image","data":""},{"type":"audio","data":"AAAA","mimeType":"audio/wav"}]}"""));
        Assert.Equal("[image]\n[image]\n[audio]", result.Text);
        Assert.Empty(result.Images);
    }

    [Fact]
    public void A_resource_link_and_an_embedded_resource_are_listed_and_the_model_reads_the_embedded_text()
    {
        var result = MCPResultReader.Read(Json("""
            {"content":[{"type":"resource_link","uri":"file:///report.pdf","name":"report","mimeType":"application/pdf","description":"the report"},
            {"type":"resource","resource":{"uri":"file:///inline.txt","mimeType":"text/plain","text":"inline words"}},
            {"type":"resource","resource":{"uri":"file:///data.bin","blob":"AAECAw=="}}]}
            """));
        Assert.Equal("[resource: file:///report.pdf]\n[resource: file:///inline.txt]\ninline words\n[resource: file:///data.bin]", result.Text);
        Assert.Equal(3, result.Resources.Count);
        Assert.Equal(new MCPResultResource("file:///report.pdf") { Name = "report", MimeType = "application/pdf", Description = "the report" }, result.Resources[0]);
        Assert.Equal(new MCPResultResource("file:///inline.txt") { MimeType = "text/plain", Embedded = true, Text = "inline words" }, result.Resources[1]);
        Assert.Equal(4L, result.Resources[2].ByteCount);
        Assert.Null(result.Resources[2].Text);
    }

    [Fact]
    public void An_embedded_app_page_is_listed_but_never_shown_to_the_model_as_text()
    {
        var result = MCPResultReader.Read(Json("""
            {"content":[{"type":"resource","resource":{"uri":"ui://board/view.html","mimeType":"text/html;profile=mcp-app","text":"<html>secret page</html>"}},
            {"type":"resource","resource":{"uri":"file:///other.html","mimeType":"text/html;profile=mcp-app","text":"<html>other</html>"}}]}
            """));
        Assert.Equal("[resource: ui://board/view.html]\n[resource: file:///other.html]", result.Text);
        Assert.DoesNotContain("secret", result.Text, StringComparison.Ordinal);
        Assert.Equal(2, result.Resources.Count);
    }

    [Fact]
    public void Embedded_text_for_the_model_is_cut_at_the_cap_and_says_so()
    {
        var long16k = new string('w', MCPResultReader.MaxEmbeddedTextChars + 500);
        var result = MCPResultReader.Read(Json("{\"content\":[{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///big.txt\",\"text\":\"" + long16k + "\"}}]}"));
        Assert.EndsWith("\n[truncated]", result.Text, StringComparison.Ordinal);
        Assert.Equal("[resource: file:///big.txt]\n".Length + MCPResultReader.MaxEmbeddedTextChars + "\n[truncated]".Length, result.Text.Length);
        Assert.Equal(MCPResultReader.MaxEmbeddedTextChars + "\n[truncated]".Length, result.Resources[0].Text!.Length);
    }

    [Fact]
    public void Structured_content_and_meta_are_kept_as_objects_and_nothing_else_is()
    {
        var result = MCPResultReader.Read(Json("""{"content":[{"type":"text","text":"ok"}],"structuredContent":{"count":3},"_meta":{"viewState":"abc"}}"""));
        Assert.Equal("ok", result.Text);
        Assert.Equal(3, result.StructuredContent!["count"]!.JSONValueInt);
        Assert.Equal("abc", result.Meta!["viewState"]!.JSONValueString);

        var wrong = MCPResultReader.Read(Json("""{"content":[],"structuredContent":"not an object","_meta":[1]}"""));
        Assert.Null(wrong.StructuredContent);
        Assert.Null(wrong.Meta);
    }

    [Fact]
    public void A_result_with_only_structured_content_reads_as_that_content_for_the_model()
    {
        var result = MCPResultReader.Read(Json("""{"content":[],"structuredContent":{"answer":42}}"""));
        Assert.Equal("""{"answer":42}""", result.Text);
        Assert.NotNull(result.ToPayload());
    }

    [Fact]
    public void An_error_flag_and_a_missing_or_odd_result_do_not_throw()
    {
        Assert.True(MCPResultReader.Read(Json("""{"content":[{"type":"text","text":"failed"}],"isError":true}""")).IsError);
        Assert.Equal("", MCPResultReader.Read(null).Text);
        Assert.Equal("", MCPResultReader.Read(Json("[]")).Text);
        Assert.Equal("", MCPResultReader.Read(Json("""{"content":"nope"}""")).Text);
    }

    [Fact]
    public void The_payload_of_a_result_carries_the_server_tool_ui_resource_images_links_structure_and_meta()
    {
        var result = MCPResultReader.Read(Json("""
            {"content":[{"type":"text","text":"x"},{"type":"image","data":"iVBORw0KGgo=","mimeType":"image/png"},
            {"type":"resource_link","uri":"file:///a.txt"}],"structuredContent":{"n":1},"_meta":{"k":"v"}}
            """)) with { Server = "srv", ToolName = "board", UiResourceUri = "ui://board/view.html" };

        var payload = result.ToPayload()!;
        Assert.Equal("srv", payload.Server);
        Assert.Equal("board", payload.Tool);
        Assert.Equal("ui://board/view.html", payload.UiResourceUri);
        var image = Assert.Single(payload.Images);
        Assert.Equal(("image/png", "iVBORw0KGgo=", 8L, false), (image.MimeType, image.Data, image.ByteCount, image.Omitted));
        Assert.Equal("file:///a.txt", Assert.Single(payload.ResourceLinks).Uri);
        Assert.Equal("""{"n":1}""", payload.StructuredContent);
        Assert.Equal("""{"k":"v"}""", payload.Meta);
    }

    [Fact]
    public void A_tool_that_names_a_view_has_a_payload_even_when_the_result_is_only_text()
    {
        var result = MCPResultReader.Read(Json("""{"content":[{"type":"text","text":"x"}]}""")) with { Server = "srv", ToolName = "board", UiResourceUri = "ui://board/view.html" };
        var payload = result.ToPayload()!;
        Assert.Equal("ui://board/view.html", payload.UiResourceUri);
        Assert.Empty(payload.Images);
    }

    [Fact]
    public void A_huge_image_is_reduced_to_its_size_in_the_payload()
    {
        var fiveMiB = Convert.ToBase64String(new byte[5 * 1024 * 1024]);
        var result = MCPResultReader.Read(Json("{\"content\":[{\"type\":\"image\",\"data\":\"" + fiveMiB + "\",\"mimeType\":\"image/png\"}]}"));
        Assert.Single(result.Images); // the live result keeps everything the server sent
        var image = Assert.Single(result.ToPayload()!.Images);
        Assert.True(image.Omitted);
        Assert.Null(image.Data);
        Assert.Equal(5L * 1024 * 1024, image.ByteCount);
    }
}
