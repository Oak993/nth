using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;
using Marrow.Core.Support;
using static Marrow.Core.Port.SwiftDecoding;

// Source: DroppyCode/Core/Models/MCPCustom.swift (B3 52bc792), :1-184.

namespace Marrow.Core.Models;

// :7-119 struct MCPCustomServer: Codable, Sendable, Hashable, Identifiable
[JsonConverter(typeof(MCPCustomServerConverter))]
public sealed record MCPCustomServer(string Id, string Name, MCPCustomServer.Kind ServerKind)
{
    // :8-11 enum Kind: String, Codable, Sendable
    public enum Kind
    {
        [RawValue("stdio")] Stdio,
        [RawValue("http")] Http,
    }

    // :15-19 struct Variable: Codable, Sendable, Hashable, Identifiable. The synthesized Decodable still
    // requires "isSecret" even though the property has a default (R-CODE-1; PR-MCP-089): Read below uses
    // Decode, not DecodeIfPresent, for isSecret.
    [JsonConverter(typeof(MCPCustomServerVariableConverter))]
    public sealed record Variable(string Key, bool IsSecret = false)
    {
        // :18 var id: String { key }
        public string Id => Key;

        internal static Variable Read(JsonElement e) => new(
            Decode(e, "key", ReadString),
            Decode(e, "isSecret", ReadBool));

        internal void Write(Utf8JsonWriter w)
        {
            w.WriteStartObject();
            w.WriteString("key", Key);
            w.WriteBoolean("isSecret", IsSecret);
            w.WriteEndObject();
        }
    }

    // :26 var command = ""
    public string Command { get; init; } = "";

    // :27 var args: [String] = []
    public ValueList<string> Args { get; init; } = ValueList<string>.Empty;

    // :28 var env: [Variable] = []
    public ValueList<Variable> Env { get; init; } = ValueList<Variable>.Empty;

    // :30 var url = ""
    public string Url { get; init; } = "";

    // :31 var headers: [Variable] = []
    public ValueList<Variable> Headers { get; init; } = ValueList<Variable>.Empty;

    // :58 var variables: [Variable] { kind == .stdio ? env : headers } (PR-MCP-090)
    public ValueList<Variable> Variables => ServerKind == Kind.Stdio ? Env : Headers;

    // :62-76 static func makeID(_ name: String) -> String (PR-MCP-085)
    public static string MakeID(string name)
    {
        var slug = new StringBuilder();
        var lastWasDash = true;
        foreach (var character in SwiftString.Characters(SwiftString.Lowercased(name))) // :65 for character in name.lowercased()
        {
            if (SwiftCharacter.IsLetter(character) || SwiftCharacter.IsNumber(character)) // :66
            {
                slug.Append(character); // :67
                lastWasDash = false; // :68
            }
            else if (!lastWasDash) // :69
            {
                slug.Append('-'); // :70
                lastWasDash = true; // :71
            }
        }
        var result = slug.ToString();
        if (SwiftString.HasSuffix(result, "-")) result = SwiftString.DropLast(result, 1); // :74
        return "custom-" + (result.Length == 0 ? "server" : result); // :75
    }

    // :79-87 var summary: String (PR-MCP-086)
    public string Summary => ServerKind switch
    {
        // :81-83. DEVIATION OD-0008 B4: "Runs on this PC: " in place of upstream's "Runs on this Mac: ".
        Kind.Stdio => $"Runs on this PC: {string.Join(" ", new[] { Command }.Concat(Args))}.",
        // :85
        Kind.Http => $"Remote server at {SwiftURL.Create(Url)?.Host ?? Url}.",
        _ => throw new UnreachableException(),
    };

