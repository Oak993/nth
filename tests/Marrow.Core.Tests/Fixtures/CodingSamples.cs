using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;
using static Marrow.Core.Port.SwiftDecoding;

namespace Marrow.Core.Tests.Fixtures;

// Trimmed shapes of real Codable types (B2), to prove the coding rules; not port code.

/// <summary>Core/Models/Timeline.swift:58-70 UserMessage (synthesized Codable; Attachment trimmed to a string).</summary>
public sealed record UserMessageSample(string Text, ValueList<string> Attachments, ValueList<long>? HydraHeads, bool? HydraBrief)
{
    // Synthesized init(from:): `var attachments: [Attachment] = []` is still a REQUIRED key.
    public static UserMessageSample Read(JsonElement e) => new(
        Decode(e, "text", ReadString),
        Decode(e, "attachments", a => ReadArray(a, ReadString)),
        DecodeIfPresent(e, "hydraHeads", a => ReadArray(a, ReadInt)),
        DecodeIfPresentValue(e, "hydraBrief", ReadBool));

    // Synthesized encode(to:): every stored property; nil optionals are omitted (encodeIfPresent).
    public void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteString("text", Text);
        w.WriteStartArray("attachments");
        foreach (var attachment in Attachments) w.WriteStringValue(attachment);
        w.WriteEndArray();
        if (HydraHeads is not null)
        {
            w.WriteStartArray("hydraHeads");
            foreach (var head in HydraHeads) w.WriteNumberValue(head);
            w.WriteEndArray();
        }
        if (HydraBrief is { } brief) w.WriteBoolean("hydraBrief", brief);
        w.WriteEndObject();
    }
}

/// <summary>Core/Models/Library.swift:81-91 Project.init(from:), trimmed: id and path strict, name and addedAt lenient.</summary>
public sealed record ProjectSample(Guid Id, string Path, string Name, DateTimeOffset AddedAt)
{
    public static ProjectSample Read(JsonElement e)
    {
        var id = Decode(e, "id", ReadUUID);                                                   // :84 strict
        var path = Decode(e, "path", ReadString);                                             // :85 strict
        var name = Value(e, "name", ReadString, () => SwiftPath.LastPathComponent(path));      // :86 lenient
        var addedAt = Value(e, "addedAt", ReadStorageDate, () => SwiftDate.Now);               // :87 lenient, fresh
        return new(id, path, name, addedAt);
    }
}

/// <summary>Core/Models/Timeline.swift:17-26 TimelineItem.Content, trimmed to two cases (SE-0295 coding).</summary>
public abstract record ContentSample
{
    private ContentSample() { }

    public sealed record User(UserMessageSample Value) : ContentSample;

    public sealed record Todos(ValueList<string> Value) : ContentSample;

    public static ContentSample Read(JsonElement element)
    {
        var (name, payload) = SwiftEnumCoding.ReadCase(element);
        return name switch
        {
            "user" => new User(Decode(payload, "_0", UserMessageSample.Read)),
            "todos" => new Todos(Decode(payload, "_0", a => ReadArray(a, ReadString))),
            _ => throw new DecodingError("dataCorrupted", $"Cannot initialize Content from invalid key {name}."),
        };
    }

    public void Write(Utf8JsonWriter writer)
    {
        switch (this)
        {
            case User user:
                SwiftEnumCoding.WriteCase(writer, "user", w =>
                {
                    w.WritePropertyName("_0");
                    user.Value.Write(w);
                });
                break;
            case Todos todos:
                SwiftEnumCoding.WriteCase(writer, "todos", w =>
                {
                    w.WriteStartArray("_0");
                    foreach (var step in todos.Value) w.WriteStringValue(step);
                    w.WriteEndArray();
                });
                break;
            default:
                throw new UnreachableException();
        }
    }

    /// <summary>A switch-bearing member, for the union coverage test.</summary>
    public string CaseName => this switch
    {
        User => "user",
        Todos => "todos",
        _ => throw new UnreachableException(),
    };
}

/// <summary>A persisted model shaped like ChatThread (Library.swift): id, title, sortOrder, createdAt, tags, note.</summary>
[JsonConverter(typeof(ThreadSampleConverter))]
public sealed record ThreadSample(Guid Id, string Title, double SortOrder, DateTimeOffset CreatedAt, ValueList<string> Tags, string? Note);

/// <summary>The hand-written converter every persisted type gets (R-CODE-3): reads through SwiftDecoding, writes Swift fields.</summary>
public sealed class ThreadSampleConverter : JsonConverter<ThreadSample>
{
    public override ThreadSample Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var e = document.RootElement;
        return new(
            Decode(e, "id", ReadUUID),
            Decode(e, "title", ReadString),
            Decode(e, "sortOrder", ReadDouble),
            Decode(e, "createdAt", ReadStorageDate),
            Decode(e, "tags", a => ReadArray(a, ReadString)),
            DecodeIfPresent(e, "note", ReadString));
    }

    public override void Write(Utf8JsonWriter w, ThreadSample value, JsonSerializerOptions options)
    {
        // Key order does not matter here: SwiftJson sorts under the storage style.
        w.WriteStartObject();
        w.WriteString("id", SwiftUUID.UuidString(value.Id));
        w.WriteString("title", value.Title);
        SwiftJson.WriteDouble(w, "sortOrder", value.SortOrder);
        w.WriteString("createdAt", FoundationISO8601.Format(value.CreatedAt));
        w.WriteStartArray("tags");
        foreach (var tag in value.Tags) w.WriteStringValue(tag);
        w.WriteEndArray();
        if (value.Note is not null) w.WriteString("note", value.Note);
        w.WriteEndObject();
    }
}

/// <summary>The pitfall shape: a type left to System.Text.Json's default binding.</summary>
public sealed class LenientBindingProbe
{
    [JsonPropertyName("text")] public string Text { get; init; } = "";

    [JsonPropertyName("attachments")] public List<string> Attachments { get; init; } = [];
}

/// <summary>A String enum missing a [RawValue]: SwiftEnum refuses it at type initialization.</summary>
public enum BrokenRawEnum
{
    [RawValue("a")] A,
    B,
}

/// <summary>A test-only String enum whose raw values have canonically equivalent non-ASCII spellings.</summary>
public enum CanonicalRawSample
{
    [RawValue("K")] Kelvin,
    [RawValue("caf\U000000E9")] Cafe,
}

/// <summary>The MCPTransport shape (Marrow.Core/Models/MCP.cs:8-22), reduced, with one switch that forgot a case.</summary>
public abstract record TransportSample
{
    private TransportSample() { }

    public sealed record Stdio(string Command) : TransportSample;

    public sealed record Http(string Url) : TransportSample;

    public sealed record OAuth(string Url) : TransportSample;

    public string Name => this switch
    {
        Stdio => "stdio",
        Http => "http",
        OAuth => "oauth",
        _ => throw new UnreachableException(),
    };

    public string NameForgettingOAuth => this switch
    {
        Stdio => "stdio",
        Http => "http",
        _ => throw new UnreachableException(), // compiles cleanly under TreatWarningsAsErrors
    };
}

/// <summary>The storage options a Coding.cs twin would own: converters only, never default binding.</summary>
public static class SampleCoding
{
    public static JsonSerializerOptions StorageOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { MaxDepth = 512 };
        options.Converters.Add(new SwiftUUID.JsonConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
