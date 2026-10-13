using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Models;

// PORT-ONLY (web surfaces W18, DESIGN.md 7.6, ED-NEW-web-w18-1): the structured part of a tool result that the
// text Output cannot carry. An MCP tool can answer with images, resource links, embedded resources, a
// structuredContent object and a _meta object; the timeline row keeps the text in ToolCall.Output as before and
// keeps the rest here, so the chat can draw the image and an MCP App can be mounted from the row again after a
// restart. Everything is plain data (strings, numbers, lists): it round-trips through the storage JSON and
// through the handoff journal without a converter registry.
//
// Persistence is capped on purpose. A thread is saved as JSON, so an image is kept as base64 only up to the caps
// below; a larger one keeps its type and size with Omitted = true, and the row says the image was not kept.

/// <summary>One image of a tool result: base64 data, or only its size when it was too big to keep.</summary>
[JsonConverter(typeof(ToolImageConverter))]
public sealed record ToolImage
{
    public required string MimeType { get; init; }
    /// <summary>Base64 of the image bytes; null when <see cref="Omitted"/>.</summary>
    public string? Data { get; init; }
    /// <summary>The size of the decoded image in bytes, kept even when the data is not.</summary>
    public long ByteCount { get; init; }
    /// <summary>True when the data was left out because of the caps in <see cref="ToolPayload"/>.</summary>
    public bool Omitted { get; init; }
}

/// <summary>A resource the result points at (a resource_link), or one it embeds (a resource block).</summary>
[JsonConverter(typeof(ToolResourceLinkConverter))]
public sealed record ToolResourceLink
{
    public required string Uri { get; init; }
    public string? Name { get; init; }
    public string? MimeType { get; init; }
    public string? Description { get; init; }
    /// <summary>True for an embedded resource block, false for a link.</summary>
    public bool Embedded { get; init; }
    /// <summary>The text of an embedded text resource, cut at <see cref="ToolPayload.MaxEmbeddedTextChars"/>.</summary>
    public string? Text { get; init; }
    /// <summary>The decoded size of an embedded binary resource (its blob is never kept).</summary>
    public long? ByteCount { get; init; }
}

[JsonConverter(typeof(ToolPayloadConverter))]
public sealed record ToolPayload
{
    public const int MaxImages = 8;
    public const long MaxImageBytes = 4L * 1024 * 1024;
    public const long MaxTotalImageBytes = 8L * 1024 * 1024;
    public const int MaxResourceLinks = 32;
    public const int MaxEmbeddedTextChars = 16_384;
    public const int MaxStructuredContentChars = 262_144;
    public const int MaxMetaChars = 16_384;

    /// <summary>The MCP server (its id in nth) that produced the result.</summary>
    public string? Server { get; init; }
    public string? Tool { get; init; }
    public ValueList<ToolImage> Images { get; init; } = ValueList<ToolImage>.Empty;
    public ValueList<ToolResourceLink> ResourceLinks { get; init; } = ValueList<ToolResourceLink>.Empty;
    /// <summary>The <c>ui://</c> resource the tool says draws its result (an MCP App), or null.</summary>
    public string? UiResourceUri { get; init; }
    /// <summary>The result's <c>structuredContent</c> as compact JSON; null when absent or over the cap.</summary>
    public string? StructuredContent { get; init; }
    /// <summary>True when the result had a structuredContent that was left out because of the cap.</summary>
    public bool StructuredContentOmitted { get; init; }
    /// <summary>The result's <c>_meta</c> as compact JSON; null when absent or over the cap.</summary>
    public string? Meta { get; init; }
    /// <summary>PORT-ONLY (web surfaces W24, ED-NEW-web-w24-1): the live answer this row is (ui_show, ui_update, html_widget or a fenced nth-ui or nth-html block), or null.</summary>
    public ToolLiveView? LiveView { get; init; }

    public bool IsEmpty =>
        Images.Count == 0 && ResourceLinks.Count == 0 && UiResourceUri is null && StructuredContent is null
        && !StructuredContentOmitted && Meta is null && LiveView is null;

    /// <summary>
    /// Adds an image of <paramref name="decodedBytes"/> bytes under the caps: kept with its data while there is room
    /// (at most <see cref="MaxImages"/> images, <see cref="MaxImageBytes"/> each, <see cref="MaxTotalImageBytes"/> in
    /// all), otherwise kept as an Omitted stub with its size. Images past <see cref="MaxImages"/> are dropped.
    /// </summary>
    public ToolPayload WithImage(string mimeType, string base64, long decodedBytes)
    {
        if (Images.Count >= MaxImages) return this;
        var used = Images.Where(image => !image.Omitted).Sum(image => image.ByteCount);
        var keep = decodedBytes <= MaxImageBytes && used + decodedBytes <= MaxTotalImageBytes;
        var image = keep
            ? new ToolImage { MimeType = mimeType, Data = base64, ByteCount = decodedBytes }
            : new ToolImage { MimeType = mimeType, ByteCount = decodedBytes, Omitted = true };
        return this with { Images = new ValueList<ToolImage>(Images.Append(image)) };
    }

    public ToolPayload WithResourceLink(ToolResourceLink link) =>
        ResourceLinks.Count >= MaxResourceLinks ? this : this with { ResourceLinks = new ValueList<ToolResourceLink>(ResourceLinks.Append(link)) };

    /// <summary>Sets <see cref="StructuredContent"/> from compact JSON, or the Omitted flag when it is over the cap.</summary>
    public ToolPayload WithStructuredContent(string compactJson) =>
        compactJson.Length > MaxStructuredContentChars
            ? this with { StructuredContent = null, StructuredContentOmitted = true }
            : this with { StructuredContent = compactJson, StructuredContentOmitted = false };