    // :93-118 func catalogEntry() -> MCPCatalogEntry (PR-MCP-087)
    public MCPCatalogEntry CatalogEntry()
    {
        // :94-96
        var fields = new ValueList<MCPField>(Variables.Select(variable =>
            new MCPField(variable.Key, variable.Key, "", variable.IsSecret ? MCPField.Kind.Secret : MCPField.Kind.Text)));

        // :97-98. Drafts reach the editor's header before duplicate keys are validated: keep the FIRST of
        // duplicate keys (Dictionary(..., uniquingKeysWith: { first, _ in first })).
        var placeholders = new Dictionary<string, string>(SwiftStringComparer.Instance);
        foreach (var variable in Variables)
        {
            if (!placeholders.ContainsKey(variable.Key)) placeholders[variable.Key] = "{" + variable.Key + "}";
        }
        var placeholderMap = new ValueMap<string, string>(placeholders, SwiftStringComparer.Instance);

        // :99-113
        var entry = new MCPCatalogEntry(
            Id: Id,
            Name: Name,
            Vendor: "Custom",
            Summary: Summary,
            Category: MCPCategory.Developer,
            Asset: "",
            ColorValue: 0,
            Transport: ServerKind == Kind.Stdio
                ? new MCPTransport.Stdio(Command, Args)
                : new MCPTransport.Http(Url, placeholderMap),
            DocsURL: "")
        {
            IsMonochrome = true,
            Fields = fields,
        };
        // :114-116
        if (ServerKind == Kind.Stdio) entry = entry with { Env = placeholderMap };
        return entry; // :117
    }
}

// :39-49 init(from:): id, name, kind required; command, args, env, url, headers decodeIfPresent with empty
// fallbacks (PR-MCP-088). encode(to:) is synthesized: the same eight keys in declaration order.
public sealed class MCPCustomServerConverter : JsonConverter<MCPCustomServer>
{
    public override bool HandleNull => true;

    public override MCPCustomServer Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var e = document.RootElement;
        return new MCPCustomServer(
            Decode(e, "id", ReadString), // :41
            Decode(e, "name", ReadString), // :42
            Decode(e, "kind", ReadRawValue<MCPCustomServer.Kind>)) // :43
        {
            Command = DecodeIfPresent(e, "command", ReadString) ?? "", // :44
            Args = DecodeIfPresent(e, "args", a => ReadArray(a, ReadString)) ?? ValueList<string>.Empty, // :45
            Env = DecodeIfPresent(e, "env", a => ReadArray(a, MCPCustomServer.Variable.Read)) ?? ValueList<MCPCustomServer.Variable>.Empty, // :46
            Url = DecodeIfPresent(e, "url", ReadString) ?? "", // :47
            Headers = DecodeIfPresent(e, "headers", a => ReadArray(a, MCPCustomServer.Variable.Read)) ?? ValueList<MCPCustomServer.Variable>.Empty, // :48
        };
    }

    public override void Write(Utf8JsonWriter w, MCPCustomServer value, JsonSerializerOptions options)
    {
        if (value is null) { w.WriteNullValue(); return; }
        w.WriteStartObject();
        w.WriteString("id", value.Id);
        w.WriteString("name", value.Name);
        w.WriteString("kind", SwiftEnum<MCPCustomServer.Kind>.RawValue(value.ServerKind));
        w.WriteString("command", value.Command);
        w.WriteStartArray("args");
        foreach (var arg in value.Args) w.WriteStringValue(arg);
        w.WriteEndArray();
        w.WriteStartArray("env");
        foreach (var variable in value.Env) variable.Write(w);
        w.WriteEndArray();
        w.WriteString("url", value.Url);
        w.WriteStartArray("headers");
        foreach (var header in value.Headers) header.Write(w);
        w.WriteEndArray();
        w.WriteEndObject();
    }
}

public sealed class MCPCustomServerVariableConverter : JsonConverter<MCPCustomServer.Variable>
{
    public override bool HandleNull => true;

    public override MCPCustomServer.Variable Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return MCPCustomServer.Variable.Read(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, MCPCustomServer.Variable value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        value.Write(writer);
    }
}

// :122-183 enum MCPCustomError: Error, Equatable (R-ENUM-2: abstract record, nested sealed records)
public abstract record MCPCustomError
{
    private MCPCustomError() { }

    // :123
    public sealed record EmptyName : MCPCustomError;

    // :124
    public sealed record TakenName(string Name) : MCPCustomError;

    // :125
    public sealed record EmptyCommand : MCPCustomError;

