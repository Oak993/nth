using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;
using Marrow.Core.Support;
using static Marrow.Core.Port.SwiftDecoding;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/MCP.swift (B2 1fbf81e), :1-263.
//
// resolve(values:) (:152-169) and MCPPaths (:236-253) reach MCPProxy.url(for:) and Storage.root, both
// Services-layer (HANDOFF section 9, IMPLEMENTING 2.1); they are ported to Marrow.Services (already begun as
// Marrow.Services.MCP.MCPResolver, architecture V1) once the still-unbuilt Services.Store.Storage unit lands, and
// are not implemented in this Core file. The Color(rgb:) extension (:255-263) is SwiftUI/SwiftColor territory
// (ED-095) and out of scope here; this file only stores the ColorValue a SwiftColor is built from elsewhere.

// :39-61 enum MCPCategory: String, CaseIterable, Sendable, Identifiable
public enum MCPCategory
{
    [RawValue("developer")] Developer,
    [RawValue("browser")] Browser,
    [RawValue("search")] Search,
    [RawValue("work")] Work,
    [RawValue("data")] Data,
    [RawValue("cloud")] Cloud,
    [RawValue("knowledge")] Knowledge,
}

public static class MCPCategoryExtensions
{
    // :48 var id: String { rawValue }
    public static string Id(this MCPCategory category) => SwiftEnum<MCPCategory>.RawValue(category);

    // :50-60 var title: String
    public static string Title(this MCPCategory category) => category switch
    {
        MCPCategory.Developer => "Developer",
        MCPCategory.Browser => "Browser",
        MCPCategory.Search => "Search",
        MCPCategory.Work => "Work",
        MCPCategory.Data => "Data",
        MCPCategory.Cloud => "Cloud",
        MCPCategory.Knowledge => "Knowledge",
    };
}

// :5-15 enum MCPTransport: Sendable, Hashable (not Codable; R-ENUM-2 associated-value union)
public abstract record MCPTransport
{
    private MCPTransport() { }

    // :8 A local process speaking JSON-RPC over stdio.
    public sealed record Stdio(string Command, ValueList<string> Args) : MCPTransport;

    // :10 A remote streamable-HTTP server.
    public sealed record Http(string Url, ValueMap<string, string> Headers) : MCPTransport;

    // :14 A remote server behind OAuth, proxied through MCPProxy (Services).
    public sealed record Oauth(string Url) : MCPTransport;
}

// :18-37 struct MCPField: Sendable, Hashable, Identifiable
public sealed record MCPField(string Key, string Label, string Placeholder,
    MCPField.Kind FieldKind = MCPField.Kind.Secret, string? Help = null,
    string? DefaultValue = null, bool IsRequired = true)
{
    // :19-23 enum Kind: Sendable, Hashable (no raw value)
    public enum Kind { Secret, Text, Path }

    // :36 var id: String { key }
    public string Id => Key;
}

