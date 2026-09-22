using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marrow.Core.Models;

// DroppyCode/Core/Models/MCP.swift:4-100.
public abstract record MCPTransport
{
    private MCPTransport() { }
    public sealed record Stdio(string Command, IReadOnlyList<string> Args) : MCPTransport
    {
        private IReadOnlyList<string> _args = MCPValues.List(Args);
        public IReadOnlyList<string> Args { get => _args; init => _args = MCPValues.List(value); }
    }
    public sealed record Http(string Url, IReadOnlyDictionary<string, string> Headers) : MCPTransport
    {
        private IReadOnlyDictionary<string, string> _headers = MCPValues.Map(Headers);
        public IReadOnlyDictionary<string, string> Headers { get => _headers; init => _headers = MCPValues.Map(value); }
    }
    public sealed record OAuth(string Url) : MCPTransport;
}

public sealed record MCPField(string Key, string Label, string Placeholder,
    MCPField.Kind FieldKind = MCPField.Kind.Secret, string? Help = null,
    string? DefaultValue = null, bool IsRequired = true)
{
    public enum Kind { Secret, Text, Path }
    public string Id => Key;
}

public enum MCPCategory { Developer, Browser, Search, Work, Data, Cloud, Knowledge }

public static class MCPCategoryExtensions
{
    public static string Id(this MCPCategory category) => category switch
    {
        MCPCategory.Developer => "developer", MCPCategory.Browser => "browser",
        MCPCategory.Search => "search", MCPCategory.Work => "work", MCPCategory.Data => "data",
        MCPCategory.Cloud => "cloud", MCPCategory.Knowledge => "knowledge",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    public static string Title(this MCPCategory category) => category switch
    {
        MCPCategory.Developer => "Developer", MCPCategory.Browser => "Browser",
        MCPCategory.Search => "Search", MCPCategory.Work => "Work", MCPCategory.Data => "Data",
        MCPCategory.Cloud => "Cloud", MCPCategory.Knowledge => "Knowledge",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
}

public sealed record MCPCatalogEntry(string Id, string Name, string Vendor, string Summary,
    MCPCategory Category, string Asset, uint ColorValue, MCPTransport Transport, string DocsUrl)
{
    public bool IsMonochrome { get; init; }
    private IReadOnlyList<MCPField> _fields = MCPValues.List<MCPField>([]);
    private IReadOnlyDictionary<string, string> _env = MCPValues.Map(new Dictionary<string, string>());
    private IReadOnlyList<string> _sampleTools = MCPValues.List<string>([]);
    public IReadOnlyList<MCPField> Fields { get => _fields; init => _fields = MCPValues.List(value); }
    public IReadOnlyDictionary<string, string> Env { get => _env; init => _env = MCPValues.Map(value); }
    public IReadOnlyList<string> SampleTools { get => _sampleTools; init => _sampleTools = MCPValues.List(value); }
    public string? KeysUrl { get; init; }
    public bool NeedsInput => Fields.Any(item => item.IsRequired);
    public bool IsOAuth => Transport is MCPTransport.OAuth;

    public string? OAuthUrl(IReadOnlyDictionary<string, string>? values = null) =>
        Transport is MCPTransport.OAuth oauth ? Fill(oauth.Url, values) : null;

    public string Fill(string text, IReadOnlyDictionary<string, string>? values)
    {
        var output = text;
        foreach (var field in Fields)
        {
            var value = values is not null && values.TryGetValue(field.Key, out var supplied) && supplied.Length > 0
                ? supplied : field.DefaultValue ?? "";
            var token = "{" + field.Key + "}";
            if (output.Contains("://" + token, StringComparison.Ordinal))
            {
                value = value.Trim();
                foreach (var scheme in new[] { "https://", "http://" })
                    if (value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) value = value[scheme.Length..];
                value = value.TrimEnd('/');
            }
            output = output.Replace(token, value, StringComparison.Ordinal);
        }
        return output;
    }
}

// The Swift proxy dependency is resolved in Marrow.Services.MCP.MCPResolver (architecture V1).
public sealed record MCPResolvedServer(string Id, string Name)
{
    public string? Command { get; init; }
    private IReadOnlyList<string> _args = MCPValues.List<string>([]);
    private IReadOnlyDictionary<string, string> _env = MCPValues.Map(new Dictionary<string, string>());
    private IReadOnlyDictionary<string, string> _headers = MCPValues.Map(new Dictionary<string, string>());
    public IReadOnlyList<string> Args { get => _args; init => _args = MCPValues.List(value); }
    public IReadOnlyDictionary<string, string> Env { get => _env; init => _env = MCPValues.Map(value); }
    public string? Url { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get => _headers; init => _headers = MCPValues.Map(value); }
    public string? OAuthUpstream { get; init; }
    public bool IsRemote => Url is not null;
}

[JsonConverter(typeof(MCPToolSummaryConverter))]
public sealed record MCPToolSummary(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("summary")] string? Summary = null)
{
    [JsonIgnore] public string Id => Name;
}

internal sealed class MCPToolSummaryConverter : JsonConverter<MCPToolSummary>
{
    public override bool HandleNull => true;

