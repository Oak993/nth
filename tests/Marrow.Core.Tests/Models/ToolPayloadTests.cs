using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Models;

// PORT-ONLY: web surfaces W18 (DESIGN.md 7.6, ED-NEW-web-w18-1). ToolCall.Payload carries the structured part of an
// MCP tool result (images, resource links, the UI resource address, structuredContent, _meta) next to the text Output.
// These tests pin its persistence: the storage JSON, the caps that keep a thread file small, and a lenient read so a
// damaged payload never makes a thread unreadable.
[Trait("Decision", "ED-NEW-web-w18-1")]
public sealed class ToolPayloadTests
{
    private static string Encode<T>(T value) => Encoding.UTF8.GetString(SwiftJson.Encode(value, Coding.StorageOptions, SwiftJsonStyle.Storage));

    private static T Decode<T>(string json) => Coding.Decode<T>(Encoding.UTF8.GetBytes(json))!;

    // A fixed start time: the storage JSON keeps milliseconds, and a clock reading has more.
    private static ToolCall Call(ToolPayload? payload) => new()
    {
        ToolCallKind = ToolCall.Kind.Mcp,
        Title = "srv · board",
        StartedAt = new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.Zero),
        Payload = payload,
    };

    [Fact]
    public void A_tool_call_without_a_payload_writes_no_payload_member_and_reads_back_without_one()
    {
        var json = Encode(Call(null));
        Assert.DoesNotContain("payload", json, StringComparison.Ordinal);
        Assert.Null(Decode<ToolCall>(json).Payload);
    }

    [Fact]
    public void A_payload_with_nothing_but_the_server_and_tool_is_empty_and_is_not_written()
    {
        var payload = new ToolPayload { Server = "srv", Tool = "board" };
        Assert.True(payload.IsEmpty);
        Assert.DoesNotContain("payload", Encode(Call(payload)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_full_payload_round_trips_through_the_storage_json()
    {
        var payload = new ToolPayload { Server = "srv", Tool = "board", UiResourceUri = "ui://board/view.html" }
            .WithImage("image/png", "iVBORw0KGgo=", 8)
            .WithResourceLink(new ToolResourceLink { Uri = "file:///report.pdf", Name = "report", MimeType = "application/pdf", Description = "the report" })
            .WithResourceLink(new ToolResourceLink { Uri = "file:///inline.txt", Embedded = true, Text = "inline words", ByteCount = null })
            .WithStructuredContent("{\"count\":3}")
            .WithMeta("{\"viewState\":\"abc\"}");
        var call = Call(payload);

        var json = Encode(call);
        var back = Decode<ToolCall>(json);

        Assert.Equal(call, back);
        Assert.Equal("ui://board/view.html", back.Payload!.UiResourceUri);
        Assert.Equal("image/png", Assert.Single(back.Payload.Images).MimeType);
        Assert.Equal(2, back.Payload.ResourceLinks.Count);
        Assert.Equal("{\"count\":3}", back.Payload.StructuredContent);
        Assert.Equal("{\"viewState\":\"abc\"}", back.Payload.Meta);
    }

    [Fact]
    public void A_damaged_payload_never_makes_the_tool_call_unreadable()
    {
        static string WithPayload(string rawPayload) => Encode(Call(null))[..^1] + ",\"payload\":" + rawPayload + "}";
        Assert.Null(Decode<ToolCall>(WithPayload("7")).Payload);
        Assert.Null(Decode<ToolCall>(WithPayload("null")).Payload);

        var partial = Decode<ToolCall>(WithPayload("{\"images\":\"nope\",\"uiResourceUri\":5,\"server\":\"srv\",\"structuredContent\":\"{}\"}")).Payload;
        Assert.NotNull(partial);
        Assert.Equal("srv", partial.Server);
        Assert.Null(partial.UiResourceUri);
        Assert.Empty(partial.Images);
        Assert.Equal("{}", partial.StructuredContent);
    }

    [Fact]
    public void An_image_over_the_per_image_cap_keeps_its_type_and_size_but_not_its_data()
    {
        var payload = new ToolPayload().WithImage("image/png", "AAAA", ToolPayload.MaxImageBytes + 1);
        var image = Assert.Single(payload.Images);
        Assert.True(image.Omitted);
        Assert.Null(image.Data);
        Assert.Equal(ToolPayload.MaxImageBytes + 1, image.ByteCount);
        Assert.Equal("image/png", image.MimeType);

        var exactly = new ToolPayload().WithImage("image/png", "AAAA", ToolPayload.MaxImageBytes);
        Assert.False(Assert.Single(exactly.Images).Omitted);
    }

    [Fact]
    public void Images_past_the_total_cap_are_kept_as_stubs_and_images_past_eight_are_dropped()
    {
        var threeMiB = 3L * 1024 * 1024;
        var payload = new ToolPayload();
        for (var i = 0; i < 4; i++) payload = payload.WithImage("image/jpeg", "AAAA", threeMiB);
        Assert.Equal([false, false, true, true], payload.Images.Select(image => image.Omitted).ToArray());

        var many = new ToolPayload();
        for (var i = 0; i < 12; i++) many = many.WithImage("image/png", "AAAA", 4);
        Assert.Equal(ToolPayload.MaxImages, many.Images.Count);
    }

    [Fact]
    public void Resource_links_stop_at_the_cap()
    {
        var payload = new ToolPayload();
        for (var i = 0; i < ToolPayload.MaxResourceLinks + 5; i++)
            payload = payload.WithResourceLink(new ToolResourceLink { Uri = "file:///" + i });
        Assert.Equal(ToolPayload.MaxResourceLinks, payload.ResourceLinks.Count);
    }

    [Fact]
    public void Structured_content_over_the_cap_is_flagged_and_dropped_and_a_big_meta_is_dropped()
    {
        var big = new ToolPayload().WithStructuredContent(new string('x', ToolPayload.MaxStructuredContentChars + 1));
        Assert.Null(big.StructuredContent);
        Assert.True(big.StructuredContentOmitted);
        Assert.False(big.IsEmpty);

        var fits = new ToolPayload().WithStructuredContent(new string('x', ToolPayload.MaxStructuredContentChars));
        Assert.NotNull(fits.StructuredContent);
        Assert.False(fits.StructuredContentOmitted);

        var meta = new ToolPayload().WithMeta(new string('m', ToolPayload.MaxMetaChars + 1));
        Assert.Null(meta.Meta);
        Assert.True(meta.IsEmpty);

        var round = Decode<ToolCall>(Encode(Call(big)));
        Assert.True(round.Payload!.StructuredContentOmitted);
    }
}
