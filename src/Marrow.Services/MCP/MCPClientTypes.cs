using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// PORT-ONLY (web surfaces W18, DESIGN.md 7.6, ED-NEW-web-w18-1): the types the upgraded MCP client hands out. The
// client used to flatten everything a server said into text; it now keeps resources, tool _meta (where an MCP App
// names its UI), structured results and images, and says when a server's lists changed. Everything here is plain
// data or a pure function so it can be tested without a server process.

namespace Marrow.Services.MCP;

/// <summary>Who may call a tool, from <c>_meta.ui.visibility</c>. The default is both.</summary>
[Flags]
public enum MCPToolVisibility
{
    None = 0,
    /// <summary>The model may see and call the tool.</summary>
    Model = 1,
    /// <summary>The tool's MCP App view may call it.</summary>
    App = 2,
    Both = Model | App,
}

/// <summary>Who is calling a tool through the hub. An app-only tool refuses a model caller.</summary>
public enum MCPCaller
{
    Model,
    App,
}

/// <summary>What the server's list-changed notification said changed.</summary>
public sealed record MCPListChange(string Server, MCPWire.ListKind Kind);

/// <summary>A resource a server lists (resources/list).</summary>
public sealed record MCPResource(string Server, string Uri, string Name)
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? MimeType { get; init; }
    public long? Size { get; init; }
    public JSONValue? Meta { get; init; }
}

/// <summary>A resource template a server lists (resources/templates/list).</summary>
public sealed record MCPResourceTemplate(string Server, string UriTemplate, string Name)
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? MimeType { get; init; }
    public JSONValue? Meta { get; init; }
}

/// <summary>One item of a resources/read answer: text, or a base64 blob, with its own _meta.</summary>
public sealed record MCPResourceContent(string Uri)
{
    public string? MimeType { get; init; }
    public string? Text { get; init; }
    /// <summary>Base64 of the bytes of a binary resource.</summary>
    public string? Blob { get; init; }
    public JSONValue? Meta { get; init; }

    /// <summary>The text of the item: its text, or its blob decoded as UTF-8; null when it has neither.</summary>
    public string? TextOrDecodedBlob()
    {
        if (Text is { } text) return text;
        if (Blob is not { } blob) return null;
        try { return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(blob)); }
        catch (Exception error) when (error is FormatException or ArgumentException) { return null; }
    }
}

/// <summary>The domains a UI resource asks to reach (<c>_meta.ui.csp</c>). nth never widens them (DESIGN.md 7.6).</summary>
public sealed record MCPUiCsp
{
    public ValueList<string> ConnectDomains { get; init; } = ValueList<string>.Empty;
    public ValueList<string> ResourceDomains { get; init; } = ValueList<string>.Empty;
    public ValueList<string> FrameDomains { get; init; } = ValueList<string>.Empty;
    public ValueList<string> BaseUriDomains { get; init; } = ValueList<string>.Empty;

    public bool IsEmpty => ConnectDomains.Count == 0 && ResourceDomains.Count == 0 && FrameDomains.Count == 0 && BaseUriDomains.Count == 0;
}

/// <summary>What a UI resource asks the host for (<c>_meta.ui.permissions</c>).</summary>
public sealed record MCPUiPermissions(bool Camera, bool Microphone, bool Geolocation, bool ClipboardWrite)
{
    public static readonly MCPUiPermissions None = new(false, false, false, false);
}

/// <summary>The <c>_meta.ui</c> object of a UI resource, read leniently: a malformed member is dropped.</summary>
public sealed record MCPUiMeta
{
    public MCPUiCsp Csp { get; init; } = new();
    public MCPUiPermissions Permissions { get; init; } = MCPUiPermissions.None;
    public string? Domain { get; init; }
    public bool? PrefersBorder { get; init; }

    public static readonly MCPUiMeta Empty = new();