    public override MCPToolSummary Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
            throw new JsonException();
        string? summary = null;
        if (root.TryGetProperty("summary", out var value) && value.ValueKind != JsonValueKind.Null)
        {
            if (value.ValueKind != JsonValueKind.String) throw new JsonException();
            summary = value.GetString();
        }
        return new(name.GetString()!, summary);
    }

    public override void Write(Utf8JsonWriter writer, MCPToolSummary value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteString("name", value.Name);
        if (value.Summary is not null) writer.WriteString("summary", value.Summary);
        writer.WriteEndObject();
    }
}

// Construction defaults do not make synthesized Swift Codable fields optional during decoding.
[JsonConverter(typeof(MCPConnectionConverter))]
public sealed record MCPConnection(string CatalogID)
{
    public bool IsEnabled { get; init; } = true;
    private IReadOnlyDictionary<string, string> _values = MCPValues.Map(new Dictionary<string, string>());
    private IReadOnlyList<MCPToolSummary> _tools = MCPValues.List<MCPToolSummary>([]);
    public IReadOnlyDictionary<string, string> Values { get => _values; init => _values = MCPValues.Map(value); }
    public DateTimeOffset? ConnectedAt { get; init; }
    public string? ServerVersion { get; init; }
    public IReadOnlyList<MCPToolSummary> Tools { get => _tools; init => _tools = MCPValues.List(value); }
    public string Id => CatalogID;
}

// Swift arrays/dictionaries are values: copying and content equality must survive the C# boundary.
internal static class MCPValues
{
    public static IReadOnlyList<T> List<T>(IEnumerable<T> values) => new ValueList<T>(values);
    public static IReadOnlyDictionary<string, string> Map(IReadOnlyDictionary<string, string> values) => new ValueMap(values);

    private sealed class ValueList<T>(IEnumerable<T> values) : System.Collections.ObjectModel.ReadOnlyCollection<T>(values.ToArray())
    {
        public override bool Equals(object? obj) => obj is ValueList<T> other && this.SequenceEqual(other);
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var item in this) hash.Add(item);
            return hash.ToHashCode();
        }
    }

    private sealed class ValueMap(IReadOnlyDictionary<string, string> values) :
        System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
    {
        public override bool Equals(object? obj) => obj is ValueMap other && Count == other.Count
            && this.All(pair => other.TryGetValue(pair.Key, out var value) && pair.Value == value);
        public override int GetHashCode()
        {
            var hash = 0;
            foreach (var pair in this) hash ^= HashCode.Combine(pair.Key, pair.Value);
            return hash;
        }
    }
}

internal sealed class MCPConnectionConverter : JsonConverter<MCPConnection>
{
    public override bool HandleNull => true;

    public override MCPConnection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var id = Required(root, "catalogID", JsonValueKind.String).GetString()!;
        if (!root.TryGetProperty("isEnabled", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new JsonException();
        var values = new Dictionary<string, string>();
        foreach (var pair in Required(root, "values", JsonValueKind.Object).EnumerateObject())
        {
            if (pair.Value.ValueKind != JsonValueKind.String) throw new JsonException();
            values[pair.Name] = pair.Value.GetString()!;
        }
        var tools = new List<MCPToolSummary>();
        foreach (var tool in Required(root, "tools", JsonValueKind.Array).EnumerateArray())
            tools.Add(new(Required(tool, "name", JsonValueKind.String).GetString()!, OptionalString(tool, "summary")));
        DateTimeOffset? date = null;
        if (root.TryGetProperty("connectedAt", out var dateElement) && dateElement.ValueKind != JsonValueKind.Null)
            date = dateElement.Deserialize<DateTimeOffset>(options);
        return new(id) { IsEnabled = enabled.GetBoolean(), Values = values, Tools = tools,
            ConnectedAt = date, ServerVersion = OptionalString(root, "serverVersion") };
    }

    private static JsonElement Required(JsonElement root, string key, JsonValueKind kind)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out var value) || value.ValueKind != kind) throw new JsonException();
        return value;
    }

    private static string? OptionalString(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new JsonException();
        return value.GetString();
    }

    public override void Write(Utf8JsonWriter writer, MCPConnection value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteString("catalogID", value.CatalogID);
        writer.WriteBoolean("isEnabled", value.IsEnabled);
        writer.WritePropertyName("values"); JsonSerializer.Serialize(writer, value.Values, options);
        if (value.ConnectedAt is { } date) { writer.WritePropertyName("connectedAt"); JsonSerializer.Serialize(writer, date, options); }
        if (value.ServerVersion is not null) writer.WriteString("serverVersion", value.ServerVersion);
        writer.WritePropertyName("tools"); JsonSerializer.Serialize(writer, value.Tools, options);
        writer.WriteEndObject();
    }
}