// :65-101 struct MCPCatalogEntry: Sendable, Hashable, Identifiable
public sealed record MCPCatalogEntry(string Id, string Name, string Vendor, string Summary,
    MCPCategory Category, string Asset, uint ColorValue, MCPTransport Transport, string DocsURL)
{
    // :79 var isMonochrome = false
    public bool IsMonochrome { get; init; }

    // :81 var fields: [MCPField] = []
    public ValueList<MCPField> Fields { get; init; } = ValueList<MCPField>.Empty;

    // :83 var env: [String: String] = [:]
    public ValueMap<string, string> Env { get; init; } = ValueMap<string, string>.Empty;

    // :86 var sampleTools: [String] = []
    public ValueList<string> SampleTools { get; init; } = ValueList<string>.Empty;

    // :90 var keysURL: String? = nil
    public string? KeysURL { get; init; }

    // :95 var needsInput: Bool { fields.contains { $0.isRequired } }
    public bool NeedsInput => Fields.Any(item => item.IsRequired);

    // :97-100 var isOAuth: Bool
    public bool IsOAuth => Transport is MCPTransport.Oauth;

    // :127-130 func oauthURL(values: [String: String] = [:]) -> String?
    public string? OauthURL(ValueMap<string, string>? values = null) =>
        Transport is MCPTransport.Oauth oauth ? Fill(oauth.Url, values ?? ValueMap<string, string>.Empty) : null;

    // :135-150 func fill(_ text: String, values: [String: String]) -> String
    public string Fill(string text, ValueMap<string, string> values)
    {
        var output = text;
        foreach (var field in Fields) // :137
        {
            // :138 values[field.key].flatMap { $0.isEmpty ? nil : $0 } ?? field.defaultValue ?? ""
            var typed = values.TryGetValue(field.Key, out var supplied) && supplied.Length > 0 ? supplied : null;
            var value = typed ?? field.DefaultValue ?? "";
            var token = "{" + field.Key + "}"; // :139
            if (ContainsCanonical(output, "://" + token)) // :140
            {
                value = FoundationText.Trim(value, includeNewlines: true); // :141
                foreach (var scheme in new[] { "https://", "http://" }) // :142, no break: both schemes are tried in order
                    if (SwiftString.HasPrefix(SwiftString.Lowercased(value), scheme))
                        value = SwiftString.DropFirst(value, SwiftString.Count(scheme)); // :143
                while (SwiftString.HasSuffix(value, "/")) value = SwiftString.DropLast(value); // :145
            }
            output = ReplaceAllCanonical(output, token, value); // :147
        }
        return output; // :149
    }

    // Swift `contains` over Characters, canonical equivalence (R-STR-2 extended to a substring search: no shared
    // helper exists for `contains`/`replacingOccurrences`, so this file owns the two it needs).
    private static bool ContainsCanonical(string text, string other)
    {
        if (other.Length == 0) return true;
        var haystack = SwiftString.Characters(text).ToArray();
        var needle = SwiftString.Characters(other).ToArray();
        for (var start = 0; start <= haystack.Length - needle.Length; start++)
            if (MatchesAt(haystack, start, needle)) return true;
        return false;
    }

    // Swift `replacingOccurrences(of:with:)` with no `.literal` option: canonical equivalence, left to right,
    // non-overlapping (Foundation's own behaviour, matched over Characters rather than NSString UTF-16 units).
    private static string ReplaceAllCanonical(string text, string token, string value)
    {
        var haystack = SwiftString.Characters(text).ToArray();
        var needle = SwiftString.Characters(token).ToArray();
        if (needle.Length == 0) return text;
        var result = new StringBuilder();
        var i = 0;
        while (i < haystack.Length)
        {
            if (i + needle.Length <= haystack.Length && MatchesAt(haystack, i, needle))
            {
                result.Append(value);
                i += needle.Length;
            }
            else
            {
                result.Append(haystack[i]);
                i++;
            }
        }
        return result.ToString();
    }

    private static bool MatchesAt(string[] haystack, int start, string[] needle)
    {
        for (var i = 0; i < needle.Length; i++)
            if (!SwiftString.Equal(haystack[start + i], needle[i])) return false;
        return true;
    }
}

// :104-119 struct MCPResolvedServer: Sendable, Hashable, Identifiable
// The Swift extension that builds one, resolve(values:) (:152-169), needs MCPProxy (Services) and lives in
// Marrow.Services.MCP.MCPResolver.
public sealed record MCPResolvedServer(string Id, string Name)
{
    // :108 Set for a local server.
    public string? Command { get; init; }

    // :109 var args: [String] = []
    public ValueList<string> Args { get; init; } = ValueList<string>.Empty;

    // :110 var env: [String: String] = [:]
    public ValueMap<string, string> Env { get; init; } = ValueMap<string, string>.Empty;

    // :112 Set for a remote streamable-HTTP server.
    public string? Url { get; init; }

    // :113 var headers: [String: String] = [:]
    public ValueMap<string, string> Headers { get; init; } = ValueMap<string, string>.Empty;

    // :116 For an OAuth server, the remote address behind the proxy route in `url`.
    public string? OauthUpstream { get; init; }

    // :118 var isRemote: Bool { url != nil }
    public bool IsRemote => Url is not null;
}

// :173-177 struct MCPToolSummary: Codable, Sendable, Hashable, Identifiable
[JsonConverter(typeof(MCPToolSummaryConverter))]
public sealed record MCPToolSummary(string Name, string? Summary = null)
{
    // :176 var id: String { name }, computed, never coded
    public string Id => Name;

    public static MCPToolSummary Read(JsonElement e) =>
        new(Decode(e, "name", ReadString), DecodeIfPresent(e, "summary", ReadString));