    public static MCPUiMeta Read(JSONValue? meta)
    {
        if (meta?["ui"] is not JSONValue.Object ui) return Empty;
        static ValueList<string> Strings(JSONValue? value) => value?.JSONValueArray is { } items
            ? new ValueList<string>(items.Select(item => item.JSONValueString).Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text!))
            : ValueList<string>.Empty;
        var csp = ui["csp"];
        var permissions = ui["permissions"];
        return new MCPUiMeta
        {
            Csp = new MCPUiCsp
            {
                ConnectDomains = Strings(csp?["connectDomains"]),
                ResourceDomains = Strings(csp?["resourceDomains"]),
                FrameDomains = Strings(csp?["frameDomains"]),
                BaseUriDomains = Strings(csp?["baseUriDomains"]),
            },
            Permissions = new MCPUiPermissions(
                permissions?["camera"] is { IsNull: false },
                permissions?["microphone"] is { IsNull: false },
                permissions?["geolocation"] is { IsNull: false },
                permissions?["clipboardWrite"] is { IsNull: false }),
            Domain = ui["domain"]?.JSONValueString,
            PrefersBorder = ui["prefersBorder"]?.JSONValueBool,
        };
    }
}

/// <summary>A <c>ui://</c> resource read from a server: the page and what it asks for. The MCP Apps host (W19) mounts it.</summary>
public sealed record MCPUiResource(string Server, string Uri, string Html, MCPUiMeta Ui, JSONValue? Meta);

/// <summary>How a tool's <c>_meta</c> names its UI and says who may call it. Pure functions over the tool's _meta.</summary>
public static class MCPToolMeta
{
    private const int MaxUriLength = 2_048;

    /// <summary>
    /// The <c>ui://</c> resource the tool says draws its result. Read from <c>_meta.ui.resourceUri</c>, else the
    /// older flat <c>_meta["ui/resourceUri"]</c>, else <c>_meta["openai/outputTemplate"]</c>. A value that is not a
    /// <c>ui://</c> address is ignored, so a tool cannot point its view at a web page or a file.
    /// </summary>
    public static string? UiResourceUri(JSONValue? meta)
    {
        if (meta is null) return null;
        foreach (var candidate in new[]
                 {
                     meta["ui"]?["resourceUri"]?.JSONValueString,
                     meta["ui/resourceUri"]?.JSONValueString,
                     meta["openai/outputTemplate"]?.JSONValueString,
                 })
        {
            if (candidate is { Length: > 0 and <= MaxUriLength } uri && IsUiUri(uri)) return uri;
        }
        return null;
    }

    public static bool IsUiUri(string uri) => uri.StartsWith("ui://", StringComparison.OrdinalIgnoreCase) && uri.Length > "ui://".Length;