    /// <summary>Sets <see cref="Meta"/> from compact JSON; a meta over the cap is dropped.</summary>
    public ToolPayload WithMeta(string compactJson) =>
        compactJson.Length > MaxMetaChars ? this : this with { Meta = compactJson };
}

internal sealed class ToolImageConverter : JsonConverter<ToolImage>
{
    public override ToolImage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static ToolImage Decode(JsonElement element) => new()
    {
        MimeType = SwiftDecoding.Decode(element, "mimeType", SwiftDecoding.ReadString),
        Data = SwiftDecoding.DecodeIfPresent(element, "data", SwiftDecoding.ReadString),
        ByteCount = SwiftDecoding.Value(element, "byteCount", SwiftDecoding.ReadInt, () => 0L),
        Omitted = SwiftDecoding.Value(element, "omitted", SwiftDecoding.ReadBool, () => false),
    };

    public override void Write(Utf8JsonWriter writer, ToolImage value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, ToolImage value)
    {
        writer.WriteStartObject();
        writer.WriteString("mimeType", value.MimeType);
        if (value.Data is { } data) writer.WriteString("data", data);
        writer.WriteNumber("byteCount", value.ByteCount);
        if (value.Omitted) writer.WriteBoolean("omitted", true);
        writer.WriteEndObject();
    }
}

internal sealed class ToolResourceLinkConverter : JsonConverter<ToolResourceLink>
{
    public override ToolResourceLink Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static ToolResourceLink Decode(JsonElement element) => new()
    {
        Uri = SwiftDecoding.Decode(element, "uri", SwiftDecoding.ReadString),
        Name = SwiftDecoding.DecodeIfPresent(element, "name", SwiftDecoding.ReadString),
        MimeType = SwiftDecoding.DecodeIfPresent(element, "mimeType", SwiftDecoding.ReadString),
        Description = SwiftDecoding.DecodeIfPresent(element, "description", SwiftDecoding.ReadString),
        Embedded = SwiftDecoding.Value(element, "embedded", SwiftDecoding.ReadBool, () => false),
        Text = SwiftDecoding.DecodeIfPresent(element, "text", SwiftDecoding.ReadString),
        ByteCount = SwiftDecoding.DecodeIfPresentValue(element, "byteCount", SwiftDecoding.ReadInt),
    };

    public override void Write(Utf8JsonWriter writer, ToolResourceLink value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, ToolResourceLink value)
    {
        writer.WriteStartObject();
        writer.WriteString("uri", value.Uri);
        if (value.Name is { } name) writer.WriteString("name", name);
        if (value.MimeType is { } mimeType) writer.WriteString("mimeType", mimeType);
        if (value.Description is { } description) writer.WriteString("description", description);
        if (value.Embedded) writer.WriteBoolean("embedded", true);
        if (value.Text is { } text) writer.WriteString("text", text);
        if (value.ByteCount is { } byteCount) writer.WriteNumber("byteCount", byteCount);
        writer.WriteEndObject();
    }
}

internal sealed class ToolPayloadConverter : JsonConverter<ToolPayload>
{
    public override ToolPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    private static string? OptionalString(JsonElement element, string key) =>
        SwiftDecoding.Value<string?>(element, key, field => SwiftDecoding.ReadString(field), () => null);

    // Every member is optional and a malformed member is dropped, so one bad field never costs a whole thread.
    internal static ToolPayload Decode(JsonElement element) => new()
    {
        Server = OptionalString(element, "server"),
        Tool = OptionalString(element, "tool"),
        Images = SwiftDecoding.LenientList(element, "images", ToolImageConverter.Decode, () => ValueList<ToolImage>.Empty),
        ResourceLinks = SwiftDecoding.LenientList(element, "resourceLinks", ToolResourceLinkConverter.Decode, () => ValueList<ToolResourceLink>.Empty),
        UiResourceUri = OptionalString(element, "uiResourceUri"),
        StructuredContent = OptionalString(element, "structuredContent"),
        StructuredContentOmitted = SwiftDecoding.Value(element, "structuredContentOmitted", SwiftDecoding.ReadBool, () => false),
        Meta = OptionalString(element, "meta"),
        LiveView = SwiftDecoding.Value<ToolLiveView?>(element, "liveView", field => field.ValueKind == JsonValueKind.Object ? ToolLiveViewConverter.Decode(field) : null, () => null),
    };

    public override void Write(Utf8JsonWriter writer, ToolPayload value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, ToolPayload value)
    {
        writer.WriteStartObject();
        if (value.Server is { } server) writer.WriteString("server", server);
        if (value.Tool is { } tool) writer.WriteString("tool", tool);
        if (value.Images.Count > 0)
        {
            writer.WritePropertyName("images");
            writer.WriteStartArray();
            foreach (var image in value.Images) ToolImageConverter.WriteMembers(writer, image);
            writer.WriteEndArray();
        }
        if (value.ResourceLinks.Count > 0)
        {
            writer.WritePropertyName("resourceLinks");
            writer.WriteStartArray();
            foreach (var link in value.ResourceLinks) ToolResourceLinkConverter.WriteMembers(writer, link);
            writer.WriteEndArray();
        }
        if (value.UiResourceUri is { } uiResourceUri) writer.WriteString("uiResourceUri", uiResourceUri);
        if (value.StructuredContent is { } structuredContent) writer.WriteString("structuredContent", structuredContent);
        if (value.StructuredContentOmitted) writer.WriteBoolean("structuredContentOmitted", true);
        if (value.Meta is { } meta) writer.WriteString("meta", meta);
        if (value.LiveView is { } liveView)
        {
            writer.WritePropertyName("liveView");
            ToolLiveViewConverter.WriteMembers(writer, liveView);
        }
        writer.WriteEndObject();
    }
}