    public void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteString("name", Name);
        if (Summary is not null) w.WriteString("summary", Summary);
        w.WriteEndObject();
    }
}

// R-CODE-1/R-CODE-3: the hand-written reader/writer every persisted type gets; never System.Text.Json default binding.
public sealed class MCPToolSummaryConverter : JsonConverter<MCPToolSummary>
{
    public override bool HandleNull => true;

    public override MCPToolSummary Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return MCPToolSummary.Read(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, MCPToolSummary value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        value.Write(writer);
    }
}

// :179-184 struct MCPProbeResult: Sendable, Hashable (not Codable)
public sealed record MCPProbeResult(string? ServerName, string? ServerVersion, ValueList<MCPToolSummary> Tools);

// :188-199 struct MCPConnection: Codable, Sendable, Hashable, Identifiable
[JsonConverter(typeof(MCPConnectionConverter))]
public sealed record MCPConnection(string CatalogID)
{
    // :191 var isEnabled = true. Synthesized Decodable still requires the key (R-CODE-1).
    public bool IsEnabled { get; init; } = true;

    // :193 var values: [String: String] = [:]
    public ValueMap<string, string> Values { get; init; } = ValueMap<string, string>.Empty;

    // :194 var connectedAt: Date?
    public DateTimeOffset? ConnectedAt { get; init; }

    // :195 var serverVersion: String?
    public string? ServerVersion { get; init; }

    // :196 var tools: [MCPToolSummary] = []
    public ValueList<MCPToolSummary> Tools { get; init; } = ValueList<MCPToolSummary>.Empty;

    // :198 var id: String { catalogID }
    public string Id => CatalogID;
}

public sealed class MCPConnectionConverter : JsonConverter<MCPConnection>
{
    public override bool HandleNull => true;

    public override MCPConnection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var e = document.RootElement;
        return new(Decode(e, "catalogID", ReadString))
        {
            IsEnabled = Decode(e, "isEnabled", ReadBool),
            Values = Decode(e, "values", a => ReadMap(a, ReadString)),
            ConnectedAt = DecodeIfPresentValue(e, "connectedAt", ReadStorageDate),
            ServerVersion = DecodeIfPresent(e, "serverVersion", ReadString),
            Tools = Decode(e, "tools", a => ReadArray(a, MCPToolSummary.Read)),
        };
    }

    public override void Write(Utf8JsonWriter w, MCPConnection value, JsonSerializerOptions options)
    {
        if (value is null) { w.WriteNullValue(); return; }
        w.WriteStartObject();
        w.WriteString("catalogID", value.CatalogID);
        w.WriteBoolean("isEnabled", value.IsEnabled);
        w.WriteStartObject("values");
        foreach (var pair in value.Values) w.WriteString(pair.Key, pair.Value);
        w.WriteEndObject();
        if (value.ConnectedAt is { } date) w.WriteString("connectedAt", FoundationISO8601.Format(date));
        if (value.ServerVersion is not null) w.WriteString("serverVersion", value.ServerVersion);
        w.WriteStartArray("tools");
        foreach (var tool in value.Tools) tool.Write(w);
        w.WriteEndArray();
        w.WriteEndObject();
    }
}

// :203-215 struct MCPSetupGuide: Sendable, Hashable (not Codable)
public sealed record MCPSetupGuide(string Title, string Reason, ValueList<string> Steps, string PageLabel, string PageURL, string? Note = null);

// :218-232 enum MCPConnectionState: Sendable, Hashable (not Codable; R-ENUM-2 associated-value union)
public abstract record MCPConnectionState
{
    private MCPConnectionState() { }

    public sealed record NotConnected : MCPConnectionState;

    public sealed record Connecting : MCPConnectionState;

    public sealed record Connected(long ToolCount) : MCPConnectionState;

    public sealed record Failed(string Message) : MCPConnectionState;

    // :224 Signed in or keyed correctly, but the server itself needs a step on its side first.
    public sealed record NeedsSetup(MCPSetupGuide Guide) : MCPConnectionState;

    // :227-228 var isConnected: Bool
    public bool IsConnected => this is Connected;

    // :231 var isConnecting: Bool { self == .connecting }
    public bool IsConnecting => this is Connecting;
}