    /// <summary>True for the MCP App page MIME type, <c>text/html;profile=mcp-app</c> (case, spaces and quotes around the profile ignored).</summary>
    public static bool IsAppPageMime(string? mime)
    {
        if (mime is null) return false;
        var compact = mime.Replace(" ", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal).ToLowerInvariant();
        return compact == MCPWire.UiMimeType || compact.StartsWith(MCPWire.UiMimeType + ";", StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>_meta.ui.visibility</c>: an array that may hold "model" and "app". Absent, or not an array, means both. An
    /// array is taken as written: ["app"] hides the tool from the model, an empty array hides it from both.
    /// </summary>
    public static MCPToolVisibility Visibility(JSONValue? meta)
    {
        if (meta?["ui"]?["visibility"]?.JSONValueArray is not { } entries) return MCPToolVisibility.Both;
        var visibility = MCPToolVisibility.None;
        foreach (var entry in entries)
        {
            switch (entry.JSONValueString)
            {
                case "model": visibility |= MCPToolVisibility.Model; break;
                case "app": visibility |= MCPToolVisibility.App; break;
            }
        }
        return visibility;
    }
}

/// <summary>An image of a tool result, as the server sent it (base64).</summary>
public sealed record MCPResultImage(string MimeType, string Data);

/// <summary>A resource link or an embedded resource of a tool result.</summary>
public sealed record MCPResultResource(string Uri)
{
    public string? Name { get; init; }
    public string? MimeType { get; init; }
    public string? Description { get; init; }
    public bool Embedded { get; init; }
    public string? Text { get; init; }
    public long? ByteCount { get; init; }
}

/// <summary>Reads a tools/call result into the hub's structured Result. A pure function.</summary>
public static class MCPResultReader
{
    /// <summary>The most embedded-resource text the flattened Text carries for the model.</summary>
    public const int MaxEmbeddedTextChars = ToolPayload.MaxEmbeddedTextChars;

    public static MCPHub.Result Read(JSONValue? result)
    {
        var lines = new List<string>();
        var images = new List<MCPResultImage>();
        var resources = new List<MCPResultResource>();
        if (result?["content"] is JSONValue.Array contentArray)
        {
            foreach (var item in contentArray.Value)
            {
                var type = item["type"]?.JSONValueString;
                if (item["text"] is JSONValue.String { Value: { } text })
                {
                    lines.Add(text);
                }
                else if (type == "image")
                {
                    // The text for a caller that cannot show pictures stays "[image]"; the picture itself is in Images.
                    lines.Add("[image]");
                    if (item["data"]?.JSONValueString is { Length: > 0 } data)
                        images.Add(new MCPResultImage(item["mimeType"]?.JSONValueString is { Length: > 0 } mime ? mime : "image/png", data));
                }
                else if (type == "audio")
                {
                    lines.Add("[audio]");
                }
                else if (type == "resource")
                {
                    var resource = item["resource"];
                    var uri = resource?["uri"]?.JSONValueString ?? "";
                    var embeddedText = resource?["text"]?.JSONValueString;
                    var blob = resource?["blob"]?.JSONValueString;
                    var mime = resource?["mimeType"]?.JSONValueString;
                    var isUiPage = MCPToolMeta.IsUiUri(uri) || MCPToolMeta.IsAppPageMime(mime);
                    // The model sees an embedded text resource, except an MCP App page: that is for the view, not the model.
                    lines.Add(embeddedText is { Length: > 0 } && !isUiPage
                        ? $"[resource: {uri}]\n{CapText(embeddedText)}"
                        : $"[resource: {uri}]");
                    if (uri.Length > 0)
                    {
                        resources.Add(new MCPResultResource(uri)
                        {
                            Name = resource?["name"]?.JSONValueString,
                            MimeType = mime,
                            Embedded = true,
                            Text = embeddedText is null ? null : CapText(embeddedText),
                            ByteCount = blob is null ? null : Base64Bytes(blob),
                        });
                    }
                }
                else if (type == "resource_link")
                {
                    var uri = item["uri"]?.JSONValueString ?? "";
                    lines.Add($"[resource: {uri}]");
                    if (uri.Length > 0)
                    {
                        resources.Add(new MCPResultResource(uri)
                        {
                            Name = item["name"]?.JSONValueString,
                            MimeType = item["mimeType"]?.JSONValueString,
                            Description = item["description"]?.JSONValueString,
                        });
                    }
                }
            }
        }
        var structured = result?["structuredContent"] is JSONValue.Object found ? found : null;
        var meta = result?["_meta"] is JSONValue.Object foundMeta ? foundMeta : null;
        var text2 = string.Join("\n", lines);
        // A server that answers only with structuredContent (it should add the JSON as text too, but not every one
        // does) still gives the model something to read.
        if (text2.Length == 0 && structured is not null) text2 = structured.CompactString;
        var isError = result?["isError"] is JSONValue.Bool { Value: true };
        return new MCPHub.Result(text2, isError)
        {
            Images = new ValueList<MCPResultImage>(images),
            Resources = new ValueList<MCPResultResource>(resources),
            StructuredContent = structured,
            Meta = meta,
        };
    }

    private static string CapText(string text) =>
        text.Length <= MaxEmbeddedTextChars ? text : string.Concat(text.AsSpan(0, MaxEmbeddedTextChars), "\n[truncated]");

    internal static long Base64Bytes(string base64)
    {
        if (base64.Length == 0) return 0;
        var padding = base64.EndsWith("==", StringComparison.Ordinal) ? 2 : base64.EndsWith('=') ? 1 : 0;
        return Math.Max(0, base64.Length * 3L / 4 - padding);
    }
}