    // :126
    public sealed record BadURL : MCPCustomError;

    // :127
    public sealed record BadVariableKey(string Key) : MCPCustomError;

    // :128
    public sealed record DuplicateVariableKey(string Key) : MCPCustomError;

    // :130-139 var message: String (PR-MCP-093). Curly quotes U+201C/U+201D; backticks are plain ASCII.
    public string Message => this switch
    {
        EmptyName => "The server needs a name.", // :132
        TakenName(var name) => $"A server named “{name}” already exists.", // :133
        EmptyCommand => "A stdio server needs a command.", // :134
        BadURL => "The URL must start with https:// (or http:// for a local server).", // :135
        BadVariableKey(var key) =>
            $"“{key}” is not a valid variable name: letters, digits, `_`, `-` and `.`, starting with a letter or `_`.", // :136
        DuplicateVariableKey(var key) => $"“{key}” is listed twice.", // :137
        _ => throw new UnreachableException(),
    };

    // :143-146 static func validVariableKey(_ key: String) -> Bool (PR-MCP-092)
    public static bool ValidVariableKey(string key)
    {
        using (var enumerator = SwiftString.Characters(key).GetEnumerator())
        {
            if (!enumerator.MoveNext()) return false; // :144 key.first is nil
            var first = enumerator.Current;
            if (!(SwiftCharacter.IsLetter(first) || first == "_")) return false; // :144
        }
        foreach (var character in SwiftString.Characters(key)) // :145
        {
            if (!(SwiftCharacter.IsLetter(character) || SwiftCharacter.IsNumber(character)
                  || character == "_" || character == "-" || character == "."))
                return false;
        }
        return true;
    }

    // :150-168 static func validate(_:existingIDs:) -> MCPCustomError? (PR-MCP-091)
    public static MCPCustomError? Validate(MCPCustomServer server, IReadOnlySet<string> existingIDs)
    {
        var name = server.Name.Trimmed(); // :151
        if (name.Length == 0) return new EmptyName(); // :152
        var id = MCPCustomServer.MakeID(name); // :153
        if (existingIDs.Contains(id)) return new TakenName(name); // :154
        switch (server.ServerKind) // :155
        {
            case MCPCustomServer.Kind.Stdio:
                if (server.Command.Trimmed().Length == 0) return new EmptyCommand(); // :157
                break;
            case MCPCustomServer.Kind.Http:
                if (!ValidRemoteURL(server.Url)) return new BadURL(); // :159
                break;
            default:
                throw new UnreachableException();
        }
        var seen = new HashSet<string>(SwiftStringComparer.Instance); // :161
        foreach (var variable in server.Variables) // :162
        {
            if (!ValidVariableKey(variable.Key)) return new BadVariableKey(variable.Key); // :163
            var key = server.ServerKind == MCPCustomServer.Kind.Http
                ? SwiftString.Lowercased(variable.Key)
                : variable.Key; // :164
            if (!seen.Add(key)) return new DuplicateVariableKey(variable.Key); // :165
        }
        return null; // :167
    }

    // :172-182 static func validRemoteURL(_ text: String) -> Bool (PR-MCP-094); ED-169 URLComponents.
    public static bool ValidRemoteURL(string text)
    {
        var trimmed = text.Trimmed(); // :173
        var parsed = URLComponents.Create(trimmed); // :174
        if (parsed is null) return false;
        var scheme = parsed.Scheme; // :175
        if (scheme is null) return false;
        scheme = SwiftString.Lowercased(scheme);
        var host = parsed.Host; // :176
        if (host is null || host.Length == 0) return false;
        if (!(SwiftString.Equal(scheme, "https") || SwiftString.Equal(scheme, "http"))) return false; // :177
        if (SwiftString.Equal(scheme, "http")) // :178
        {
            var lowerHost = SwiftString.Lowercased(host);
            return SwiftString.Equal(lowerHost, "localhost") || SwiftString.Equal(lowerHost, "127.0.0.1")
                || SwiftString.Equal(lowerHost, "::1") || SwiftString.Equal(lowerHost, "[::1]"); // :179
        }
        return true; // :181
    }
}
